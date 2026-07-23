using Crosscheck.Application.Clients;
using Crosscheck.Application.Common;
using Crosscheck.Application.Employees;
using Crosscheck.Application.Holidays;
using Crosscheck.Application.Preferences;
using Crosscheck.Application.Projects;
using Crosscheck.Application.Roles;
using Crosscheck.Application.TimeEntries;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;

namespace Crosscheck.UnitTests.Fakes;

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

    public Task RenameAsync(Guid roleId, string displayName, CancellationToken cancellationToken = default)
    {
        Roles.Single(r => r.Id == roleId).DisplayName = displayName;
        return Task.CompletedTask;
    }
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

    public Task UpdateProfileAsync(Guid employeeId, string displayName, Domain.Enums.EmploymentType employmentType, CancellationToken cancellationToken = default)
    {
        var employee = Employees.Single(e => e.Id == employeeId);
        employee.DisplayName = displayName;
        employee.EmploymentType = employmentType;
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

    public Task<IReadOnlyList<string>> GetRoleDisplayNamesAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var roleIds = RoleIdsByEmployee.GetValueOrDefault(employeeId, []);
        IReadOnlyList<string> names = roles.Roles
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.DisplayName)
            .OrderBy(n => n)
            .ToList();
        return Task.FromResult(names);
    }

    public Task<IReadOnlySet<Guid>> GetRoleIdsAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>>(RoleIdsByEmployee.GetValueOrDefault(employeeId, []).ToHashSet());

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

    public Task<IReadOnlyList<string>> GetActiveEmailsInRoleAsync(string roleName, CancellationToken cancellationToken = default)
    {
        var roleIds = roles.Roles.Where(r => r.Name == roleName).Select(r => r.Id).ToHashSet();
        IReadOnlyList<string> emails = Employees
            .Where(e => e.IsActive && RoleIdsByEmployee.GetValueOrDefault(e.Id, []).Overlaps(roleIds))
            .Select(e => e.Email)
            .ToList();
        return Task.FromResult(emails);
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

    /// <summary>Projects with logged time — blocks deletion.</summary>
    public HashSet<Guid> ProjectsWithTime { get; } = [];

    public List<Guid> Deleted { get; } = [];

    public Task<IReadOnlyList<ProjectSummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectSummary>>(
            Projects.Select(p => new ProjectSummary(p, "client", "pm")).ToList());

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Projects.FirstOrDefault(p => p.Id == id));

    public Task<Project?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Projects.FirstOrDefault(p =>
            string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<Project?> GetByClientAndCodeAsync(Guid clientId, string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Projects.FirstOrDefault(p =>
            p.ClientId == clientId && string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase)));

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

    public Task<bool> HasTimeEntriesAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ProjectsWithTime.Contains(projectId));

    public Task DeleteAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        Projects.RemoveAll(p => p.Id == projectId);
        Deleted.Add(projectId);
        return Task.CompletedTask;
    }
}

public sealed class FakeRateCardRepository(FakeRoleRepository roles) : IRateCardRepository
{
    public List<ProjectRateCard> Rates { get; } = [];
    public List<Guid> Deleted { get; } = [];

    /// <summary>(project, role) pairs with invoiced time — decides whether a rate is frozen.</summary>
    public HashSet<(Guid ProjectId, Guid RoleId)> InvoicedTime { get; } = [];

    /// <summary>(project, role) pairs with any logged time, regardless of status. Invoiced time
    /// also counts as logged, so it is folded in automatically.</summary>
    public HashSet<(Guid ProjectId, Guid RoleId)> LoggedTime { get; } = [];

    private bool HasInvoiced(Guid projectId, Guid roleId) =>
        InvoicedTime.Contains((projectId, roleId));

    private bool HasLogged(Guid projectId, Guid roleId) =>
        HasInvoiced(projectId, roleId) || LoggedTime.Contains((projectId, roleId));

    public Task<IReadOnlyList<RateCardSummary>> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RateCardSummary>>(Rates
            .Where(r => r.ProjectId == projectId)
            .Select(r => new RateCardSummary(r, roles.Roles.Single(x => x.Id == r.RoleId).Name,
                HasInvoiced(r.ProjectId, r.RoleId),
                HasLogged(r.ProjectId, r.RoleId)))
            .ToList());

    public Task<ProjectRateCard?> GetByIdAsync(Guid rateCardId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rates.FirstOrDefault(r => r.Id == rateCardId));

    public Task<ProjectRateCard?> GetForRoleAsync(Guid projectId, Guid roleId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rates.FirstOrDefault(r => r.ProjectId == projectId && r.RoleId == roleId));

    public Task AddAsync(ProjectRateCard rateCard, CancellationToken cancellationToken = default)
    {
        Rates.Add(rateCard);
        return Task.CompletedTask;
    }

    public Task<bool> HasInvoicedTimeAsync(Guid projectId, Guid roleId, CancellationToken cancellationToken = default) =>
        Task.FromResult(HasInvoiced(projectId, roleId));

    public Task<bool> HasLoggedTimeAsync(Guid projectId, Guid roleId, CancellationToken cancellationToken = default) =>
        Task.FromResult(HasLogged(projectId, roleId));

    public Task CorrectAsync(Guid rateCardId, decimal hourlyRate, CancellationToken cancellationToken = default)
    {
        Rates.Single(r => r.Id == rateCardId).HourlyRate = hourlyRate;
        return Task.CompletedTask;
    }

    public Task SoftDeleteAsync(Guid rateCardId, CancellationToken cancellationToken = default)
    {
        Rates.RemoveAll(r => r.Id == rateCardId);
        Deleted.Add(rateCardId);
        return Task.CompletedTask;
    }

    /// <summary>Module rate overrides resolved ahead of the project rates. Key (moduleId,
    /// roleId); roleId null = module-wide. Tests set these directly.</summary>
    public Dictionary<(Guid ModuleId, Guid? RoleId), decimal> ModuleRates { get; } = [];

    public Task<decimal?> ResolveAsync(Guid projectId, Guid? moduleId, Guid roleId, CancellationToken cancellationToken = default)
    {
        if (moduleId is { } m)
        {
            if (ModuleRates.TryGetValue((m, roleId), out var roleRate))
            {
                return Task.FromResult<decimal?>(roleRate);
            }

            if (ModuleRates.TryGetValue((m, null), out var moduleWideRate))
            {
                return Task.FromResult<decimal?>(moduleWideRate);
            }
        }

        return Task.FromResult(Rates
            .Where(r => r.ProjectId == projectId && r.RoleId == roleId)
            .Select(r => (decimal?)r.HourlyRate)
            .FirstOrDefault());
    }
}

public sealed class FakeModuleRepository(FakeRoleRepository roles) : IModuleRepository
{
    public List<ProjectModule> Modules { get; } = [];
    public List<ProjectModuleRate> ModuleRates { get; } = [];
    public List<Guid> DeletedRates { get; } = [];

    /// <summary>(module, billing role) pairs with invoiced / any logged time. Tests seed these
    /// to exercise the rate-override guards.</summary>
    public HashSet<(Guid ModuleId, Guid RoleId)> Invoiced { get; } = [];
    public HashSet<(Guid ModuleId, Guid RoleId)> Logged { get; } = [];

    private bool HasInvoiced(Guid moduleId, Guid? roleId) =>
        Invoiced.Any(t => t.ModuleId == moduleId && (roleId is null || t.RoleId == roleId));

    private bool HasLogged(Guid moduleId, Guid? roleId) =>
        HasInvoiced(moduleId, roleId) || Logged.Any(t => t.ModuleId == moduleId && (roleId is null || t.RoleId == roleId));

    public Task<IReadOnlyList<ProjectModule>> GetForProjectAsync(Guid projectId, bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectModule>>(Modules
            .Where(m => m.ProjectId == projectId && (includeDeleted || !m.IsDeleted))
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Name)
            .ToList());

    public Task<IReadOnlyList<ProjectModule>> GetForProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectModule>>(Modules
            .Where(m => projectIds.Contains(m.ProjectId) && !m.IsDeleted)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Name)
            .ToList());

    public Task<ProjectModule?> GetByIdAsync(Guid moduleId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Modules.FirstOrDefault(m => m.Id == moduleId));

    public Task<bool> HasLiveModulesAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Modules.Any(m => m.ProjectId == projectId && !m.IsDeleted));

    public Task AddAsync(ProjectModule module, CancellationToken cancellationToken = default)
    {
        Modules.Add(module);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ProjectModule module, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ReplaceAllocationsAsync(Guid moduleId, IReadOnlyList<ModuleRoleAllocation> allocations, CancellationToken cancellationToken = default)
    {
        Modules.Single(m => m.Id == moduleId).Allocations = allocations.ToList();
        return Task.CompletedTask;
    }

    public Task SoftDeleteAsync(Guid moduleId, CancellationToken cancellationToken = default)
    {
        Modules.Single(m => m.Id == moduleId).DeletedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public Task<bool> HasLoggedTimeAsync(Guid moduleId, CancellationToken cancellationToken = default) =>
        Task.FromResult(HasLogged(moduleId, null));

    public Task<IReadOnlyList<ModuleRateSummary>> GetRatesAsync(Guid moduleId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ModuleRateSummary>>(ModuleRates
            .Where(r => r.ModuleId == moduleId)
            .OrderBy(r => r.RoleId is not null)
            .Select(r => new ModuleRateSummary(
                r,
                r.RoleId is { } rid ? roles.Roles.Single(x => x.Id == rid).Name : null,
                HasInvoiced(moduleId, r.RoleId),
                HasLogged(moduleId, r.RoleId)))
            .ToList());

    public Task<ProjectModuleRate?> GetRateByIdAsync(Guid moduleRateId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ModuleRates.FirstOrDefault(r => r.Id == moduleRateId));

    public Task<ProjectModuleRate?> GetRateForRoleAsync(Guid moduleId, Guid? roleId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ModuleRates.FirstOrDefault(r => r.ModuleId == moduleId && r.RoleId == roleId));

    public Task AddRateAsync(ProjectModuleRate rate, CancellationToken cancellationToken = default)
    {
        ModuleRates.Add(rate);
        return Task.CompletedTask;
    }

    public Task CorrectRateAsync(Guid moduleRateId, decimal hourlyRate, CancellationToken cancellationToken = default)
    {
        ModuleRates.Single(r => r.Id == moduleRateId).HourlyRate = hourlyRate;
        return Task.CompletedTask;
    }

    public Task SoftDeleteRateAsync(Guid moduleRateId, CancellationToken cancellationToken = default)
    {
        ModuleRates.RemoveAll(r => r.Id == moduleRateId);
        DeletedRates.Add(moduleRateId);
        return Task.CompletedTask;
    }

    public Task<bool> HasInvoicedTimeAsync(Guid moduleId, Guid? roleId, CancellationToken cancellationToken = default) =>
        Task.FromResult(HasInvoiced(moduleId, roleId));

    public Task<bool> HasLoggedTimeForRoleAsync(Guid moduleId, Guid? roleId, CancellationToken cancellationToken = default) =>
        Task.FromResult(HasLogged(moduleId, roleId));

    public Task<IReadOnlyList<Guid>> GetLoggedRoleIdsAsync(Guid moduleId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(Invoiced.Concat(Logged)
            .Where(t => t.ModuleId == moduleId)
            .Select(t => t.RoleId)
            .Distinct()
            .ToList());
}

public sealed class FakeBudgetRepository : IBudgetRepository
{
    public List<Budget> Budgets { get; } = [];
    public List<BudgetRevision> Revisions { get; } = [];

    /// <summary>Display name returned for every reviser in <see cref="GetRevisionsAsync"/>.</summary>
    public string RevisedByName { get; set; } = "reviser";

    public Task<Budget?> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Budgets.FirstOrDefault(b => b.ProjectId == projectId));

    public Task SaveAsync(Budget budget, BudgetRevision revision, CancellationToken cancellationToken = default)
    {
        if (Budgets.All(b => b.Id != budget.Id))
        {
            Budgets.Add(budget);
        }

        Revisions.Add(revision);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BudgetRevisionSummary>> GetRevisionsAsync(Guid budgetId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BudgetRevisionSummary>>(Revisions
            .Where(r => r.BudgetId == budgetId)
            .OrderByDescending(r => r.RevisedAt)
            .Select(r => new BudgetRevisionSummary(r, RevisedByName))
            .ToList());
}

public sealed class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class FakeBudgetAlertRepository : IBudgetAlertRepository
{
    public List<BudgetAlert> Alerts { get; } = [];

    public Task<IReadOnlySet<string>> GetFiredKeysAsync(Guid budgetId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<string>>(Alerts
            .Where(a => a.BudgetId == budgetId)
            .Select(a => a.AlertKey)
            .ToHashSet(StringComparer.Ordinal));

    public Task RecordAsync(BudgetAlert alert, CancellationToken cancellationToken = default)
    {
        if (!Alerts.Any(a => a.BudgetId == alert.BudgetId && a.AlertKey == alert.AlertKey))
        {
            Alerts.Add(alert);
        }

        return Task.CompletedTask;
    }

    public Task ClearForBudgetAsync(Guid budgetId, CancellationToken cancellationToken = default)
    {
        Alerts.RemoveAll(a => a.BudgetId == budgetId);
        return Task.CompletedTask;
    }

    public Task ClearForBudgetAsync(Guid budgetId, string keyPrefix, CancellationToken cancellationToken = default)
    {
        Alerts.RemoveAll(a => a.BudgetId == budgetId && a.AlertKey.StartsWith(keyPrefix, StringComparison.Ordinal));
        return Task.CompletedTask;
    }
}

/// <summary>No-op alert service for the services that trigger alerts as a side effect —
/// records which projects were poked so tests can assert the hook fired.</summary>
public sealed class FakeBudgetAlertService : IBudgetAlertService
{
    public List<Guid> Evaluated { get; } = [];
    public List<Guid> Changed { get; } = [];

    public Task EvaluateAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        Evaluated.Add(projectId);
        return Task.CompletedTask;
    }

    public Task OnBudgetChangedAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        Changed.Add(projectId);
        return Task.CompletedTask;
    }

    public List<(Guid ProjectId, Guid ModuleId)> ModuleChanged { get; } = [];

    public Task OnModuleChangedAsync(Guid projectId, Guid moduleId, CancellationToken cancellationToken = default)
    {
        ModuleChanged.Add((projectId, moduleId));
        return Task.CompletedTask;
    }
}

public sealed class FakeAssignmentRepository : IAssignmentRepository
{
    public List<ProjectAssignment> Assignments { get; } = [];
    public List<Guid> Deleted { get; } = [];

    /// <summary>(project, employee) pairs that have logged time — gates removal in tests.</summary>
    public HashSet<(Guid ProjectId, Guid EmployeeId)> WithTimeEntries { get; } = [];

    public Task<IReadOnlyList<AssignmentSummary>> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AssignmentSummary>>(Assignments
            .Where(a => a.ProjectId == projectId)
            .Select(a => new AssignmentSummary(a, "employee", null,
                WithTimeEntries.Contains((a.ProjectId, a.EmployeeId))))
            .ToList());

    /// <summary>Project start/end dates surfaced on <see cref="EmployeeAssignment"/>, keyed by
    /// project id. Projects not listed have open-ended (null) dates.</summary>
    public Dictionary<Guid, (DateOnly? Start, DateOnly? End)> ProjectDates { get; } = [];

    /// <summary>Project statuses surfaced on <see cref="EmployeeAssignment"/>, keyed by
    /// project id. Projects not listed are Active.</summary>
    public Dictionary<Guid, ProjectStatus> ProjectStatuses { get; } = [];

    public Task<IReadOnlyList<EmployeeAssignment>> GetForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EmployeeAssignment>>(Assignments
            .Where(a => a.EmployeeId == employeeId)
            .Select(a =>
            {
                var dates = ProjectDates.GetValueOrDefault(a.ProjectId);
                return new EmployeeAssignment(a, "GEO-000", "project", "client", dates.Start, dates.End,
                    ProjectStatuses.GetValueOrDefault(a.ProjectId, ProjectStatus.Active));
            })
            .ToList());

    public Task<ProjectAssignment?> GetAsync(Guid assignmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Assignments.FirstOrDefault(a => a.Id == assignmentId));

    public Task<ProjectAssignment?> GetByProjectAndEmployeeAsync(Guid projectId, Guid employeeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Assignments.FirstOrDefault(a => a.ProjectId == projectId && a.EmployeeId == employeeId));

    public Task<bool> HasTimeEntriesAsync(Guid projectId, Guid employeeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(WithTimeEntries.Contains((projectId, employeeId)));

    public Task AddAsync(ProjectAssignment assignment, CancellationToken cancellationToken = default)
    {
        Assignments.Add(assignment);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ProjectAssignment assignment, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        Assignments.RemoveAll(a => a.Id == assignmentId);
        Deleted.Add(assignmentId);
        return Task.CompletedTask;
    }
}

public sealed class FakeTimeEntryRepository : ITimeEntryRepository
{
    public List<TimeEntry> Entries { get; } = [];

    public Task<TimeEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Entries.FirstOrDefault(e => e.Id == id));

    public Task<TimeEntry?> GetByCellAsync(Guid employeeId, Guid projectId, Guid? moduleId, DateOnly date, CancellationToken cancellationToken = default) =>
        Task.FromResult(Entries.FirstOrDefault(e =>
            e.EmployeeId == employeeId && e.ProjectId == projectId && e.ModuleId == moduleId && e.EntryDate == date));

    public Task<IReadOnlyList<TimeEntry>> GetForEmployeeRangeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TimeEntry>>(Entries
            .Where(e => e.EmployeeId == employeeId && e.EntryDate >= from && e.EntryDate <= to)
            .OrderBy(e => e.EntryDate)
            .ToList());

    public Task<IReadOnlyList<ApprovalEntry>> GetForProjectRangeAsync(Guid projectId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ApprovalEntry>>(Entries
            .Where(e => e.ProjectId == projectId && e.EntryDate >= from && e.EntryDate <= to)
            .Select(e => new ApprovalEntry(e, "employee", "role"))
            .ToList());

    /// <summary>Rates the fake resolves for a burn row, keyed on billing role. Tests set these.</summary>
    public Dictionary<Guid, decimal> RatesByRole { get; } = [];
    public Dictionary<Guid, string> EmployeeNames { get; } = [];
    public Dictionary<Guid, string> RoleNames { get; } = [];
    public Dictionary<Guid, string> ModuleNames { get; } = [];
    public HashSet<Guid> DeletedModules { get; } = [];

    public Task<IReadOnlyList<BurnRow>> GetBurnRowsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BurnRow>>(Entries
            .Where(e => e.ProjectId == projectId)
            .OrderByDescending(e => e.EntryDate)
            .Select(e => new BurnRow(
                e.Id, e.EntryDate, e.Status, e.IsBillable,
                e.EmployeeId, EmployeeNames.GetValueOrDefault(e.EmployeeId, "employee"),
                e.BillingRoleId, RoleNames.GetValueOrDefault(e.BillingRoleId, "role"),
                e.ModuleId,
                e.ModuleId is { } mid ? ModuleNames.GetValueOrDefault(mid, "module") : null,
                e.ModuleId is { } mdel && DeletedModules.Contains(mdel),
                e.HoursWorked, e.HoursBilled,
                RatesByRole.TryGetValue(e.BillingRoleId, out var rate) ? rate : null))
            .ToList());

    public Task AddAsync(TimeEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(TimeEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Entries.RemoveAll(e => e.Id == id);
        return Task.CompletedTask;
    }
}

public sealed class FakeTimesheetPeriodRepository : ITimesheetPeriodRepository
{
    public List<TimesheetPeriod> Periods { get; } = [];

    public Task<TimesheetPeriod?> GetByStartAsync(DateOnly periodStart, CancellationToken cancellationToken = default) =>
        Task.FromResult(Periods.FirstOrDefault(p => p.PeriodStart == periodStart));

    public Task<IReadOnlyList<TimesheetPeriod>> GetInRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TimesheetPeriod>>(Periods
            .Where(p => p.PeriodStart <= to && p.PeriodEnd >= from)
            .OrderBy(p => p.PeriodStart)
            .ToList());

    public Task UpsertAsync(TimesheetPeriod period, CancellationToken cancellationToken = default)
    {
        var existing = Periods.FirstOrDefault(p => p.PeriodStart == period.PeriodStart);
        if (existing is not null)
        {
            existing.Status = period.Status;
            existing.ClosedById = period.ClosedById;
            existing.ClosedAt = period.ClosedAt;
        }
        else
        {
            Periods.Add(period);
        }

        return Task.CompletedTask;
    }
}

public sealed class FakeCompanyHolidayRepository : ICompanyHolidayRepository
{
    public List<CompanyHoliday> Holidays { get; } = [];
    public List<(Guid Id, Guid? DeletedBy)> Deleted { get; } = [];

    public Task<IReadOnlyList<CompanyHoliday>> GetInRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CompanyHoliday>>(Holidays
            .Where(h => h.Date >= from && h.Date <= to)
            .OrderBy(h => h.Date)
            .ToList());

    public Task<CompanyHoliday?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Holidays.FirstOrDefault(h => h.Id == id));

    public Task<CompanyHoliday?> GetByDateAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        Task.FromResult(Holidays.FirstOrDefault(h => h.Date == date));

    public Task AddAsync(CompanyHoliday holiday, CancellationToken cancellationToken = default)
    {
        Holidays.Add(holiday);
        return Task.CompletedTask;
    }

    public Task SoftDeleteAsync(Guid id, Guid? deletedBy, CancellationToken cancellationToken = default)
    {
        Holidays.RemoveAll(h => h.Id == id);
        Deleted.Add((id, deletedBy));
        return Task.CompletedTask;
    }
}

public sealed class FakeEmployeePreferenceRepository : IEmployeePreferenceRepository
{
    public Dictionary<(Guid, string), string> Values { get; } = [];

    public Task<string?> GetAsync(Guid employeeId, string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Values.TryGetValue((employeeId, key), out var v) ? v : null);

    public Task SetAsync(Guid employeeId, string key, string value, CancellationToken cancellationToken = default)
    {
        Values[(employeeId, key)] = value;
        return Task.CompletedTask;
    }
}
