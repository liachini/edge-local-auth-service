using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using LocalAuthService.Data;
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

    public TokenController(AuthDbContext db)
    {
        _db = db;
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

        return BadRequest(new OpenIddictResponse
        {
            Error = Errors.UnsupportedGrantType,
            ErrorDescription = "The specified grant type is not supported."
        });
    }

    private async Task<IActionResult> HandlePasswordGrant(OpenIddictRequest request)
    {
        // Trova utente
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username);

        if (user == null || !user.Enabled)
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new Microsoft.AspNetCore.Authentication.AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid username or password."
                }));
        }

        // Verifica password
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new Microsoft.AspNetCore.Authentication.AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid username or password."
                }));
        }

        // Crea claims
        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

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

        // Aggiungi ruoli
        if (!string.IsNullOrEmpty(user.Roles))
        {
            var roles = JsonSerializer.Deserialize<string[]>(user.Roles);
            if (roles != null)
            {
                foreach (var role in roles)
                {
                    identity.AddClaim(Claims.Role, role);
                }
            }
        }

        var principal = new ClaimsPrincipal(identity);

        principal.SetScopes(new[]
        {
            Scopes.OpenId,
            Scopes.Email,
            Scopes.Profile
        });

        Console.WriteLine($"✅ Password grant: {user.Username} authenticated");

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

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
}