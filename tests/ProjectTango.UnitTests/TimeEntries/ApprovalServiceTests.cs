using ProjectTango.Application.TimeEntries;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;
using ProjectTango.UnitTests.Fakes;

namespace ProjectTango.UnitTests.TimeEntries;

public class ApprovalServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeTimeEntryRepository _entries = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeRateCardRepository _rateCards;
    private readonly FakeAuditLog _audit = new();
    private readonly ApprovalService _service;

    private readonly Project _project;
    private readonly Role _developer;
    private static readonly DateOnly Day = new(2026, 7, 8);

    public ApprovalServiceTests()
    {
        _rateCards = new FakeRateCardRepository(_roles);
        _service = new ApprovalService(_currentUser, _projects, _entries, _rateCards, _audit, new FakeBudgetAlertService());

        _project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Name = "P",
            Code = "GEO-001",
            Status = ProjectStatus.Active,
            ProjectManagerId = _currentUser.EmployeeId!.Value, // current user is the PM
        };
        _projects.Projects.Add(_project);
        _currentUser.Roles.Add(RoleNames.ProjectManager);

        _developer = new Role { Id = Guid.NewGuid(), Name = RoleNames.Developer, DisplayName = "Developer", IsBillable = true };
        _roles.Roles.Add(_developer);
        _rateCards.Rates.Add(new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            RoleId = _developer.Id,
            HourlyRate = 150m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
        });
    }

    private TimeEntry AddOpenEntry(bool billable = true, decimal worked = 8m)
    {
        var entry = new TimeEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = _project.Id,
            EmployeeId = Guid.NewGuid(),
            BillingRoleId = _developer.Id,
            EntryDate = Day,
            HoursWorked = worked,
            HoursBilled = worked,
            IsBillable = billable,
            Status = TimeEntryStatus.Open,
        };
        _entries.Entries.Add(entry);
        return entry;
    }

    [Fact]
    public async Task Approve_stamps_approver_and_audits()
    {
        var entry = AddOpenEntry();

        await _service.ApproveAsync(entry.Id, billedHours: null);

        Assert.Equal(TimeEntryStatus.Approved, entry.Status);
        Assert.Equal(_currentUser.EmployeeId, entry.ApprovedById);
        Assert.NotNull(entry.ApprovedAt);
        Assert.Single(_audit.Events, e => e.Action == "timeentry.approved");
    }

    [Fact]
    public async Task Approve_can_bill_fewer_hours_than_worked()
    {
        var entry = AddOpenEntry(worked: 8m);

        await _service.ApproveAsync(entry.Id, billedHours: 6m);

        Assert.Equal(8m, entry.HoursWorked); // never altered
        Assert.Equal(6m, entry.HoursBilled);
    }

    [Fact]
    public async Task Approve_cannot_bill_more_than_worked()
    {
        var entry = AddOpenEntry(worked: 8m);

        await Assert.ThrowsAsync<DomainException>(() => _service.ApproveAsync(entry.Id, billedHours: 9m));
    }

    [Fact]
    public async Task Billable_entry_without_a_rate_card_cannot_be_approved()
    {
        _rateCards.Rates.Clear();
        var entry = AddOpenEntry(billable: true);

        var ex = await Assert.ThrowsAsync<DomainException>(() => _service.ApproveAsync(entry.Id, null));
        Assert.Contains("rate card", ex.Message);
    }

    [Fact]
    public async Task Non_billable_entry_needs_no_rate_card()
    {
        _rateCards.Rates.Clear();
        var entry = AddOpenEntry(billable: false);

        await _service.ApproveAsync(entry.Id, null);

        Assert.Equal(TimeEntryStatus.Approved, entry.Status);
    }

    [Fact]
    public async Task Unapprove_returns_the_entry_to_open_and_audits()
    {
        var entry = AddOpenEntry();
        await _service.ApproveAsync(entry.Id, null);

        await _service.UnapproveAsync(entry.Id, "please fix the notes");

        Assert.Equal(TimeEntryStatus.Open, entry.Status);
        Assert.Null(entry.ApprovedById);
        Assert.Null(entry.ApprovedAt);
        Assert.Single(_audit.Events, e => e.Action == "timeentry.unapproved");
    }

    [Fact]
    public async Task Invoiced_entries_cannot_be_unapproved()
    {
        var entry = AddOpenEntry();
        entry.Status = TimeEntryStatus.Invoiced;

        var ex = await Assert.ThrowsAsync<DomainException>(() => _service.UnapproveAsync(entry.Id));
        Assert.Contains("void the invoice", ex.Message);
    }

    [Fact]
    public async Task A_pm_of_another_project_cannot_approve()
    {
        _project.ProjectManagerId = Guid.NewGuid();
        var entry = AddOpenEntry();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.ApproveAsync(entry.Id, null));
    }

    [Fact]
    public async Task ApproveMany_approves_all_open_entries()
    {
        var a = AddOpenEntry();
        var b = AddOpenEntry();

        var count = await _service.ApproveManyAsync([a.Id, b.Id]);

        Assert.Equal(2, count);
        Assert.All(_entries.Entries, e => Assert.Equal(TimeEntryStatus.Approved, e.Status));
    }
}
