using Microsoft.Extensions.Logging.Abstractions;
using ProjectTango.Application.Projects;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;
using ProjectTango.UnitTests.Fakes;

namespace ProjectTango.UnitTests.Projects;

public class BudgetAlertServiceTests
{
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeBudgetRepository _budgets = new();
    private readonly FakeBudgetAlertRepository _alerts = new();
    private readonly FakeTimeEntryRepository _entries = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeEmployeeRepository _employees;
    private readonly FakeEmailSender _email = new();
    private readonly BudgetAlertService _service;

    private readonly Guid _devRole = Guid.NewGuid();
    private readonly Guid _pmId = Guid.NewGuid();
    private readonly Project _project;
    private readonly Budget _budget;

    public BudgetAlertServiceTests()
    {
        _employees = new FakeEmployeeRepository(_roles);
        _service = new BudgetAlertService(
            _projects, _budgets, _alerts, _entries, _employees, _email,
            NullLogger<BudgetAlertService>.Instance);

        _roles.Roles.Add(new Role { Id = Guid.NewGuid(), Name = RoleNames.OperationsManager });
        _entries.RatesByRole[_devRole] = 100m;

        _project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Name = "Redesign",
            Code = "GEO-014",
            ProjectManagerId = _pmId,
        };
        _projects.Projects.Add(_project);
        _employees.Employees.Add(new Employee { Id = _pmId, Email = "pm@geo.test", DisplayName = "Pat PM" });

        _budget = new Budget
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            Type = BudgetType.TimeAndMaterialsCap,
            Amount = 1000m,
            AlertThresholds = [50, 75, 90],
        };
        _budgets.Budgets.Add(_budget);
    }

    private void AddApproved(decimal hours) => _entries.Entries.Add(new TimeEntry
    {
        Id = Guid.NewGuid(),
        ProjectId = _project.Id,
        EmployeeId = Guid.NewGuid(),
        BillingRoleId = _devRole,
        EntryDate = new DateOnly(2026, 7, 8),
        HoursWorked = hours,
        HoursBilled = hours,
        IsBillable = true,
        Status = TimeEntryStatus.Approved,
    });

    private Guid AddOps(string email)
    {
        var opsRoleId = _roles.Roles.Single(r => r.Name == RoleNames.OperationsManager).Id;
        var id = Guid.NewGuid();
        _employees.Employees.Add(new Employee { Id = id, Email = email, DisplayName = "Olivia Ops" });
        _employees.RoleIdsByEmployee[id] = [opsRoleId];
        return id;
    }

    [Fact]
    public async Task Crossing_a_threshold_emails_the_pm_once()
    {
        AddApproved(6m); // 6 × 100 = 600 → 60% burn, crosses 50 (not 75)

        await _service.EvaluateAsync(_project.Id);

        var message = Assert.Single(_email.Sent);
        Assert.Contains("pm@geo.test", message.To);
        Assert.Contains("50%", message.Subject);

        // A second evaluation with no change re-sends nothing (deduped).
        await _service.EvaluateAsync(_project.Id);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task Multiple_thresholds_crossed_at_once_each_fire()
    {
        AddApproved(8m); // 800 → 80% burn, crosses 50 and 75 (not 90)

        await _service.EvaluateAsync(_project.Id);

        Assert.Equal(2, _email.Sent.Count);
        Assert.Equal(2, _alerts.Alerts.Count);
    }

    [Fact]
    public async Task Ops_is_notified_at_ninety_percent_but_not_below()
    {
        AddOps("ops@geo.test");
        AddApproved(6m); // 60% — PM only, no Ops yet

        await _service.EvaluateAsync(_project.Id);
        Assert.DoesNotContain(_email.Sent, m => m.To.Contains("ops@geo.test"));

        AddApproved(3m); // now 900 → 90% — Ops looped in
        await _service.EvaluateAsync(_project.Id);
        Assert.Contains(_email.Sent, m => m.To.Contains("ops@geo.test") && m.Subject.Contains("90%"));
    }

    [Fact]
    public async Task Overrun_fires_a_distinct_ops_alert()
    {
        AddOps("ops@geo.test");
        AddApproved(12m); // 1200 → 120% — crosses every threshold and overrun

        await _service.EvaluateAsync(_project.Id);

        var overrun = Assert.Single(_email.Sent, m => m.Subject.Contains("over budget"));
        Assert.Contains("ops@geo.test", overrun.To);
        Assert.Contains(_alerts.Alerts, a => a.AlertKey == "overrun");
    }

    [Fact]
    public async Task No_budget_sends_nothing()
    {
        _budgets.Budgets.Clear();
        AddApproved(50m);

        await _service.EvaluateAsync(_project.Id);

        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task Pending_open_work_does_not_trip_thresholds()
    {
        _entries.Entries.Add(new TimeEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            EmployeeId = Guid.NewGuid(),
            BillingRoleId = _devRole,
            EntryDate = new DateOnly(2026, 7, 8),
            HoursWorked = 9m,
            HoursBilled = 9m,
            IsBillable = true,
            Status = TimeEntryStatus.Open, // 900 pending, but not yet "spent"
        });

        await _service.EvaluateAsync(_project.Id);

        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task On_budget_change_re_arms_so_a_raised_budget_alerts_again()
    {
        AddApproved(6m); // 60%
        await _service.EvaluateAsync(_project.Id);
        Assert.Single(_email.Sent); // 50% fired

        // Owner raises the budget; thresholds re-arm. Burn is now 600/2000 = 30%, below 50%.
        _budget.Amount = 2000m;
        await _service.OnBudgetChangedAsync(_project.Id);
        Assert.Empty(_alerts.Alerts); // cleared
        Assert.Single(_email.Sent);   // nothing new to fire at 30%

        // More work pushes back over 50% of the new budget — fires again.
        AddApproved(5m); // 1100/2000 = 55%
        await _service.EvaluateAsync(_project.Id);
        Assert.Equal(2, _email.Sent.Count);
    }
}
