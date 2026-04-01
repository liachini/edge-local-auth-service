namespace LocalAuthService.Models;

public class MachineConfig
{
    public int Id { get; set; }
    public string MachineId { get; set; } = Environment.MachineName;
    public string LocalRealmName { get; set; } = "local";
    
    public string? JwksKeyId { get; set; }
    public DateTime? JwksGeneratedAt { get; set; }
    
    public string? KeycloakUrl { get; set; }
    public string? KeycloakRealm { get; set; }
    public DateTime? LastKeycloakSync { get; set; }
    public bool KeycloakSyncEnabled { get; set; } = false;
}