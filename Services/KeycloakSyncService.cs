using LocalAuthService.Data;
using LocalAuthService.Models;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using System.Net.Http.Headers;
using System.Text.Json;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace LocalAuthService.Services;

public class KeycloakSyncService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<KeycloakSyncService> _logger;
    private readonly HttpClient _http;

    public KeycloakSyncService(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<KeycloakSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
        _http = new HttpClient();
    }

    public async Task SyncFromKeycloakAsync()
    {
        var token = await GetAdminTokenAsync();
        if (token == null) return;

        var realm = _config["Keycloak:Realm"];
        var url = _config["Keycloak:Url"];

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _http.GetAsync($"{url}/admin/realms/{realm}/users?max=1000");
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to fetch users from Keycloak: {Status}", response.StatusCode);
            return;
        }

        var json = await response.Content.ReadAsStringAsync();
        var kcUsers = JsonSerializer.Deserialize<List<KeycloakUser>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (kcUsers == null) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        foreach (var kcUser in kcUsers)
        {
            var local = await db.Users.FirstOrDefaultAsync(u => u.KeycloakUserId == kcUser.Id);
            if (local == null)
            {
                local = await db.Users.FirstOrDefaultAsync(u => u.Username == kcUser.Username);
            }

            if (local == null)
            {
                db.Users.Add(new User
                {
                    Username = kcUser.Username ?? kcUser.Id,
                    PasswordHash = "",
                    HasLocalPassword = false,
                    Email = kcUser.Email,
                    FirstName = kcUser.FirstName,
                    LastName = kcUser.LastName,
                    EmailVerified = kcUser.EmailVerified,
                    Enabled = kcUser.Enabled,
                    CreatedLocally = false,
                    KeycloakUserId = kcUser.Id,
                    LastSyncFromKeycloak = DateTime.UtcNow
                });
                _logger.LogInformation("Imported user from Keycloak: {Username}", kcUser.Username);
            }
            else
            {
                local.Email = kcUser.Email;
                local.FirstName = kcUser.FirstName;
                local.LastName = kcUser.LastName;
                local.EmailVerified = kcUser.EmailVerified;
                local.Enabled = kcUser.Enabled;
                local.KeycloakUserId = kcUser.Id;
                local.LastSyncFromKeycloak = DateTime.UtcNow;
                local.UpdatedAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("Sync from Keycloak complete: {Count} users processed", kcUsers.Count);
    }

    public async Task SyncToKeycloakAsync()
    {
        var token = await GetAdminTokenAsync();
        if (token == null) return;

        var realm = _config["Keycloak:Realm"];
        var url = _config["Keycloak:Url"];

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        var localUsers = await db.Users
            .Where(u => u.CreatedLocally && u.KeycloakUserId == null)
            .ToListAsync();

        foreach (var user in localUsers)
        {
            var body = JsonSerializer.Serialize(new
            {
                username = user.Username,
                email = user.Email,
                firstName = user.FirstName,
                lastName = user.LastName,
                enabled = user.Enabled,
                emailVerified = user.EmailVerified
            });

            var response = await _http.PostAsync(
                $"{url}/admin/realms/{realm}/users",
                new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

            if (response.StatusCode == System.Net.HttpStatusCode.Created)
            {
                var location = response.Headers.Location?.ToString();
                var kcId = location?.Split('/').Last();
                user.KeycloakUserId = kcId;
                user.LastSyncToKeycloak = DateTime.UtcNow;
                _logger.LogInformation("Synced local user to Keycloak: {Username}", user.Username);
            }
            else
            {
                _logger.LogWarning("Failed to sync user {Username} to Keycloak: {Status}", user.Username, response.StatusCode);
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task<string?> GetAdminTokenAsync()
    {
        var url = _config["Keycloak:Url"];
        var realm = _config["Keycloak:AdminRealm"] ?? "master";
        var clientId = _config["Keycloak:AdminClientId"] ?? "admin-cli";
        var username = _config["Keycloak:AdminUsername"];
        var password = _config["Keycloak:AdminPassword"];

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            _logger.LogWarning("Keycloak admin credentials not configured");
            return null;
        }

        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
            ["username"] = username,
            ["password"] = password
        });

        try
        {
            var response = await _http.PostAsync($"{url}/realms/{realm}/protocol/openid-connect/token", body);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("access_token").GetString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to get Keycloak admin token: {Message}", ex.Message);
            return null;
        }
    }

    public async Task SyncClientsFromKeycloakAsync()
    {
        _logger.LogInformation("SyncClientsFromKeycloak: getting admin token...");
        var token = await GetAdminTokenAsync();
        if (token == null)
        {
            _logger.LogWarning("SyncClientsFromKeycloak: failed to get admin token");
            return;
        }

        var realm = _config["Keycloak:Realm"];
        var url = _config["Keycloak:Url"];

        _logger.LogInformation("SyncClientsFromKeycloak: fetching clients from {Url}/admin/realms/{Realm}/clients", url, realm);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _http.GetAsync($"{url}/admin/realms/{realm}/clients?max=100");
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("SyncClientsFromKeycloak: failed {Status} — {Body}", response.StatusCode, await response.Content.ReadAsStringAsync());
            return;
        }

        var json = await response.Content.ReadAsStringAsync();
        var kcClients = JsonSerializer.Deserialize<List<KeycloakClient>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (kcClients == null) return;

        using var scope = _scopeFactory.CreateScope();
        var clientManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        var internalClients = new HashSet<string> { "account", "account-console", "admin-cli", "broker", "realm-management", "security-admin-console" };

        foreach (var kc in kcClients.Where(c => c.Enabled && !c.BearerOnly
            && !internalClients.Contains(c.ClientId)
            && !c.ClientId.StartsWith("${")))
        {
            var existing = await clientManager.FindByClientIdAsync(kc.ClientId);
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = kc.ClientId,
                DisplayName = kc.Name ?? kc.ClientId,
                ClientType = kc.PublicClient ? ClientTypes.Public : ClientTypes.Confidential,
            };

            // Redirect URIs
            foreach (var uri in kc.RedirectUris ?? [])
            {
                if (Uri.TryCreate(uri.Replace("*", "callback"), UriKind.Absolute, out var parsed))
                    descriptor.RedirectUris.Add(parsed);
            }

            // Permissions base
            descriptor.Permissions.Add(Permissions.Endpoints.Token);
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + "openid");
            descriptor.Permissions.Add(Permissions.Scopes.Email);
            descriptor.Permissions.Add(Permissions.Scopes.Profile);

            if (kc.DirectAccessGrantsEnabled)
            {
                descriptor.Permissions.Add(Permissions.GrantTypes.Password);
                descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
                descriptor.Permissions.Add(Permissions.Prefixes.Scope + Scopes.OfflineAccess);
            }

            if (kc.StandardFlowEnabled)
            {
                descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
                descriptor.Permissions.Add(Permissions.GrantTypes.AuthorizationCode);
                descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
                descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
                descriptor.ConsentType = ConsentTypes.Explicit;
                descriptor.Permissions.Add(Permissions.Prefixes.Scope + Scopes.OfflineAccess);
            }

            if (kc.ServiceAccountsEnabled)
            {
                descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
            }

            // Secret per client confidenziali
            if (!kc.PublicClient)
            {
                var secretResponse = await _http.GetAsync($"{url}/admin/realms/{realm}/clients/{kc.Id}/client-secret");
                if (secretResponse.IsSuccessStatusCode)
                {
                    var secretJson = await secretResponse.Content.ReadAsStringAsync();
                    var secretDoc = JsonDocument.Parse(secretJson);
                    descriptor.ClientSecret = secretDoc.RootElement.GetProperty("value").GetString();
                }
            }

            if (existing == null)
            {
                await clientManager.CreateAsync(descriptor);
                _logger.LogInformation("Imported client from Keycloak: {ClientId}", kc.ClientId);
            }
            else
            {
                await clientManager.UpdateAsync(existing, descriptor);
                _logger.LogInformation("Updated client from Keycloak: {ClientId}", kc.ClientId);
            }
        }

        _logger.LogInformation("Client sync from Keycloak complete");
    }

    private class KeycloakClient
    {
        public string Id { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string? Name { get; set; }
        public bool Enabled { get; set; }
        public bool PublicClient { get; set; }
        public bool BearerOnly { get; set; }
        public bool DirectAccessGrantsEnabled { get; set; }
        public bool StandardFlowEnabled { get; set; }
        public bool ServiceAccountsEnabled { get; set; }
        public List<string>? RedirectUris { get; set; }
    }

    private class KeycloakUser
    {
        public string Id { get; set; } = "";
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public bool EmailVerified { get; set; }
        public bool Enabled { get; set; }
    }
}
