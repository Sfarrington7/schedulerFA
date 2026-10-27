// Purpose: Names, connection strings, and cleanup for throwaway SQL Server test databases. The server comes
// from the FAS_TEST_SQLSERVER environment variable (no database name), defaulting to localhost with Windows auth.
using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;

namespace FireAlarmScheduler.Tests.TestSupport;

public static class TestDatabase
{
    private const string DefaultServer = "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>Databases handed out to tests; only these are dropped, so file-only tests never need SQL Server.</summary>
    private static readonly ConcurrentDictionary<string, bool> Used = new();

    private static string Server => Environment.GetEnvironmentVariable("FAS_TEST_SQLSERVER") ?? DefaultServer;

    /// <summary>One database per temporary folder, so a second harness on the same folder sees the same data.</summary>
    public static string NameFor(string tempDirectory) => "FasTest_" + Path.GetFileName(tempDirectory).Replace('-', '_');

    /// <summary>Pooling is off so no idle connection keeps the database open when the test drops it.</summary>
    public static string ConnectionStringFor(string databaseName)
    {
        Used[databaseName] = true;
        return BuildConnectionString(databaseName);
    }

    /// <summary>Drops the database if a test used it; does nothing otherwise.</summary>
    public static void DropIfUsed(string databaseName)
    {
        if (!Used.TryRemove(databaseName, out _))
        {
            return;
        }

        using var connection = new SqlConnection(BuildConnectionString("master"));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END
            """;
        command.ExecuteNonQuery();
    }

    private static string BuildConnectionString(string databaseName) =>
        new SqlConnectionStringBuilder(Server) { InitialCatalog = databaseName, Pooling = false }.ConnectionString;
}
