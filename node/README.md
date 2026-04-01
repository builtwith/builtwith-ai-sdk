# BuiltWith AI First SDK

Official SDK for the [BuiltWith](https://builtwith.com) MCP API. Available for **Node.js** and **.NET**.

## API Key

Get your API key at [https://api.builtwith.com](https://api.builtwith.com).

Set it as an environment variable:

```bash
export BUILTWITH_API_KEY=your-key-here
```

---

## Node.js

### Requirements

- Node.js >= 22.0.0

### Installation

```bash
npm install @builtwith/sdk
```

### Quick Start

```js
const { BuiltWithClient } = require('@builtwith/sdk');

const client = new BuiltWithClient(process.env.BUILTWITH_API_KEY);

const result = await client.domain_lookup_live({ domain: 'spotify.com' });
if (result.ok) {
  console.log(result.data);
} else {
  console.log(result.error);
}
```

### Available Methods

| Method | Parameter | Description |
|---|---|---|
| `domain_lookup_live({ domain })` | Root domain | Live technology lookup |
| `domain_lookup({ lookup })` | Root domain | Full domain API data |
| `relationships({ lookup })` | Root domain | Related websites |
| `free_summary({ lookup })` | Root domain | Free category/group counts |
| `company_to_url({ company })` | Company name | Domains from a company name |
| `tags_lookup({ lookup })` | IP or attribute | Related domains from IP or attributes |
| `recommendations({ lookup })` | Root domain | Technology recommendations |
| `redirects({ lookup })` | Root domain | Live and historical redirects |
| `keywords({ lookup })` | Root domain | Keyword data |
| `trends({ tech })` | Technology name | Technology trend data |
| `product_search({ query })` | Search query | Ecommerce product search |
| `trust({ lookup })` | Root domain | Trust scoring |
| `financial({ lookup })` | Root domain | Financial data |
| `social({ lookup })` | Root domain | Social profile related domains |
| `vector_search({ query, limit? })` | Search query | Semantic technology/category search |
| `payment_discovery()` | — | Agent Payment API: credit balance |
| `payment_configuration()` | — | Agent Payment API: spending limits |
| `payment_purchase({ credits })` | Integer ≥ 2000 | Agent Payment API: purchase credits |

### Response Format

Every method returns:

```js
{
  ok: true | false,
  data: { ... },      // parsed result (null on error)
  raw: { ... },       // raw JSON-RPC response
  error: { ... },     // error details (null on success)
  meta: { request_id, tool, cached }
}
```

### Running Examples

```bash
cd node
BUILTWITH_API_KEY=your-key npm run example
```

---

## .NET (C#)

### Requirements

- .NET 8.0 or .NET Framework 4.8

### Quick Start

```csharp
using BuiltWith.Sdk;

var apiKey = Environment.GetEnvironmentVariable("BUILTWITH_API_KEY");
using var client = new BuiltWithClient(apiKey);

var result = await client.domain_lookup_live("spotify.com");
if (result.Ok)
    Console.WriteLine(result.Data);
else
    Console.WriteLine(result.Error.Message);
```

### Available Methods

All methods accept a `CancellationToken` as an optional last parameter.

| Method | Parameter | Description |
|---|---|---|
| `domain_lookup_live(domain)` | `string` | Live technology lookup |
| `domain_lookup(lookup)` | `string` | Full domain API data |
| `relationships(lookup)` | `string` | Related websites |
| `free_summary(lookup)` | `string` | Free category/group counts |
| `company_to_url(company)` | `string` | Domains from a company name |
| `tags_lookup(lookup)` | `string` | Related domains from IP or attributes |
| `recommendations(lookup)` | `string` | Technology recommendations |
| `redirects(lookup)` | `string` | Live and historical redirects |
| `keywords(lookup)` | `string` | Keyword data |
| `trends(tech)` | `string` | Technology trend data |
| `product_search(query)` | `string` | Ecommerce product search |
| `trust(lookup)` | `string` | Trust scoring |
| `financial(lookup)` | `string` | Financial data |
| `social(lookup)` | `string` | Social profile related domains |
| `vector_search(query, limit?)` | `string`, `int?` | Semantic technology/category search |
| `payment_discovery()` | — | Agent Payment API: credit balance |
| `payment_configuration()` | — | Agent Payment API: spending limits |
| `payment_purchase(credits)` | `int` ≥ 2000 | Agent Payment API: purchase credits |

### Response Format

Every method returns an `SdkResult`:

```csharp
result.Ok       // bool
result.Data     // object - parsed result
result.Raw      // object - raw JSON-RPC response
result.Error    // SdkError - error details (null on success)
result.Meta     // SdkMeta - request_id, tool, cached
```

### Running Examples

```bash
cd csharp/Examples
BUILTWITH_API_KEY=your-key dotnet run
```

---

## Agent Payment API

The payment methods let AI agents check credit balances, view spending configuration, and purchase credits. They route through the standard MCP endpoint using your existing API key. Configure billing at [payments.builtwith.com/agent-payment-api-config](https://payments.builtwith.com/agent-payment-api-config).

```js
// Node.js
const result = await client.payment_discovery();
// result.data => { credits_total, credits_used, credits_available }
```

```csharp
// C#
var result = await client.payment_discovery();
```

---

## Prompt Helpers

Both SDKs include prompt helper methods for use with AI agents:

- `prompt_analyze_tech_stack(domain)`
- `prompt_find_related_websites(domain)`
- `prompt_get_technology_recommendations(domain)`
- `prompt_research_company(company)`
- `prompt_check_domain_trust(domain)`

These return structured prompt objects for MCP-compatible AI workflows.

## License

MIT
