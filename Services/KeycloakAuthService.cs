using System.Text.Json;

namespace LocalAuthService.Services;

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

    public async Task<bool> ValidateCredentialsAsync(string username, string password, string clientId, string? clientSecret = null)
    {
        var url = _config["Keycloak:Url"];
        var realm = _config["Keycloak:Realm"];

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(realm))
            return false;

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

            var body = new FormUrlEncodedContent(fields);

            var response = await _http.PostAsync(
                $"{url}/realms/{realm}/protocol/openid-connect/token", body);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Keycloak auth failed: {Message}", ex.Message);
            return false;
        }
    }
}
