using Microsoft.EntityFrameworkCore;
using LocalAuthService.Data;
using LocalAuthService.Models;
using LocalAuthService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database SQLite
var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "LocalAuthService",
    "auth.db"
);

// Assicura che la directory esista
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

builder.Services.AddDbContext<AuthDbContext>(options =>
{
    options.UseSqlite($"Data Source={dbPath}");
    options.UseOpenIddict(); // Per OpenIddict
});

// JWKS Manager (chiave locale)
builder.Services.AddSingleton<JwksManager>();

// OpenIddict OAuth2 Server
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<AuthDbContext>();
    })
    .AddServer(options =>
    {
        // Endpoints
        options.SetTokenEndpointUris("/connect/token")
            .SetAuthorizationEndpointUris("/connect/authorize")
            .SetUserInfoEndpointUris("/connect/userinfo");

        // Scopes
        options.RegisterScopes(
            OpenIddict.Abstractions.OpenIddictConstants.Scopes.OpenId,
            OpenIddict.Abstractions.OpenIddictConstants.Scopes.Email,
            OpenIddict.Abstractions.OpenIddictConstants.Scopes.Profile,
            OpenIddict.Abstractions.OpenIddictConstants.Scopes.OfflineAccess,
            "api.read",
            "api.write"
        );

        // Grant types (OpenIddict 6.0 API)
        options.AllowPasswordFlow()
            .AllowClientCredentialsFlow()
            .AllowAuthorizationCodeFlow()
            .AllowRefreshTokenFlow();

        // JWKS locale — istanziato direttamente perché non ha dipendenze da DI
        var signingKey = new JwksManager().GetOrCreateKey();
        options.AddSigningKey(signingKey);

        // Development encryption
        options.AddDevelopmentEncryptionCertificate();

        // ASP.NET Core integration
        options.UseAspNetCore()
            .EnableTokenEndpointPassthrough()
            .EnableAuthorizationEndpointPassthrough()
            .DisableTransportSecurityRequirement(); // Solo development!
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

// Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
})
.AddCookie("LocalAuth", options =>
{
    options.LoginPath = "/account/login";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

Console.WriteLine($"📂 Database: {dbPath}");

var app = builder.Build();

// Crea/aggiorna database automaticamente
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    
    Console.WriteLine("🔄 Applying migrations...");
    db.Database.Migrate();
    Console.WriteLine("✅ Database ready!");
    
    // Seed dati iniziali (se database vuoto)
    if (!db.Users.Any())
    {
        Console.WriteLine("🌱 Seeding initial data...");
        
        // Admin user
        db.Users.Add(new User
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
            Email = "admin@local.dev",
            FirstName = "Admin",
            LastName = "Local",
            EmailVerified = true,
            Enabled = true,
            Roles = "[\"admin\"]",
            CreatedLocally = true
        });
        
        // Test user (operatore)
        db.Users.Add(new User
        {
            Username = "operator1",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("oper123"),
            Email = "operator1@local.dev",
            FirstName = "Mario",
            LastName = "Rossi",
            EmailVerified = true,
            Enabled = true,
            Roles = "[\"operator\"]",
            CreatedLocally = true
        });
        
        // Test client (HMI)
        db.Clients.Add(new OAuthClient
        {
            ClientId = "hmi-local",
            ClientName = "HMI Local Client",
            IsConfidential = false, // Public client
            ServiceAccountEnabled = false,
            AllowedScopes = "[\"openid\",\"profile\",\"email\"]"
        });
        
        // Machine config
        db.MachineConfigs.Add(new MachineConfig
        {
            MachineId = Environment.MachineName,
            LocalRealmName = "local",
            KeycloakSyncEnabled = false
        });
        
        db.SaveChanges();
        Console.WriteLine("✅ Seed data created!");
        Console.WriteLine("   Users: admin/admin123, operator1/oper123");
    }

    // Registra client OAuth in OpenIddict
    var clientManager = scope.ServiceProvider.GetRequiredService<OpenIddict.Abstractions.IOpenIddictApplicationManager>();
    
    if (await clientManager.FindByClientIdAsync("hmi-local") == null)
    {
        Console.WriteLine("🔧 Registering OAuth clients in OpenIddict...");
        
        await clientManager.CreateAsync(new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
        {
            ClientId = "hmi-local",
            DisplayName = "HMI Local Client",
            ClientType = OpenIddict.Abstractions.OpenIddictConstants.ClientTypes.Public,
            Permissions =
            {
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.Password,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Scopes.Email,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Scopes.Profile,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddict.Abstractions.OpenIddictConstants.Scopes.OfflineAccess,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + "openid"
            }
        });
        
        Console.WriteLine("✅ OAuth client 'hmi-local' registered");

        if (await clientManager.FindByClientIdAsync("mes-fornitore") == null)
    {
        await clientManager.CreateAsync(new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
        {
            ClientId = "mes-fornitore",
            ClientSecret = "mes-secret-123",
            DisplayName = "MES Fornitore",
            ClientType = OpenIddict.Abstractions.OpenIddictConstants.ClientTypes.Confidential,
            ConsentType = OpenIddict.Abstractions.OpenIddictConstants.ConsentTypes.Explicit, // Richiede consent!
            RedirectUris =
            {
                new Uri("http://localhost:7000/callback") // URL di test locale
            },
            Permissions =
            {
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Scopes.Email,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Scopes.Profile,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddict.Abstractions.OpenIddictConstants.Scopes.OfflineAccess,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + "openid"
            }
        });
        
        Console.WriteLine("✅ OAuth client 'mes-fornitore' registered (confidential)");
    }
    
    // Client #3: Office API (Service Account - Client Credentials M2M)
    if (await clientManager.FindByClientIdAsync("office-api") == null)
    {
        await clientManager.CreateAsync(new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
        {
            ClientId = "office-api",
            ClientSecret = "office-secret-456",
            DisplayName = "Office API Service Account",
            ClientType = OpenIddict.Abstractions.OpenIddictConstants.ClientTypes.Confidential,
            Permissions =
            {
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + "openid",
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + "api.read",
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + "api.write"
            }
        });
        
        Console.WriteLine("✅ OAuth client 'office-api' registered (service account)");
    }
    }

    // Aggiorna client esistenti con il permesso OfflineAccess (se mancante)
    var offlinePermission = OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope
        + OpenIddict.Abstractions.OpenIddictConstants.Scopes.OfflineAccess;
    foreach (var clientId in new[] { "hmi-local", "mes-fornitore" })
    {
        var client = await clientManager.FindByClientIdAsync(clientId);
        if (client != null)
        {
            var descriptor = new OpenIddict.Abstractions.OpenIddictApplicationDescriptor();
            await clientManager.PopulateAsync(descriptor, client);
            if (!descriptor.Permissions.Contains(offlinePermission))
            {
                descriptor.Permissions.Add(offlinePermission);
                await clientManager.UpdateAsync(client, descriptor);
                Console.WriteLine($"✅ Added offline_access permission to '{clientId}'");
            }
        }
    }
}

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication(); 
app.UseAuthorization();
app.MapControllers();

Console.WriteLine("🚀 LocalAuthService ready!");
app.Run();