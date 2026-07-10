using Dapper;
using Npgsql;
using ProjectTango.Application.Projects;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Infrastructure.Persistence.Repositories;

public class BudgetAlertRepository(NpgsqlDataSource dataSource) : IBudgetAlertRepository
{
    public async Task<IReadOnlySet<string>> GetFiredKeysAsync(Guid budgetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var keys = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT alert_key FROM budget_alerts WHERE budget_id = @budgetId",
            new { budgetId },
            cancellationToken: cancellationToken));
        return keys.ToHashSet(StringComparer.Ordinal);
    }

    public async Task RecordAsync(BudgetAlert alert, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO budget_alerts (id, budget_id, alert_key, burn_percent, notified_at)
            VALUES (@Id, @BudgetId, @AlertKey, @BurnPercent, @NotifiedAt)
            ON CONFLICT (budget_id, alert_key) DO NOTHING
            """,
            new { alert.Id, alert.BudgetId, alert.AlertKey, alert.BurnPercent, alert.NotifiedAt },
            cancellationToken: cancellationToken));
    }

    public async Task ClearForBudgetAsync(Guid budgetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM budget_alerts WHERE budget_id = @budgetId",
            new { budgetId },
            cancellationToken: cancellationToken));
    }
}
