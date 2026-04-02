using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace LocalAuthService.Controllers;

[ApiController]
public class ApiController : ControllerBase
{
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
