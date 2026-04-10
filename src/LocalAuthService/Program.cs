using Microsoft.EntityFrameworkCore;
using LocalAuthService.Data;
using LocalAuthService.Models;
using LocalAuthService.Services;
using LocalAuthService.Filters;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

// Add services
builder.Services.AddHttpContextAccessor(); // ← Per LegacyCredentialService
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<ApiErrorFilter>();
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database SQLite
var dataDir = Environment.ExpandEnvironmentVariables(
    builder.Configuration["DataDirectory"]
    ?? Environment.GetEnvironmentVariable("DataDirectory")
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LocalAuthService"
    ));
var dbPath = Path.Combine(dataDir, "auth.db");

// Assicura che la directory esista
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

builder.Services.AddDbContext<AuthDbContext>(options =>
{
    options.UseSqlite($"Data Source={dbPath}");
    options.UseOpenIddict(); // Per OpenIddict
});

// JWKS Manager (chiave locale)
builder.Services.AddSingleton(new JwksManager(dataDir));

// Vault key service (encryption key from local file, not derived from hostname)
builder.Services.AddSingleton<VaultKeyService>();

// Legacy Credentials (encrypted, vault-key based)
builder.Services.AddSingleton<LegacyCredentialEncryptionService>();
builder.Services.AddScoped<LegacyCredentialService>();

// Keycloak sync
builder.Services.AddSingleton<OperatingModeDetector>();
builder.Services.AddSingleton<KeycloakAuthService>();
builder.Services.AddSingleton<KeycloakSyncService>();
builder.Services.AddHostedService<SyncBackgroundService>();

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
        var signingKey = new JwksManager(dataDir).GetOrCreateKey();
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
    
    // Seed dati iniziali (se database vuoto e SeedSampleData=true)
    var seedSampleData = app.Configuration.GetValue<bool>("SeedSampleData", true);
    if (seedSampleData && !db.Users.Any())
    {
        Console.WriteLine("🌱 Seeding Falegnameria Verdi S.r.l. — realm: falegnameria-verdi");

        // ── Utenti ──────────────────────────────────────────────────────────
        db.Users.AddRange(
            // Amministratore di sistema
            new User
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
                HasLocalPassword = true,
                Email = "admin@falegnameria-verdi.it",
                FirstName = "Admin",
                LastName = "Sistema",
                EmailVerified = true,
                Enabled = true,
                Roles = "[\"admin\"]",
                CreatedLocally = true
            },
            // Capomastro
            new User
            {
                Username = "marco.bianchi",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Marco2024!"),
                HasLocalPassword = true,
                Email = "marco.bianchi@falegnameria-verdi.it",
                FirstName = "Marco",
                LastName = "Bianchi",
                EmailVerified = true,
                Enabled = true,
                Roles = "[\"supervisor\",\"operator\"]",
                CreatedLocally = true
            },
            // Operatore CNC
            new User
            {
                Username = "luigi.ferrari",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Luigi2024!"),
                HasLocalPassword = true,
                Email = "luigi.ferrari@falegnameria-verdi.it",
                FirstName = "Luigi",
                LastName = "Ferrari",
                EmailVerified = true,
                Enabled = true,
                Roles = "[\"operator\"]",
                CreatedLocally = true
            },
            // Operatore verniciatura
            new User
            {
                Username = "giuseppe.conti",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Giuseppe2024!"),
                HasLocalPassword = true,
                Email = "giuseppe.conti@falegnameria-verdi.it",
                FirstName = "Giuseppe",
                LastName = "Conti",
                EmailVerified = true,
                Enabled = true,
                Roles = "[\"operator\"]",
                CreatedLocally = true
            },
            // Responsabile magazzino
            new User
            {
                Username = "anna.ricci",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Anna2024!"),
                HasLocalPassword = true,
                Email = "anna.ricci@falegnameria-verdi.it",
                FirstName = "Anna",
                LastName = "Ricci",
                EmailVerified = true,
                Enabled = true,
                Roles = "[\"warehouse-manager\"]",
                CreatedLocally = true
            }
        );

        // ── Client (tabella OAuthClients — metadati, non OpenIddict) ────────
        db.Clients.Add(new OAuthClient
        {
            ClientId = "hmi-local",
            ClientName = "HMI Macchina CNC1",
            IsConfidential = false,
            ServiceAccountEnabled = false,
            AllowedScopes = "[\"openid\",\"profile\",\"email\"]"
        });

        // ── MachineConfig ────────────────────────────────────────────────────
        db.MachineConfigs.Add(new MachineConfig
        {
            MachineId = Environment.MachineName,
            LocalRealmName = "falegnameria-verdi",
            KeycloakSyncEnabled = false
        });

        db.SaveChanges();
        Console.WriteLine("✅ Seed completato — Falegnameria Verdi S.r.l.");
        Console.WriteLine("   admin          / admin123");
        Console.WriteLine("   marco.bianchi  / Marco2024!    (supervisor, operator)");
        Console.WriteLine("   luigi.ferrari  / Luigi2024!    (operator)");
        Console.WriteLine("   giuseppe.conti / Giuseppe2024! (operator)");
        Console.WriteLine("   anna.ricci     / Anna2024!     (warehouse-manager)");
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

        var mesCallbackUrl = builder.Configuration["Clients:MesCallbackUrl"] ?? "http://localhost:7000/callback";
        var mesDescriptor = new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
        {
            ClientId = "mes-fornitore",
            ClientSecret = builder.Configuration["Clients:MesFornitore:Secret"]
                ?? throw new InvalidOperationException("Clients:MesFornitore:Secret not configured"),
            DisplayName = "MES Fornitore",
            ClientType = OpenIddict.Abstractions.OpenIddictConstants.ClientTypes.Confidential,
            ConsentType = OpenIddict.Abstractions.OpenIddictConstants.ConsentTypes.Explicit,
            RedirectUris =
            {
                new Uri(mesCallbackUrl),
                new Uri("http://localhost:5063/test/callback")
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
        };

        var existingMesClient = await clientManager.FindByClientIdAsync("mes-fornitore");
        if (existingMesClient == null)
            await clientManager.CreateAsync(mesDescriptor);
        else
            await clientManager.UpdateAsync(existingMesClient, mesDescriptor);

        Console.WriteLine("✅ OAuth client 'mes-fornitore' registered (confidential)");
    }

    // Client #3: Office API (Service Account - Client Credentials M2M)
    if (await clientManager.FindByClientIdAsync("office-api") == null)
    {
        await clientManager.CreateAsync(new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
        {
            ClientId = "office-api",
            ClientSecret = builder.Configuration["Clients:OfficeApi:Secret"]
                ?? throw new InvalidOperationException("Clients:OfficeApi:Secret not configured"),
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

    // Client #4: CLI Simulator (Service Account - Client Credentials for legacy credential access)
    if (await clientManager.FindByClientIdAsync("cli-simulator") == null)
    {
        await clientManager.CreateAsync(new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
        {
            ClientId = "cli-simulator",
            ClientSecret = builder.Configuration["Clients:CliSimulator:Secret"]
                ?? throw new InvalidOperationException("Clients:CliSimulator:Secret not configured"),
            DisplayName = "CLI Simulator Service Account",
            ClientType = OpenIddict.Abstractions.OpenIddictConstants.ClientTypes.Confidential,
            Permissions =
            {
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + "openid"
            }
        });

        Console.WriteLine("✅ OAuth client 'cli-simulator' registered (Client Credentials for legacy credentials)");
    }

    // Client #5: Legacy Credentials Manager (for saving/managing legacy credentials)
    if (await clientManager.FindByClientIdAsync("legacy-credentials-manager") == null)
    {
        await clientManager.CreateAsync(new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
        {
            ClientId = "legacy-credentials-manager",
            ClientSecret = builder.Configuration["Clients:LegacyCredentialsManager:Secret"]
                ?? throw new InvalidOperationException("Clients:LegacyCredentialsManager:Secret not configured"),
            DisplayName = "Legacy Credentials Manager",
            ClientType = OpenIddict.Abstractions.OpenIddictConstants.ClientTypes.Confidential,
            Permissions =
            {
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + "openid"
            }
        });

        Console.WriteLine("✅ OAuth client 'legacy-credentials-manager' registered");
    }

    // Client #6: ERP Simulator (for reading legacy credentials)
    if (await clientManager.FindByClientIdAsync("erp-simulator") == null)
    {
        await clientManager.CreateAsync(new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
        {
            ClientId = "erp-simulator",
            ClientSecret = builder.Configuration["Clients:ErpSimulator:Secret"]
                ?? throw new InvalidOperationException("Clients:ErpSimulator:Secret not configured"),
            DisplayName = "ERP Simulator Service Account",
            ClientType = OpenIddict.Abstractions.OpenIddictConstants.ClientTypes.Confidential,
            Permissions =
            {
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + "openid"
            }
        });

        Console.WriteLine("✅ OAuth client 'erp-simulator' registered");
    }

    // Client #7: CRM Simulator (for reading legacy credentials)
    if (await clientManager.FindByClientIdAsync("crm-simulator") == null)
    {
        await clientManager.CreateAsync(new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
        {
            ClientId = "crm-simulator",
            ClientSecret = builder.Configuration["Clients:CrmSimulator:Secret"]
                ?? throw new InvalidOperationException("Clients:CrmSimulator:Secret not configured"),
            DisplayName = "CRM Simulator Service Account",
            ClientType = OpenIddict.Abstractions.OpenIddictConstants.ClientTypes.Confidential,
            Permissions =
            {
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + "openid"
            }
        });

        Console.WriteLine("✅ OAuth client 'crm-simulator' registered");
    }

    // Client #8: Unauthorized Test Client (NO legacy roles — for testing authorization)
    if (await clientManager.FindByClientIdAsync("unauthorized-test") == null)
    {
        await clientManager.CreateAsync(new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
        {
            ClientId = "unauthorized-test",
            ClientSecret = builder.Configuration["Clients:UnauthorizedTest:Secret"]
                ?? throw new InvalidOperationException("Clients:UnauthorizedTest:Secret not configured"),
            DisplayName = "Unauthorized Test Client",
            ClientType = OpenIddict.Abstractions.OpenIddictConstants.ClientTypes.Confidential,
            Permissions =
            {
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + "openid"
            }
        });

        Console.WriteLine("✅ OAuth client 'unauthorized-test' registered (NO legacy roles for testing)");
    }

    // Aggiunge /test/callback a mes-fornitore (se mancante)
    var mesClient = await clientManager.FindByClientIdAsync("mes-fornitore");
    if (mesClient != null)
    {
        var descriptor = new OpenIddict.Abstractions.OpenIddictApplicationDescriptor();
        await clientManager.PopulateAsync(descriptor, mesClient);
        var testCallbackUri = new Uri("http://localhost:5063/test/callback");
        if (!descriptor.RedirectUris.Contains(testCallbackUri))
        {
            descriptor.RedirectUris.Add(testCallbackUri);
            await clientManager.UpdateAsync(mesClient, descriptor);
            Console.WriteLine("✅ Added test/callback redirect URI to 'mes-fornitore'");
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

    // ✅ Esplicito close della connessione per evitare SQLite file lock
    db.Database.CloseConnection();
}

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// HTTPS enforcement (always redirect; HSTS only in production)
app.UseHttpsRedirection();
if (!app.Environment.IsDevelopment())
    app.UseHsts();

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

    if (!app.Environment.IsDevelopment())
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");

    // No-cache for endpoints that return sensitive credentials
    if (context.Request.Path.StartsWithSegments("/api/legacy"))
    {
        context.Response.Headers.Append("Cache-Control", "no-store, no-cache, must-revalidate");
        context.Response.Headers.Append("Pragma", "no-cache");
    }

    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

Console.WriteLine("🚀 LocalAuthService ready!");
app.Run();