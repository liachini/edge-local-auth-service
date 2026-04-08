namespace LocalAuthService.Models;

/// <summary>
/// Credenziali per servizi legacy non-OAuth2 (CLI, ERP, Database)
/// Salvate criptate (AES-256) nel database locale.
/// Non sincronizzate con Keycloak (specifiche della macchina).
/// </summary>
public class LegacyServiceCredential
{
    public int Id { get; set; }

    /// <summary>
    /// Identificatore univoco del servizio legacy (es: "cli-legacy", "erp-db", "crm-api")
    /// </summary>
    public required string ServiceId { get; set; }

    /// <summary>
    /// Username per il servizio legacy
    /// </summary>
    public required string Username { get; set; }

    /// <summary>
    /// Password criptata con AES-256 (NEVER plaintext)
    /// </summary>
    public required string EncryptedPassword { get; set; }

    /// <summary>
    /// Tipo di servizio legacy (cli, database, api, erp, etc)
    /// </summary>
    public required string ServiceType { get; set; }

    /// <summary>
    /// Nome della macchina (per documentazione/audit)
    /// </summary>
    public string MachineName { get; set; } = Environment.MachineName;

    /// <summary>
    /// Data creazione
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Data ultimo accesso (per audit)
    /// </summary>
    public DateTime? LastAccessedAt { get; set; }

    /// <summary>
    /// Chi ha acceduto per ultimo (per audit)
    /// </summary>
    public string? LastAccessedBy { get; set; }

    /// <summary>
    /// Se false, credenziale è stata revocata
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Descrizione opzionale (es: "Password per ERP Sap sulla Falegnameria")
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Client OAuth2 autorizzati a leggere questa credenziale (JSON array di client_ids)
    /// Se null/empty: tutti con ruolo "legacy-password-reader" possono leggerla
    /// Se compilato: solo questi client (+ admin) possono leggerla
    /// Esempio: ["cli-simulator", "erp-simulator"]
    /// </summary>
    public string? AllowedClientIds { get; set; }
}
