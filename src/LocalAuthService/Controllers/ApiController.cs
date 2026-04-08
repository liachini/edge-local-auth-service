using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace LocalAuthService.Controllers;

[ApiController]
public class ApiController : ControllerBase
{
    private readonly IOpenIddictApplicationManager _clientManager;

    public ApiController(IOpenIddictApplicationManager clientManager)
    {
        _clientManager = clientManager;
    }

    [HttpGet("~/api/clients")]
    public async Task<IActionResult> GetClients()
    {
        var result = new List<object>();
        await foreach (var app in _clientManager.ListAsync())
        {
            var descriptor = new OpenIddictApplicationDescriptor();
            await _clientManager.PopulateAsync(descriptor, app);

            var grants = new List<string>();
            if (descriptor.Permissions.Contains(Permissions.GrantTypes.Password)) grants.Add("password");
            if (descriptor.Permissions.Contains(Permissions.GrantTypes.AuthorizationCode)) grants.Add("authorization_code");
            if (descriptor.Permissions.Contains(Permissions.GrantTypes.ClientCredentials)) grants.Add("client_credentials");

            result.Add(new
            {
                clientId = descriptor.ClientId,
                displayName = descriptor.DisplayName,
                type = descriptor.ClientType,
                grantTypes = grants
            });
        }
        return Ok(result);
    }

    /// <summary>
    /// Endpoint protetto — richiede un Bearer token valido.
    /// Restituisce i claims visti lato server dopo la validazione.
    /// </summary>
    [HttpGet("~/api/me")]
    [Authorize]
    public IActionResult Me()
    {
        var claims = User.Claims
            .Select(c => new { type = c.Type, value = c.Value })
            .ToList();

        return Ok(new
        {
            authenticated = true,
            subject       = User.Claims.FirstOrDefault(c => c.Type == Claims.Subject)?.Value,
            username      = User.Claims.FirstOrDefault(c => c.Type == Claims.Name)?.Value,
            email         = User.Claims.FirstOrDefault(c => c.Type == Claims.Email)?.Value,
            roles         = User.Claims.Where(c => c.Type == Claims.Role).Select(c => c.Value).ToList(),
            scope         = User.Claims.FirstOrDefault(c => c.Type == "scope")?.Value,
            claims
        });
    }
}
