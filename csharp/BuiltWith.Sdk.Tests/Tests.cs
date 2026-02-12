using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BuiltWith.Sdk.Tests
{
    // ── Mock HTTP handler for testing ────────────────────────────────────────

    public class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _handler;
        private int _callCount;

        public int CallCount => _callCount;

        public MockHandler(Func<HttpRequestMessage, int, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var count = Interlocked.Increment(ref _callCount);
            return Task.FromResult(_handler(request, count));
        }
    }

    // ── Domain validation tests ──────────────────────────────────────────────

    public class DomainValidationTests
    {
        [Theory]
        [InlineData("https://example.com")]
        [InlineData("http://example.com")]
        [InlineData("ftp://example.com")]
        public void Rejects_domains_with_scheme(string input)
        {
            var ex = Assert.Throws<BuiltWithException>(() =>
                BuiltWithClient.prompt_analyze_tech_stack(input));
            Assert.Equal("VALIDATION_ERROR", ex.ErrorCode);
            Assert.Contains("scheme", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("example.com/path")]
        [InlineData("example.com/path/to/page")]
        public void Rejects_domains_with_path(string input)
        {
            var ex = Assert.Throws<BuiltWithException>(() =>
                BuiltWithClient.prompt_analyze_tech_stack(input));
            Assert.Equal("VALIDATION_ERROR", ex.ErrorCode);
            Assert.Contains("path", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Rejects_domains_with_query()
        {
            var ex = Assert.Throws<BuiltWithException>(() =>
                BuiltWithClient.prompt_analyze_tech_stack("example.com?foo=bar"));
            Assert.Equal("VALIDATION_ERROR", ex.ErrorCode);
            Assert.Contains("query", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("example.com")]
        [InlineData("example.co.uk")]
        [InlineData("my-site.org")]
        public void Accepts_valid_domains(string input)
        {
            var result = BuiltWithClient.prompt_analyze_tech_stack(input);
            Assert.NotNull(result);
        }
    }

    // ── Missing required params tests ────────────────────────────────────────

    public class MissingParamsTests
    {
        [Fact]
        public void Domain_lookup_live_rejects_null_domain()
        {
            using var client = new BuiltWithClient("test-key");
            Assert.Throws<BuiltWithException>(() => client.domain_lookup_live(null).GetAwaiter().GetResult());
        }

        [Fact]
        public void Domain_lookup_live_rejects_empty_domain()
        {
            using var client = new BuiltWithClient("test-key");
            Assert.Throws<BuiltWithException>(() => client.domain_lookup_live("").GetAwaiter().GetResult());
        }

        [Fact]
        public void Domain_lookup_rejects_null_lookup()
        {
            using var client = new BuiltWithClient("test-key");
            Assert.Throws<BuiltWithException>(() => client.domain_lookup(null).GetAwaiter().GetResult());
        }

        [Fact]
        public void Company_to_url_rejects_empty_company()
        {
            using var client = new BuiltWithClient("test-key");
            Assert.Throws<BuiltWithException>(() => client.company_to_url("").GetAwaiter().GetResult());
        }

        [Fact]
        public void Trends_rejects_null_tech()
        {
            using var client = new BuiltWithClient("test-key");
            Assert.Throws<BuiltWithException>(() => client.trends(null).GetAwaiter().GetResult());
        }

        [Fact]
        public void Constructor_rejects_null_key()
        {
            Assert.Throws<ArgumentException>(() => new BuiltWithClient(null));
        }

        [Fact]
        public void Constructor_rejects_empty_key()
        {
            Assert.Throws<ArgumentException>(() => new BuiltWithClient(""));
        }
    }

    // ── Retry tests ──────────────────────────────────────────────────────────

    public class RetryTests
    {
        [Fact]
        public async Task Retries_on_429_then_succeeds()
        {
            var handler = new MockHandler((req, count) =>
            {
                if (count <= 2)
                {
                    var r = new HttpResponseMessage((HttpStatusCode)429);
                    r.Headers.Add("Retry-After", "0");
                    r.Content = new StringContent("{\"error\":\"rate limited\"}");
                    return r;
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"techs\\\":[\\\"WordPress\\\"]}\"}]},\"id\":\"1\"}")
                };
            });

            using var http = new HttpClient(handler);
            using var client = new BuiltWithClient("test-key", httpClient: http, maxRetries: 3);
            var result = await client.domain_lookup_live("example.com");

            Assert.True(result.Ok);
            Assert.Equal(3, handler.CallCount);
        }

        [Fact]
        public async Task Exhausts_retries_on_429()
        {
            var handler = new MockHandler((req, count) =>
            {
                var r = new HttpResponseMessage((HttpStatusCode)429);
                r.Headers.Add("Retry-After", "0");
                r.Content = new StringContent("{\"error\":\"rate limited\"}");
                return r;
            });

            using var http = new HttpClient(handler);
            using var client = new BuiltWithClient("test-key", httpClient: http, maxRetries: 2);
            var result = await client.domain_lookup_live("example.com");

            Assert.False(result.Ok);
            Assert.Equal("RATE_LIMITED", result.Error.ErrorCode);
            Assert.Equal(3, handler.CallCount); // initial + 2 retries
        }

        [Fact]
        public async Task Retries_on_500()
        {
            var handler = new MockHandler((req, count) =>
            {
                if (count == 1)
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("server error")
                    };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"{}\"}]},\"id\":\"1\"}")
                };
            });

            using var http = new HttpClient(handler);
            using var client = new BuiltWithClient("test-key", httpClient: http, maxRetries: 3);
            var result = await client.domain_lookup("example.com");

            Assert.True(result.Ok);
            Assert.Equal(2, handler.CallCount);
        }
    }
}
