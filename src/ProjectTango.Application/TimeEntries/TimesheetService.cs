using ProjectTango.Application.Common;
using ProjectTango.Application.Projects;
using ProjectTango.Application.Roles;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.TimeEntries;

public record TimesheetProject(Guid ProjectId, string Code, string Name, string ClientName, Guid? DefaultBillingRoleId);

public record BillableRoleOption(Guid Id, string DisplayName);

/// <summary>Everything the signed-in employee's monthly grid needs: the project rows they
/// may log against this month, their existing entries, and the billable roles to pick from.</summary>
public class MyMonthTimesheet
{
    public required IReadOnlyList<TimesheetProject> Projects { get; init; }
    public required IReadOnlyList<TimeEntry> Entries { get; init; }
    public required IReadOnlyList<BillableRoleOption> BillableRoles { get; init; }
}

/// <summary>Read-side assembly of an employee's own timesheet grid. It reads only the
/// caller's own data, so no role is required.</summary>
public class TimesheetService(
    ICurrentUser currentUser,
    IAssignmentRepository assignments,
    ITimeEntryRepository entries,
    IRoleRepository roles)
{
    public async Task<MyMonthTimesheet> GetMyRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var me = currentUser.EmployeeId ?? throw new UnauthorizedAccessException("No signed-in employee.");

        var monthEntries = await entries.GetForEmployeeRangeAsync(me, from, to, cancellationToken);
        var projectsWithEntries = monthEntries.Select(e => e.ProjectId).ToHashSet();

        // Rows: assignments that overlap the month, plus any project the employee already
        // has entries on this month (so historical rows never disappear).
        var myAssignments = await assignments.GetForEmployeeAsync(me, cancellationToken);
        var projects = myAssignments
            .Where(a => Overlaps(a.Assignment, from, to) || projectsWithEntries.Contains(a.Assignment.ProjectId))
            .Select(a => new TimesheetProject(a.Assignment.ProjectId, a.ProjectCode, a.ProjectName, a.ClientName, a.Assignment.DefaultBillingRoleId))
            .DistinctBy(p => p.ProjectId)
            .OrderBy(p => p.Code)
            .ToList();

        var billableRoles = (await roles.GetAllAsync(cancellationToken))
            .Where(r => r.IsBillable)
            .Select(r => new BillableRoleOption(r.Id, r.DisplayName))
            .OrderBy(r => r.DisplayName)
            .ToList();

        return new MyMonthTimesheet { Projects = projects, Entries = monthEntries, BillableRoles = billableRoles };
    }

    private static bool Overlaps(ProjectAssignment a, DateOnly from, DateOnly to) =>
        (a.StartDate is null || a.StartDate <= to) && (a.EndDate is null || a.EndDate >= from);
}
