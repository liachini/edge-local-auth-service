namespace LocalAuthService.Services;

public class OperatingModeDetector
{
    private readonly IConfiguration _config;
    private readonly ILogger<OperatingModeDetector> _logger;
    private readonly HttpClient _http;
    private bool _isOnline = false;
    private DateTime _lastCheckTime = DateTime.MinValue;
    private const int CheckCacheSeconds = 5;

    public bool IsOnline => _isOnline;
    public event Action<bool>? OnModeChanged;

    public OperatingModeDetector(IConfiguration config, ILogger<OperatingModeDetector> logger)
    {
        _config = config;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) }; // Reduced timeout to 2 seconds
    }

    public async Task<bool> CheckAsync()
    {
        var keycloakUrl = _config["Keycloak:Url"];
        if (string.IsNullOrEmpty(keycloakUrl))
        {
            SetOnline(false);
            return false;
        }

        // Cache the check result for CheckCacheSeconds
        if (DateTime.UtcNow.Subtract(_lastCheckTime).TotalSeconds < CheckCacheSeconds)
        {
            return _isOnline;
        }

        _lastCheckTime = DateTime.UtcNow;

        try
        {
            var checkUrl = $"{keycloakUrl}/health/ready";
            _logger.LogInformation("Checking Keycloak at {Url}", checkUrl);
            var response = await _http.GetAsync(checkUrl);
            _logger.LogInformation("Keycloak responded: {Status}", response.StatusCode);
            SetOnline(response.IsSuccessStatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Keycloak unreachable: {Message}", ex.Message);
            SetOnline(false);
        }

        return _isOnline;
    }

    private void SetOnline(bool value)
    {
        if (_isOnline == value) return;
        _isOnline = value;
        _logger.LogInformation("Mode changed: {Mode}", value ? "ONLINE" : "OFFLINE");
        OnModeChanged?.Invoke(value);
    }
}
