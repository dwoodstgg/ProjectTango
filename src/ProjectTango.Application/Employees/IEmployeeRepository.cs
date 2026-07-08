using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Employees;

public interface IEmployeeRepository
{
    Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Employee?> GetByEntraOidAsync(string entraOid, CancellationToken cancellationToken = default);

    Task<Employee?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeSummary>> GetAllAsync(CancellationToken cancellationToken = default);

    Task LinkEntraOidAsync(Guid employeeId, string entraOid, CancellationToken cancellationToken = default);

    Task AddAsync(Employee employee, CancellationToken cancellationToken = default);

    Task UpdateProfileAsync(Guid employeeId, string displayName, Domain.Enums.EmploymentType employmentType, CancellationToken cancellationToken = default);

    Task SetActiveAsync(Guid employeeId, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>Stable role <c>name</c> keys — used for auth claims and admin-guard logic.</summary>
    Task<IReadOnlyList<string>> GetRoleNamesAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Editable role display labels — used for badges and UI.</summary>
    Task<IReadOnlyList<string>> GetRoleDisplayNamesAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetRoleIdsAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <returns>False when the employee already holds the role.</returns>
    Task<bool> GrantRoleAsync(Guid employeeId, Guid roleId, Guid grantedBy, CancellationToken cancellationToken = default);

    /// <returns>False when the employee did not hold the role.</returns>
    Task<bool> RevokeRoleAsync(Guid employeeId, Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Active employees holding a system-admin role — must never reach zero.</summary>
    Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken = default);
}
