namespace LocalAuthService.Services;

public class SyncBackgroundService : BackgroundService
{
    private readonly OperatingModeDetector _modeDetector;
    private readonly KeycloakSyncService _syncService;
    private readonly ILogger<SyncBackgroundService> _logger;
    private readonly IConfiguration _config;

    public SyncBackgroundService(
        OperatingModeDetector modeDetector,
        KeycloakSyncService syncService,
        ILogger<SyncBackgroundService> logger,
        IConfiguration config)
    {
        _modeDetector = modeDetector;
        _syncService = syncService;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = _config.GetValue<int>("Keycloak:SyncIntervalMinutes", 15);
        var offlineRetrySeconds = _config.GetValue<int>("Keycloak:OfflineRetrySeconds", 10);

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Sync tick — checking Keycloak...");
            await _modeDetector.CheckAsync();

            if (_modeDetector.IsOnline)
            {
                _logger.LogInformation("Keycloak ONLINE — starting sync");
                try
                {
                    await _syncService.EnsureRealmExistsAsync();
                    await _syncService.SyncRolesToKeycloakAsync();       // ruoli prima degli utenti
                    await _syncService.SyncClientsToKeycloakAsync();     // client locali → Keycloak
                    await _syncService.SyncClientsFromKeycloakAsync();   // client Keycloak → locale
                    await _syncService.SyncFromKeycloakAsync();          // utenti Keycloak → locale
                    await _syncService.SyncToKeycloakAsync();            // utenti locali → Keycloak (con ruoli)
                    _logger.LogInformation("Sync completed successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Sync failed: {Message}", ex.Message);
                }

                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            }
            else
            {
                _logger.LogInformation("Keycloak OFFLINE — retry in {Seconds}s", offlineRetrySeconds);
                await Task.Delay(TimeSpan.FromSeconds(offlineRetrySeconds), stoppingToken);
            }
        }
    }
}
