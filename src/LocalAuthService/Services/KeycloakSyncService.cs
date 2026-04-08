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

    public async Task EnsureRealmExistsAsync()
    {
        var token = await GetAdminTokenAsync();
        if (token == null) return;

        var url = _config["Keycloak:Url"];
        var realm = _config["Keycloak:Realm"];

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var check = await _http.GetAsync($"{url}/admin/realms/{realm}");
        if (check.IsSuccessStatusCode)
        {
            _logger.LogInformation("Keycloak realm '{Realm}' already exists", realm);
            await EnsureLoginClientExistsAsync(url!, realm!);
            return;
        }

        var body = JsonSerializer.Serialize(new
        {
            realm = realm,
            displayName = realm,
            enabled = true,
            registrationAllowed = false,
            resetPasswordAllowed = true,
            bruteForceProtected = true
        });

        var response = await _http.PostAsync(
            $"{url}/admin/realms",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Keycloak realm '{Realm}' created successfully", realm);
            await EnsureLoginClientExistsAsync(url!, realm!);
        }
        else
        {
            _logger.LogWarning("Failed to create Keycloak realm '{Realm}': {Status} — {Body}",
                realm, response.StatusCode, await response.Content.ReadAsStringAsync());
        }
    }

    private async Task EnsureLoginClientExistsAsync(string url, string realm)
    {
        var clientId = _config["Keycloak:LoginClientId"] ?? "account";

        var check = await _http.GetAsync($"{url}/admin/realms/{realm}/clients?clientId={Uri.EscapeDataString(clientId)}");
        if (!check.IsSuccessStatusCode) return;

        var existing = JsonSerializer.Deserialize<List<JsonElement>>(await check.Content.ReadAsStringAsync());
        if (existing == null || existing.Count == 0) return;

        var internalId = existing[0].GetProperty("id").GetString();
        var directAccessEnabled = existing[0].TryGetProperty("directAccessGrantsEnabled", out var prop) && prop.GetBoolean();

        if (directAccessEnabled)
        {
            _logger.LogInformation("Keycloak client '{ClientId}' already has Direct Access Grants enabled", clientId);
            return;
        }

        // PUT richiede la rappresentazione completa: leggo il client attuale e modifico solo il campo
        var fullClient = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(existing[0].GetRawText())!;
        fullClient["directAccessGrantsEnabled"] = JsonDocument.Parse("true").RootElement;

        var updateBody = JsonSerializer.Serialize(fullClient);
        var response = await _http.PutAsync(
            $"{url}/admin/realms/{realm}/clients/{internalId}",
            new StringContent(updateBody, System.Text.Encoding.UTF8, "application/json"));

        if (response.IsSuccessStatusCode)
            _logger.LogInformation("Enabled Direct Access Grants on Keycloak client '{ClientId}'", clientId);
        else
            _logger.LogWarning("Failed to update client '{ClientId}': {Status}", clientId, response.StatusCode);
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

    public async Task SyncRolesToKeycloakAsync()
    {
        var token = await GetAdminTokenAsync();
        if (token == null) return;

        var url = _config["Keycloak:Url"];
        var realm = _config["Keycloak:Realm"];
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        var allRoles = (await db.Users
            .Where(u => u.Roles != null)
            .Select(u => u.Roles!)
            .ToListAsync())
            .SelectMany(r => JsonSerializer.Deserialize<string[]>(r) ?? [])
            .Distinct()
            .ToList();

        foreach (var role in allRoles)
        {
            var check = await _http.GetAsync($"{url}/admin/realms/{realm}/roles/{Uri.EscapeDataString(role)}");
            if (check.IsSuccessStatusCode)
            {
                _logger.LogInformation("Keycloak role '{Role}' already exists", role);
                continue;
            }

            var body = JsonSerializer.Serialize(new { name = role, description = $"Role: {role}" });
            var response = await _http.PostAsync(
                $"{url}/admin/realms/{realm}/roles",
                new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

            if (response.IsSuccessStatusCode)
                _logger.LogInformation("Created Keycloak role '{Role}'", role);
            else
                _logger.LogWarning("Failed to create Keycloak role '{Role}': {Status}", role, response.StatusCode);
        }
    }

    public async Task SyncClientsToKeycloakAsync()
    {
        var token = await GetAdminTokenAsync();
        if (token == null) return;

        var url = _config["Keycloak:Url"];
        var realm = _config["Keycloak:Realm"];
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var scope = _scopeFactory.CreateScope();
        var clientManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        await foreach (var app in clientManager.ListAsync())
        {
            var descriptor = new OpenIddictApplicationDescriptor();
            await clientManager.PopulateAsync(descriptor, app);

            if (string.IsNullOrEmpty(descriptor.ClientId)) continue;

            // Salta i client interni di OpenIddict
            if (descriptor.ClientId.StartsWith("openiddict")) continue;

            var isPublic = descriptor.ClientType == ClientTypes.Public;
            var redirectUris = descriptor.RedirectUris.Select(u => u.ToString()).ToList();

            var clientRepresentation = new Dictionary<string, object>
            {
                ["clientId"] = descriptor.ClientId,
                ["name"] = descriptor.DisplayName ?? descriptor.ClientId,
                ["enabled"] = true,
                ["publicClient"] = isPublic,
                ["directAccessGrantsEnabled"] = descriptor.Permissions.Contains(Permissions.GrantTypes.Password),
                ["standardFlowEnabled"] = descriptor.Permissions.Contains(Permissions.GrantTypes.AuthorizationCode),
                ["serviceAccountsEnabled"] = descriptor.Permissions.Contains(Permissions.GrantTypes.ClientCredentials),
                ["redirectUris"] = redirectUris,
                ["webOrigins"] = redirectUris  // necessario per CORS
            };

            var checkResponse = await _http.GetAsync($"{url}/admin/realms/{realm}/clients?clientId={Uri.EscapeDataString(descriptor.ClientId)}");
            var existingList = checkResponse.IsSuccessStatusCode
                ? JsonSerializer.Deserialize<List<JsonElement>>(await checkResponse.Content.ReadAsStringAsync())
                : null;

            if (existingList?.Count > 0)
            {
                // Aggiorna il client esistente preservando il secret
                var internalId = existingList[0].GetProperty("id").GetString();
                var body = JsonSerializer.Serialize(clientRepresentation);
                var response = await _http.PutAsync(
                    $"{url}/admin/realms/{realm}/clients/{internalId}",
                    new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

                if (response.IsSuccessStatusCode)
                    _logger.LogInformation("Updated Keycloak client '{ClientId}'", descriptor.ClientId);
                else
                    _logger.LogWarning("Failed to update Keycloak client '{ClientId}': {Status}", descriptor.ClientId, response.StatusCode);
            }
            else
            {
                // Crea nuovo client
                if (!isPublic)
                {
                    var secret = Guid.NewGuid().ToString();
                    clientRepresentation["secret"] = secret;
                    _logger.LogWarning("Keycloak client secret for '{ClientId}': {Secret} — save this, it won't be shown again", descriptor.ClientId, secret);
                }

                var body = JsonSerializer.Serialize(clientRepresentation);
                var response = await _http.PostAsync(
                    $"{url}/admin/realms/{realm}/clients",
                    new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

                if (response.IsSuccessStatusCode)
                    _logger.LogInformation("Created Keycloak client '{ClientId}'", descriptor.ClientId);
                else
                    _logger.LogWarning("Failed to create Keycloak client '{ClientId}': {Status} — {Body}",
                        descriptor.ClientId, response.StatusCode, await response.Content.ReadAsStringAsync());
            }
        }
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

                if (kcId != null)
                {
                    await SetTemporaryPasswordAsync(url!, realm!, kcId, user.Username);
                    await AssignRolesToKeycloakUserAsync(url!, realm!, kcId, user.Roles);
                }

                _logger.LogInformation("Synced local user to Keycloak: {Username}", user.Username);
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to sync user {Username} to Keycloak: {Status} — {Body}", user.Username, response.StatusCode, errorBody);
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
                // Non sovrascrivere client già definiti localmente: Keycloak non è autoritativo sui secret locali
                _logger.LogInformation("Skipping update for existing local client '{ClientId}'", kc.ClientId);
            }
        }

        _logger.LogInformation("Client sync from Keycloak complete");
    }

    private async Task AssignRolesToKeycloakUserAsync(string url, string realm, string kcId, string? rolesJson)
    {
        if (string.IsNullOrEmpty(rolesJson)) return;

        var roles = JsonSerializer.Deserialize<string[]>(rolesJson);
        if (roles == null || roles.Length == 0) return;

        var roleObjects = new List<object>();
        foreach (var roleName in roles)
        {
            var response = await _http.GetAsync($"{url}/admin/realms/{realm}/roles/{Uri.EscapeDataString(roleName)}");
            if (!response.IsSuccessStatusCode) continue;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            roleObjects.Add(new
            {
                id = doc.RootElement.GetProperty("id").GetString(),
                name = roleName
            });
        }

        if (roleObjects.Count == 0) return;

        var body = JsonSerializer.Serialize(roleObjects);
        await _http.PostAsync(
            $"{url}/admin/realms/{realm}/users/{kcId}/role-mappings/realm",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        _logger.LogInformation("Assigned roles {Roles} to Keycloak user {KcId}", string.Join(", ", roles), kcId);
    }

    private async Task SetTemporaryPasswordAsync(string url, string realm, string kcId, string username)
    {
        var tempPassword = GenerateTemporaryPassword();

        var body = JsonSerializer.Serialize(new
        {
            type = "password",
            value = tempPassword,
            temporary = true
        });

        var response = await _http.PutAsync(
            $"{url}/admin/realms/{realm}/users/{kcId}/reset-password",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        if (response.IsSuccessStatusCode)
            // La password temporanea viene loggata una sola volta: l'admin deve comunicarla all'utente
            _logger.LogWarning("Temporary Keycloak password for '{Username}': {TempPassword} — user must change it on first online login", username, tempPassword);
        else
            _logger.LogWarning("Failed to set temporary Keycloak password for '{Username}': {Status}", username, response.StatusCode);
    }

    private static string GenerateTemporaryPassword()
    {
        // 12 caratteri base64url → sufficientemente random, nessun carattere ambiguo
        var bytes = new byte[9];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return "Tmp-" + Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
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
