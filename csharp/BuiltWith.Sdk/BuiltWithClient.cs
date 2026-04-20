using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BuiltWith.Sdk
{
    public class BuiltWithClient : IDisposable
    {
        private const string DefaultEndpoint = "https://api.builtwith.com/mcp";
        private const int DefaultMaxRetries = 3;
        private const int InitialBackoffMs = 1000;

        private static readonly Regex DomainRegex = new Regex(
            @"^(?!-)[a-zA-Z0-9-]{1,63}(?<!-)(\.[a-zA-Z]{2,})+$",
            RegexOptions.Compiled);

        private static readonly Regex SchemeRegex = new Regex(
            @"^[a-zA-Z][a-zA-Z+\-.]*://",
            RegexOptions.Compiled);

        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly string _endpoint;
        private readonly int _maxRetries;
        private readonly bool _ownsHttpClient;

        public BuiltWithClient(string apiKey, string endpoint = null, int maxRetries = DefaultMaxRetries, HttpClient httpClient = null)
        {
            if (string.IsNullOrEmpty(apiKey))
                throw new ArgumentException("apiKey is required", nameof(apiKey));

            _apiKey = apiKey;
            _endpoint = endpoint ?? DefaultEndpoint;
            _maxRetries = maxRetries;

            if (httpClient != null)
            {
                _http = httpClient;
                _ownsHttpClient = false;
            }
            else
            {
                _http = new HttpClient();
                _ownsHttpClient = true;
            }

            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        }

        public void Dispose()
        {
            if (_ownsHttpClient) _http.Dispose();
        }

        // ── Validation ───────────────────────────────────────────────────────

        private static void ValidateDomain(string value)
        {
            if (string.IsNullOrEmpty(value))
                throw new BuiltWithException("VALIDATION_ERROR", "Domain is required and must be a non-empty string.", 0, "Provide a root domain like \"example.com\".");

            if (SchemeRegex.IsMatch(value))
                throw new BuiltWithException("VALIDATION_ERROR", $"Domain must not include a scheme. Got: \"{value}\"", 0, "Remove the scheme and pass only the root domain.");

            if (value.Contains("/"))
                throw new BuiltWithException("VALIDATION_ERROR", $"Domain must not include a path. Got: \"{value}\"", 0, "Remove path segments and pass only the root domain.");

            if (value.Contains("?") || value.Contains("#"))
                throw new BuiltWithException("VALIDATION_ERROR", $"Domain must not include query or fragment. Got: \"{value}\"", 0, "Pass only the root domain.");

            if (!DomainRegex.IsMatch(value))
                throw new BuiltWithException("VALIDATION_ERROR", $"Invalid domain format: \"{value}\"", 0, "Provide a valid root domain like \"example.com\".");
        }

        private static void ValidateString(string name, string value)
        {
            if (string.IsNullOrEmpty(value))
                throw new BuiltWithException("VALIDATION_ERROR", $"{name} is required and must be a non-empty string.", 0, $"Provide a valid {name}.");
        }

        // ── Result helpers ───────────────────────────────────────────────────

        private static SdkResult Ok(object data, object raw, string tool, string requestId = null)
        {
            return new SdkResult
            {
                Ok = true,
                Data = data,
                Raw = raw,
                Error = null,
                Meta = new SdkMeta { RequestId = requestId, Tool = tool, Cached = null }
            };
        }

        private static SdkResult Err(SdkError error, string tool = null)
        {
            return new SdkResult
            {
                Ok = false,
                Data = null,
                Raw = null,
                Error = error,
                Meta = new SdkMeta { RequestId = null, Tool = tool, Cached = null }
            };
        }

        private static SdkResult Err(BuiltWithException ex, string tool = null) => Err(ex.ToSdkError(), tool);

        // ── SSE parsing ─────────────────────────────────────────────────────

        private static string ParseSseBody(string rawBody)
        {
            var trimmed = rawBody.Trim();
            if (trimmed.StartsWith("event:") || trimmed.StartsWith("data:"))
            {
                var lines = trimmed.Split('\n');
                var dataLines = new List<string>();
                foreach (var line in lines)
                {
                    if (line.StartsWith("data:"))
                        dataLines.Add(line.Substring(5).Trim());
                }
                return string.Join("", dataLines);
            }
            return rawBody;
        }

        // ── Request pipeline ─────────────────────────────────────────────────

        private async Task<SdkResult> RequestAsync(string mcpTool, object arguments, CancellationToken ct = default)
        {
            var requestId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var rpcRequest = new JsonRpcRequest
            {
                Params = new JsonRpcParams { Name = mcpTool, Arguments = arguments },
                Id = requestId
            };

            var json = JsonSerializer.Serialize(rpcRequest);
            BuiltWithException lastError = null;

            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                    using var response = await _http.PostAsync(_endpoint, content, ct).ConfigureAwait(false);
                    var status = (int)response.StatusCode;

                    // Retry on 429 or 5xx
                    if (status == 429 || status >= 500)
                    {
                        var code = status == 429 ? "RATE_LIMITED" : "SERVER_ERROR";
                        var msg = $"HTTP {status}";
                        var fix = status == 429
                            ? "Reduce request rate or wait before retrying."
                            : "The server encountered an error. Try again later.";
                        lastError = new BuiltWithException(code, msg, status, fix);

                        if (attempt < _maxRetries)
                        {
                            var retryAfterHeader = response.Headers.Contains("Retry-After")
                                ? response.Headers.GetValues("Retry-After").FirstOrDefault()
                                : null;
                            int backoff = retryAfterHeader != null && int.TryParse(retryAfterHeader, out var ra)
                                ? ra * 1000
                                : InitialBackoffMs * (int)Math.Pow(2, attempt);
                            await Task.Delay(backoff, ct).ConfigureAwait(false);
                            continue;
                        }
                        return Err(lastError, mcpTool);
                    }

                    if (status == 401 || status == 403)
                        return Err(new BuiltWithException("AUTH_ERROR", "Authentication failed. Check your API key.", status, "Verify your BuiltWith API key is correct and active."), mcpTool);

                    if (status < 200 || status >= 300)
                        return Err(new BuiltWithException("HTTP_ERROR", $"HTTP {status}", status), mcpTool);

                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var jsonBody = ParseSseBody(body);
                    JsonElement parsed;
                    try
                    {
                        parsed = JsonSerializer.Deserialize<JsonElement>(jsonBody);
                    }
                    catch
                    {
                        return Err(new BuiltWithException("PARSE_ERROR", "Failed to parse response JSON.", status), mcpTool);
                    }

                    if (parsed.TryGetProperty("error", out var rpcError))
                    {
                        var errMsg = rpcError.TryGetProperty("message", out var m) ? m.GetString() : "MCP error";
                        return Err(new SdkError
                        {
                            ErrorCode = "MCP_ERROR",
                            Message = errMsg,
                            HttpStatus = status
                        }, mcpTool);
                    }

                    object data = null;
                    if (parsed.TryGetProperty("result", out var result))
                    {
                        if (result.TryGetProperty("content", out var contentArr) && contentArr.ValueKind == JsonValueKind.Array)
                        {
                            var texts = new List<string>();
                            foreach (var item in contentArr.EnumerateArray())
                            {
                                if (item.TryGetProperty("type", out var t) && t.GetString() == "text" &&
                                    item.TryGetProperty("text", out var txt))
                                {
                                    texts.Add(txt.GetString());
                                }
                            }
                            var joined = string.Join("", texts);
                            try { data = JsonSerializer.Deserialize<JsonElement>(joined); }
                            catch { data = joined; }
                        }
                        else
                        {
                            data = result;
                        }
                    }

                    return Ok(data, parsed, mcpTool, requestId);
                }
                catch (TaskCanceledException) { throw; }
                catch (Exception ex)
                {
                    lastError = new BuiltWithException("NETWORK_ERROR", ex.Message, 0, "Check network connectivity.");
                    if (attempt < _maxRetries)
                    {
                        await Task.Delay(InitialBackoffMs * (int)Math.Pow(2, attempt), ct).ConfigureAwait(false);
                        continue;
                    }
                    return Err(lastError, mcpTool);
                }
            }

            return Err(lastError ?? new BuiltWithException("UNKNOWN_ERROR", "Request failed", 0), mcpTool);
        }

        // ── Public SDK methods ───────────────────────────────────────────────

        public Task<SdkResult> domain_lookup_live(string domain, bool liveOnly = true, CancellationToken ct = default)
        {
            ValidateDomain(domain);
            return RequestAsync("domain-lookup", new { domain, liveOnly }, ct);
        }

        public Task<SdkResult> domain_lookup(string lookup, CancellationToken ct = default)
        {
            ValidateDomain(lookup);
            return RequestAsync("domain-api", new { lookup }, ct);
        }

        public Task<SdkResult> relationships(string lookup, CancellationToken ct = default)
        {
            ValidateDomain(lookup);
            return RequestAsync("relationships-api", new { lookup }, ct);
        }

        public Task<SdkResult> free_summary(string lookup, CancellationToken ct = default)
        {
            ValidateDomain(lookup);
            return RequestAsync("free-api", new { lookup }, ct);
        }

        public Task<SdkResult> company_to_url(string company, CancellationToken ct = default)
        {
            ValidateString("company", company);
            return RequestAsync("company-to-url", new { company }, ct);
        }

        public Task<SdkResult> tags_lookup(string lookup, CancellationToken ct = default)
        {
            ValidateString("lookup", lookup);
            return RequestAsync("tags-api", new { lookup }, ct);
        }

        public Task<SdkResult> recommendations(string lookup, CancellationToken ct = default)
        {
            ValidateDomain(lookup);
            return RequestAsync("recommendations-api", new { lookup }, ct);
        }

        public Task<SdkResult> redirects(string lookup, CancellationToken ct = default)
        {
            ValidateDomain(lookup);
            return RequestAsync("redirects-api", new { lookup }, ct);
        }

        public Task<SdkResult> keywords(string lookup, CancellationToken ct = default)
        {
            ValidateDomain(lookup);
            return RequestAsync("keywords-api", new { lookup }, ct);
        }

        public Task<SdkResult> trends(string tech, CancellationToken ct = default)
        {
            ValidateString("tech", tech);
            return RequestAsync("trends-api", new { tech }, ct);
        }

        public Task<SdkResult> product_search(string query, CancellationToken ct = default)
        {
            ValidateString("query", query);
            return RequestAsync("product-api", new { query }, ct);
        }

        public Task<SdkResult> trust(string lookup, CancellationToken ct = default)
        {
            ValidateDomain(lookup);
            return RequestAsync("trust-api", new { lookup }, ct);
        }

        public Task<SdkResult> financial(string lookup, CancellationToken ct = default)
        {
            ValidateDomain(lookup);
            return RequestAsync("financial-api", new { lookup }, ct);
        }

        public Task<SdkResult> social(string lookup, CancellationToken ct = default)
        {
            ValidateDomain(lookup);
            return RequestAsync("social-api", new { lookup }, ct);
        }

        public Task<SdkResult> vector_search(string query, int? limit = null, CancellationToken ct = default)
        {
            ValidateString("query", query);
            if (limit.HasValue)
                return RequestAsync("vector-search", new { query, limit = limit.Value }, ct);
            return RequestAsync("vector-search", new { query }, ct);
        }

        public Task<SdkResult> keyword_search(string keyword, int? limit = null, string? offset = null, CancellationToken ct = default)
        {
            ValidateString("keyword", keyword);
            if (limit.HasValue && !string.IsNullOrEmpty(offset))
                return RequestAsync("keyword-search-api", new { keyword, limit = limit.Value, offset }, ct);
            if (limit.HasValue)
                return RequestAsync("keyword-search-api", new { keyword, limit = limit.Value }, ct);
            if (!string.IsNullOrEmpty(offset))
                return RequestAsync("keyword-search-api", new { keyword, offset }, ct);
            return RequestAsync("keyword-search-api", new { keyword }, ct);
        }

        public Task<SdkResult> payment_discovery(CancellationToken ct = default)
        {
            return RequestAsync("payment-balance", new { }, ct);
        }

        public Task<SdkResult> payment_configuration(CancellationToken ct = default)
        {
            return RequestAsync("payment-config", new { }, ct);
        }

        public Task<SdkResult> payment_purchase(int credits, CancellationToken ct = default)
        {
            if (credits < 2000)
                throw new BuiltWithException("VALIDATION_ERROR", "credits must be at least 2000.", 0, "Minimum purchase is 2000 credits.");
            return RequestAsync("payment-purchase", new { credits }, ct);
        }

        // ── Prompt helpers ───────────────────────────────────────────────────

        public static object prompt_analyze_tech_stack(string domain)
        {
            ValidateDomain(domain);
            return new { mcp_prompt = "analyze-tech-stack", arguments = new { domain } };
        }

        public static object prompt_find_related_websites(string domain)
        {
            ValidateDomain(domain);
            return new { mcp_prompt = "find-related-websites", arguments = new { domain } };
        }

        public static object prompt_get_technology_recommendations(string domain)
        {
            ValidateDomain(domain);
            return new { mcp_prompt = "get-technology-recommendations", arguments = new { domain } };
        }

        public static object prompt_research_company(string company)
        {
            ValidateString("company", company);
            return new { mcp_prompt = "research-company", arguments = new { company } };
        }

        public static object prompt_check_domain_trust(string domain)
        {
            ValidateDomain(domain);
            return new { mcp_prompt = "check-domain-trust", arguments = new { domain } };
        }

        // ── Agent Device-Code Authorization (no API key required) ────────────

        /// <summary>
        /// Start the Agent Device-Code Authorization flow. No API key required.
        /// Returns device_code and verification_uri. Direct the user to open the URI in their browser,
        /// then poll agent_auth_token every 5 seconds until approved or denied.
        /// </summary>
        public static async Task<SdkResult> agent_auth_start(HttpClient httpClient = null, CancellationToken ct = default)
        {
            bool ownsClient = httpClient == null;
            httpClient ??= new HttpClient();
            try
            {
                using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                using var response = await httpClient.PostAsync("https://api.builtwith.com/agent-auth-start", content, ct).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (status < 200 || status >= 300)
                    return Err(new SdkError { ErrorCode = "HTTP_ERROR", Message = $"HTTP {status}", HttpStatus = status }, "agent-auth-start");
                object data;
                try { data = JsonSerializer.Deserialize<JsonElement>(body); }
                catch { return Err(new SdkError { ErrorCode = "PARSE_ERROR", Message = "Failed to parse agent-auth-start response.", HttpStatus = status }, "agent-auth-start"); }
                return Ok(data, default, "agent-auth-start", null);
            }
            catch (TaskCanceledException) { throw; }
            catch (Exception ex)
            {
                return Err(new SdkError { ErrorCode = "NETWORK_ERROR", Message = ex.Message, HttpStatus = 0 }, "agent-auth-start");
            }
            finally
            {
                if (ownsClient) httpClient.Dispose();
            }
        }

        /// <summary>
        /// Poll for the result of an Agent Device-Code Authorization flow. No API key required.
        /// Call every 5 seconds after agent_auth_start. On approval, data.access_token contains the bw-... token.
        /// </summary>
        public static async Task<SdkResult> agent_auth_token(string deviceCode, HttpClient httpClient = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(deviceCode))
                throw new BuiltWithException("VALIDATION_ERROR", "deviceCode is required and must be a non-empty string.", 0, "Provide the device_code from agent_auth_start.");

            bool ownsClient = httpClient == null;
            httpClient ??= new HttpClient();
            try
            {
                var bodyJson = JsonSerializer.Serialize(new { device_code = deviceCode });
                using var content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json");
                using var response = await httpClient.PostAsync("https://api.builtwith.com/agent-auth-token", content, ct).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var status = (int)response.StatusCode;
                // Pass through body even on 4xx — error field carries meaningful status (access_denied, expired_token)
                object data;
                try { data = JsonSerializer.Deserialize<JsonElement>(body); }
                catch { return Err(new SdkError { ErrorCode = "PARSE_ERROR", Message = "Failed to parse agent-auth-token response.", HttpStatus = status }, "agent-auth-token"); }
                return Ok(data, default, "agent-auth-token", null);
            }
            catch (TaskCanceledException) { throw; }
            catch (Exception ex)
            {
                return Err(new SdkError { ErrorCode = "NETWORK_ERROR", Message = ex.Message, HttpStatus = 0 }, "agent-auth-token");
            }
            finally
            {
                if (ownsClient) httpClient.Dispose();
            }
        }
    }
}
