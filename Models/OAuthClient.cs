namespace LocalAuthService.Models;

public class OAuthClient
{
    public string ClientId { get; set; } = null!;
    public required string ClientName { get; set; }
    public string? ClientSecretHash { get; set; }
    public bool IsConfidential { get; set; }
    public bool ServiceAccountEnabled { get; set; }
    public string? RedirectUris { get; set; }
    public string? AllowedScopes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}