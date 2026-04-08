using System.Security.Cryptography;
using System.Text;

namespace LocalAuthService.Services;

/// <summary>
/// Encrypts/Decrypts legacy credentials using AES-256-CBC.
/// Key comes from VaultKeyService (local file), not from machine name.
/// </summary>
public class LegacyCredentialEncryptionService
{
    private readonly VaultKeyService _vaultKeyService;
    private readonly ILogger<LegacyCredentialEncryptionService> _logger;

    public LegacyCredentialEncryptionService(
        VaultKeyService vaultKeyService,
        ILogger<LegacyCredentialEncryptionService> logger)
    {
        _vaultKeyService = vaultKeyService;
        _logger = logger;
        _logger.LogInformation("✅ LegacyCredentialEncryptionService initialized (vault key)");
    }

    /// <summary>
    /// Encrypts a plaintext password with AES-256-CBC.
    /// Returns: [IV (16 bytes)][Ciphertext] as Base64.
    /// </summary>
    public string Encrypt(string plainPassword)
    {
        if (string.IsNullOrEmpty(plainPassword))
            throw new ArgumentException("Password non può essere vuota");

        try
        {
            var key = Convert.FromBase64String(_vaultKeyService.GetEncryptionKey());

            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();

            // Prepend IV in plaintext (needed for decrypt)
            ms.Write(aes.IV, 0, aes.IV.Length);

            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs, Encoding.UTF8))
            {
                sw.Write(plainPassword);
            }

            return Convert.ToBase64String(ms.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Errore durante cifratura password legacy");
            throw;
        }
    }

    /// <summary>
    /// Decrypts an AES-256-CBC encrypted password.
    /// Reads IV from the first 16 bytes.
    /// </summary>
    public string Decrypt(string encryptedPassword)
    {
        if (string.IsNullOrEmpty(encryptedPassword))
            throw new ArgumentException("Password criptata non può essere vuota");

        try
        {
            var key = Convert.FromBase64String(_vaultKeyService.GetEncryptionKey());
            var buffer = Convert.FromBase64String(encryptedPassword);

            if (buffer.Length < 16)
                throw new ArgumentException("Dati criptati non validi (troppo corti)");

            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.IV = buffer.Take(16).ToArray();

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(buffer, 16, buffer.Length - 16);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);
            return sr.ReadToEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Errore durante decifratura password legacy");
            throw;
        }
    }
}