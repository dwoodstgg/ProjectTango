using ProjectTango.Application.Clients;
using ProjectTango.Application.Common;
using ProjectTango.Application.Employees;
using ProjectTango.Application.Preferences;
using ProjectTango.Application.Projects;
using ProjectTango.Application.Roles;
using ProjectTango.Application.TimeEntries;
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
    public List<Guid> Deleted { get; } = [];

    /// <summary>Invoiced time entries (project, role, date) used to decide whether a rate is frozen.</summary>
    public List<(Guid ProjectId, Guid RoleId, DateOnly Date)> InvoicedTime { get; } = [];

    private bool HasInvoiced(Guid projectId, Guid roleId, DateOnly from, DateOnly? to) =>
        InvoicedTime.Any(t => t.ProjectId == projectId && t.RoleId == roleId
            && t.Date >= from && (to is null || t.Date <= to));

    public Task<IReadOnlyList<RateCardSummary>> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RateCardSummary>>(Rates
            .Where(r => r.ProjectId == projectId)
            .Select(r => new RateCardSummary(r, roles.Roles.Single(x => x.Id == r.RoleId).Name,
                HasInvoiced(r.ProjectId, r.RoleId, r.EffectiveFrom, r.EffectiveTo)))
            .ToList());

    public Task<ProjectRateCard?> GetByIdAsync(Guid rateCardId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rates.FirstOrDefault(r => r.Id == rateCardId));

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

    public Task<bool> HasInvoicedTimeAsync(
        Guid projectId, Guid roleId, DateOnly effectiveFrom, DateOnly? effectiveTo,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(HasInvoiced(projectId, roleId, effectiveFrom, effectiveTo));

    public Task CorrectAsync(
        Guid rateCardId, decimal hourlyRate, DateOnly effectiveFrom,
        Guid? priorRowId, DateOnly? priorEffectiveTo,
        CancellationToken cancellationToken = default)
    {
        var row = Rates.Single(r => r.Id == rateCardId);
        row.HourlyRate = hourlyRate;
        row.EffectiveFrom = effectiveFrom;
        if (priorRowId is not null)
        {
            Rates.Single(r => r.Id == priorRowId).EffectiveTo = priorEffectiveTo;
        }

        return Task.CompletedTask;
    }

    public Task SoftDeleteAsync(Guid rateCardId, Guid? reopenPriorRowId, CancellationToken cancellationToken = default)
    {
        Rates.RemoveAll(r => r.Id == rateCardId);
        Deleted.Add(rateCardId);
        if (reopenPriorRowId is not null)
        {
            Rates.Single(r => r.Id == reopenPriorRowId).EffectiveTo = null;
        }

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

    public Task<IReadOnlyList<EmployeeAssignment>> GetForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EmployeeAssignment>>(Assignments
            .Where(a => a.EmployeeId == employeeId)
            .Select(a => new EmployeeAssignment(a, "GEO-000", "project", "client"))
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

public sealed class FakeTimeEntryRepository : ITimeEntryRepository
{
    public List<TimeEntry> Entries { get; } = [];

    public Task<TimeEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Entries.FirstOrDefault(e => e.Id == id));

    public Task<TimeEntry?> GetByCellAsync(Guid employeeId, Guid projectId, DateOnly date, CancellationToken cancellationToken = default) =>
        Task.FromResult(Entries.FirstOrDefault(e =>
            e.EmployeeId == employeeId && e.ProjectId == projectId && e.EntryDate == date));

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

    public Task<IReadOnlyList<BurnRow>> GetBurnRowsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BurnRow>>(Entries
            .Where(e => e.ProjectId == projectId)
            .OrderByDescending(e => e.EntryDate)
            .Select(e => new BurnRow(
                e.Id, e.EntryDate, e.Status, e.IsBillable,
                e.EmployeeId, EmployeeNames.GetValueOrDefault(e.EmployeeId, "employee"),
                e.BillingRoleId, RoleNames.GetValueOrDefault(e.BillingRoleId, "role"),
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
