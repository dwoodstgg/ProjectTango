using ProjectTango.Application.Projects;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.UnitTests.Fakes;

namespace ProjectTango.UnitTests.Projects;

public class RateCardServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeRateCardRepository _rateCards;
    private readonly FakeAuditLog _audit = new();
    private readonly RateCardService _service;

    private readonly Role _developerRole = new() { Id = Guid.NewGuid(), Name = RoleNames.Developer };
    private readonly Role _adminRole = new() { Id = Guid.NewGuid(), Name = RoleNames.Admin, IsBillable = false, IsSystemAdmin = true };
    private readonly Project _project;

    public RateCardServiceTests()
    {
        _rateCards = new FakeRateCardRepository(_roles);
        _service = new RateCardService(_currentUser, _projects, _rateCards, _roles, _audit);

        _roles.Roles.AddRange([_developerRole, _adminRole]);
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
    public async Task First_rate_is_open_ended()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 150m, new DateOnly(2026, 1, 1));

        var rate = Assert.Single(_rateCards.Rates);
        Assert.Equal(150m, rate.HourlyRate);
        Assert.Null(rate.EffectiveTo);
        Assert.Single(_audit.Events, e => e.Action == "rate.set");
    }

    [Fact]
    public async Task Rate_change_closes_previous_row_day_before()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 150m, new DateOnly(2026, 1, 1));
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 165m, new DateOnly(2026, 7, 1));

        Assert.Equal(2, _rateCards.Rates.Count);
        var closed = Assert.Single(_rateCards.Closed);
        Assert.Equal(new DateOnly(2026, 6, 30), closed.EffectiveTo);

        // Resolution picks the correct row on each side of the boundary.
        Assert.Equal(150m, await _rateCards.ResolveAsync(_project.Id, _developerRole.Id, new DateOnly(2026, 6, 30)));
        Assert.Equal(165m, await _rateCards.ResolveAsync(_project.Id, _developerRole.Id, new DateOnly(2026, 7, 1)));
        Assert.Null(await _rateCards.ResolveAsync(_project.Id, _developerRole.Id, new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public async Task Rate_change_must_start_after_latest_existing()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 150m, new DateOnly(2026, 6, 1));

        await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetRateAsync(_project.Id, _developerRole.Id, 165m, new DateOnly(2026, 6, 1)));
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetRateAsync(_project.Id, _developerRole.Id, 165m, new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public async Task Non_billable_role_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.SetRateAsync(_project.Id, _adminRole.Id, 150m, new DateOnly(2026, 1, 1)));

        Assert.Contains("not a billable role", ex.Message);
    }

    [Fact]
    public async Task Pm_of_other_project_cannot_set_rates()
    {
        _project.ProjectManagerId = Guid.NewGuid();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.SetRateAsync(_project.Id, _developerRole.Id, 150m, new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public async Task Correct_fixes_amount_in_place()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 135m, new DateOnly(2026, 7, 8));
        var rate = Assert.Single(_rateCards.Rates);

        await _service.CorrectRateAsync(_project.Id, rate.Id, 150m, new DateOnly(2026, 7, 8));

        Assert.Equal(150m, Assert.Single(_rateCards.Rates).HourlyRate);
        Assert.Single(_audit.Events, e => e.Action == "rate.correct");
    }

    [Fact]
    public async Task Correct_moves_start_and_reabuts_predecessor()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 150m, new DateOnly(2026, 1, 1));
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 165m, new DateOnly(2026, 7, 1));
        var current = _rateCards.Rates.Single(r => r.EffectiveTo is null);
        var prior = _rateCards.Rates.Single(r => r.EffectiveTo is not null);

        // Slide the change earlier; the predecessor's close date should follow.
        await _service.CorrectRateAsync(_project.Id, current.Id, 165m, new DateOnly(2026, 6, 1));

        Assert.Equal(new DateOnly(2026, 6, 1), current.EffectiveFrom);
        Assert.Equal(new DateOnly(2026, 5, 31), prior.EffectiveTo);
    }

    [Fact]
    public async Task Correct_rejects_start_on_or_before_previous()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 150m, new DateOnly(2026, 1, 1));
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 165m, new DateOnly(2026, 7, 1));
        var current = _rateCards.Rates.Single(r => r.EffectiveTo is null);

        await Assert.ThrowsAsync<DomainException>(() =>
            _service.CorrectRateAsync(_project.Id, current.Id, 165m, new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public async Task Correct_blocked_once_time_is_invoiced()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 135m, new DateOnly(2026, 7, 8));
        var rate = Assert.Single(_rateCards.Rates);
        _rateCards.InvoicedTime.Add((_project.Id, _developerRole.Id, new DateOnly(2026, 7, 10)));

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.CorrectRateAsync(_project.Id, rate.Id, 150m, new DateOnly(2026, 7, 8)));
        Assert.Contains("invoiced", ex.Message);
    }

    [Fact]
    public async Task Delete_removes_current_row_and_reopens_predecessor()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 150m, new DateOnly(2026, 1, 1));
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 165m, new DateOnly(2026, 7, 1));
        var current = _rateCards.Rates.Single(r => r.EffectiveTo is null);
        var priorId = _rateCards.Rates.Single(r => r.EffectiveTo is not null).Id;

        await _service.DeleteRateAsync(_project.Id, current.Id);

        Assert.DoesNotContain(_rateCards.Rates, r => r.Id == current.Id);
        Assert.Null(_rateCards.Rates.Single(r => r.Id == priorId).EffectiveTo);
        Assert.Single(_audit.Events, e => e.Action == "rate.delete");
    }

    [Fact]
    public async Task Delete_blocked_once_time_is_invoiced()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 135m, new DateOnly(2026, 7, 8));
        var rate = Assert.Single(_rateCards.Rates);
        _rateCards.InvoicedTime.Add((_project.Id, _developerRole.Id, new DateOnly(2026, 7, 10)));

        await Assert.ThrowsAsync<DomainException>(() =>
            _service.DeleteRateAsync(_project.Id, rate.Id));
        Assert.DoesNotContain(_rateCards.Deleted, id => id == rate.Id);
    }

    [Fact]
    public async Task Pm_of_other_project_cannot_correct_rates()
    {
        await _service.SetRateAsync(_project.Id, _developerRole.Id, 135m, new DateOnly(2026, 7, 8));
        var rate = Assert.Single(_rateCards.Rates);
        _project.ProjectManagerId = Guid.NewGuid();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.CorrectRateAsync(_project.Id, rate.Id, 150m, new DateOnly(2026, 7, 8)));
    }
}
