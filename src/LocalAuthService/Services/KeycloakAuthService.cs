using System.Text.Json;

namespace LocalAuthService.Services;

public enum KeycloakAuthResult { Success, InvalidCredentials, AccountNotSetup, Unavailable }

public class KeycloakAuthService
{
    private readonly IConfiguration _config;
    private readonly ILogger<KeycloakAuthService> _logger;
    private readonly HttpClient _http;

    public KeycloakAuthService(IConfiguration config, ILogger<KeycloakAuthService> logger)
    {
        _config = config;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public string GetLoginClientId() => _config["Keycloak:LoginClientId"] ?? "account";

    public async Task SyncPasswordToKeycloakAsync(string keycloakUserId, string plainPassword)
    {
        var url = _config["Keycloak:Url"];
        var realm = _config["Keycloak:Realm"];
        var adminRealm = _config["Keycloak:AdminRealm"] ?? "master";
        var adminClientId = _config["Keycloak:AdminClientId"] ?? "admin-cli";
        var adminUsername = _config["Keycloak:AdminUsername"];
        var adminPassword = _config["Keycloak:AdminPassword"];

        if (string.IsNullOrEmpty(adminUsername) || string.IsNullOrEmpty(adminPassword)) return;

        try
        {
            // Ottieni admin token
            var tokenBody = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = adminClientId,
                ["username"] = adminUsername,
                ["password"] = adminPassword
            });
            var tokenResponse = await _http.PostAsync($"{url}/realms/{adminRealm}/protocol/openid-connect/token", tokenBody);
            if (!tokenResponse.IsSuccessStatusCode) return;

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
            var adminToken = JsonDocument.Parse(tokenJson).RootElement.GetProperty("access_token").GetString();

            // Aggiorna la password su Keycloak (non temporanea)
            var request = new HttpRequestMessage(HttpMethod.Put, $"{url}/admin/realms/{realm}/users/{keycloakUserId}/reset-password");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { type = "password", value = plainPassword, temporary = false }),
                System.Text.Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode)
                _logger.LogInformation("Keycloak password synced for user '{KcId}'", keycloakUserId);
            else
                _logger.LogWarning("Failed to sync password to Keycloak for '{KcId}': {Status}", keycloakUserId, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error syncing password to Keycloak: {Message}", ex.Message);
        }
    }

    public async Task<KeycloakAuthResult> ValidateCredentialsAsync(string username, string password, string clientId, string? clientSecret = null)
    {
        var url = _config["Keycloak:Url"];
        var realm = _config["Keycloak:Realm"];

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(realm))
            return KeycloakAuthResult.Unavailable;

        try
        {
            var fields = new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = clientId,
                ["username"] = username,
                ["password"] = password
            };

            if (!string.IsNullOrEmpty(clientSecret))
                fields["client_secret"] = clientSecret;

            var response = await _http.PostAsync(
                $"{url}/realms/{realm}/protocol/openid-connect/token",
                new FormUrlEncodedContent(fields));

            if (response.IsSuccessStatusCode)
                return KeycloakAuthResult.Success;

            // Distingui "password temporanea" da "credenziali errate"
            var json = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Keycloak auth response for '{Username}': {Status} — {Body}", username, response.StatusCode, json);

            if (json.Contains("Account is not fully set up"))
            {
                _logger.LogInformation("Keycloak: account '{Username}' has temporary password, falling back to local", username);
                return KeycloakAuthResult.AccountNotSetup;
            }

            return KeycloakAuthResult.InvalidCredentials;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Keycloak auth failed: {Message}", ex.Message);
            return KeycloakAuthResult.Unavailable;
        }
    }
}
