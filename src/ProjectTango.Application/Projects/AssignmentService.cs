using ProjectTango.Application.Common;
using ProjectTango.Application.Employees;
using ProjectTango.Application.Roles;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Projects;

/// <summary>Team assignment. One row per person per project — ending an assignment
/// sets its end date (nothing is deleted); assigning the same person again reopens it.</summary>
public class AssignmentService(
    ICurrentUser currentUser,
    IProjectRepository projects,
    IAssignmentRepository assignments,
    IEmployeeRepository employees,
    IRoleRepository roles,
    IAuditLog audit)
{
    public async Task<IReadOnlyList<AssignmentSummary>> ListForProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager, RoleNames.ProjectManager);
        return await assignments.GetForProjectAsync(projectId, cancellationToken);
    }

    public async Task AssignAsync(
        Guid projectId, Guid employeeId, Guid? defaultBillingRoleId, DateOnly? startDate,
        CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");
        var adminOverride = currentUser.RequireCanManage(project);

        var employee = await employees.GetByIdAsync(employeeId, cancellationToken);
        if (employee is null || !employee.IsActive)
        {
            throw new DomainException("Employee must exist and be active.");
        }

        if (defaultBillingRoleId is not null)
        {
            var role = await roles.GetByIdAsync(defaultBillingRoleId.Value, cancellationToken)
                ?? throw new DomainException("Unknown billing role.");
            if (!role.IsBillable)
            {
                throw new DomainException($"{role.Name} is not a billable role.");
            }
        }

        var existing = await assignments.GetByProjectAndEmployeeAsync(projectId, employeeId, cancellationToken);
        if (existing is not null)
        {
            if (existing.EndDate is null)
            {
                throw new DomainException($"{employee.DisplayName} is already assigned to this project.");
            }

            // Reopen the ended assignment (unique row per person per project).
            existing.EndDate = null;
            existing.DefaultBillingRoleId = defaultBillingRoleId;
            existing.StartDate = startDate ?? existing.StartDate;
            await assignments.UpdateAsync(existing, cancellationToken);

            await audit.WriteAsync(new AuditEvent(
                currentUser.EmployeeId, "assignment.reopened", "project", projectId,
                new { Employee = employee.DisplayName, adminOverride }), cancellationToken);
            return;
        }

        var assignment = new ProjectAssignment
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            EmployeeId = employeeId,
            DefaultBillingRoleId = defaultBillingRoleId,
            StartDate = startDate,
        };
        await assignments.AddAsync(assignment, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "assignment.added", "project", projectId,
            new { Employee = employee.DisplayName, adminOverride }), cancellationToken);
    }

    public async Task EndAsync(Guid assignmentId, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        var assignment = await assignments.GetAsync(assignmentId, cancellationToken)
            ?? throw new DomainException("Unknown assignment.");
        var project = await projects.GetByIdAsync(assignment.ProjectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");
        var adminOverride = currentUser.RequireCanManage(project);

        if (assignment.EndDate is not null)
        {
            throw new DomainException("Assignment is already ended.");
        }

        if (assignment.StartDate is not null && endDate < assignment.StartDate)
        {
            throw new DomainException("End date cannot be before the assignment's start date.");
        }

        assignment.EndDate = endDate;
        await assignments.UpdateAsync(assignment, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "assignment.ended", "project", assignment.ProjectId,
            new { assignment.EmployeeId, EndDate = endDate.ToString("yyyy-MM-dd"), adminOverride }), cancellationToken);
    }
}
