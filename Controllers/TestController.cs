using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LocalAuthService.Data;

namespace LocalAuthService.Controllers;

public class TestController : Controller
{
    private readonly AuthDbContext _db;

    public TestController(AuthDbContext db)
    {
        _db = db;
    }

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
    public async Task<IActionResult> Refresh([FromForm] string refreshToken)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        using var http = new HttpClient();
        var tokenResponse = await http.PostAsync(
            $"{baseUrl}/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"]    = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"]     = "mes-fornitore",
                ["client_secret"] = "mes-secret-123",
            }));

        var json = await tokenResponse.Content.ReadAsStringAsync();
        Response.StatusCode = (int)tokenResponse.StatusCode;
        return Content(json, "application/json");
    }

    [HttpGet("~/test/callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? error,
        [FromQuery] string? error_description)
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

        // Scambia il code per il token (server-side perché mes-fornitore è confidential)
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        using var http = new HttpClient();
        var tokenResponse = await http.PostAsync(
            $"{baseUrl}/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"]    = "authorization_code",
                ["code"]          = code,
                ["redirect_uri"]  = $"{baseUrl}/test/callback",
                ["client_id"]     = "mes-fornitore",
                ["client_secret"] = "mes-secret-123",
            }));

        ViewBag.TokenJson = await tokenResponse.Content.ReadAsStringAsync();
        ViewBag.TokenStatus = (int)tokenResponse.StatusCode;
        return View();
    }
}
