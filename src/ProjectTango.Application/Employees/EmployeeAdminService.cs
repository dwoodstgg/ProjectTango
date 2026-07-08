using ProjectTango.Application.Common;
using ProjectTango.Application.Roles;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;

namespace ProjectTango.Application.Employees;

/// <summary>Employee and role administration (Admin / Operations Manager only).
/// Enforces the last-Admin guard and writes every permission mutation to the audit log.</summary>
public class EmployeeAdminService(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    IRoleRepository roles,
    IAuditLog audit)
{
    public async Task<IReadOnlyList<EmployeeSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        RequireEmployeeAdmin();
        return await employees.GetAllAsync(cancellationToken);
    }

    public async Task<EmployeeSummary?> GetAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        RequireEmployeeAdmin();
        var employee = await employees.GetByIdAsync(employeeId, cancellationToken);
        if (employee is null)
        {
            return null;
        }

        var roleNames = await employees.GetRoleDisplayNamesAsync(employeeId, cancellationToken);
        return new EmployeeSummary(employee, roleNames);
    }

    public async Task<IReadOnlySet<Guid>> GetHeldRoleIdsAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        RequireEmployeeAdmin();
        return await employees.GetRoleIdsAsync(employeeId, cancellationToken);
    }

    public async Task UpdateProfileAsync(
        Guid employeeId, string displayName, EmploymentType employmentType, CancellationToken cancellationToken = default)
    {
        RequireEmployeeAdmin();

        var employee = await GetEmployeeOrThrowAsync(employeeId, cancellationToken);

        displayName = displayName?.Trim() ?? "";
        if (displayName.Length == 0)
        {
            throw new DomainException("Display name cannot be empty.");
        }

        if (employee.DisplayName == displayName && employee.EmploymentType == employmentType)
        {
            return;
        }

        await employees.UpdateProfileAsync(employeeId, displayName, employmentType, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            Actor(), "employee.updated", "employee", employeeId,
            new { DisplayName = displayName, EmploymentType = employmentType.ToString() }), cancellationToken);
    }

    public async Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        RequireEmployeeAdmin();
        return await roles.GetAllAsync(cancellationToken);
    }

    public async Task<Employee> CreateAsync(
        string email, string displayName, EmploymentType employmentType, CancellationToken cancellationToken = default)
    {
        RequireEmployeeAdmin();

        email = email.Trim();
        displayName = displayName.Trim();

        if (await employees.GetByEmailAsync(email, cancellationToken) is not null)
        {
            throw new DomainException($"An employee with email {email} already exists.");
        }

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            EmploymentType = employmentType,
        };
        await employees.AddAsync(employee, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            Actor(), "employee.created", "employee", employee.Id,
            new { employee.Email, employee.DisplayName, EmploymentType = employmentType.ToString() }), cancellationToken);

        return employee;
    }

    public async Task GrantRoleAsync(Guid employeeId, Guid roleId, CancellationToken cancellationToken = default)
    {
        RequireEmployeeAdmin();

        var role = await GetRoleOrThrowAsync(roleId, cancellationToken);
        _ = await GetEmployeeOrThrowAsync(employeeId, cancellationToken);

        if (!await employees.GrantRoleAsync(employeeId, roleId, Actor(), cancellationToken))
        {
            throw new DomainException($"Employee already holds the {role.Name} role.");
        }

        await audit.WriteAsync(new AuditEvent(
            Actor(), "role.granted", "employee", employeeId, new { Role = role.Name }), cancellationToken);
    }

    public async Task RevokeRoleAsync(Guid employeeId, Guid roleId, CancellationToken cancellationToken = default)
    {
        RequireEmployeeAdmin();

        var role = await GetRoleOrThrowAsync(roleId, cancellationToken);
        var employee = await GetEmployeeOrThrowAsync(employeeId, cancellationToken);

        if (role.IsSystemAdmin && employee.IsActive && await employees.CountActiveAdminsAsync(cancellationToken) <= 1)
        {
            throw new DomainException("At least one active Admin must exist — grant Admin to someone else first.");
        }

        if (!await employees.RevokeRoleAsync(employeeId, roleId, cancellationToken))
        {
            throw new DomainException($"Employee does not hold the {role.Name} role.");
        }

        await audit.WriteAsync(new AuditEvent(
            Actor(), "role.revoked", "employee", employeeId, new { Role = role.Name }), cancellationToken);
    }

    public async Task SetActiveAsync(Guid employeeId, bool isActive, CancellationToken cancellationToken = default)
    {
        RequireEmployeeAdmin();

        var employee = await GetEmployeeOrThrowAsync(employeeId, cancellationToken);
        if (employee.IsActive == isActive)
        {
            return;
        }

        if (!isActive)
        {
            var roleNames = await employees.GetRoleNamesAsync(employeeId, cancellationToken);
            var isAdmin = (await roles.GetAllAsync(cancellationToken))
                .Any(r => r.IsSystemAdmin && roleNames.Contains(r.Name));
            if (isAdmin && await employees.CountActiveAdminsAsync(cancellationToken) <= 1)
            {
                throw new DomainException("At least one active Admin must exist — this is the last one.");
            }
        }

        await employees.SetActiveAsync(employeeId, isActive, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            Actor(), isActive ? "employee.reactivated" : "employee.deactivated", "employee", employeeId), cancellationToken);
    }

    private async Task<Role> GetRoleOrThrowAsync(Guid roleId, CancellationToken cancellationToken) =>
        await roles.GetByIdAsync(roleId, cancellationToken)
        ?? throw new DomainException("Unknown role.");

    private async Task<Employee> GetEmployeeOrThrowAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await employees.GetByIdAsync(employeeId, cancellationToken)
        ?? throw new DomainException("Unknown employee.");

    private void RequireEmployeeAdmin()
    {
        if (!currentUser.IsInRole(RoleNames.Admin) && !currentUser.IsInRole(RoleNames.OperationsManager))
        {
            throw new UnauthorizedAccessException("Managing employees requires the Admin or Operations Manager role.");
        }
    }

    private Guid Actor() =>
        currentUser.EmployeeId
        ?? throw new UnauthorizedAccessException("The signed-in user has no employee record.");
}
