using Dapper;
using Npgsql;
using ProjectTango.Application.Projects;
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
                   r.display_name AS role_name
            FROM project_rate_cards rc
            JOIN roles r ON r.id = rc.role_id
            WHERE rc.project_id = @projectId
            ORDER BY r.display_name, rc.effective_from DESC
            """,
            new { projectId },
            cancellationToken: cancellationToken));
        return rows.Select(row => new RateCardSummary(ToEntity(row), row.RoleName!)).ToList();
    }

    public async Task<IReadOnlyList<ProjectRateCard>> GetForRoleAsync(Guid projectId, Guid roleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<RateCardRow>(new CommandDefinition(
            """
            SELECT id, project_id, role_id, hourly_rate, effective_from, effective_to
            FROM project_rate_cards
            WHERE project_id = @projectId AND role_id = @roleId
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

    public async Task<decimal?> ResolveAsync(Guid projectId, Guid roleId, DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition(
            """
            SELECT hourly_rate FROM project_rate_cards
            WHERE project_id = @projectId AND role_id = @roleId
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
    }
}
