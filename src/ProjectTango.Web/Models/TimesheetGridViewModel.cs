using Microsoft.AspNetCore.Mvc.Rendering;

namespace ProjectTango.Web.Models;

public class TimesheetGridViewModel
{
    /// <summary>"week" (default) or "month".</summary>
    public string ViewMode { get; init; } = "week";
    public bool IsWeek => ViewMode != "month";

    public string RangeLabel { get; init; } = "";

    /// <summary>Anchor dates for the prev/next/today links, in the current view's step.</summary>
    public DateOnly CurrentAnchor { get; init; }
    public DateOnly PrevAnchor { get; init; }
    public DateOnly NextAnchor { get; init; }
    public DateOnly TodayAnchor { get; init; }

    /// <summary>False when the signed-in user has no employee record yet (not provisioned).</summary>
    public bool HasEmployee { get; init; } = true;

    public List<DayColumn> Days { get; init; } = [];
    public List<TimesheetRowVm> Rows { get; init; } = [];
    public List<SelectListItem> BillableRoleOptions { get; init; } = [];
}

public record DayColumn(int Day, DateOnly Date, string WeekdayLabel, bool IsWeekend, bool Locked)
{
    /// <summary>Stable per-column key (unique even when a week spans two months).</summary>
    public int DayNumber => Date.DayNumber;
}

public class TimesheetRowVm
{
    public Guid ProjectId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public Guid? DefaultBillingRoleId { get; init; }

    /// <summary>Day-of-month → the cell entry, when one exists.</summary>
    public Dictionary<int, CellVm> Cells { get; init; } = [];
}

public record CellVm(decimal Hours, Guid BillingRoleId, string Status, string? Notes, bool Locked);
