using System;
using System.Text.Json;
using System.Threading.Tasks;
using BuiltWith.Sdk;

class Program
{
    static async Task Main(string[] args)
    {
        var apiKey = Environment.GetEnvironmentVariable("BUILTWITH_API_KEY") ?? "YOUR_API_KEY";
        using var client = new BuiltWithClient(apiKey);

        // ── Example 1: domain_lookup_live ────────────────────────────────────
        Console.WriteLine("--- domain_lookup_live ---");
        var live = await client.domain_lookup_live("spotify.com");
        if (live.Ok)
            Console.WriteLine($"Technologies: {JsonSerializer.Serialize(live.Data).Substring(0, Math.Min(500, JsonSerializer.Serialize(live.Data).Length))}");
        else
            Console.WriteLine($"Error: {live.Error.Message}");

        // ── Example 2: domain_lookup ─────────────────────────────────────────
        Console.WriteLine("\n--- domain_lookup ---");
        var lookup = await client.domain_lookup("spotify.com");
        if (lookup.Ok)
            Console.WriteLine($"Domain data: {JsonSerializer.Serialize(lookup.Data).Substring(0, Math.Min(500, JsonSerializer.Serialize(lookup.Data).Length))}");
        else
            Console.WriteLine($"Error: {lookup.Error.Message}");

        // ── Example 3: trust ─────────────────────────────────────────────────
        Console.WriteLine("\n--- trust ---");
        var trustResult = await client.trust("spotify.com");
        if (trustResult.Ok)
            Console.WriteLine($"Trust score: {JsonSerializer.Serialize(trustResult.Data).Substring(0, Math.Min(500, JsonSerializer.Serialize(trustResult.Data).Length))}");
        else
            Console.WriteLine($"Error: {trustResult.Error.Message}");

        // ── Example 4: company_to_url ────────────────────────────────────────
        Console.WriteLine("\n--- company_to_url ---");
        var company = await client.company_to_url("Spotify");
        if (company.Ok)
            Console.WriteLine($"Company domains: {JsonSerializer.Serialize(company.Data).Substring(0, Math.Min(500, JsonSerializer.Serialize(company.Data).Length))}");
        else
            Console.WriteLine($"Error: {company.Error.Message}");
    }
}
