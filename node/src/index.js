'use strict';

const https = require('https');
const { URL } = require('url');

// ── Constants ──────────────────────────────────────────────────────────────────

const MCP_ENDPOINT = 'https://api.builtwith.com/mcp';
const MAX_RETRIES = 3;
const INITIAL_BACKOFF_MS = 1000;
const DOMAIN_RE = /^(?!-)[a-zA-Z0-9-]{1,63}(?<!-)(\.[a-zA-Z]{2,})+$/;

// ── Errors ─────────────────────────────────────────────────────────────────────

class BuiltWithError extends Error {
  constructor(error_code, message, http_status, details = null, suggested_fix = null) {
    super(message);
    this.name = 'BuiltWithError';
    this.error_code = error_code;
    this.message = message;
    this.http_status = http_status;
    this.details = details;
    this.suggested_fix = suggested_fix;
  }

  toJSON() {
    const obj = { error_code: this.error_code, message: this.message, http_status: this.http_status };
    if (this.details != null) obj.details = this.details;
    if (this.suggested_fix != null) obj.suggested_fix = this.suggested_fix;
    return obj;
  }
}

// ── Helpers ────────────────────────────────────────────────────────────────────

function _validate_domain(value) {
  if (typeof value !== 'string' || value.length === 0) {
    throw new BuiltWithError('VALIDATION_ERROR', 'Domain is required and must be a non-empty string.', 0, null, 'Provide a root domain like "example.com".');
  }
  if (/^[a-zA-Z][a-zA-Z+\-.]*:\/\//.test(value)) {
    throw new BuiltWithError('VALIDATION_ERROR', `Domain must not include a scheme. Got: "${value}"`, 0, null, 'Remove the scheme (e.g. "https://") and pass only the root domain.');
  }
  if (value.includes('/')) {
    throw new BuiltWithError('VALIDATION_ERROR', `Domain must not include a path. Got: "${value}"`, 0, null, 'Remove any path segments and pass only the root domain.');
  }
  if (value.includes('?') || value.includes('#')) {
    throw new BuiltWithError('VALIDATION_ERROR', `Domain must not include query or fragment. Got: "${value}"`, 0, null, 'Pass only the root domain.');
  }
  if (!DOMAIN_RE.test(value)) {
    throw new BuiltWithError('VALIDATION_ERROR', `Invalid domain format: "${value}"`, 0, null, 'Provide a valid root domain like "example.com".');
  }
}

function _validate_string(name, value) {
  if (typeof value !== 'string' || value.length === 0) {
    throw new BuiltWithError('VALIDATION_ERROR', `${name} is required and must be a non-empty string.`, 0, null, `Provide a valid ${name}.`);
  }
}

function _validate_input(schema_props, required, params) {
  for (const key of required) {
    if (params[key] === undefined || params[key] === null) {
      throw new BuiltWithError('VALIDATION_ERROR', `Missing required parameter: "${key}".`, 0, null, `Provide the "${key}" parameter.`);
    }
  }
  for (const [key, value] of Object.entries(params)) {
    if (!(key in schema_props)) {
      throw new BuiltWithError('VALIDATION_ERROR', `Unknown parameter: "${key}".`, 0, null, `Remove the "${key}" parameter.`);
    }
  }
}

function _ok(data, raw, tool, request_id = null) {
  return { ok: true, data, raw, error: null, meta: { request_id, tool, cached: null } };
}

function _err(error, tool = null) {
  return { ok: false, data: null, raw: null, error: error.toJSON ? error.toJSON() : error, meta: { request_id: null, tool, cached: null } };
}

function _sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

function _parse_sse_body(raw_body) {
  // If the body looks like SSE (starts with "event:" or "data:"), extract JSON from data lines
  const trimmed = raw_body.trim();
  if (trimmed.startsWith('event:') || trimmed.startsWith('data:')) {
    const data_lines = trimmed.split('\n')
      .filter(line => line.startsWith('data:'))
      .map(line => line.substring(5).trim());
    return data_lines.join('');
  }
  // Otherwise return as-is (plain JSON)
  return raw_body;
}

// ── HTTP transport ─────────────────────────────────────────────────────────────

function _http_post(url_str, body, headers, timeout_ms) {
  return new Promise((resolve, reject) => {
    const parsed = new URL(url_str);
    const payload = JSON.stringify(body);
    const opts = {
      hostname: parsed.hostname,
      port: parsed.port || 443,
      path: parsed.pathname + parsed.search,
      method: 'POST',
      headers: {
        ...headers,
        'Accept': 'application/json, text/event-stream',
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(payload),
      },
      timeout: timeout_ms,
    };

    const req = https.request(opts, (res) => {
      const chunks = [];
      res.on('data', chunk => chunks.push(chunk));
      res.on('end', () => {
        const raw_body = Buffer.concat(chunks).toString('utf-8');
        resolve({ status: res.statusCode, headers: res.headers, body: raw_body });
      });
    });

    req.on('error', reject);
    req.on('timeout', () => { req.destroy(); reject(new Error('Request timed out')); });
    req.write(payload);
    req.end();
  });
}

// ── Client ─────────────────────────────────────────────────────────────────────

class BuiltWithClient {
  constructor(api_key, options = {}) {
    if (!api_key || typeof api_key !== 'string') {
      throw new Error('api_key is required');
    }
    this._api_key = api_key;
    this._endpoint = options.endpoint || MCP_ENDPOINT;
    this._max_retries = options.max_retries != null ? options.max_retries : MAX_RETRIES;
    this._timeout_ms = options.timeout_ms || 30000;
  }

  // ── Request pipeline ───────────────────────────────────────────────────────

  async _request(mcp_tool, params) {
    const body = {
      jsonrpc: '2.0',
      method: 'tools/call',
      params: { name: mcp_tool, arguments: params },
      id: Date.now().toString(),
    };

    const headers = { Authorization: `Bearer ${this._api_key}` };

    let last_error = null;
    for (let attempt = 0; attempt <= this._max_retries; attempt++) {
      try {
        const res = await _http_post(this._endpoint, body, headers, this._timeout_ms);
        const status = res.status;

        // Retry on 429 or 5xx
        if (status === 429 || status >= 500) {
          const retry_after = res.headers['retry-after'];
          const backoff = retry_after
            ? parseInt(retry_after, 10) * 1000
            : INITIAL_BACKOFF_MS * Math.pow(2, attempt);

          last_error = new BuiltWithError(
            status === 429 ? 'RATE_LIMITED' : 'SERVER_ERROR',
            `HTTP ${status}: ${res.body.substring(0, 200)}`,
            status,
            null,
            status === 429 ? 'Reduce request rate or wait before retrying.' : 'The server encountered an error. Try again later.'
          );

          if (attempt < this._max_retries) {
            await _sleep(backoff);
            continue;
          }
          return _err(last_error, mcp_tool);
        }

        // Non-retryable errors
        if (status === 401 || status === 403) {
          return _err(new BuiltWithError('AUTH_ERROR', 'Authentication failed. Check your API key.', status, null, 'Verify your BuiltWith API key is correct and active.'), mcp_tool);
        }

        if (status < 200 || status >= 300) {
          return _err(new BuiltWithError('HTTP_ERROR', `HTTP ${status}: ${res.body.substring(0, 200)}`, status), mcp_tool);
        }

        // Parse response (handle SSE format)
        const json_body = _parse_sse_body(res.body);
        let parsed;
        try {
          parsed = JSON.parse(json_body);
        } catch (_) {
          return _err(new BuiltWithError('PARSE_ERROR', 'Failed to parse response JSON.', status), mcp_tool);
        }

        // Handle JSON-RPC error
        if (parsed.error) {
          return _err(new BuiltWithError('MCP_ERROR', parsed.error.message || 'MCP error', status, parsed.error), mcp_tool);
        }

        // Extract result
        const result = parsed.result;
        let data = result;

        // If result has content array (MCP standard), extract text
        if (result && Array.isArray(result.content)) {
          const text_parts = result.content.filter(c => c.type === 'text').map(c => c.text);
          try {
            data = JSON.parse(text_parts.join(''));
          } catch (_) {
            data = text_parts.join('');
          }
        }

        return _ok(data, parsed, mcp_tool, body.id);

      } catch (err) {
        last_error = new BuiltWithError('NETWORK_ERROR', err.message, 0, null, 'Check network connectivity.');
        if (attempt < this._max_retries) {
          await _sleep(INITIAL_BACKOFF_MS * Math.pow(2, attempt));
          continue;
        }
        return _err(last_error, mcp_tool);
      }
    }

    return _err(last_error || new BuiltWithError('UNKNOWN_ERROR', 'Request failed', 0), mcp_tool);
  }

  // ── Public SDK methods ─────────────────────────────────────────────────────

  async domain_lookup_live(params) {
    const { domain, live_only = true } = params || {};
    _validate_domain(domain);
    return this._request('domain-lookup', { domain, liveOnly: live_only });
  }

  async domain_lookup(params) {
    const { lookup } = params || {};
    _validate_domain(lookup);
    return this._request('domain-api', { lookup });
  }

  async relationships(params) {
    const { lookup } = params || {};
    _validate_domain(lookup);
    return this._request('relationships-api', { lookup });
  }

  async free_summary(params) {
    const { lookup } = params || {};
    _validate_domain(lookup);
    return this._request('free-api', { lookup });
  }

  async company_to_url(params) {
    const { company } = params || {};
    _validate_string('company', company);
    return this._request('company-to-url', { company });
  }

  async tags_lookup(params) {
    const { lookup } = params || {};
    _validate_string('lookup', lookup);
    return this._request('tags-api', { lookup });
  }

  async recommendations(params) {
    const { lookup } = params || {};
    _validate_domain(lookup);
    return this._request('recommendations-api', { lookup });
  }

  async redirects(params) {
    const { lookup } = params || {};
    _validate_domain(lookup);
    return this._request('redirects-api', { lookup });
  }

  async keywords(params) {
    const { lookup } = params || {};
    _validate_domain(lookup);
    return this._request('keywords-api', { lookup });
  }

  async trends(params) {
    const { tech } = params || {};
    _validate_string('tech', tech);
    return this._request('trends-api', { tech });
  }

  async product_search(params) {
    const { query } = params || {};
    _validate_string('query', query);
    return this._request('product-api', { query });
  }

  async trust(params) {
    const { lookup } = params || {};
    _validate_domain(lookup);
    return this._request('trust-api', { lookup });
  }

  async financial(params) {
    const { lookup } = params || {};
    _validate_domain(lookup);
    return this._request('financial-api', { lookup });
  }

  async social(params) {
    const { lookup } = params || {};
    _validate_domain(lookup);
    return this._request('social-api', { lookup });
  }

  async vector_search(params) {
    const { query, limit } = params || {};
    _validate_string('query', query);
    return this._request('vector-search', { query, ...(limit != null ? { limit } : {}) });
  }

  // ── Prompt helpers ─────────────────────────────────────────────────────────

  prompt_analyze_tech_stack(params) {
    const { domain } = params || {};
    _validate_domain(domain);
    return { mcp_prompt: 'analyze-tech-stack', arguments: { domain } };
  }

  prompt_find_related_websites(params) {
    const { domain } = params || {};
    _validate_domain(domain);
    return { mcp_prompt: 'find-related-websites', arguments: { domain } };
  }

  prompt_get_technology_recommendations(params) {
    const { domain } = params || {};
    _validate_domain(domain);
    return { mcp_prompt: 'get-technology-recommendations', arguments: { domain } };
  }

  prompt_research_company(params) {
    const { company } = params || {};
    _validate_string('company', company);
    return { mcp_prompt: 'research-company', arguments: { company } };
  }

  prompt_check_domain_trust(params) {
    const { domain } = params || {};
    _validate_domain(domain);
    return { mcp_prompt: 'check-domain-trust', arguments: { domain } };
  }
}

module.exports = { BuiltWithClient, BuiltWithError };
