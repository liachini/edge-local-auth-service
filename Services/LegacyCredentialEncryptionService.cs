using System.Security.Cryptography;
using System.Text;

namespace LocalAuthService.Services;

/// <summary>
/// Encrypts/Decrypts legacy credentials using AES-256
/// Key is derived from machine name (machine-bound)
/// </summary>
public class LegacyCredentialEncryptionService
{
    private readonly string _encryptionKey;
    private readonly ILogger<LegacyCredentialEncryptionService> _logger;

    // Salt costante (non random) per derivazione deterministica della chiave
    private const string KeyDerivationSalt = "LocalAuthService-LegacyCredentials-v1";

    public LegacyCredentialEncryptionService(ILogger<LegacyCredentialEncryptionService> logger)
    {
        _logger = logger;
        _encryptionKey = DeriveKeyFromMachine();
        _logger.LogInformation("✅ LegacyCredentialEncryptionService initialized (machine-bound key)");
    }

    /// <summary>
    /// Deriva chiave AES-256 da nome macchina usando PBKDF2-SHA256
    /// Così la chiave è unica per macchina e deterministica
    /// </summary>
    private string DeriveKeyFromMachine()
    {
        var machineId = Environment.MachineName;

        try
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(
                machineId,
                Encoding.UTF8.GetBytes(KeyDerivationSalt),
                iterations: 10000,
                HashAlgorithmName.SHA256))
            {
                var derivedKey = pbkdf2.GetBytes(32); // 256 bit per AES-256
                return Convert.ToBase64String(derivedKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Errore derivazione chiave crittografica da machine name");
            throw;
        }
    }

    /// <summary>
    /// Cripta una password plaintext
    /// Restituisce: [IV (16 bytes)][Ciphertext] in Base64
    /// </summary>
    public string Encrypt(string plainPassword)
    {
        if (string.IsNullOrEmpty(plainPassword))
            throw new ArgumentException("Password non può essere vuota");

        try
        {
            var key = Convert.FromBase64String(_encryptionKey);

            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream())
                {
                    // Scrivi IV in chiaro (primo 16 bytes) per poterlo usare in decrypt
                    ms.Write(aes.IV, 0, aes.IV.Length);

                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    using (var sw = new StreamWriter(cs, Encoding.UTF8))
                    {
                        sw.Write(plainPassword);
                    }

                    var encryptedBytes = ms.ToArray();
                    return Convert.ToBase64String(encryptedBytes);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Errore durante cifratura password legacy");
            throw;
        }
    }

    /// <summary>
    /// Decripta una password criptata
    /// Estrae IV dai primi 16 bytes
    /// </summary>
    public string Decrypt(string encryptedPassword)
    {
        if (string.IsNullOrEmpty(encryptedPassword))
            throw new ArgumentException("Password criptata non può essere vuota");

        try
        {
            var key = Convert.FromBase64String(_encryptionKey);
            var buffer = Convert.FromBase64String(encryptedPassword);

            if (buffer.Length < 16)
                throw new ArgumentException("Dati criptati non validi (troppo corti)");

            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                // Estrai IV dai primi 16 bytes
                aes.IV = buffer.Take(16).ToArray();

                using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream(buffer, 16, buffer.Length - 16))
                using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                using (var sr = new StreamReader(cs, Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Errore durante decifratura password legacy");
            throw;
        }
    }
}
