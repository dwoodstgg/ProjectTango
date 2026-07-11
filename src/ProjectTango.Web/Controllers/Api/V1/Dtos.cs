using ProjectTango.Application.TimeEntries;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Web.Controllers.Api.V1;

/// <summary>API projection of a time entry. Kept separate from the domain entity so the wire
/// contract (consumed by mobile/desktop clients) is stable and independent of internal changes.
/// Status is serialized as its text value to match the database representation (design-doc §7).</summary>
public sealed record TimeEntryDto(
    Guid Id,
    Guid ProjectId,
    Guid EmployeeId,
    Guid BillingRoleId,
    DateOnly EntryDate,
    decimal HoursWorked,
    decimal HoursBilled,
    string? Notes,
    bool IsBillable,
    string Status)
{
    public static TimeEntryDto From(TimeEntry e) => new(
        e.Id, e.ProjectId, e.EmployeeId, e.BillingRoleId, e.EntryDate,
        e.HoursWorked, e.HoursBilled, e.Notes, e.IsBillable, e.Status.ToString());
}

public sealed record TimesheetProjectDto(
    Guid ProjectId, string Code, string Name, string ClientName, Guid? DefaultBillingRoleId);

public sealed record BillableRoleDto(Guid Id, string DisplayName);

/// <summary>The signed-in employee's timesheet grid for a date range: the project rows they may
/// log against, their existing entries, and the billable roles to pick from.</summary>
public sealed record MyTimesheetResponse(
    IReadOnlyList<TimesheetProjectDto> Projects,
    IReadOnlyList<TimeEntryDto> Entries,
    IReadOnlyList<BillableRoleDto> BillableRoles)
{
    public static MyTimesheetResponse From(MyMonthTimesheet m) => new(
        m.Projects
            .Select(p => new TimesheetProjectDto(p.ProjectId, p.Code, p.Name, p.ClientName, p.DefaultBillingRoleId))
            .ToList(),
        m.Entries.Select(TimeEntryDto.From).ToList(),
        m.BillableRoles.Select(r => new BillableRoleDto(r.Id, r.DisplayName)).ToList());
}

/// <summary>Records hours for one (project, day) cell for the signed-in employee. Zero hours
/// clears the cell.</summary>
public sealed record SaveTimeEntryRequest(
    Guid ProjectId, DateOnly Date, decimal Hours, Guid BillingRoleId, string? Notes);
