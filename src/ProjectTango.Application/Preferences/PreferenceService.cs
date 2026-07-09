using ProjectTango.Application.Common;

namespace ProjectTango.Application.Preferences;

/// <summary>Per-user UI preferences that follow the signed-in employee across devices.
/// Always scoped to the current user — a caller can only read/write their own settings.</summary>
public class PreferenceService(ICurrentUser currentUser, IEmployeePreferenceRepository preferences)
{
    public const string TimesheetLayoutKey = "timesheet_layout";

    private static readonly string[] TimesheetLayouts = ["grid", "daily"];

    /// <summary>The employee's last-used timesheet layout, or null when unset/invalid.</summary>
    public async Task<string?> GetTimesheetLayoutAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.EmployeeId is not { } employeeId) return null;
        var value = await preferences.GetAsync(employeeId, TimesheetLayoutKey, cancellationToken);
        return TimesheetLayouts.Contains(value) ? value : null;
    }

    public async Task SetTimesheetLayoutAsync(string layout, CancellationToken cancellationToken = default)
    {
        var employeeId = currentUser.EmployeeId ?? throw new UnauthorizedAccessException("No signed-in employee.");
        if (!TimesheetLayouts.Contains(layout))
        {
            throw new ArgumentException($"Unknown timesheet layout '{layout}'.", nameof(layout));
        }
        await preferences.SetAsync(employeeId, TimesheetLayoutKey, layout, cancellationToken);
    }
}
