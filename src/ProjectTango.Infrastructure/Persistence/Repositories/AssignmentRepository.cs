using Dapper;
using Npgsql;
using ProjectTango.Application.Projects;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Infrastructure.Persistence.Repositories;

public class AssignmentRepository(NpgsqlDataSource dataSource) : IAssignmentRepository
{
    private const string SelectColumns =
        """
        SELECT a.id, a.project_id, a.employee_id, a.default_billing_role_id, a.start_date, a.end_date
        FROM project_assignments a
        """;

    public async Task<IReadOnlyList<AssignmentSummary>> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AssignmentRow>(new CommandDefinition(
            """
            SELECT a.id, a.project_id, a.employee_id, a.default_billing_role_id, a.start_date, a.end_date,
                   e.display_name AS employee_name, r.name AS default_role_name
            FROM project_assignments a
            JOIN employees e ON e.id = a.employee_id
            LEFT JOIN roles r ON r.id = a.default_billing_role_id
            WHERE a.project_id = @projectId
            ORDER BY e.display_name
            """,
            new { projectId },
            cancellationToken: cancellationToken));
        return rows.Select(row => new AssignmentSummary(ToEntity(row), row.EmployeeName!, row.DefaultRoleName)).ToList();
    }

    public async Task<ProjectAssignment?> GetAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<AssignmentRow>(new CommandDefinition(
            $"{SelectColumns} WHERE a.id = @assignmentId",
            new { assignmentId },
            cancellationToken: cancellationToken));
        return row is null ? null : ToEntity(row);
    }

    public async Task<ProjectAssignment?> GetByProjectAndEmployeeAsync(Guid projectId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<AssignmentRow>(new CommandDefinition(
            $"{SelectColumns} WHERE a.project_id = @projectId AND a.employee_id = @employeeId",
            new { projectId, employeeId },
            cancellationToken: cancellationToken));
        return row is null ? null : ToEntity(row);
    }

    public async Task AddAsync(ProjectAssignment assignment, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO project_assignments (id, project_id, employee_id, default_billing_role_id, start_date, end_date)
            VALUES (@Id, @ProjectId, @EmployeeId, @DefaultBillingRoleId, @StartDate, @EndDate)
            """,
            new
            {
                assignment.Id,
                assignment.ProjectId,
                assignment.EmployeeId,
                assignment.DefaultBillingRoleId,
                assignment.StartDate,
                assignment.EndDate,
            },
            cancellationToken: cancellationToken));
    }

    public async Task UpdateAsync(ProjectAssignment assignment, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE project_assignments SET
                default_billing_role_id = @DefaultBillingRoleId,
                start_date = @StartDate,
                end_date = @EndDate
            WHERE id = @Id
            """,
            new
            {
                assignment.Id,
                assignment.DefaultBillingRoleId,
                assignment.StartDate,
                assignment.EndDate,
            },
            cancellationToken: cancellationToken));
    }

    private static ProjectAssignment ToEntity(AssignmentRow row) => new()
    {
        Id = row.Id,
        ProjectId = row.ProjectId,
        EmployeeId = row.EmployeeId,
        DefaultBillingRoleId = row.DefaultBillingRoleId,
        StartDate = row.StartDate,
        EndDate = row.EndDate,
    };

    private sealed class AssignmentRow
    {
        public Guid Id { get; set; }
        public Guid ProjectId { get; set; }
        public Guid EmployeeId { get; set; }
        public Guid? DefaultBillingRoleId { get; set; }
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public string? EmployeeName { get; set; }
        public string? DefaultRoleName { get; set; }
    }
}
