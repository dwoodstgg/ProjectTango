using System.Text.Json;
using Dapper;
using Npgsql;
using Crosscheck.Application.Projects;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;

namespace Crosscheck.Infrastructure.Persistence.Repositories;

public class ProjectRepository(NpgsqlDataSource dataSource) : IProjectRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string SelectColumns =
        """
        SELECT p.id, p.client_id, p.name, p.code, p.status, p.project_type, p.closed_at, p.closed_by AS closed_by_id,
               p.project_manager_id, p.start_date, p.end_date, p.currency, p.breakdown_label,
               p.billing_contact_name, p.billing_contact_email,
               p.billing_address::text AS billing_address_json, p.payment_terms_days
        FROM projects p
        """;

    public async Task<IReadOnlyList<ProjectSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ProjectRow>(new CommandDefinition(
            """
            SELECT p.id, p.client_id, p.name, p.code, p.status, p.project_type, p.closed_at, p.closed_by AS closed_by_id,
                   p.project_manager_id, p.start_date, p.end_date, p.currency, p.breakdown_label,
                   p.billing_contact_name, p.billing_contact_email,
                   p.billing_address::text AS billing_address_json, p.payment_terms_days,
                   c.name AS client_name, e.display_name AS project_manager_name,
                   EXISTS (SELECT 1 FROM time_entries te WHERE te.project_id = p.id) AS has_time_entries
            FROM projects p
            JOIN clients c ON c.id = p.client_id
            JOIN employees e ON e.id = p.project_manager_id
            ORDER BY p.code
            """,
            cancellationToken: cancellationToken));
        return rows.Select(r =>
            new ProjectSummary(ToProject(r), r.ClientName!, r.ProjectManagerName!, r.HasTimeEntries)).ToList();
    }

    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ProjectRow>(new CommandDefinition(
            $"{SelectColumns} WHERE p.id = @id",
            new { id },
            cancellationToken: cancellationToken));
        return row is null ? null : ToProject(row);
    }

    public async Task<Project?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ProjectRow>(new CommandDefinition(
            $"{SelectColumns} WHERE p.code = @code",
            new { code },
            cancellationToken: cancellationToken));
        return row is null ? null : ToProject(row);
    }

    public async Task<Project?> GetByClientAndCodeAsync(Guid clientId, string code, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ProjectRow>(new CommandDefinition(
            $"{SelectColumns} WHERE p.client_id = @clientId AND p.code = @code",
            new { clientId, code },
            cancellationToken: cancellationToken));
        return row is null ? null : ToProject(row);
    }

    public async Task AddAsync(Project project, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO projects
                (id, client_id, name, code, status, project_type, project_manager_id, start_date, end_date, currency,
                 breakdown_label, billing_contact_name, billing_contact_email, billing_address, payment_terms_days)
            VALUES
                (@Id, @ClientId, @Name, @Code, @status, @projectType, @ProjectManagerId, @StartDate, @EndDate, @Currency,
                 @breakdownLabel, @BillingContactName, @BillingContactEmail, @billingAddressJson::jsonb, @PaymentTermsDays)
            """,
            InsertParams(project),
            cancellationToken: cancellationToken));
    }

    public async Task UpdateAsync(Project project, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE projects SET
                client_id = @ClientId,
                name = @Name,
                code = @Code,
                project_type = @projectType,
                project_manager_id = @ProjectManagerId,
                start_date = @StartDate,
                end_date = @EndDate,
                breakdown_label = @breakdownLabel,
                billing_contact_name = @BillingContactName,
                billing_contact_email = @BillingContactEmail,
                billing_address = @billingAddressJson::jsonb,
                payment_terms_days = @PaymentTermsDays
            WHERE id = @Id
            """,
            InsertParams(project),
            cancellationToken: cancellationToken));
    }

    public async Task SetStatusAsync(Guid projectId, ProjectStatus status, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE projects SET status = @status WHERE id = @projectId",
            new { projectId, status = DbEnum.ToDb(status) },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> HasTimeEntriesAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM time_entries WHERE project_id = @projectId)",
            new { projectId },
            cancellationToken: cancellationToken));
    }

    public async Task DeleteAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Child rows first (module/budget grandchildren cascade from their parents). The
        // time_entries FK still backstops the service-layer "no time logged" guard: any
        // entry that snuck in makes this delete fail rather than orphan data.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM project_module_rates
            WHERE module_id IN (SELECT id FROM project_modules WHERE project_id = @projectId);
            DELETE FROM project_modules WHERE project_id = @projectId;
            DELETE FROM budget_alerts
            WHERE budget_id IN (SELECT id FROM budgets WHERE project_id = @projectId);
            DELETE FROM budget_revisions
            WHERE budget_id IN (SELECT id FROM budgets WHERE project_id = @projectId);
            DELETE FROM budgets WHERE project_id = @projectId;
            DELETE FROM project_rate_cards WHERE project_id = @projectId;
            DELETE FROM project_assignments WHERE project_id = @projectId;
            DELETE FROM projects WHERE id = @projectId;
            """,
            new { projectId },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
    }

    private static object InsertParams(Project project) => new
    {
        project.Id,
        project.ClientId,
        project.Name,
        project.Code,
        status = DbEnum.ToDb(project.Status),
        projectType = DbEnum.ToDb(project.Type),
        project.ProjectManagerId,
        project.StartDate,
        project.EndDate,
        project.Currency,
        breakdownLabel = DbEnum.ToDb(project.BreakdownLabel),
        project.BillingContactName,
        project.BillingContactEmail,
        billingAddressJson = project.BillingAddress is null
            ? null
            : JsonSerializer.Serialize(project.BillingAddress, JsonOptions),
        project.PaymentTermsDays,
    };

    private static Project ToProject(ProjectRow row) => new()
    {
        Id = row.Id,
        ClientId = row.ClientId,
        Name = row.Name,
        Code = row.Code,
        Status = DbEnum.FromDb<ProjectStatus>(row.Status),
        Type = DbEnum.FromDb<ProjectType>(row.ProjectType),
        ClosedAt = row.ClosedAt,
        ClosedById = row.ClosedById,
        ProjectManagerId = row.ProjectManagerId,
        StartDate = row.StartDate,
        EndDate = row.EndDate,
        Currency = row.Currency,
        BreakdownLabel = DbEnum.FromDb<BreakdownLabel>(row.BreakdownLabel),
        BillingContactName = row.BillingContactName,
        BillingContactEmail = row.BillingContactEmail,
        BillingAddress = row.BillingAddressJson is null
            ? null
            : JsonSerializer.Deserialize<BillingAddress>(row.BillingAddressJson, JsonOptions),
        PaymentTermsDays = row.PaymentTermsDays,
    };

    // Property-based (not a positional record): Dapper materializes via setters, which
    // tolerates provider type differences (timestamptz→DateTimeOffset) that strict
    // constructor matching does not.
    private sealed class ProjectRow
    {
        public Guid Id { get; set; }
        public Guid ClientId { get; set; }
        public string Name { get; set; } = null!;
        public string Code { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string ProjectType { get; set; } = null!;
        public DateTimeOffset? ClosedAt { get; set; }
        public Guid? ClosedById { get; set; }
        public Guid ProjectManagerId { get; set; }
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public string Currency { get; set; } = null!;
        public string BreakdownLabel { get; set; } = null!;
        public string? BillingContactName { get; set; }
        public string? BillingContactEmail { get; set; }
        public string? BillingAddressJson { get; set; }
        public int? PaymentTermsDays { get; set; }
        public string? ClientName { get; set; }
        public string? ProjectManagerName { get; set; }
        public bool HasTimeEntries { get; set; }
    }
}
