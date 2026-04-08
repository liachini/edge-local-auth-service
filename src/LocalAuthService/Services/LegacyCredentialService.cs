using LocalAuthService.Data;
using LocalAuthService.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace LocalAuthService.Services;

/// <summary>
/// Gestisce salvataggio, recupero e revoca di credenziali legacy
/// Con encryption, audit logging e autorizzazione
/// </summary>
public class LegacyCredentialService
{
    private readonly AuthDbContext _db;
    private readonly LegacyCredentialEncryptionService _encryption;
    private readonly IHttpContextAccessor _httpContext;
    private readonly ILogger<LegacyCredentialService> _logger;

    public LegacyCredentialService(
        AuthDbContext db,
        LegacyCredentialEncryptionService encryption,
        IHttpContextAccessor httpContext,
        ILogger<LegacyCredentialService> logger)
    {
        _db = db;
        _encryption = encryption;
        _httpContext = httpContext;
        _logger = logger;
    }

    /// <summary>
    /// Salva una credenziale legacy (encriptata)
    /// </summary>
    public async Task SaveCredentialAsync(
        string serviceId,
        string username,
        string plainPassword,
        string? description = null,
        string? allowedClientIds = null)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("ServiceId non può essere vuoto");

        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username non può essere vuoto");

        if (string.IsNullOrWhiteSpace(plainPassword))
            throw new ArgumentException("Password non può essere vuota");

        try
        {
            var encrypted = _encryption.Encrypt(plainPassword);

            // Verifica se credenziale già esiste
            var existing = await _db.LegacyServiceCredentials
                .FirstOrDefaultAsync(c => c.ServiceId == serviceId);

            if (existing != null)
            {
                // Update
                existing.Username = username;
                existing.EncryptedPassword = encrypted;
                existing.Description = description;
                existing.AllowedClientIds = allowedClientIds;
                existing.IsActive = true;
                existing.CreatedAt = DateTime.UtcNow;

                _db.LegacyServiceCredentials.Update(existing);

                _logger.LogInformation(
                    "✅ Credenziale legacy aggiornata: {ServiceId} (user={Username})",
                    serviceId, username);
            }
            else
            {
                // Insert
                var credential = new LegacyServiceCredential
                {
                    ServiceId = serviceId,
                    Username = username,
                    EncryptedPassword = encrypted,
                    Description = description,
                    AllowedClientIds = allowedClientIds,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                _db.LegacyServiceCredentials.Add(credential);

                _logger.LogInformation(
                    "✅ Credenziale legacy salvata: {ServiceId} (user={Username})",
                    serviceId, username);
            }

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Errore salvataggio credenziale legacy {ServiceId}", serviceId);
            throw;
        }
    }

    /// <summary>
    /// Restituisce password decriptata SOLO se autorizzato
    /// ⚠️ Require Bearer token + authorization check
    /// </summary>
    public async Task<string?> GetPlainPasswordAsync(string serviceId)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("ServiceId non può essere vuoto");

        try
        {
            // 🔒 Autorizzazione: solo legacy-password-reader role
            var context = _httpContext.HttpContext;
            if (context?.User == null)
                throw new UnauthorizedAccessException("Utente non autenticato");

            // ⭐ Cerca il ruolo sia in forma OpenIddict ("role") che System.Security ("http://...")
            var userRoles = context.User.FindAll(c =>
                c.Type == ClaimTypes.Role ||
                c.Type == "role"  // OpenIddict form
            ).Select(c => c.Value).ToList();
            var clientId = context.User.FindFirst("client_id")?.Value;
            var currentUser = context.User.FindFirst("sub")?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.User.FindFirst(ClaimTypes.Name)?.Value
                ?? "unknown";

            var hasLegacyPasswordReaderRole = userRoles.Contains("legacy-password-reader");

            if (!hasLegacyPasswordReaderRole)
            {
                _logger.LogWarning(
                    "❌ DENIED: Tentativo accesso credenziale legacy {ServiceId} da {User} (client={ClientId}, roles={Roles})",
                    serviceId, currentUser, clientId ?? "none", string.Join(",", userRoles));

                throw new UnauthorizedAccessException(
                    $"Ruolo 'legacy-password-reader' richiesto. Ruoli attuali: {string.Join(",", userRoles)}");
            }

            // Recupera credenziale dal DB
            var credential = await _db.LegacyServiceCredentials
                .FirstOrDefaultAsync(c => c.ServiceId == serviceId && c.IsActive);

            if (credential == null)
            {
                _logger.LogWarning(
                    "⚠️  Credenziale legacy non trovata: {ServiceId}",
                    serviceId);
                return null;
            }

            // ⭐ Controllo aggiuntivo: se credenziale ha AllowedClientIds, solo quei client possono leggerla
            if (!string.IsNullOrWhiteSpace(credential.AllowedClientIds))
            {
                var allowedClients = JsonSerializer.Deserialize<string[]>(credential.AllowedClientIds) ?? Array.Empty<string>();
                var isAuthorized = allowedClients.Contains(clientId) || userRoles.Contains("admin");

                if (!isAuthorized)
                {
                    _logger.LogWarning(
                        "❌ DENIED: Client {ClientId} non è nella lista AllowedClientIds per {ServiceId}",
                        clientId, serviceId);

                    throw new UnauthorizedAccessException(
                        $"Client '{clientId}' non è autorizzato ad accedere a credenziale '{serviceId}'");
                }
            }

            // 🔓 Decripta
            var plainPassword = _encryption.Decrypt(credential.EncryptedPassword);

            // 📋 Audit log
            credential.LastAccessedAt = DateTime.UtcNow;
            credential.LastAccessedBy = currentUser;
            _db.LegacyServiceCredentials.Update(credential);
            await _db.SaveChangesAsync();

            _logger.LogWarning(
                "⚠️  AUDIT: Password decriptata per {ServiceId} " +
                "da {User} alle {Time}",
                serviceId, currentUser, DateTime.UtcNow);

            return plainPassword;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Errore retrieval credenziale legacy {ServiceId}", serviceId);
            throw;
        }
    }

    /// <summary>
    /// Revoca una credenziale (mark as inactive)
    /// </summary>
    public async Task RevokeCredentialAsync(string serviceId)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("ServiceId non può essere vuoto");

        try
        {
            var credential = await _db.LegacyServiceCredentials
                .FirstOrDefaultAsync(c => c.ServiceId == serviceId);

            if (credential != null)
            {
                credential.IsActive = false;
                credential.EncryptedPassword = ""; // Clear
                _db.LegacyServiceCredentials.Update(credential);
                await _db.SaveChangesAsync();

                _logger.LogWarning(
                    "🔒 Credenziale legacy revocata: {ServiceId}",
                    serviceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Errore revoca credenziale legacy {ServiceId}", serviceId);
            throw;
        }
    }

    /// <summary>
    /// Lista tutte le credenziali legacy (SOLO metadata, senza password)
    /// </summary>
    public async Task<List<LegacyServiceCredentialInfo>> ListCredentialsAsync()
    {
        try
        {
            var credentials = await _db.LegacyServiceCredentials
                .Where(c => c.IsActive)
                .Select(c => new LegacyServiceCredentialInfo
                {
                    ServiceId = c.ServiceId,
                    Username = c.Username,
                    Description = c.Description,
                    CreatedAt = c.CreatedAt,
                    LastAccessedAt = c.LastAccessedAt,
                    LastAccessedBy = c.LastAccessedBy,
                    IsActive = c.IsActive
                })
                .ToListAsync();

            return credentials;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Errore listing credenziali legacy");
            throw;
        }
    }
}

/// <summary>
/// Info credenziale (SENZA password)
/// </summary>
public class LegacyServiceCredentialInfo
{
    public string ServiceId { get; set; } = "";
    public string Username { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public string? LastAccessedBy { get; set; }
    public bool IsActive { get; set; }
}
