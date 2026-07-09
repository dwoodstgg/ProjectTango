using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectTango.Application.Preferences;
using ProjectTango.Application.TimeEntries;
using ProjectTango.Domain;
using ProjectTango.Domain.Enums;
using ProjectTango.Web.Models;

namespace ProjectTango.Web.Controllers;

/// <summary>The employee's own timesheet grid (projects × days). Defaults to the current
/// week (Sunday-start); a month view is also available. Any signed-in employee records
/// their own time.</summary>
[Authorize]
public class TimesheetController(
    TimesheetService timesheet,
    TimeEntryService timeEntries,
    TimesheetPeriodService periods,
    PreferenceService preferences) : Controller
{
    public async Task<IActionResult> Index(DateOnly? anchor, string? view, CancellationToken cancellationToken)
    {
        var employeeId = User.GetEmployeeId();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var isWeek = !string.Equals(view, "month", StringComparison.OrdinalIgnoreCase);
        var mode = isWeek ? "week" : "month";
        var at = anchor ?? today;

        // Visible range + navigation step depend on the view.
        DateOnly rangeStart, rangeEnd, prevAnchor, nextAnchor;
        string label;
        if (isWeek)
        {
            rangeStart = at.AddDays(-(int)at.DayOfWeek); // DayOfWeek.Sunday == 0 → Sunday-start week
            rangeEnd = rangeStart.AddDays(6);
            prevAnchor = rangeStart.AddDays(-7);
            nextAnchor = rangeStart.AddDays(7);
            label = rangeStart.Month == rangeEnd.Month
                ? $"{rangeStart:MMM d} – {rangeEnd:d}, {rangeEnd:yyyy}"
                : $"{rangeStart:MMM d} – {rangeEnd:MMM d, yyyy}";
        }
        else
        {
            rangeStart = new DateOnly(at.Year, at.Month, 1);
            rangeEnd = new DateOnly(at.Year, at.Month, DateTime.DaysInMonth(at.Year, at.Month));
            prevAnchor = rangeStart.AddMonths(-1);
            nextAnchor = rangeStart.AddMonths(1);
            label = rangeStart.ToString("MMMM yyyy");
        }

        if (employeeId is null)
        {
            return View(new TimesheetGridViewModel { HasEmployee = false, ViewMode = mode, RangeLabel = label });
        }

        var my = await timesheet.GetMyRangeAsync(rangeStart, rangeEnd, cancellationToken);
        var closedStarts = (await periods.ListInRangeAsync(rangeStart, rangeEnd, cancellationToken))
            .Where(p => p.Status == TimesheetPeriodStatus.Closed)
            .Select(p => p.PeriodStart)
            .ToHashSet();

        bool IsLocked(DateOnly date) => closedStarts.Contains(SemiMonthlyPeriod.Containing(date).Start);

        var days = new List<DayColumn>();
        for (var date = rangeStart; date <= rangeEnd; date = date.AddDays(1))
        {
            var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            days.Add(new DayColumn(date.Day, date, date.ToString("ddd", CultureInfo.InvariantCulture)[..2], isWeekend, IsLocked(date)));
        }

        // Cells are keyed by ISO day-number within the visible range (unique per column).
        var entriesByProject = my.Entries
            .GroupBy(e => e.ProjectId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(e => e.EntryDate.DayNumber, e => e));

        var rows = my.Projects.Select(p =>
        {
            var cells = new Dictionary<int, CellVm>();
            if (entriesByProject.TryGetValue(p.ProjectId, out var byDay))
            {
                foreach (var (dayNumber, e) in byDay)
                {
                    cells[dayNumber] = new CellVm(e.HoursWorked, e.BillingRoleId, DbStatus(e.Status), e.Notes, IsLocked(e.EntryDate));
                }
            }

            return new TimesheetRowVm
            {
                ProjectId = p.ProjectId,
                Code = p.Code,
                Name = p.Name,
                ClientName = p.ClientName,
                DefaultBillingRoleId = p.DefaultBillingRoleId ?? my.BillableRoles.FirstOrDefault()?.Id,
                Cells = cells,
            };
        }).ToList();

        var model = new TimesheetGridViewModel
        {
            ViewMode = mode,
            InitialLayout = await preferences.GetTimesheetLayoutAsync(cancellationToken) ?? "grid",
            RangeLabel = label,
            CurrentAnchor = at,
            PrevAnchor = prevAnchor,
            NextAnchor = nextAnchor,
            TodayAnchor = today,
            Days = days,
            Rows = rows,
            BillableRoleOptions = my.BillableRoles.Select(r => new SelectListItem(r.DisplayName, r.Id.ToString())).ToList(),
        };
        return View(model);
    }

    /// <summary>JSON feed for the daily view: every assigned project plus the caller's entries
    /// for a full calendar month, so time can be logged on any day of the chosen month.</summary>
    public async Task<IActionResult> DailyMonth(int year, int month, CancellationToken cancellationToken)
    {
        if (User.GetEmployeeId() is null || month is < 1 or > 12 || year is < 2000 or > 2100)
        {
            return Json(new { days = Array.Empty<object>(), projects = Array.Empty<object>() });
        }

        var rangeStart = new DateOnly(year, month, 1);
        var rangeEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        var my = await timesheet.GetMyRangeAsync(rangeStart, rangeEnd, cancellationToken);
        var closedStarts = (await periods.ListInRangeAsync(rangeStart, rangeEnd, cancellationToken))
            .Where(p => p.Status == TimesheetPeriodStatus.Closed)
            .Select(p => p.PeriodStart)
            .ToHashSet();
        bool IsLocked(DateOnly date) => closedStarts.Contains(SemiMonthlyPeriod.Containing(date).Start);

        var days = new List<object>();
        for (var date = rangeStart; date <= rangeEnd; date = date.AddDays(1))
        {
            days.Add(new { date = date.ToString("yyyy-MM-dd"), locked = IsLocked(date) });
        }

        var entriesByProject = my.Entries
            .GroupBy(e => e.ProjectId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(e => e.EntryDate.ToString("yyyy-MM-dd"), e => e));

        var fallbackRole = my.BillableRoles.FirstOrDefault()?.Id;
        var projects = my.Projects.Select(p =>
        {
            entriesByProject.TryGetValue(p.ProjectId, out var byDate);
            var cells = (byDate ?? []).ToDictionary(
                kv => kv.Key,
                kv => (object)new { hours = kv.Value.HoursWorked, notes = kv.Value.Notes ?? "", status = DbStatus(kv.Value.Status) });
            return new
            {
                id = p.ProjectId,
                label = $"{p.ClientName} – {p.Name}",
                roleId = p.DefaultBillingRoleId ?? fallbackRole,
                cells,
            };
        }).ToList();

        return Json(new { days, projects });
    }

    /// <summary>Persists the user's chosen timesheet layout (grid/daily) so it follows them
    /// across devices. Best-effort: a bad value or unlinked account is a no-op.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLayout(string layout, CancellationToken cancellationToken)
    {
        try
        {
            await preferences.SetTimesheetLayoutAsync(layout, cancellationToken);
            return Json(new { ok = true });
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            return Json(new { ok = false });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveHours(
        Guid projectId, DateOnly date, decimal hours, Guid billingRoleId, string? notes, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await timeEntries.SaveHoursAsync(projectId, date, hours, billingRoleId, notes, cancellationToken: cancellationToken);
            return Json(new { ok = true, hours = entry?.HoursWorked ?? 0m, notes = entry?.Notes ?? "", cleared = entry is null });
        }
        catch (DescriptionRequiredException ex)
        {
            return Json(new { ok = false, needsDescription = true, error = ex.Message });
        }
        catch (Exception ex) when (ex is DomainException or UnauthorizedAccessException)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    private static string DbStatus(TimeEntryStatus status) => status.ToString().ToLowerInvariant();
}
