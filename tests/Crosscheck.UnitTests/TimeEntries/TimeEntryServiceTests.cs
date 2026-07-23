using Crosscheck.Application.TimeEntries;
using Crosscheck.Domain;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;
using Crosscheck.UnitTests.Fakes;

namespace Crosscheck.UnitTests.TimeEntries;

public class TimeEntryServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeClientRepository _clients = new();
    private readonly FakeAssignmentRepository _assignments = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeTimeEntryRepository _entries = new();
    private readonly FakeRateCardRepository _rateCards;
    private readonly FakeModuleRepository _modules;
    private readonly FakeTimesheetPeriodRepository _periods = new();
    private readonly TimeEntryService _service;

    private readonly Client _client;
    private readonly Project _project;
    private readonly Role _developer;
    private readonly Role _admin;
    private static readonly DateOnly Day = new(2026, 7, 8);

    public TimeEntryServiceTests()
    {
        _rateCards = new FakeRateCardRepository(_roles);
        _modules = new FakeModuleRepository(_roles);
        _service = new TimeEntryService(_currentUser, _projects, _clients, _assignments, _roles, _entries, _rateCards, _modules, _periods, new FakeBudgetAlertService());

        _client = new Client { Id = Guid.NewGuid(), Name = "Acme" };
        _clients.Clients.Add(_client);
        _project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = _client.Id,
            Name = "P",
            Code = "GEO-001",
            Status = ProjectStatus.Active,
            ProjectManagerId = Guid.NewGuid(),
        };
        _projects.Projects.Add(_project);

        _developer = new Role { Id = Guid.NewGuid(), Name = RoleNames.Developer, DisplayName = "Developer", IsBillable = true };
        _admin = new Role { Id = Guid.NewGuid(), Name = RoleNames.Admin, DisplayName = "Admin", IsBillable = false, IsSystemAdmin = true };
        _roles.Roles.AddRange([_developer, _admin]);

        // The signed-in user is actively assigned to the project.
        _assignments.Assignments.Add(new ProjectAssignment
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            EmployeeId = _currentUser.EmployeeId!.Value,
        });

        // A rate card covers (project, Developer) so billable entries auto-approve on save.
        _rateCards.Rates.Add(new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            RoleId = _developer.Id,
            HourlyRate = 150m,
        });
    }

    [Fact]
    public async Task Save_auto_approves_a_billable_entry_when_a_rate_card_covers_it()
    {
        var entry = await _service.SaveHoursAsync(_project.Id, Day, 6.5m, _developer.Id, "did work");

        Assert.NotNull(entry);
        var stored = Assert.Single(_entries.Entries);
        Assert.Equal(TimeEntryStatus.Approved, stored.Status);
        Assert.Equal(_currentUser.EmployeeId, stored.ApprovedById);
        Assert.NotNull(stored.ApprovedAt);
        Assert.Equal(6.5m, stored.HoursWorked);
        Assert.Equal(6.5m, stored.HoursBilled);
        Assert.True(stored.IsBillable);
        Assert.Equal("did work", stored.Notes);
    }

    [Fact]
    public async Task Save_leaves_a_billable_entry_open_when_no_rate_card_covers_it()
    {
        _rateCards.Rates.Clear();

        var entry = await _service.SaveHoursAsync(_project.Id, Day, 6.5m, _developer.Id, "did work");

        Assert.NotNull(entry);
        var stored = Assert.Single(_entries.Entries);
        Assert.Equal(TimeEntryStatus.Open, stored.Status);
        Assert.Null(stored.ApprovedById);
        Assert.Null(stored.ApprovedAt);
    }

    [Fact]
    public async Task Save_zero_hours_clears_the_cell()
    {
        await _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, "work");
        var result = await _service.SaveHoursAsync(_project.Id, Day, 0m, _developer.Id, null);

        Assert.Null(result);
        Assert.Empty(_entries.Entries);
    }

    [Fact]
    public async Task Save_updates_the_existing_entry_in_place()
    {
        await _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, "work");
        await _service.SaveHoursAsync(_project.Id, Day, 7.25m, _developer.Id, "more");

        var stored = Assert.Single(_entries.Entries);
        Assert.Equal(7.25m, stored.HoursWorked);
        Assert.Equal(7.25m, stored.HoursBilled);
        Assert.Equal("more", stored.Notes);
    }

    [Fact]
    public async Task An_auto_approved_entry_can_still_be_edited_by_the_owner()
    {
        await _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, "work");
        Assert.Equal(TimeEntryStatus.Approved, _entries.Entries.Single().Status);

        var updated = await _service.SaveHoursAsync(_project.Id, Day, 5m, _developer.Id, "revised");

        Assert.NotNull(updated);
        Assert.Equal(5m, _entries.Entries.Single().HoursWorked);
    }

    [Fact]
    public async Task Save_requires_an_active_assignment()
    {
        _assignments.Assignments.Clear();

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, null));
        Assert.Contains("assigned to", ex.Message);
    }

    [Fact]
    public async Task Save_rejects_non_quarter_hour_increments()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.SaveHoursAsync(_project.Id, Day, 1.1m, _developer.Id, null));
    }

    [Fact]
    public async Task Save_rejects_hours_over_24()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.SaveHoursAsync(_project.Id, Day, 24.25m, _developer.Id, null));
    }

    [Fact]
    public async Task Save_is_blocked_when_the_window_is_closed()
    {
        var window = SemiMonthlyPeriod.Containing(Day);
        _periods.Periods.Add(new TimesheetPeriod
        {
            Id = Guid.NewGuid(),
            PeriodStart = window.Start,
            PeriodEnd = window.End,
            Status = TimesheetPeriodStatus.Closed,
        });

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, null));
        Assert.Contains("closed", ex.Message);
    }

    [Fact]
    public async Task Save_is_blocked_on_a_closed_project()
    {
        _project.Status = ProjectStatus.Closed;

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, null));
        Assert.Contains("accepts new time", ex.Message);
    }

    [Fact]
    public async Task Save_rejects_a_non_billable_billing_role()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.SaveHoursAsync(_project.Id, Day, 4m, _admin.Id, null));
    }

    [Fact]
    public async Task Billable_time_requires_a_description()
    {
        await Assert.ThrowsAsync<DescriptionRequiredException>(() =>
            _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, null));

        await Assert.ThrowsAsync<DescriptionRequiredException>(() =>
            _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, "   "));

        Assert.Empty(_entries.Entries);
    }

    [Fact]
    public async Task Internal_client_entries_are_non_billable_and_need_no_description()
    {
        _client.IsInternal = true;
        _rateCards.Rates.Clear(); // non-billable time needs no rate card to auto-approve

        var entry = await _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, null);

        Assert.NotNull(entry);
        Assert.False(entry!.IsBillable);
        Assert.Null(entry.Notes);
        Assert.Equal(TimeEntryStatus.Approved, entry.Status);
    }

    [Fact]
    public async Task Internal_project_entries_are_non_billable_and_auto_approve_without_a_rate()
    {
        _project.Type = ProjectType.Internal;
        _rateCards.Rates.Clear(); // non-billable time needs no rate card to auto-approve

        var entry = await _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, "sorted the storage room");

        Assert.NotNull(entry);
        Assert.False(entry!.IsBillable);
        Assert.Equal("sorted the storage room", entry.Notes);   // description still carried
        Assert.Equal(TimeEntryStatus.Approved, entry.Status);
    }

    [Fact]
    public async Task An_invoiced_entry_cannot_be_edited_by_the_owner()
    {
        await _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, "work");
        _entries.Entries.Single().Status = TimeEntryStatus.Invoiced;

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.SaveHoursAsync(_project.Id, Day, 5m, _developer.Id, "revised"));
        Assert.Contains("invoice", ex.Message);
    }

    // Modules -------------------------------------------------------------------

    private ProjectModule AddModule(string name = "Ag Chem", decimal? amount = null)
    {
        var module = new ProjectModule
        {
            Id = Guid.NewGuid(), ProjectId = _project.Id, Name = name, Amount = amount,
        };
        _modules.Modules.Add(module);
        return module;
    }

    [Fact]
    public async Task Modular_project_requires_a_module_on_new_entries()
    {
        AddModule();

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, "work"));
        Assert.Contains("pick a module", ex.Message);

        // Milestone-labeled projects word the error accordingly.
        _project.BreakdownLabel = BreakdownLabel.Milestone;
        var ex2 = await Assert.ThrowsAsync<DomainException>(() =>
            _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, "work"));
        Assert.Contains("milestone", ex2.Message);
    }

    [Fact]
    public async Task Non_modular_project_rejects_a_module()
    {
        var foreign = new ProjectModule { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Name = "Other" };
        _modules.Modules.Add(foreign);

        await Assert.ThrowsAsync<DomainException>(() =>
            _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, "work", moduleId: foreign.Id));
    }

    [Fact]
    public async Task Deleted_module_is_rejected_for_new_time()
    {
        var module = AddModule();
        module.DeletedAt = DateTimeOffset.UtcNow;
        AddModule("Water Levels"); // project stays modular

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, "work", moduleId: module.Id));
        Assert.Contains("removed", ex.Message);
    }

    [Fact]
    public async Task Same_day_can_hold_entries_on_two_modules()
    {
        var agChem = AddModule();
        var waterLevels = AddModule("Water Levels");

        await _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, "ag chem work", moduleId: agChem.Id);
        await _service.SaveHoursAsync(_project.Id, Day, 2m, _developer.Id, "water levels work", moduleId: waterLevels.Id);

        Assert.Equal(2, _entries.Entries.Count);
        Assert.All(_entries.Entries, e => Assert.Equal(TimeEntryStatus.Approved, e.Status));

        // Editing one cell touches only that module's entry.
        await _service.SaveHoursAsync(_project.Id, Day, 5m, _developer.Id, "more", moduleId: agChem.Id);
        Assert.Equal(5m, _entries.Entries.Single(e => e.ModuleId == agChem.Id).HoursWorked);
        Assert.Equal(2m, _entries.Entries.Single(e => e.ModuleId == waterLevels.Id).HoursWorked);
    }

    [Fact]
    public async Task Clearing_an_unassigned_cell_on_a_modular_project_still_works()
    {
        // Entry created before the project became modular (no module).
        await _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, "old work");
        AddModule();

        var result = await _service.SaveHoursAsync(_project.Id, Day, 0m, _developer.Id, null);

        Assert.Null(result);
        Assert.Empty(_entries.Entries);
    }

    [Fact]
    public async Task Auto_approve_prefers_module_override_then_module_wide_then_project_rate()
    {
        var module = AddModule();
        _rateCards.Rates.Clear(); // no project rate

        // No rate anywhere → stays open.
        await _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, "work", moduleId: module.Id);
        Assert.Equal(TimeEntryStatus.Open, _entries.Entries.Single().Status);
        _entries.Entries.Clear();

        // A module-wide override approves the entry even with no project rate.
        _rateCards.ModuleRates[(module.Id, null)] = 85m;
        await _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, "work", moduleId: module.Id);
        Assert.Equal(TimeEntryStatus.Approved, _entries.Entries.Single().Status);
    }

    [Fact]
    public async Task Fixed_price_module_entries_approve_without_any_rate()
    {
        var module = AddModule("Cost Share", amount: 5400m);
        _rateCards.Rates.Clear();

        await _service.SaveHoursAsync(_project.Id, Day, 4m, _developer.Id, "work", moduleId: module.Id);

        Assert.Equal(TimeEntryStatus.Approved, _entries.Entries.Single().Status);
    }
}
