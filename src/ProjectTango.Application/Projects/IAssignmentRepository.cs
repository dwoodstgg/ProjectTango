using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Projects;

public record AssignmentSummary(ProjectAssignment Assignment, string EmployeeName, string? DefaultRoleName);

public interface IAssignmentRepository
{
    Task<IReadOnlyList<AssignmentSummary>> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<ProjectAssignment?> GetAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    Task<ProjectAssignment?> GetByProjectAndEmployeeAsync(Guid projectId, Guid employeeId, CancellationToken cancellationToken = default);

    Task AddAsync(ProjectAssignment assignment, CancellationToken cancellationToken = default);

    Task UpdateAsync(ProjectAssignment assignment, CancellationToken cancellationToken = default);
}
