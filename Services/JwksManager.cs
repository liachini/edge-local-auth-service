using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;

namespace LocalAuthService.Services;

public class JwksManager
{
    private readonly string _jwksPath;
    private RsaSecurityKey? _cachedKey;

    public JwksManager(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _jwksPath = Path.Combine(dataDir, "jwks.json");
    }

    public RsaSecurityKey GetOrCreateKey()
    {
        if (_cachedKey != null)
            return _cachedKey;

        if (File.Exists(_jwksPath))
        {
            Console.WriteLine($"🔑 Loading existing JWKS from: {_jwksPath}");
            var json = File.ReadAllText(_jwksPath);
            var parameters = JsonSerializer.Deserialize<RSAParametersSerializable>(json);
            
            var rsa = RSA.Create();
            rsa.ImportParameters(parameters!.ToRSAParameters());
            
            _cachedKey = new RsaSecurityKey(rsa)
            {
                KeyId = $"{Environment.MachineName}-{DateTime.UtcNow:yyyyMMdd}"
            };
            
            return _cachedKey;
        }
        else
        {
            Console.WriteLine($"🔐 Generating new JWKS...");
            var rsa = RSA.Create(2048);
            var parameters = rsa.ExportParameters(includePrivateParameters: true);
            
            // Salva su disco
            var serializable = new RSAParametersSerializable(parameters);
            var json = JsonSerializer.Serialize(serializable, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_jwksPath, json);
            
            _cachedKey = new RsaSecurityKey(rsa)
            {
                KeyId = $"{Environment.MachineName}-{DateTime.UtcNow:yyyyMMdd}"
            };
            
            Console.WriteLine($"✅ JWKS created and saved to: {_jwksPath}");
            Console.WriteLine($"   KeyId: {_cachedKey.KeyId}");
            
            return _cachedKey;
        }
    }

    // Classe helper per serializzare RSAParameters
    private class RSAParametersSerializable
    {
        public byte[]? D { get; set; }
        public byte[]? DP { get; set; }
        public byte[]? DQ { get; set; }
        public byte[]? Exponent { get; set; }
        public byte[]? InverseQ { get; set; }
        public byte[]? Modulus { get; set; }
        public byte[]? P { get; set; }
        public byte[]? Q { get; set; }

        public RSAParametersSerializable() { }

        public RSAParametersSerializable(RSAParameters parameters)
        {
            D = parameters.D;
            DP = parameters.DP;
            DQ = parameters.DQ;
            Exponent = parameters.Exponent;
            InverseQ = parameters.InverseQ;
            Modulus = parameters.Modulus;
            P = parameters.P;
            Q = parameters.Q;
        }

        public RSAParameters ToRSAParameters()
        {
            return new RSAParameters
            {
                D = D,
                DP = DP,
                DQ = DQ,
                Exponent = Exponent,
                InverseQ = InverseQ,
                Modulus = Modulus,
                P = P,
                Q = Q
            };
        }
    }
}