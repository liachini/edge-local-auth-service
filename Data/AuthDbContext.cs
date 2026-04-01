using Microsoft.EntityFrameworkCore;
using LocalAuthService.Models;

namespace LocalAuthService.Data;

public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) 
        : base(options) 
    { }

    public DbSet<User> Users => Set<User>();
    public DbSet<OAuthClient> Clients => Set<OAuthClient>();
    public DbSet<MachineConfig> MachineConfigs => Set<MachineConfig>();
    public DbSet<UserConsent> UserConsents => Set<UserConsent>(); // ← AGGIUNGI

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PasswordHash).IsRequired();
        });

        // OAuthClient configuration
        modelBuilder.Entity<OAuthClient>(entity =>
        {
            entity.HasKey(e => e.ClientId);
            entity.Property(e => e.ClientName).IsRequired();
        });

        // MachineConfig configuration
        modelBuilder.Entity<MachineConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        // UserConsent configuration
        modelBuilder.Entity<UserConsent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.ClientId });
        });
    }
}