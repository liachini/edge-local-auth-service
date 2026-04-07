using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using LocalAuthService.Data;
using LocalAuthService.Services;
using System.Security.Claims;
using System.Text.Json;
using static OpenIddict.Abstractions.OpenIddictConstants;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;

namespace LocalAuthService.Controllers;

[ApiController]
public class TokenController : ControllerBase
{
    private readonly AuthDbContext _db;
    private readonly OperatingModeDetector _modeDetector;
    private readonly KeycloakAuthService _keycloakAuth;
    private readonly ILogger<TokenController> _logger;

    public TokenController(AuthDbContext db, OperatingModeDetector modeDetector, KeycloakAuthService keycloakAuth, ILogger<TokenController> logger)
    {
        _db = db;
        _modeDetector = modeDetector;
        _keycloakAuth = keycloakAuth;
        _logger = logger;
    }

    [HttpPost("~/connect/token")]
    [Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenIddict request cannot be retrieved.");

        if (request.IsPasswordGrantType())
        {
            return await HandlePasswordGrant(request);
        }

        if (request.IsClientCredentialsGrantType())
        {
            return await HandleClientCredentialsGrant(request);
        }

        if (request.IsAuthorizationCodeGrantType())
        {
            return await HandleAuthorizationCodeGrant(request);
        }

        if (request.IsRefreshTokenGrantType())
        {
            return await HandleRefreshTokenGrant(request);
        }

        return BadRequest(new OpenIddictResponse
        {
            Error = Errors.UnsupportedGrantType,
            ErrorDescription = "The specified grant type is not supported."
        });
    }

    private async Task<IActionResult> HandlePasswordGrant(OpenIddictRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);

        await _modeDetector.CheckAsync();
        _logger.LogWarning("PASSWORD GRANT for {Username} - Keycloak online: {Online}", request.Username, _modeDetector.IsOnline);

        if (_modeDetector.IsOnline)
        {
            // Online: Keycloak e autoritativo
            var loginClientId = _keycloakAuth.GetLoginClientId();
            _logger.LogWarning("Validating with Keycloak clientId: {ClientId}", loginClientId);
            var keycloakOk = await _keycloakAuth.ValidateCredentialsAsync(request.Username!, request.Password!, loginClientId);
            _logger.LogWarning("Keycloak result for {Username}: {Result}", request.Username, keycloakOk);
            if (!keycloakOk)
                return InvalidGrant("Invalid username or password.");

            if (user == null)
            {
                user = new Models.User
                {
                    Username = request.Username!,
                    PasswordHash = "",
                    HasLocalPassword = false,
                    CreatedLocally = false
                };
                _db.Users.Add(user);
                await _db.SaveChangesAsync();
            }
            Console.WriteLine($"✅ Password grant (Keycloak): {user.Username}");
            return BuildTokenResult(user, request);
        }

        // Offline: fallback locale
        if (user == null || !user.Enabled)
            return InvalidGrant("Invalid username or password.");

        if (!user.HasLocalPassword)
            return InvalidGrant("No local password set. Please login online first.");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return InvalidGrant("Invalid username or password.");

        Console.WriteLine($"✅ Password grant (local): {user.Username}");
        return BuildTokenResult(user, request);
    }

    private IActionResult BuildTokenResult(Models.User user, OpenIddictRequest request)
    {
        var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        identity.AddClaim(Claims.Subject, user.Id);
        identity.AddClaim(Claims.Name, user.Username);
        identity.AddClaim(Claims.PreferredUsername, user.Username);

        if (!string.IsNullOrEmpty(user.Email))
        {
            identity.AddClaim(Claims.Email, user.Email);
            identity.AddClaim(Claims.EmailVerified, user.EmailVerified.ToString().ToLower());
        }

        if (!string.IsNullOrEmpty(user.FirstName))
            identity.AddClaim(Claims.GivenName, user.FirstName);

        if (!string.IsNullOrEmpty(user.LastName))
            identity.AddClaim(Claims.FamilyName, user.LastName);

        if (!string.IsNullOrEmpty(user.Roles))
        {
            var roles = JsonSerializer.Deserialize<string[]>(user.Roles);
            if (roles != null)
                foreach (var role in roles)
                    identity.AddClaim(Claims.Role, role);
        }

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());

        foreach (var claim in principal.Claims)
            claim.SetDestinations(GetDestinations(claim));

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private IActionResult InvalidGrant(string description) => Forbid(
        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
        properties: new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
        }));

    private Task<IActionResult> HandleClientCredentialsGrant(OpenIddictRequest request)
    {
        // Per client credentials, OpenIddict ha già validato il client
        // e verificato che abbia permessi per questo grant type
        
        var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        identity.AddClaim(Claims.Subject, $"service-account-{request.ClientId}");
        identity.AddClaim(Claims.Name, $"{request.ClientId} Service Account");
        identity.AddClaim("client_id", request.ClientId!);

        var principal = new ClaimsPrincipal(identity);

        // Gli scopes sono già stati validati da OpenIddict
        principal.SetScopes(request.GetScopes());

        Console.WriteLine($"✅ Client credentials grant: {request.ClientId} authenticated");

        return Task.FromResult<IActionResult>(SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));
    }

    private async Task<IActionResult> HandleRefreshTokenGrant(OpenIddictRequest request)
    {
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        if (result?.Principal == null)
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new Microsoft.AspNetCore.Authentication.AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The refresh token is invalid or has expired."
                }));
        }

        // Verifica che l'utente esista ancora e sia attivo
        var subject = result.Principal.GetClaim(Claims.Subject);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == subject);

        if (user == null || !user.Enabled)
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new Microsoft.AspNetCore.Authentication.AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user account is no longer active."
                }));
        }

        var principal = result.Principal;

        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(GetDestinations(claim));
        }

        Console.WriteLine($"✅ Refresh token grant: renewed for user '{user.Username}'");

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<IActionResult> HandleAuthorizationCodeGrant(OpenIddictRequest request)
    {
        Console.WriteLine($"✅ Authorization code grant: exchanging code for token");
        
        // Usa AuthenticateAsync per recuperare il principal dal code
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        
        if (result?.Principal == null)
        {
            return BadRequest(new OpenIddictResponse
            {
                Error = Errors.InvalidGrant,
                ErrorDescription = "The authorization code is invalid or has expired."
            });
        }

        var claimsPrincipal = result.Principal;
        
        // DEBUG
        Console.WriteLine($"🔍 Claims recuperati dal code:");
        foreach (var claim in claimsPrincipal.Claims)
        {
            Console.WriteLine($"   - {claim.Type}: {claim.Value}");
        }
        
        var subject = claimsPrincipal.GetClaim(Claims.Subject);
        Console.WriteLine($"🔍 Subject: {subject ?? "MISSING!"}");
        
        // Ritorna il principal così com'è (è già autenticato)
        return SignIn(claimsPrincipal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IEnumerable<string> GetDestinations(System.Security.Claims.Claim claim)
    {
        switch (claim.Type)
        {
            case Claims.Subject:
            case Claims.Name:
            case Claims.PreferredUsername:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            case Claims.Email:
            case Claims.EmailVerified:
            case Claims.GivenName:
            case Claims.FamilyName:
                yield return Destinations.IdentityToken;
                yield break;

            case Claims.Role:
                yield return Destinations.AccessToken;
                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}