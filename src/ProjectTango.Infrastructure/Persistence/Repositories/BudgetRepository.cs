using Dapper;
using Npgsql;
using ProjectTango.Application.Projects;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;

namespace ProjectTango.Infrastructure.Persistence.Repositories;

public class BudgetRepository(NpgsqlDataSource dataSource) : IBudgetRepository
{
    public async Task<Budget?> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<BudgetRow>(new CommandDefinition(
            """
            SELECT id, project_id, type, amount, hours, alert_thresholds, created_at, updated_at
            FROM budgets
            WHERE project_id = @projectId
            """,
            new { projectId },
            cancellationToken: cancellationToken));
        return row is null ? null : ToEntity(row);
    }

    public async Task SaveAsync(Budget budget, BudgetRevision revision, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The budget row holds the current values; existing rows are updated in place while
        // history accumulates in budget_revisions. created_at is preserved across updates.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO budgets (id, project_id, type, amount, hours, alert_thresholds, created_at, updated_at)
            VALUES (@Id, @ProjectId, @Type, @Amount, @Hours, @AlertThresholds, @CreatedAt, @UpdatedAt)
            ON CONFLICT (id) DO UPDATE SET
                type = EXCLUDED.type,
                amount = EXCLUDED.amount,
                hours = EXCLUDED.hours,
                alert_thresholds = EXCLUDED.alert_thresholds,
                updated_at = EXCLUDED.updated_at
            """,
            new
            {
                budget.Id,
                budget.ProjectId,
                Type = DbEnum.ToDb(budget.Type),
                budget.Amount,
                budget.Hours,
                budget.AlertThresholds,
                budget.CreatedAt,
                budget.UpdatedAt,
            },
            transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO budget_revisions
                (id, budget_id, revised_by, revised_at, from_type, from_amount, from_hours, to_type, to_amount, to_hours, reason)
            VALUES
                (@Id, @BudgetId, @RevisedById, @RevisedAt, @FromType, @FromAmount, @FromHours, @ToType, @ToAmount, @ToHours, @Reason)
            """,
            new
            {
                revision.Id,
                revision.BudgetId,
                revision.RevisedById,
                revision.RevisedAt,
                FromType = revision.FromType is null ? null : DbEnum.ToDb(revision.FromType.Value),
                revision.FromAmount,
                revision.FromHours,
                ToType = DbEnum.ToDb(revision.ToType),
                revision.ToAmount,
                revision.ToHours,
                revision.Reason,
            },
            transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BudgetRevisionSummary>> GetRevisionsAsync(Guid budgetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<BudgetRevisionRow>(new CommandDefinition(
            """
            SELECT br.id, br.budget_id, br.revised_by AS revised_by_id, br.revised_at,
                   br.from_type, br.from_amount, br.from_hours,
                   br.to_type, br.to_amount, br.to_hours, br.reason,
                   e.display_name AS revised_by_name
            FROM budget_revisions br
            JOIN employees e ON e.id = br.revised_by
            WHERE br.budget_id = @budgetId
            ORDER BY br.revised_at DESC
            """,
            new { budgetId },
            cancellationToken: cancellationToken));

        return rows.Select(row => new BudgetRevisionSummary(
            new BudgetRevision
            {
                Id = row.Id,
                BudgetId = row.BudgetId,
                RevisedById = row.RevisedById,
                RevisedAt = row.RevisedAt,
                FromType = row.FromType is null ? null : DbEnum.FromDb<BudgetType>(row.FromType),
                FromAmount = row.FromAmount,
                FromHours = row.FromHours,
                ToType = DbEnum.FromDb<BudgetType>(row.ToType!),
                ToAmount = row.ToAmount,
                ToHours = row.ToHours,
                Reason = row.Reason,
            },
            row.RevisedByName!)).ToList();
    }

    private static Budget ToEntity(BudgetRow row) => new()
    {
        Id = row.Id,
        ProjectId = row.ProjectId,
        Type = DbEnum.FromDb<BudgetType>(row.Type!),
        Amount = row.Amount,
        Hours = row.Hours,
        AlertThresholds = row.AlertThresholds ?? [],
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt,
    };

    private sealed class BudgetRow
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        public string? Type { get; set; }
        public decimal? Amount { get; set; }
        public decimal? Hours { get; set; }
        public int[]? AlertThresholds { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class BudgetRevisionRow
    {
        public Guid Id { get; set; }
        public Guid BudgetId { get; set; }
        public Guid RevisedById { get; set; }
        public DateTimeOffset RevisedAt { get; set; }
        public string? FromType { get; set; }
        public decimal? FromAmount { get; set; }
        public decimal? FromHours { get; set; }
        public string? ToType { get; set; }
        public decimal? ToAmount { get; set; }
        public decimal? ToHours { get; set; }
        public string? Reason { get; set; }
        public string? RevisedByName { get; set; }
    }
}
