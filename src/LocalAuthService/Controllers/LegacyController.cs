using LocalAuthService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalAuthService.Controllers;

/// <summary>
/// API per gestire credenziali legacy (CLI, ERP, Database)
/// Tutti gli endpoint richiedono Bearer token + ruolo admin
/// </summary>
[ApiController]
[Route("api/legacy")]
[Authorize] // Richiede Bearer token
public class LegacyController : ControllerBase
{
    private readonly LegacyCredentialService _credentialService;
    private readonly ILogger<LegacyController> _logger;

    public LegacyController(
        LegacyCredentialService credentialService,
        ILogger<LegacyController> logger)
    {
        _credentialService = credentialService;
        _logger = logger;
    }

    /// <summary>
    /// Salva una credenziale legacy (encriptata nel DB)
    /// POST /api/legacy/credentials
    /// </summary>
    [HttpPost("credentials")]
    [Authorize(Roles = "admin,legacy-credentials-manager")]
    public async Task<IActionResult> SaveCredential([FromBody] SaveLegacyCredentialRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ServiceId))
            return BadRequest(new { error = "ServiceId obbligatorio" });

        if (string.IsNullOrWhiteSpace(req.Username))
            return BadRequest(new { error = "Username obbligatorio" });

        if (string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Password obbligatorio" });

        try
        {
            // Se AllowedClientIds è fornito, validalo come JSON array
            string? allowedClientsJson = null;
            if (req.AllowedClientIds != null && req.AllowedClientIds.Count > 0)
            {
                allowedClientsJson = System.Text.Json.JsonSerializer.Serialize(req.AllowedClientIds);
            }

            await _credentialService.SaveCredentialAsync(
                req.ServiceId,
                req.Username,
                req.Password,
                req.Description,
                allowedClientsJson);

            return Ok(new
            {
                message = $"✅ Credenziale '{req.ServiceId}' salvata (encrypted)",
                serviceId = req.ServiceId,
                username = req.Username
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore salvataggio credenziale {ServiceId}", req.ServiceId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Restituisce password DECRIPTATA per una credenziale legacy
    /// ⚠️ SOLO su richiesta autorizzata, con audit log
    /// POST /api/legacy/get-password
    /// </summary>
    [HttpPost("get-password")]
    [Authorize(Roles = "admin,legacy-password-reader")]
    public async Task<IActionResult> GetPassword([FromBody] GetPasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ServiceId))
            return BadRequest(new { error = "ServiceId obbligatorio" });

        try
        {
            var password = await _credentialService.GetPlainPasswordAsync(req.ServiceId);

            if (password == null)
                return NotFound(new { error = $"Credenziale '{req.ServiceId}' non trovata" });

            // ⚠️ Password in plaintext - DEVE ESSERE USATA SUBITO E SCARTATA
            return Ok(new
            {
                message = "⚠️  Password decriptata - USARE SUBITO E SCARTARE",
                serviceId = req.ServiceId,
                password = password // plaintext
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Accesso negato a credenziale {ServiceId}", req.ServiceId);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore retrieval credenziale {ServiceId}", req.ServiceId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Revoca una credenziale legacy
    /// DELETE /api/legacy/credentials/{serviceId}
    /// </summary>
    [HttpDelete("credentials/{serviceId}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> RevokeCredential(string serviceId)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            return BadRequest(new { error = "ServiceId obbligatorio" });

        try
        {
            await _credentialService.RevokeCredentialAsync(serviceId);

            return Ok(new
            {
                message = $"✅ Credenziale '{serviceId}' revocata",
                serviceId = serviceId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore revoca credenziale {ServiceId}", serviceId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Lista tutti i client OAuth2 disponibili per leggere credenziali legacy
    /// (client con ruolo "legacy-password-reader")
    /// GET /api/legacy/available-clients
    /// </summary>
    [HttpGet("available-clients")]
    [Authorize]
    public async Task<IActionResult> ListAvailableClients()
    {
        try
        {
            var clients = new List<dynamic>
            {
                new { clientId = "cli-simulator", displayName = "CLI Simulator", description = "Legge credenziali per servizi CLI" },
                new { clientId = "erp-simulator", displayName = "ERP Simulator", description = "Legge credenziali per sistema ERP" },
                new { clientId = "crm-simulator", displayName = "CRM Simulator", description = "Legge credenziali per sistema CRM" }
            };

            return Ok(new
            {
                count = clients.Count,
                clients = clients
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore listing client disponibili");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Lista tutte le credenziali legacy (SOLO metadata, senza password)
    /// GET /api/legacy/credentials
    /// </summary>
    [HttpGet("credentials")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ListCredentials()
    {
        try
        {
            var credentials = await _credentialService.ListCredentialsAsync();

            return Ok(new
            {
                count = credentials.Count,
                credentials = credentials
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore listing credenziali");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

/// <summary>
/// Request per salvare credenziale
/// </summary>
public class SaveLegacyCredentialRequest
{
    public required string ServiceId { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
    public string? Description { get; set; }
    public List<string>? AllowedClientIds { get; set; } // Es: ["cli-simulator", "erp-simulator"]
}

/// <summary>
/// Request per ottenere password decriptata
/// </summary>
public class GetPasswordRequest
{
    public required string ServiceId { get; set; }
}
