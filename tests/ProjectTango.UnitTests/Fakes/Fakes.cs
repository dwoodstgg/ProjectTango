using ProjectTango.Application.Clients;
using ProjectTango.Application.Common;
using ProjectTango.Application.Employees;
using ProjectTango.Application.Projects;
using ProjectTango.Application.Roles;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;

namespace ProjectTango.UnitTests.Fakes;

public sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? EmployeeId { get; set; } = Guid.NewGuid();
    public HashSet<string> Roles { get; } = [];

    public bool IsInRole(string roleName) => Roles.Contains(roleName);
}

public sealed class FakeAuditLog : IAuditLog
{
    public List<AuditEvent> Events { get; } = [];

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(auditEvent);
        return Task.CompletedTask;
    }
}

public sealed class FakeRoleRepository : IRoleRepository
{
    public List<Role> Roles { get; } = [];

    public Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Role>>(Roles.ToList());

    public Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Roles.FirstOrDefault(r => r.Id == id));
}

public sealed class FakeEmployeeRepository(FakeRoleRepository roles) : IEmployeeRepository
{
    public List<Employee> Employees { get; } = [];
    public List<Employee> Added { get; } = [];
    public List<(Guid EmployeeId, string EntraOid)> Linked { get; } = [];
    public Dictionary<Guid, HashSet<Guid>> RoleIdsByEmployee { get; } = [];

    public Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Employees.FirstOrDefault(e => e.Id == id));

    public Task<Employee?> GetByEntraOidAsync(string entraOid, CancellationToken cancellationToken = default) =>
        Task.FromResult(Employees.FirstOrDefault(e => e.EntraOid == entraOid));

    public Task<Employee?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        Task.FromResult(Employees.FirstOrDefault(e =>
            string.Equals(e.Email, email, StringComparison.OrdinalIgnoreCase)));

    public async Task<IReadOnlyList<EmployeeSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var summaries = new List<EmployeeSummary>();
        foreach (var employee in Employees)
        {
            summaries.Add(new EmployeeSummary(employee, await GetRoleNamesAsync(employee.Id, cancellationToken)));
        }

        return summaries;
    }

    public Task LinkEntraOidAsync(Guid employeeId, string entraOid, CancellationToken cancellationToken = default)
    {
        Linked.Add((employeeId, entraOid));
        return Task.CompletedTask;
    }

    public Task AddAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        Employees.Add(employee);
        Added.Add(employee);
        return Task.CompletedTask;
    }

    public Task SetActiveAsync(Guid employeeId, bool isActive, CancellationToken cancellationToken = default)
    {
        Employees.Single(e => e.Id == employeeId).IsActive = isActive;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetRoleNamesAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var roleIds = RoleIdsByEmployee.GetValueOrDefault(employeeId, []);
        IReadOnlyList<string> names = roles.Roles
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name)
            .OrderBy(n => n)
            .ToList();
        return Task.FromResult(names);
    }

    public Task<bool> GrantRoleAsync(Guid employeeId, Guid roleId, Guid grantedBy, CancellationToken cancellationToken = default)
    {
        var set = RoleIdsByEmployee.TryGetValue(employeeId, out var existing)
            ? existing
            : RoleIdsByEmployee[employeeId] = [];
        return Task.FromResult(set.Add(roleId));
    }

    public Task<bool> RevokeRoleAsync(Guid employeeId, Guid roleId, CancellationToken cancellationToken = default) =>
        Task.FromResult(RoleIdsByEmployee.GetValueOrDefault(employeeId, []).Remove(roleId));

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken = default)
    {
        var adminRoleIds = roles.Roles.Where(r => r.IsSystemAdmin).Select(r => r.Id).ToHashSet();
        var count = Employees.Count(e =>
            e.IsActive && RoleIdsByEmployee.GetValueOrDefault(e.Id, []).Overlaps(adminRoleIds));
        return Task.FromResult(count);
    }
}

public sealed class FakeClientRepository : IClientRepository
{
    public List<Client> Clients { get; } = [];

    public Task<IReadOnlyList<Client>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Client>>(Clients.ToList());

    public Task<Client?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Clients.FirstOrDefault(c => c.Id == id));

    public Task AddAsync(Client client, CancellationToken cancellationToken = default)
    {
        Clients.Add(client);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Client client, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetActiveAsync(Guid clientId, bool isActive, CancellationToken cancellationToken = default)
    {
        Clients.Single(c => c.Id == clientId).IsActive = isActive;
        return Task.CompletedTask;
    }
}

public sealed class FakeProjectRepository : IProjectRepository
{
    public List<Project> Projects { get; } = [];

    public Task<IReadOnlyList<ProjectSummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectSummary>>(
            Projects.Select(p => new ProjectSummary(p, "client", "pm")).ToList());

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Projects.FirstOrDefault(p => p.Id == id));

    public Task<Project?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Projects.FirstOrDefault(p =>
            string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task AddAsync(Project project, CancellationToken cancellationToken = default)
    {
        Projects.Add(project);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Project project, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetStatusAsync(Guid projectId, ProjectStatus status, CancellationToken cancellationToken = default)
    {
        Projects.Single(p => p.Id == projectId).Status = status;
        return Task.CompletedTask;
    }
}

public sealed class FakeRateCardRepository(FakeRoleRepository roles) : IRateCardRepository
{
    public List<ProjectRateCard> Rates { get; } = [];
    public List<(Guid RateCardId, DateOnly EffectiveTo)> Closed { get; } = [];

    public Task<IReadOnlyList<RateCardSummary>> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RateCardSummary>>(Rates
            .Where(r => r.ProjectId == projectId)
            .Select(r => new RateCardSummary(r, roles.Roles.Single(x => x.Id == r.RoleId).Name))
            .ToList());

    public Task<IReadOnlyList<ProjectRateCard>> GetForRoleAsync(Guid projectId, Guid roleId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectRateCard>>(Rates
            .Where(r => r.ProjectId == projectId && r.RoleId == roleId)
            .OrderBy(r => r.EffectiveFrom)
            .ToList());

    public Task AddAsync(ProjectRateCard rateCard, CancellationToken cancellationToken = default)
    {
        Rates.Add(rateCard);
        return Task.CompletedTask;
    }

    public Task CloseAsync(Guid rateCardId, DateOnly effectiveTo, CancellationToken cancellationToken = default)
    {
        Rates.Single(r => r.Id == rateCardId).EffectiveTo = effectiveTo;
        Closed.Add((rateCardId, effectiveTo));
        return Task.CompletedTask;
    }

    public Task<decimal?> ResolveAsync(Guid projectId, Guid roleId, DateOnly date, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rates
            .Where(r => r.ProjectId == projectId && r.RoleId == roleId && r.IsEffectiveOn(date))
            .Select(r => (decimal?)r.HourlyRate)
            .FirstOrDefault());
}

public sealed class FakeAssignmentRepository : IAssignmentRepository
{
    public List<ProjectAssignment> Assignments { get; } = [];

    public Task<IReadOnlyList<AssignmentSummary>> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AssignmentSummary>>(Assignments
            .Where(a => a.ProjectId == projectId)
            .Select(a => new AssignmentSummary(a, "employee", null))
            .ToList());

    public Task<ProjectAssignment?> GetAsync(Guid assignmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Assignments.FirstOrDefault(a => a.Id == assignmentId));

    public Task<ProjectAssignment?> GetByProjectAndEmployeeAsync(Guid projectId, Guid employeeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Assignments.FirstOrDefault(a => a.ProjectId == projectId && a.EmployeeId == employeeId));

    public Task AddAsync(ProjectAssignment assignment, CancellationToken cancellationToken = default)
    {
        Assignments.Add(assignment);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ProjectAssignment assignment, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
