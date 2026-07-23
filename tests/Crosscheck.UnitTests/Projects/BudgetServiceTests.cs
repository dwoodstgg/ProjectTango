using Crosscheck.Application.Projects;
using Crosscheck.Domain;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;
using Crosscheck.UnitTests.Fakes;

namespace Crosscheck.UnitTests.Projects;

public class BudgetServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeBudgetRepository _budgets = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeModuleRepository _modules;
    private readonly FakeAuditLog _audit = new();
    private readonly BudgetService _service;

    private readonly Project _project;
    private readonly Role _leadRole = new() { Id = Guid.NewGuid(), Name = "Lead Developer", DisplayName = "Lead Developer" };
    private readonly Role _adminRole = new() { Id = Guid.NewGuid(), Name = RoleNames.Admin, IsBillable = false, IsSystemAdmin = true };

    public BudgetServiceTests()
    {
        _modules = new FakeModuleRepository(_roles);
        _service = new BudgetService(_currentUser, _projects, _budgets, _modules, _roles, _audit, new FakeBudgetAlertService());
        _roles.Roles.AddRange([_leadRole, _adminRole]);
        _project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Name = "P",
            Code = "GEO-001",
            ProjectManagerId = _currentUser.EmployeeId!.Value,
        };
        _projects.Projects.Add(_project);
        _currentUser.Roles.Add(RoleNames.ProjectManager);
    }

    [Fact]
    public async Task Setting_a_budget_creates_row_revision_and_audit()
    {
        _project.Type = ProjectType.FixedRate;

        await _service.SetBudgetAsync(_project.Id, amount: 50000m, hours: null, alertThresholds: null, reason: "SOW signed");

        var budget = Assert.Single(_budgets.Budgets);
        Assert.Equal(ProjectType.FixedRate, budget.Type);   // budget mirrors the project type
        Assert.Equal(50000m, budget.Amount);
        Assert.Equal([50, 75, 90], budget.AlertThresholds);

        var revision = Assert.Single(_budgets.Revisions);
        Assert.Null(revision.FromType);           // first revision has no "from"
        Assert.Equal(ProjectType.FixedRate, revision.ToType);
        Assert.Equal(50000m, revision.ToAmount);
        Assert.Equal("SOW signed", revision.Reason);

        Assert.Single(_audit.Events, e => e.Action == "budget.set");
    }

    [Fact]
    public async Task Revising_a_budget_updates_in_place_and_records_old_and_new()
    {
        await _service.SetBudgetAsync(_project.Id, 10000m, null, null, null);
        await _service.SetBudgetAsync(_project.Id, 15000m, null, null, "Change order #2");

        var budget = Assert.Single(_budgets.Budgets);   // still one budget — updated in place
        Assert.Equal(15000m, budget.Amount);

        Assert.Equal(2, _budgets.Revisions.Count);
        var latest = _budgets.Revisions.Last();
        Assert.Equal(10000m, latest.FromAmount);
        Assert.Equal(15000m, latest.ToAmount);
        Assert.Equal("Change order #2", latest.Reason);

        Assert.Single(_audit.Events, e => e.Action == "budget.revise");
    }

    [Fact]
    public async Task Thresholds_are_deduped_sorted_and_clamped()
    {
        await _service.SetBudgetAsync(_project.Id, null, 100m, alertThresholds: [90, 50, 50, 150, 0, 75], reason: null);

        var budget = Assert.Single(_budgets.Budgets);
        Assert.Equal([50, 75, 90], budget.AlertThresholds);   // 150 and 0 dropped, 50 deduped, sorted
    }

    [Fact]
    public async Task Role_hours_persist_and_overall_hours_defaults_to_their_sum()
    {
        await _service.SetBudgetAsync(
            _project.Id, amount: null, hours: null, alertThresholds: null, reason: null,
            roleAllocations: [new RoleHourInput(_leadRole.Id, 300m)]);

        var budget = Assert.Single(_budgets.Budgets);
        var allocation = Assert.Single(budget.RoleAllocations);
        Assert.Equal(_leadRole.Id, allocation.RoleId);
        Assert.Equal(300m, allocation.Hours);
        Assert.Equal(300m, budget.Hours);   // overall hours default to the sum of allocations
    }

    [Fact]
    public async Task Explicit_overall_hours_win_over_the_allocation_sum()
    {
        await _service.SetBudgetAsync(
            _project.Id, amount: null, hours: 500m, alertThresholds: null, reason: null,
            roleAllocations: [new RoleHourInput(_leadRole.Id, 300m)]);

        Assert.Equal(500m, Assert.Single(_budgets.Budgets).Hours);
    }

    [Fact]
    public async Task Fixed_rate_project_can_still_carry_role_hours()
    {
        _project.Type = ProjectType.FixedRate;

        await _service.SetBudgetAsync(
            _project.Id, amount: 50000m, hours: null, alertThresholds: null, reason: null,
            roleAllocations: [new RoleHourInput(_leadRole.Id, 300m)]);

        var budget = Assert.Single(_budgets.Budgets);
        Assert.Equal(50000m, budget.Amount);
        Assert.Equal(300m, budget.Hours);   // hours tracked alongside the contract amount
        Assert.Single(budget.RoleAllocations);
    }

    [Fact]
    public async Task Non_billable_role_cannot_get_an_allocation()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetBudgetAsync(
                _project.Id, 1000m, null, null, null,
                roleAllocations: [new RoleHourInput(_adminRole.Id, 10m)]));
    }

    [Fact]
    public async Task Zero_hour_allocations_are_dropped()
    {
        await _service.SetBudgetAsync(
            _project.Id, amount: 1000m, hours: null, alertThresholds: null, reason: null,
            roleAllocations: [new RoleHourInput(_leadRole.Id, 0m)]);

        Assert.Empty(Assert.Single(_budgets.Budgets).RoleAllocations);
    }

    [Fact]
    public async Task Fixed_rate_requires_the_contract_amount()
    {
        _project.Type = ProjectType.FixedRate;

        await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetBudgetAsync(_project.Id, amount: null, hours: 100m, alertThresholds: null, reason: null));
    }

    [Fact]
    public async Task A_budget_needs_at_least_one_dimension()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetBudgetAsync(_project.Id, amount: null, hours: null, alertThresholds: null, reason: null));
    }

    [Fact]
    public async Task Negative_amount_is_rejected()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetBudgetAsync(_project.Id, amount: -1m, hours: null, alertThresholds: null, reason: null));
    }

    [Fact]
    public async Task Service_contract_budget_is_the_total_contract_amount()
    {
        _project.Type = ProjectType.ServiceContract;

        await _service.SetBudgetAsync(_project.Id, amount: 96000m, hours: null, alertThresholds: null, reason: null);

        var budget = Assert.Single(_budgets.Budgets);
        Assert.Equal(96000m, budget.Amount);   // total for the whole engagement — burn averages over the contract
        Assert.Equal(ProjectType.ServiceContract, budget.Type);
    }

    [Fact]
    public async Task Internal_project_cannot_carry_a_budget()
    {
        _project.Type = ProjectType.Internal;

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetBudgetAsync(_project.Id, amount: 1000m, hours: null, alertThresholds: null, reason: null));
        Assert.Contains("no budget", ex.Message);
    }

    [Fact]
    public async Task Service_contract_requires_the_total_amount()
    {
        _project.Type = ProjectType.ServiceContract;

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetBudgetAsync(_project.Id, amount: null, hours: 100m, alertThresholds: null, reason: null));
        Assert.Contains("total contract amount", ex.Message);
    }

    [Theory]
    [InlineData(2026, 1, 1, 2026, 12, 31, 12)]
    [InlineData(2026, 1, 15, 2026, 2, 1, 2)]     // partial months count by calendar month
    [InlineData(2026, 7, 1, 2027, 6, 30, 12)]    // spans a year boundary
    [InlineData(2026, 3, 1, 2026, 3, 31, 1)]
    public void Contract_months_count_calendar_months_inclusive(
        int y1, int m1, int d1, int y2, int m2, int d2, int expected)
    {
        Assert.Equal(expected, BudgetService.ContractMonths(new DateOnly(y1, m1, d1), new DateOnly(y2, m2, d2)));
    }

    [Fact]
    public async Task Role_allocations_are_rejected_once_the_project_has_modules()
    {
        _modules.Modules.Add(new Crosscheck.Domain.Entities.ProjectModule
        {
            Id = Guid.NewGuid(), ProjectId = _project.Id, Name = "Ag Chem",
        });

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetBudgetAsync(_project.Id, amount: null, hours: null,
                alertThresholds: null, reason: null,
                roleAllocations: [new RoleHourInput(_leadRole.Id, 100m)]));
        Assert.Contains("per module", ex.Message);

        // The dollar budget itself still works alongside modules.
        await _service.SetBudgetAsync(_project.Id, amount: 56160m, hours: null,
            alertThresholds: null, reason: null);
        Assert.Single(_budgets.Budgets);
    }

    [Fact]
    public async Task A_pm_on_another_project_cannot_set_its_budget()
    {
        _project.ProjectManagerId = Guid.NewGuid();   // not the current user

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.SetBudgetAsync(_project.Id, 1000m, null, null, null));
    }

    [Fact]
    public async Task Admin_override_is_flagged_in_the_audit_event()
    {
        _currentUser.Roles.Clear();
        _currentUser.Roles.Add(RoleNames.Admin);
        _project.ProjectManagerId = Guid.NewGuid();   // Admin manages anyway, but it's an override

        await _service.SetBudgetAsync(_project.Id, 1000m, null, null, null);

        var evt = Assert.Single(_audit.Events);
        Assert.Contains("\"adminOverride\":true", System.Text.Json.JsonSerializer.Serialize(evt.Details));
    }
}
