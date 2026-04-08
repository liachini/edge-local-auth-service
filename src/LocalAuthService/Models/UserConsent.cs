namespace LocalAuthService.Models;

public class UserConsent
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string ClientId { get; set; }
    public required string Scopes { get; set; } // JSON array
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }  // null = nessuna scadenza
    public bool IsRevoked { get; set; } = false;
}