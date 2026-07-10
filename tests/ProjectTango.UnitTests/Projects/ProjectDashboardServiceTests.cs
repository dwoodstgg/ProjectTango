using ProjectTango.Application.Projects;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;
using ProjectTango.UnitTests.Fakes;

namespace ProjectTango.UnitTests.Projects;

public class ProjectDashboardServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeClientRepository _clients = new();
    private readonly FakeAssignmentRepository _assignments = new();
    private readonly FakeTimeEntryRepository _entries = new();
    private readonly FakeBudgetRepository _budgets = new();
    private readonly ProjectDashboardService _service;

    private readonly Client _client;
    private readonly Project _project;
    private readonly Guid _devRole = Guid.NewGuid();
    private readonly Guid _pmRole = Guid.NewGuid();
    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();

    public ProjectDashboardServiceTests()
    {
        _service = new ProjectDashboardService(_currentUser, _projects, _clients, _assignments, _entries, _budgets);

        _client = new Client { Id = Guid.NewGuid(), Name = "Acme" };
        _clients.Clients.Add(_client);
        _project = new Project { Id = Guid.NewGuid(), ClientId = _client.Id, Name = "P", Code = "GEO-001", ProjectManagerId = Guid.NewGuid() };
        _projects.Projects.Add(_project);

        _currentUser.Roles.Add(RoleNames.OperationsManager);
        _entries.RatesByRole[_devRole] = 100m;
        _entries.RatesByRole[_pmRole] = 200m;
        _entries.EmployeeNames[_alice] = "Alice";
        _entries.EmployeeNames[_bob] = "Bob";
        _entries.RoleNames[_devRole] = "Developer";
        _entries.RoleNames[_pmRole] = "Project Manager";
    }

    private void Add(Guid employee, Guid role, TimeEntryStatus status, decimal worked, decimal billed, bool billable = true)
    {
        _entries.Entries.Add(new TimeEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            EmployeeId = employee,
            BillingRoleId = role,
            EntryDate = new DateOnly(2026, 7, 8),
            HoursWorked = worked,
            HoursBilled = billed,
            IsBillable = billable,
            Status = status,
        });
    }

    [Fact]
    public async Task Totals_value_open_on_worked_and_approved_on_billed()
    {
        Add(_alice, _devRole, TimeEntryStatus.Open, worked: 5m, billed: 5m);         // pending: 5 × 100 = 500
        Add(_bob, _devRole, TimeEntryStatus.Approved, worked: 8m, billed: 6m);       // approved: 6 × 100 = 600
        Add(_bob, _pmRole, TimeEntryStatus.Invoiced, worked: 4m, billed: 4m);        // invoiced: 4 × 200 = 800

        var dash = await _service.GetAsync(_project.Id);

        Assert.NotNull(dash);
        Assert.Equal(17m, dash!.Totals.HoursWorked);
        Assert.Equal(15m, dash.Totals.HoursBilled);
        Assert.Equal(500m, dash.Totals.PendingValue);
        Assert.Equal(600m, dash.Totals.ApprovedValue);
        Assert.Equal(800m, dash.Totals.InvoicedValue);
        Assert.Equal(1400m, dash.Totals.BillableValue);
        Assert.Equal(1, dash.Totals.OpenCount);
    }

    [Fact]
    public async Task Non_billable_and_rateless_entries_add_hours_but_no_value()
    {
        Add(_alice, _devRole, TimeEntryStatus.Approved, worked: 8m, billed: 8m, billable: false); // non-billable → 0
        var noRateRole = Guid.NewGuid();
        _entries.RoleNames[noRateRole] = "Designer";
        Add(_bob, noRateRole, TimeEntryStatus.Approved, worked: 3m, billed: 3m);                   // billable, no rate → 0, gap

        var dash = await _service.GetAsync(_project.Id);

        Assert.Equal(11m, dash!.Totals.HoursWorked);
        Assert.Equal(0m, dash.Totals.BillableValue);
        Assert.True(dash.HasRateGaps);
    }

    [Fact]
    public async Task Breakdowns_group_by_role_and_person()
    {
        Add(_alice, _devRole, TimeEntryStatus.Approved, 8m, 8m);
        Add(_bob, _devRole, TimeEntryStatus.Approved, 4m, 4m);
        Add(_bob, _pmRole, TimeEntryStatus.Approved, 2m, 2m);

        var dash = await _service.GetAsync(_project.Id);

        Assert.Equal(2, dash!.ByRole.Count);
        Assert.Equal(2, dash.ByPerson.Count);
        var dev = dash.ByRole.Single(r => r.RoleName == "Developer");
        Assert.Equal(12m, dev.HoursWorked);
        Assert.Equal(1200m, dev.Value);
    }

    [Fact]
    public async Task No_budget_leaves_budget_status_null()
    {
        Add(_alice, _devRole, TimeEntryStatus.Approved, 8m, 8m);

        var dash = await _service.GetAsync(_project.Id);

        Assert.Null(dash!.Budget);
    }

    [Fact]
    public async Task Dollar_budget_reports_spent_remaining_and_threshold()
    {
        _budgets.Budgets.Add(new Budget
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            Type = BudgetType.TimeAndMaterialsCap,
            Amount = 1000m,
            AlertThresholds = [50, 75, 90],
        });
        Add(_alice, _devRole, TimeEntryStatus.Approved, 8m, 8m);   // 8 × 100 = 800 spent (WIP)
        Add(_bob, _devRole, TimeEntryStatus.Open, 3m, 3m);         // 3 × 100 = 300 pending, not spent

        var dash = await _service.GetAsync(_project.Id);

        var budget = dash!.Budget!;
        Assert.Equal(800m, budget.SpentValue);
        Assert.Equal(300m, budget.PendingValue);
        Assert.Equal(200m, budget.RemainingValue);
        Assert.Equal(80d, budget.PercentValue);
        Assert.False(budget.IsOverBudget);
        Assert.Equal(75, budget.HighestThresholdCrossed);   // 80% burn has crossed 50 and 75, not 90
    }

    [Fact]
    public async Task Overrun_is_flagged_with_negative_remaining()
    {
        _budgets.Budgets.Add(new Budget
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            Type = BudgetType.FixedFee,
            Amount = 500m,
            AlertThresholds = [90],
        });
        Add(_alice, _devRole, TimeEntryStatus.Invoiced, 8m, 8m);   // 800 invoiced > 500 budget

        var dash = await _service.GetAsync(_project.Id);

        var budget = dash!.Budget!;
        Assert.True(budget.IsOverBudget);
        Assert.Equal(-300m, budget.RemainingValue);
        Assert.Equal(90, budget.HighestThresholdCrossed);
    }

    [Fact]
    public async Task Hours_budget_uses_billed_hours_for_spent()
    {
        _budgets.Budgets.Add(new Budget
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            Type = BudgetType.HoursCap,
            Hours = 40m,
        });
        Add(_alice, _devRole, TimeEntryStatus.Approved, worked: 10m, billed: 8m);  // spent hours = 8 (billed)
        Add(_bob, _devRole, TimeEntryStatus.Open, worked: 5m, billed: 5m);         // pending hours = 5 (worked)

        var dash = await _service.GetAsync(_project.Id);

        var budget = dash!.Budget!;
        Assert.Equal(8m, budget.SpentHours);
        Assert.Equal(5m, budget.PendingHours);
        Assert.Equal(32m, budget.RemainingHours);
        Assert.Null(budget.PercentValue);   // no dollar dimension on an hours cap
    }

    [Fact]
    public async Task Unknown_project_returns_null()
    {
        Assert.Null(await _service.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Viewer_without_a_management_role_is_rejected()
    {
        _currentUser.Roles.Clear();
        _currentUser.Roles.Add(RoleNames.Developer);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.GetAsync(_project.Id));
    }
}
