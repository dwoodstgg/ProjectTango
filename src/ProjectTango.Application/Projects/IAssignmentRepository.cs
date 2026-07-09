using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Projects;

public record AssignmentSummary(ProjectAssignment Assignment, string EmployeeName, string? DefaultRoleName);

/// <summary>An employee's assignment joined with the project it grants access to (for the
/// employee's own timesheet grid).</summary>
public record EmployeeAssignment(ProjectAssignment Assignment, string ProjectCode, string ProjectName, string ClientName);

public interface IAssignmentRepository
{
    Task<IReadOnlyList<AssignmentSummary>> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeAssignment>> GetForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<ProjectAssignment?> GetAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    Task<ProjectAssignment?> GetByProjectAndEmployeeAsync(Guid projectId, Guid employeeId, CancellationToken cancellationToken = default);

    Task AddAsync(ProjectAssignment assignment, CancellationToken cancellationToken = default);

    Task UpdateAsync(ProjectAssignment assignment, CancellationToken cancellationToken = default);
}
