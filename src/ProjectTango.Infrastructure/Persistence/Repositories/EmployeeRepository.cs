using Dapper;
using Npgsql;
using ProjectTango.Application.Employees;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Infrastructure.Persistence.Repositories;

public class EmployeeRepository(NpgsqlDataSource dataSource) : IEmployeeRepository
{
    private const string SelectColumns =
        "SELECT id, entra_oid, email, display_name, employment_type, is_active, created_at, updated_at FROM employees";

    public async Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Employee>(new CommandDefinition(
            $"{SelectColumns} WHERE id = @id",
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<EmployeeSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var all = (await connection.QueryAsync<Employee>(new CommandDefinition(
            $"{SelectColumns} ORDER BY display_name",
            cancellationToken: cancellationToken))).ToList();

        var roleRows = await connection.QueryAsync<(Guid EmployeeId, string RoleName)>(new CommandDefinition(
            """
            SELECT er.employee_id, r.display_name FROM employee_roles er
            JOIN roles r ON r.id = er.role_id
            ORDER BY r.display_name
            """,
            cancellationToken: cancellationToken));

        var rolesByEmployee = roleRows
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.RoleName).ToList());

        return all
            .Select(e => new EmployeeSummary(e, rolesByEmployee.GetValueOrDefault(e.Id, [])))
            .ToList();
    }

    public async Task UpdateProfileAsync(
        Guid employeeId, string displayName, Domain.Enums.EmploymentType employmentType, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE employees
            SET display_name = @displayName, employment_type = @employmentType, updated_at = now()
            WHERE id = @employeeId
            """,
            new { employeeId, displayName, employmentType = employmentType.ToString().ToLowerInvariant() },
            cancellationToken: cancellationToken));
    }

    public async Task SetActiveAsync(Guid employeeId, bool isActive, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE employees SET is_active = @isActive, updated_at = now() WHERE id = @employeeId",
            new { employeeId, isActive },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> GrantRoleAsync(Guid employeeId, Guid roleId, Guid grantedBy, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO employee_roles (employee_id, role_id, granted_by)
            VALUES (@employeeId, @roleId, @grantedBy)
            ON CONFLICT (employee_id, role_id) DO NOTHING
            """,
            new { employeeId, roleId, grantedBy },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> RevokeRoleAsync(Guid employeeId, Guid roleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM employee_roles WHERE employee_id = @employeeId AND role_id = @roleId",
            new { employeeId, roleId },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(DISTINCT e.id) FROM employees e
            JOIN employee_roles er ON er.employee_id = e.id
            JOIN roles r ON r.id = er.role_id
            WHERE e.is_active AND r.is_system_admin
            """,
            cancellationToken: cancellationToken));
    }

    public async Task<Employee?> GetByEntraOidAsync(string entraOid, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Employee>(new CommandDefinition(
            $"{SelectColumns} WHERE entra_oid = @entraOid",
            new { entraOid },
            cancellationToken: cancellationToken));
    }

    public async Task<Employee?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Employee>(new CommandDefinition(
            $"{SelectColumns} WHERE email = @email::citext",
            new { email },
            cancellationToken: cancellationToken));
    }

    public async Task LinkEntraOidAsync(Guid employeeId, string entraOid, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE employees SET entra_oid = @entraOid, updated_at = now() WHERE id = @employeeId",
            new { employeeId, entraOid },
            cancellationToken: cancellationToken));
    }

    public async Task AddAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO employees (id, entra_oid, email, display_name, employment_type, is_active)
            VALUES (@Id, @EntraOid, @Email::citext, @DisplayName, @employmentType, @IsActive)
            """,
            new
            {
                employee.Id,
                employee.EntraOid,
                employee.Email,
                employee.DisplayName,
                employmentType = employee.EmploymentType.ToString().ToLowerInvariant(),
                employee.IsActive,
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<string>> GetRoleNamesAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var names = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT r.name FROM roles r
            JOIN employee_roles er ON er.role_id = r.id
            WHERE er.employee_id = @employeeId
            ORDER BY r.name
            """,
            new { employeeId },
            cancellationToken: cancellationToken));
        return names.ToList();
    }

    public async Task<IReadOnlyList<string>> GetRoleDisplayNamesAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var names = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT r.display_name FROM roles r
            JOIN employee_roles er ON er.role_id = r.id
            WHERE er.employee_id = @employeeId
            ORDER BY r.display_name
            """,
            new { employeeId },
            cancellationToken: cancellationToken));
        return names.ToList();
    }

    public async Task<IReadOnlySet<Guid>> GetRoleIdsAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var ids = await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT role_id FROM employee_roles WHERE employee_id = @employeeId",
            new { employeeId },
            cancellationToken: cancellationToken));
        return ids.ToHashSet();
    }
}
