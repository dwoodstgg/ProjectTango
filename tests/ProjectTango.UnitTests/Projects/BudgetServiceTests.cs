using ProjectTango.Application.Projects;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;
using ProjectTango.UnitTests.Fakes;

namespace ProjectTango.UnitTests.Projects;

public class BudgetServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeBudgetRepository _budgets = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeAuditLog _audit = new();
    private readonly BudgetService _service;

    private readonly Project _project;
    private readonly Role _leadRole = new() { Id = Guid.NewGuid(), Name = "Lead Developer", DisplayName = "Lead Developer" };
    private readonly Role _adminRole = new() { Id = Guid.NewGuid(), Name = RoleNames.Admin, IsBillable = false, IsSystemAdmin = true };

    public BudgetServiceTests()
    {
        _service = new BudgetService(_currentUser, _projects, _budgets, _roles, _audit, new FakeBudgetAlertService());
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
        await _service.SetBudgetAsync(_project.Id, BudgetType.FixedFee, amount: 50000m, hours: null, alertThresholds: null, reason: "SOW signed");

        var budget = Assert.Single(_budgets.Budgets);
        Assert.Equal(BudgetType.FixedFee, budget.Type);
        Assert.Equal(50000m, budget.Amount);
        Assert.Equal([50, 75, 90], budget.AlertThresholds);

        var revision = Assert.Single(_budgets.Revisions);
        Assert.Null(revision.FromType);           // first revision has no "from"
        Assert.Equal(BudgetType.FixedFee, revision.ToType);
        Assert.Equal(50000m, revision.ToAmount);
        Assert.Equal("SOW signed", revision.Reason);

        Assert.Single(_audit.Events, e => e.Action == "budget.set");
    }

    [Fact]
    public async Task Revising_a_budget_updates_in_place_and_records_old_and_new()
    {
        await _service.SetBudgetAsync(_project.Id, BudgetType.TimeAndMaterialsCap, 10000m, null, null, null);
        await _service.SetBudgetAsync(_project.Id, BudgetType.TimeAndMaterialsCap, 15000m, null, null, "Change order #2");

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
        await _service.SetBudgetAsync(_project.Id, BudgetType.HoursCap, null, 100m, alertThresholds: [90, 50, 50, 150, 0, 75], reason: null);

        var budget = Assert.Single(_budgets.Budgets);
        Assert.Equal([50, 75, 90], budget.AlertThresholds);   // 150 and 0 dropped, 50 deduped, sorted
    }

    [Fact]
    public async Task Role_hours_persist_and_overall_hours_defaults_to_their_sum()
    {
        await _service.SetBudgetAsync(
            _project.Id, BudgetType.HoursCap, amount: null, hours: null, alertThresholds: null, reason: null,
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
            _project.Id, BudgetType.HoursCap, amount: null, hours: 500m, alertThresholds: null, reason: null,
            roleAllocations: [new RoleHourInput(_leadRole.Id, 300m)]);

        Assert.Equal(500m, Assert.Single(_budgets.Budgets).Hours);
    }

    [Fact]
    public async Task Fixed_dollar_project_can_still_carry_role_hours()
    {
        await _service.SetBudgetAsync(
            _project.Id, BudgetType.FixedFee, amount: 50000m, hours: null, alertThresholds: null, reason: null,
            roleAllocations: [new RoleHourInput(_leadRole.Id, 300m)]);

        var budget = Assert.Single(_budgets.Budgets);
        Assert.Equal(50000m, budget.Amount);
        Assert.Equal(300m, budget.Hours);   // hours tracked alongside the fixed fee
        Assert.Single(budget.RoleAllocations);
    }

    [Fact]
    public async Task Non_billable_role_cannot_get_an_allocation()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetBudgetAsync(
                _project.Id, BudgetType.FixedFee, 1000m, null, null, null,
                roleAllocations: [new RoleHourInput(_adminRole.Id, 10m)]));
    }

    [Fact]
    public async Task Zero_hour_allocations_are_dropped()
    {
        await _service.SetBudgetAsync(
            _project.Id, BudgetType.FixedFee, amount: 1000m, hours: null, alertThresholds: null, reason: null,
            roleAllocations: [new RoleHourInput(_leadRole.Id, 0m)]);

        Assert.Empty(Assert.Single(_budgets.Budgets).RoleAllocations);
    }

    [Fact]
    public async Task Fixed_fee_requires_an_amount()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetBudgetAsync(_project.Id, BudgetType.FixedFee, amount: null, hours: 100m, alertThresholds: null, reason: null));
    }

    [Fact]
    public async Task Hours_cap_requires_hours()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetBudgetAsync(_project.Id, BudgetType.HoursCap, amount: 1000m, hours: null, alertThresholds: null, reason: null));
    }

    [Fact]
    public async Task Negative_amount_is_rejected()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetBudgetAsync(_project.Id, BudgetType.FixedFee, amount: -1m, hours: null, alertThresholds: null, reason: null));
    }

    [Fact]
    public async Task A_pm_on_another_project_cannot_set_its_budget()
    {
        _project.ProjectManagerId = Guid.NewGuid();   // not the current user

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.SetBudgetAsync(_project.Id, BudgetType.FixedFee, 1000m, null, null, null));
    }

    [Fact]
    public async Task Admin_override_is_flagged_in_the_audit_event()
    {
        _currentUser.Roles.Clear();
        _currentUser.Roles.Add(RoleNames.Admin);
        _project.ProjectManagerId = Guid.NewGuid();   // Admin manages anyway, but it's an override

        await _service.SetBudgetAsync(_project.Id, BudgetType.FixedFee, 1000m, null, null, null);

        var evt = Assert.Single(_audit.Events);
        Assert.Contains("\"adminOverride\":true", System.Text.Json.JsonSerializer.Serialize(evt.Details));
    }
}
