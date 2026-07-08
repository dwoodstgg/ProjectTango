using DbUp;

namespace ProjectTango.Infrastructure.Persistence;

/// <summary>Runs the embedded SQL scripts (Persistence/Scripts) in order via DbUp.
/// Applied scripts are journaled in the schemaversions table and never re-run.</summary>
public static class DatabaseMigrator
{
    public static void MigrateToLatest(string connectionString)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DatabaseMigrator).Assembly)
            .WithTransactionPerScript()
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Database migration failed on script '{result.ErrorScript?.Name}'.", result.Error);
        }
    }
}
