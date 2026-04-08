namespace LocalAuthService.Tests;

/// <summary>
/// Security Fix 1.2: Database must be encrypted with SQLCipher (AES-256).
/// An attacker who copies auth.db must NOT be able to read it without the key.
///
/// STATUS: ❌ RED — SQLCipher not yet implemented (Phase 1.2 pending)
/// These tests will turn GREEN when SQLCipher is added.
/// </summary>
public class DatabaseEncryptionTests
{
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LocalAuthService",
        "auth.db");

    [Fact(Skip = "Phase 1.2 not implemented — will be GREEN after SQLCipher integration")]
    public void DatabaseFile_CannotBeOpenedWithPlainSqlite()
    {
        // After SQLCipher: opening auth.db without password must fail
        Assert.True(File.Exists(DbPath), "Database file not found — start the app first");

        var connectionString = $"Data Source={DbPath}";
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Users";

        // With SQLCipher, this must throw — file is encrypted
        Assert.ThrowsAny<Exception>(() => cmd.ExecuteScalar());
    }

    [Fact]
    public void DatabaseFile_IsCurrentlyUnencrypted_PendingSQLCipher()
    {
        // This test documents the CURRENT (insecure) state.
        // It must be REMOVED and replaced by the test above once SQLCipher is implemented.
        if (!File.Exists(DbPath))
        {
            // App not started yet — skip
            return;
        }

        var connectionString = $"Data Source={DbPath}";
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Users";
        var count = cmd.ExecuteScalar();

        // Currently succeeds — DB is NOT encrypted (known vulnerability, Phase 1.2)
        Assert.NotNull(count);
    }
}