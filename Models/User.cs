namespace LocalAuthService.Models;

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public bool EmailVerified { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Roles { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool CreatedLocally { get; set; } = true;
    
    public string? KeycloakUserId { get; set; }
    public DateTime? LastSyncToKeycloak { get; set; }
    public DateTime? LastSyncFromKeycloak { get; set; }
}