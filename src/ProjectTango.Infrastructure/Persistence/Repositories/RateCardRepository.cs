using Dapper;
using Npgsql;
using ProjectTango.Application.Projects;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Infrastructure.Persistence.Repositories;

public class RateCardRepository(NpgsqlDataSource dataSource) : IRateCardRepository
{
    public async Task<IReadOnlyList<RateCardSummary>> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<RateCardRow>(new CommandDefinition(
            """
            SELECT rc.id, rc.project_id, rc.role_id, rc.hourly_rate, rc.effective_from, rc.effective_to,
                   r.display_name AS role_name,
                   EXISTS (
                       SELECT 1 FROM time_entries te
                       WHERE te.project_id = rc.project_id AND te.billing_role_id = rc.role_id
                         AND te.status = 'invoiced'
                         AND te.entry_date >= rc.effective_from
                         AND (rc.effective_to IS NULL OR te.entry_date <= rc.effective_to)
                   ) AS has_billed_time
            FROM project_rate_cards rc
            JOIN roles r ON r.id = rc.role_id
            WHERE rc.project_id = @projectId AND rc.deleted_at IS NULL
            ORDER BY r.display_name, rc.effective_from DESC
            """,
            new { projectId },
            cancellationToken: cancellationToken));
        return rows.Select(row => new RateCardSummary(ToEntity(row), row.RoleName!, row.HasBilledTime)).ToList();
    }

    public async Task<ProjectRateCard?> GetByIdAsync(Guid rateCardId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<RateCardRow>(new CommandDefinition(
            """
            SELECT id, project_id, role_id, hourly_rate, effective_from, effective_to
            FROM project_rate_cards
            WHERE id = @rateCardId AND deleted_at IS NULL
            """,
            new { rateCardId },
            cancellationToken: cancellationToken));
        return row is null ? null : ToEntity(row);
    }

    public async Task<IReadOnlyList<ProjectRateCard>> GetForRoleAsync(Guid projectId, Guid roleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<RateCardRow>(new CommandDefinition(
            """
            SELECT id, project_id, role_id, hourly_rate, effective_from, effective_to
            FROM project_rate_cards
            WHERE project_id = @projectId AND role_id = @roleId AND deleted_at IS NULL
            ORDER BY effective_from
            """,
            new { projectId, roleId },
            cancellationToken: cancellationToken));
        return rows.Select(ToEntity).ToList();
    }

    public async Task AddAsync(ProjectRateCard rateCard, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO project_rate_cards (id, project_id, role_id, hourly_rate, effective_from, effective_to)
            VALUES (@Id, @ProjectId, @RoleId, @HourlyRate, @EffectiveFrom, @EffectiveTo)
            """,
            new
            {
                rateCard.Id,
                rateCard.ProjectId,
                rateCard.RoleId,
                rateCard.HourlyRate,
                rateCard.EffectiveFrom,
                rateCard.EffectiveTo,
            },
            cancellationToken: cancellationToken));
    }

    public async Task CloseAsync(Guid rateCardId, DateOnly effectiveTo, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE project_rate_cards SET effective_to = @effectiveTo WHERE id = @rateCardId AND effective_to IS NULL",
            new { rateCardId, effectiveTo },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> HasInvoicedTimeAsync(
        Guid projectId, Guid roleId, DateOnly effectiveFrom, DateOnly? effectiveTo,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (
                SELECT 1 FROM time_entries
                WHERE project_id = @projectId AND billing_role_id = @roleId
                  AND status = 'invoiced'
                  AND entry_date >= @effectiveFrom
                  AND (@effectiveTo::date IS NULL OR entry_date <= @effectiveTo::date)
            )
            """,
            new { projectId, roleId, effectiveFrom, effectiveTo },
            cancellationToken: cancellationToken));
    }

    public async Task CorrectAsync(
        Guid rateCardId, decimal hourlyRate, DateOnly effectiveFrom,
        Guid? priorRowId, DateOnly? priorEffectiveTo,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            // Defer the no-overlap check to commit so re-closing the predecessor and shifting
            // this row's start can happen in either order without a transient overlap.
            await connection.ExecuteAsync(new CommandDefinition(
                "SET CONSTRAINTS ex_rate_cards_no_overlap DEFERRED",
                transaction: transaction, cancellationToken: cancellationToken));

            if (priorRowId is not null)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE project_rate_cards SET effective_to = @priorEffectiveTo WHERE id = @priorRowId AND deleted_at IS NULL",
                    new { priorRowId, priorEffectiveTo }, transaction, cancellationToken: cancellationToken));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE project_rate_cards SET hourly_rate = @hourlyRate, effective_from = @effectiveFrom WHERE id = @rateCardId AND deleted_at IS NULL",
                new { rateCardId, hourlyRate, effectiveFrom }, transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ExclusionViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new DomainException("That effective date overlaps another rate for this role.");
        }
    }

    public async Task SoftDeleteAsync(Guid rateCardId, Guid? reopenPriorRowId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE project_rate_cards SET deleted_at = now() WHERE id = @rateCardId AND deleted_at IS NULL",
            new { rateCardId }, transaction, cancellationToken: cancellationToken));

        if (reopenPriorRowId is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE project_rate_cards SET effective_to = NULL WHERE id = @reopenPriorRowId AND deleted_at IS NULL",
                new { reopenPriorRowId }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<decimal?> ResolveAsync(Guid projectId, Guid roleId, DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition(
            """
            SELECT hourly_rate FROM project_rate_cards
            WHERE project_id = @projectId AND role_id = @roleId AND deleted_at IS NULL
              AND effective_from <= @date
              AND (effective_to IS NULL OR effective_to >= @date)
            """,
            new { projectId, roleId, date },
            cancellationToken: cancellationToken));
    }

    private static ProjectRateCard ToEntity(RateCardRow row) => new()
    {
        Id = row.Id,
        ProjectId = row.ProjectId,
        RoleId = row.RoleId,
        HourlyRate = row.HourlyRate,
        EffectiveFrom = row.EffectiveFrom,
        EffectiveTo = row.EffectiveTo,
    };

    private sealed class RateCardRow
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        public Guid RoleId { get; set; }
        public decimal HourlyRate { get; set; }
        public DateOnly EffectiveFrom { get; set; }
        public DateOnly? EffectiveTo { get; set; }
        public string? RoleName { get; set; }
        public bool HasBilledTime { get; set; }
    }
}
