using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using LocalAuthService.Data;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;
using LocalAuthService.Models;

namespace LocalAuthService.Controllers;

public class AuthorizationController : Controller
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictAuthorizationManager _authorizationManager;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly AuthDbContext _db;

    public AuthorizationController(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictAuthorizationManager authorizationManager,
        IOpenIddictScopeManager scopeManager,
        AuthDbContext db)
    {
        _applicationManager = applicationManager;
        _authorizationManager = authorizationManager;
        _scopeManager = scopeManager;
        _db = db;
    }

    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest() 
            ?? throw new InvalidOperationException("OpenIddict request cannot be retrieved.");

        Console.WriteLine($"🔐 Authorization request from client: {request.ClientId}");

        // Per questo spike, simuliamo che l'utente "admin" sia già loggato
        // In produzione vera, qui ci sarebbe redirect a login page se non autenticato
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == "admin");
        
        if (user == null || !user.Enabled)
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.AccessDenied,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "User not found or disabled"
                }));
        }

        // Recupera application
        var application = await _applicationManager.FindByClientIdAsync(request.ClientId ?? "");
        if (application == null)
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidClient,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Client application not found"
                }));
        }

        var applicationName = await _applicationManager.GetDisplayNameAsync(application);
        var consentType = await _applicationManager.GetConsentTypeAsync(application);

        // Controlla se consent è già stato dato (dalla URL dopo Allow)
        var consentGranted = request.GetParameter("consent_granted")?.Value?.ToString();

        // Controlla se esiste già un consent salvato nella nostra tabella
        var scopesJson = System.Text.Json.JsonSerializer.Serialize(request.GetScopes().ToArray());
        var existingConsent = await _db.UserConsents
            .FirstOrDefaultAsync(c => 
                c.UserId == user.Id && 
                c.ClientId == request.ClientId && 
                !c.IsRevoked);

        // Se richiede consent esplicito E non c'è autorizzazione permanente E non è appena stato dato
        if (consentType == ConsentTypes.Explicit && existingConsent == null && consentGranted != "true")        {
            Console.WriteLine($"📋 Showing consent screen for: {applicationName}");
            
            return View("Consent", new ConsentViewModel
            {
                ApplicationName = applicationName ?? request.ClientId ?? "Unknown",
                Scope = string.Join(" ", request.GetScopes()),
                ClientId = request.ClientId,
                RedirectUri = request.RedirectUri,
                ResponseType = request.ResponseType,
                State = request.State,
                Nonce = request.Nonce,
                CodeChallenge = request.CodeChallenge,
                CodeChallengeMethod = request.CodeChallengeMethod,
                RequestedScopes = string.Join(" ", request.GetScopes())
            });
        }

        if (existingConsent != null)
        {
            Console.WriteLine($"✅ Using existing consent for: {applicationName}");
        }
        else
        {
            Console.WriteLine($"✅ Implicit consent for: {applicationName}");
        }

        return await IssueAuthorizationCode(user, request);
    }

    [HttpPost("~/connect/authorize/accept")]
    public IActionResult Accept(
        [FromForm] string client_id,
        [FromForm] string redirect_uri,
        [FromForm] string response_type,
        [FromForm] string scope,
        [FromForm] string? state = null,
        [FromForm] string? nonce = null,
        [FromForm] string? code_challenge = null,
        [FromForm] string? code_challenge_method = null)
    {
        Console.WriteLine($"✅ User accepted consent for: {client_id}");

        // Costruisci query string con tutti i parametri
        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = client_id,
            ["redirect_uri"] = redirect_uri,
            ["response_type"] = response_type,
            ["scope"] = scope,
            ["consent_granted"] = "true" // Flag che indica consent accettato
        };

        if (!string.IsNullOrEmpty(state))
            queryParams["state"] = state;

        if (!string.IsNullOrEmpty(nonce))
            queryParams["nonce"] = nonce;

        if (!string.IsNullOrEmpty(code_challenge))
        {
            queryParams["code_challenge"] = code_challenge;
            queryParams["code_challenge_method"] = code_challenge_method ?? "";
        }

        var queryString = string.Join("&", 
            queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        // Redirect a /connect/authorize con consent granted
        return Redirect($"/connect/authorize?{queryString}");
    }

 private async Task<IActionResult> IssueAuthorizationCode(Models.User user, OpenIddictRequest request)
    {
        // Crea identity per il token
         var identity = new ClaimsIdentity(
            authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.AddClaim(new Claim(Claims.Subject, user.Id));
        identity.AddClaim(new Claim(Claims.Name, user.Username));
        identity.AddClaim(new Claim(Claims.PreferredUsername, user.Username));

        if (!string.IsNullOrEmpty(user.Email))
        {
            identity.AddClaim(new Claim(Claims.Email, user.Email));
            identity.AddClaim(new Claim(Claims.EmailVerified, user.EmailVerified.ToString().ToLower()));
        }

        if (!string.IsNullOrEmpty(user.FirstName))
            identity.AddClaim(new Claim(Claims.GivenName, user.FirstName));

        if (!string.IsNullOrEmpty(user.LastName))
            identity.AddClaim(new Claim(Claims.FamilyName, user.LastName));

        var principal = new ClaimsPrincipal(identity);

        // Imposta scopes richiesti
        principal.SetScopes(request.GetScopes());

        // Imposta resources (audiences)
        principal.SetResources(await _scopeManager.ListResourcesAsync(principal.GetScopes()).ToListAsync());

        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(GetDestinations(claim));
        }

        Console.WriteLine($"🎫 Issuing authorization code for user: {user.Username}");

        // Salva consent PRIMA di SignIn (in background task per non bloccare)
        var consentGranted = request.GetParameter("consent_granted")?.Value?.ToString();
        if (consentGranted == "true")
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _db.Database.BeginTransaction();
                    
                    var existingConsent = await _db.UserConsents
                        .FirstOrDefaultAsync(c => 
                            c.UserId == user.Id && 
                            c.ClientId == request.ClientId && 
                            !c.IsRevoked);

                    if (existingConsent == null)
                    {
                        var scopesJson = System.Text.Json.JsonSerializer.Serialize(request.GetScopes().ToArray());
                        var consent = new UserConsent
                        {
                            UserId = user.Id,
                            ClientId = request.ClientId!,
                            Scopes = scopesJson,
                            GrantedAt = DateTime.UtcNow
                        };
                        
                        _db.UserConsents.Add(consent);
                        await _db.SaveChangesAsync();
                        await scope.CommitAsync();
                        
                        Console.WriteLine($"✅ Consent saved in background");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Could not save consent: {ex.Message}");
                }
            });
        }
        
        // DEBUG
        Console.WriteLine($"🔍 Identity.IsAuthenticated = {identity.IsAuthenticated}");
        Console.WriteLine($"🔍 Identity.AuthenticationType = {identity.AuthenticationType}");
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        switch (claim.Type)
        {
            case Claims.Subject:
            case Claims.Name:
            case Claims.PreferredUsername:
                // Questi vanno sia in access token che in authorization code
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            case Claims.Email:
            case Claims.EmailVerified:
            case Claims.GivenName:
            case Claims.FamilyName:
                // Questi solo in identity token
                yield return Destinations.IdentityToken;
                yield break;

            case Claims.Role:
                // Ruoli in access token
                yield return Destinations.AccessToken;
                yield break;

            default:
                // Altri claims in access token
                yield return Destinations.AccessToken;
                yield break;
        }
    }

    // Helper per creare principal autenticato per l'autorizzazione
    private ClaimsPrincipal CreateAuthPrincipal(Models.User user)
    {
        var identity = new ClaimsIdentity(
            authenticationType: "AuthorizationManager",
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.AddClaim(new Claim(Claims.Subject, user.Id));
        identity.AddClaim(new Claim(Claims.Name, user.Username));

        return new ClaimsPrincipal(identity);
    }
}

public class ConsentViewModel
{
    public string ApplicationName { get; set; } = "";
    public string Scope { get; set; } = "";
    
    // Parametri OAuth2 da preservare
    public string? ClientId { get; set; }
    public string? RedirectUri { get; set; }
    public string? ResponseType { get; set; }
    public string? State { get; set; }
    public string? Nonce { get; set; }
    public string? CodeChallenge { get; set; }
    public string? CodeChallengeMethod { get; set; }
    public string RequestedScopes { get; set; } = "";
}