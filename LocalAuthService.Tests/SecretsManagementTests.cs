using System.Text.RegularExpressions;

namespace LocalAuthService.Tests;

/// <summary>
/// Security Fix 1.3: No secrets hardcoded in source code.
/// Client secrets must come from configuration (env vars / user secrets),
/// never from string literals in .cs files.
/// </summary>
public class SecretsManagementTests
{
    // Known dev-only secrets (acceptable in appsettings.Development.json, not in .cs files)
    private static readonly string[] KnownDevSecrets =
    [
        "mes-secret-123",
        "office-secret-456",
        "cli-simulator-secret-789",
        "legacy-manager-secret-456",
        "erp-simulator-secret-789",
        "crm-simulator-secret-789",
        "unauthorized-test-secret"
    ];

    private static readonly string SrcPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "LocalAuthService"));

    [Fact]
    public void ProgramCs_ContainsNoHardcodedClientSecrets()
    {
        var programCs = Path.Combine(SrcPath, "Program.cs");
        Assert.True(File.Exists(programCs), $"Program.cs not found at {programCs}");

        var content = File.ReadAllText(programCs);

        foreach (var secret in KnownDevSecrets)
        {
            Assert.False(content.Contains(secret),
                $"Hardcoded secret '{secret}' found in Program.cs — must be read from configuration");
        }
    }

    [Fact]
    public void ProgramCs_ReadsSecretsFromConfiguration()
    {
        var programCs = Path.Combine(SrcPath, "Program.cs");
        var content = File.ReadAllText(programCs);

        // Verify secrets are read from IConfiguration, not hardcoded
        Assert.Contains("builder.Configuration[\"Clients:", content);
    }

    [Fact]
    public void AppSettingsJson_ContainsNoProductionSecrets()
    {
        var appSettings = Path.Combine(SrcPath, "appsettings.json");
        Assert.True(File.Exists(appSettings), $"appsettings.json not found at {appSettings}");

        var content = File.ReadAllText(appSettings);

        foreach (var secret in KnownDevSecrets)
        {
            Assert.False(content.Contains(secret),
                $"Secret '{secret}' found in appsettings.json — production config must not contain secrets");
        }
    }

    [Fact]
    public void SourceCode_ContainsNoHardcodedClientSecretPattern()
    {
        // Scan all .cs files for ClientSecret = "literal-value" pattern
        var csFiles = Directory.GetFiles(SrcPath, "*.cs", SearchOption.AllDirectories);
        var secretPattern = new Regex(@"ClientSecret\s*=\s*""[a-zA-Z0-9\-]+""\s*,");

        var violations = csFiles
            .Select(f => (File: f, Content: File.ReadAllText(f)))
            .Where(x => secretPattern.IsMatch(x.Content))
            .Select(x => Path.GetRelativePath(SrcPath, x.File))
            .ToList();

        Assert.Empty(violations);
    }
}