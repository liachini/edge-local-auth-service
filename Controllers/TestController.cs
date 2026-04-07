using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LocalAuthService.Data;
using System.Collections.Concurrent;

namespace LocalAuthService.Controllers;

public class TestController : Controller
{
    private readonly AuthDbContext _db;

    // Cache in-memory per le credenziali client (TTL implicito: una sola uso)
    private static readonly ConcurrentDictionary<string, (string ClientId, string Secret)> _clientStateCache = new();

    public TestController(AuthDbContext db)
    {
        _db = db;
    }

    [HttpPost("~/test/save-client-state")]
    public IActionResult SaveClientState([FromBody] SaveClientStateRequest req)
    {
        var state = Guid.NewGuid().ToString("N");
        _clientStateCache[state] = (req.ClientId, req.Secret ?? "");
        return Ok(new { state });
    }

    public record SaveClientStateRequest(string ClientId, string? Secret);

    [HttpGet("~/test")]
    public IActionResult Index()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        ViewBag.BaseUrl = baseUrl;
        return View();
    }

    [HttpPost("~/test/revoke-consent")]
    public async Task<IActionResult> RevokeConsent([FromForm] string clientId)
    {
        var consents = await _db.UserConsents
            .Where(c => c.ClientId == clientId && !c.IsRevoked)
            .ToListAsync();

        foreach (var c in consents)
            c.IsRevoked = true;

        await _db.SaveChangesAsync();

        Console.WriteLine($"🗑️ Revoked {consents.Count} consent(s) for client '{clientId}'");

        return Redirect("/test");
    }

    [HttpPost("~/test/refresh")]
    public async Task<IActionResult> Refresh([FromForm] string refreshToken, [FromForm] string? clientId, [FromForm] string? clientSecret)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        using var http = new HttpClient();
        var refreshParams = new Dictionary<string, string>
        {
            ["grant_type"]    = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"]     = clientId ?? "",
        };
        if (!string.IsNullOrEmpty(clientSecret))
            refreshParams["client_secret"] = clientSecret;

        var tokenResponse = await http.PostAsync(
            $"{baseUrl}/connect/token",
            new FormUrlEncodedContent(refreshParams));

        var json = await tokenResponse.Content.ReadAsStringAsync();
        Response.StatusCode = (int)tokenResponse.StatusCode;
        return Content(json, "application/json");
    }

    [HttpGet("~/test/callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? error,
        [FromQuery] string? error_description,
        [FromQuery] string? state)
    {
        if (!string.IsNullOrEmpty(error))
        {
            ViewBag.Error = error;
            ViewBag.ErrorDescription = error_description;
            return View();
        }

        if (string.IsNullOrEmpty(code))
        {
            ViewBag.Error = "no_code";
            ViewBag.ErrorDescription = "Nessun authorization code ricevuto.";
            return View();
        }

        ViewBag.Code = code;

        // Recupera credenziali client dalla cache (one-time use)
        string clientId = "", clientSecret = "";
        if (!string.IsNullOrEmpty(state) && _clientStateCache.TryRemove(state, out var creds))
        {
            clientId = creds.ClientId;
            clientSecret = creds.Secret;
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        using var http = new HttpClient();
        var tokenParams = new Dictionary<string, string>
        {
            ["grant_type"]   = "authorization_code",
            ["code"]         = code,
            ["redirect_uri"] = $"{baseUrl}/test/callback",
            ["client_id"]    = clientId,
        };
        if (!string.IsNullOrEmpty(clientSecret))
            tokenParams["client_secret"] = clientSecret;

        var tokenResponse = await http.PostAsync(
            $"{baseUrl}/connect/token",
            new FormUrlEncodedContent(tokenParams));

        ViewBag.TokenJson = await tokenResponse.Content.ReadAsStringAsync();
        ViewBag.TokenStatus = (int)tokenResponse.StatusCode;
        ViewBag.ClientId = clientId;
        return View();
    }
}
