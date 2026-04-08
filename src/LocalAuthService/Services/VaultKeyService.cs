using System.Security.Cryptography;

namespace LocalAuthService.Services;

/// <summary>
/// Manages encryption key from local vault file.
/// Each machine has its own random encryption key stored in a file.
/// Key is NOT derived from hostname (which is public and predictable).
/// </summary>
public class VaultKeyService
{
    private readonly string _vaultKeyPath;
    private readonly ILogger<VaultKeyService> _logger;

    public VaultKeyService(IConfiguration config, ILogger<VaultKeyService> logger)
    {
        _logger = logger;
        var rawPath = config["Encryption:VaultFilePath"]
            ?? throw new InvalidOperationException(
                "Encryption:VaultFilePath not configured. " +
                "Set it in appsettings.Development.json or via environment variable Encryption__VaultFilePath.");

        // Support %LOCALAPPDATA%, $HOME, etc. in paths
        _vaultKeyPath = Environment.ExpandEnvironmentVariables(rawPath);
        EnsureVaultFile();
    }

    /// <summary>
    /// Returns the base64-encoded 256-bit AES encryption key from the vault file.
    /// </summary>
    public string GetEncryptionKey()
    {
        try
        {
            var key = File.ReadAllText(_vaultKeyPath).Trim();
            if (!IsValidBase64Key(key))
                throw new InvalidOperationException(
                    $"Invalid key format in vault file {_vaultKeyPath}. Expected base64-encoded 32-byte key.");
            return key;
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "Vault key file not found: {Path}", _vaultKeyPath);
            throw new InvalidOperationException($"Encryption vault file not found: {_vaultKeyPath}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Permission denied reading vault file: {Path}", _vaultKeyPath);
            throw new InvalidOperationException($"Cannot read vault file (permission denied): {_vaultKeyPath}");
        }
    }

    private void EnsureVaultFile()
    {
        if (!File.Exists(_vaultKeyPath))
        {
            _logger.LogWarning(
                "Vault file not found at {Path} — auto-generating. " +
                "This is OK for development. In production, generate and protect this file manually.", _vaultKeyPath);

            var dir = Path.GetDirectoryName(_vaultKeyPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            File.WriteAllText(_vaultKeyPath, key);

            // Restrict to owner-only on Unix (chmod 600)
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                File.SetUnixFileMode(_vaultKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            _logger.LogWarning(
                "✅ Vault key auto-generated at {Path}. " +
                "IMPORTANT: Back up this file — losing it means losing access to all encrypted credentials.", _vaultKeyPath);
        }
        else
        {
            ValidatePermissions();

            var key = File.ReadAllText(_vaultKeyPath).Trim();
            if (!IsValidBase64Key(key))
                throw new InvalidOperationException(
                    $"Invalid key format in vault file {_vaultKeyPath}. " +
                    "Expected base64-encoded 32-byte key. Regenerate with: " +
                    "openssl rand -base64 32 > <path>");

            _logger.LogInformation("✅ Encryption vault initialized from {Path}", _vaultKeyPath);
        }
    }

    private void ValidatePermissions()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try
            {
                var mode = File.GetUnixFileMode(_vaultKeyPath);
                if (mode.HasFlag(UnixFileMode.OtherRead) || mode.HasFlag(UnixFileMode.GroupRead))
                {
                    throw new InvalidOperationException(
                        $"Vault file has insecure permissions (readable by group/others). " +
                        $"Run: chmod 600 {_vaultKeyPath}");
                }
                _logger.LogInformation("✅ Vault file permissions validated (Unix 0600)");
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                _logger.LogWarning(ex, "Could not validate Unix file permissions for {Path}", _vaultKeyPath);
            }
        }
    }

    private static bool IsValidBase64Key(string key)
    {
        try
        {
            var bytes = Convert.FromBase64String(key);
            return bytes.Length == 32; // 256-bit for AES-256
        }
        catch
        {
            return false;
        }
    }
}