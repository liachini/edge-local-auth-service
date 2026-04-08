using System.Security.Cryptography;
using System.Text;
using LocalAuthService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalAuthService.Tests;

/// <summary>
/// Security Fix 1.1: Encryption key must come from a local vault file,
/// NOT derived from MachineName (which is public and predictable).
/// </summary>
public class VaultKeyServiceTests : IDisposable
{
    private readonly string _tempKeyFile;
    private readonly VaultKeyService _sut;

    public VaultKeyServiceTests()
    {
        _tempKeyFile = Path.Combine(Path.GetTempPath(), $"test-vault-{Guid.NewGuid()}.key");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:VaultFilePath"] = _tempKeyFile
            })
            .Build();

        _sut = new VaultKeyService(config, NullLogger<VaultKeyService>.Instance);
    }

    [Fact]
    public void GetEncryptionKey_ReturnsValid256BitBase64Key()
    {
        var key = _sut.GetEncryptionKey();

        var bytes = Convert.FromBase64String(key);
        Assert.Equal(32, bytes.Length); // 256-bit AES key
    }

    [Fact]
    public void GetEncryptionKey_IsNotDerivedFromMachineName()
    {
        // SECURITY: key must not be predictable from public machine info
        var vaultKey = _sut.GetEncryptionKey();
        var machineKey = DeriveMachineNameKey(Environment.MachineName);

        Assert.NotEqual(vaultKey, machineKey);
    }

    [Fact]
    public void GetEncryptionKey_IsDifferentOnEachMachine()
    {
        // Two vault files generate two different random keys
        var otherKeyFile = Path.Combine(Path.GetTempPath(), $"test-vault-other-{Guid.NewGuid()}.key");
        try
        {
            var config2 = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Encryption:VaultFilePath"] = otherKeyFile
                })
                .Build();

            var sut2 = new VaultKeyService(config2, NullLogger<VaultKeyService>.Instance);

            Assert.NotEqual(_sut.GetEncryptionKey(), sut2.GetEncryptionKey());
        }
        finally
        {
            if (File.Exists(otherKeyFile)) File.Delete(otherKeyFile);
        }
    }

    [Fact]
    public void GetEncryptionKey_AutoGeneratesFileIfMissing()
    {
        // File must be auto-generated on first run (not require manual setup)
        Assert.True(File.Exists(_tempKeyFile));
    }

    [Fact]
    public void GetEncryptionKey_ThrowsIfVaultFilePathNotConfigured()
    {
        var config = new ConfigurationBuilder().Build(); // no Encryption:VaultFilePath

        Assert.Throws<InvalidOperationException>(() =>
            new VaultKeyService(config, NullLogger<VaultKeyService>.Instance));
    }

    [Fact]
    public void LegacyCredentialEncryptionService_EncryptDecrypt_RoundTrip()
    {
        var encryptionService = new LegacyCredentialEncryptionService(
            _sut, NullLogger<LegacyCredentialEncryptionService>.Instance);

        var plaintext = "super-secret-legacy-password";
        var encrypted = encryptionService.Encrypt(plaintext);
        var decrypted = encryptionService.Decrypt(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void LegacyCredentialEncryptionService_EncryptedValue_IsDifferentEachTime()
    {
        // IV is randomized — same plaintext must produce different ciphertext
        var encryptionService = new LegacyCredentialEncryptionService(
            _sut, NullLogger<LegacyCredentialEncryptionService>.Instance);

        var encrypted1 = encryptionService.Encrypt("same-password");
        var encrypted2 = encryptionService.Encrypt("same-password");

        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Fact]
    public void LegacyCredentialEncryptionService_CannotDecryptWithDifferentKey()
    {
        // Data encrypted with vault key A cannot be decrypted with vault key B
        var otherKeyFile = Path.Combine(Path.GetTempPath(), $"test-vault-wrong-{Guid.NewGuid()}.key");
        try
        {
            var config2 = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Encryption:VaultFilePath"] = otherKeyFile
                })
                .Build();

            var sut2 = new VaultKeyService(config2, NullLogger<VaultKeyService>.Instance);
            var service1 = new LegacyCredentialEncryptionService(_sut, NullLogger<LegacyCredentialEncryptionService>.Instance);
            var service2 = new LegacyCredentialEncryptionService(sut2, NullLogger<LegacyCredentialEncryptionService>.Instance);

            var encrypted = service1.Encrypt("secret");

            Assert.ThrowsAny<Exception>(() => service2.Decrypt(encrypted));
        }
        finally
        {
            if (File.Exists(otherKeyFile)) File.Delete(otherKeyFile);
        }
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Replicates the OLD (insecure) key derivation from MachineName.
    /// Used to verify the new implementation does NOT produce this value.
    /// </summary>
    private static string DeriveMachineNameKey(string machineName)
    {
        const string salt = "LocalAuthService-LegacyCredentials-v1";
        var keyBytes = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(machineName),
            Encoding.UTF8.GetBytes(salt),
            iterations: 10000,
            HashAlgorithmName.SHA256,
            outputLength: 32);
        return Convert.ToBase64String(keyBytes);
    }

    public void Dispose()
    {
        if (File.Exists(_tempKeyFile))
            File.Delete(_tempKeyFile);
    }
}