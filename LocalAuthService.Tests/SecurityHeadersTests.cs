using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace LocalAuthService.Tests;

/// <summary>
/// Security Fix 1.4: HTTPS enforcement and security response headers.
/// Every response must include headers that protect against common web attacks.
/// </summary>
public class SecurityHeadersTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SecurityHeadersTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Development");
            })
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
    }

    [Fact]
    public async Task Response_IncludesXFrameOptions_Deny()
    {
        var response = await _client.GetAsync("/");

        Assert.True(
            response.Headers.Contains("X-Frame-Options"),
            "X-Frame-Options header missing — clickjacking protection required");

        var value = response.Headers.GetValues("X-Frame-Options").First();
        Assert.Equal("DENY", value);
    }

    [Fact]
    public async Task Response_IncludesXContentTypeOptions_NoSniff()
    {
        var response = await _client.GetAsync("/");

        Assert.True(
            response.Headers.Contains("X-Content-Type-Options"),
            "X-Content-Type-Options header missing — MIME sniffing protection required");

        var value = response.Headers.GetValues("X-Content-Type-Options").First();
        Assert.Equal("nosniff", value);
    }

    [Fact]
    public async Task Response_IncludesReferrerPolicy()
    {
        var response = await _client.GetAsync("/");

        Assert.True(
            response.Headers.Contains("Referrer-Policy"),
            "Referrer-Policy header missing");

        var value = response.Headers.GetValues("Referrer-Policy").First();
        Assert.Equal("strict-origin-when-cross-origin", value);
    }

    [Fact]
    public async Task LegacyApiResponse_IncludesNoCacheHeaders()
    {
        // Credential endpoints must never be cached
        var response = await _client.GetAsync("/api/legacy/credentials");

        // 401 Unauthorized is expected (not authenticated), but headers must still be present
        Assert.True(
            response.Headers.Contains("Cache-Control") ||
            response.Content.Headers.Contains("Cache-Control"),
            "Cache-Control header missing on /api/legacy endpoint");
    }
}