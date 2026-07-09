using ProjectTango.Application.Preferences;
using ProjectTango.UnitTests.Fakes;

namespace ProjectTango.UnitTests.Preferences;

public class PreferenceServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeEmployeePreferenceRepository _preferences = new();
    private readonly PreferenceService _service;

    public PreferenceServiceTests()
    {
        _service = new PreferenceService(_currentUser, _preferences);
    }

    [Fact]
    public async Task GetTimesheetLayout_returns_null_when_unset()
    {
        Assert.Null(await _service.GetTimesheetLayoutAsync());
    }

    [Fact]
    public async Task SetTimesheetLayout_then_get_round_trips_for_the_current_user()
    {
        await _service.SetTimesheetLayoutAsync("daily");

        Assert.Equal("daily", await _service.GetTimesheetLayoutAsync());
        Assert.Equal("daily", _preferences.Values[(_currentUser.EmployeeId!.Value, PreferenceService.TimesheetLayoutKey)]);
    }

    [Fact]
    public async Task SetTimesheetLayout_rejects_an_unknown_value()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SetTimesheetLayoutAsync("kanban"));
    }

    [Fact]
    public async Task SetTimesheetLayout_requires_a_signed_in_employee()
    {
        _currentUser.EmployeeId = null;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.SetTimesheetLayoutAsync("grid"));
    }

    [Fact]
    public async Task GetTimesheetLayout_ignores_a_stored_invalid_value()
    {
        _preferences.Values[(_currentUser.EmployeeId!.Value, PreferenceService.TimesheetLayoutKey)] = "bogus";

        Assert.Null(await _service.GetTimesheetLayoutAsync());
    }

    [Fact]
    public async Task GetTimesheetLayout_returns_null_when_not_signed_in()
    {
        _currentUser.EmployeeId = null;

        Assert.Null(await _service.GetTimesheetLayoutAsync());
    }
}
