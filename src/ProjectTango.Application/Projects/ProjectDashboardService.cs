using ProjectTango.Application.Clients;
using ProjectTango.Application.Common;
using ProjectTango.Application.TimeEntries;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;

namespace ProjectTango.Application.Projects;

public record DashboardTotals(
    decimal HoursWorked,
    decimal HoursBilled,
    decimal ApprovedValue,
    decimal InvoicedValue,
    decimal PendingValue,
    int OpenCount,
    int ApprovedCount,
    int InvoicedCount)
{
    /// <summary>Realized + realizable value: approved (WIP) plus already invoiced.</summary>
    public decimal BillableValue => ApprovedValue + InvoicedValue;
}

public record RoleBurn(string RoleName, decimal HoursWorked, decimal HoursBilled, decimal Value);

public record PersonBurn(string EmployeeName, decimal HoursWorked, decimal HoursBilled, decimal Value);

public record RecentEntry(
    DateOnly Date, string EmployeeName, string RoleName,
    decimal HoursWorked, decimal HoursBilled, TimeEntryStatus Status, bool IsBillable);

/// <summary>Hours budget vs. burn for one billing role (e.g. Lead Developer 300h). "Spent" is
/// billed hours on approved/invoiced entries; "pending" is worked hours still open.</summary>
public record RoleBudget(
    Guid RoleId, string RoleName, decimal AllocatedHours, decimal SpentHours, decimal PendingHours)
{
    public decimal RemainingHours => AllocatedHours - SpentHours;
    public double PercentHours => AllocatedHours > 0 ? (double)(SpentHours / AllocatedHours) * 100 : 0;
    public bool IsOver => SpentHours > AllocatedHours;
}

/// <summary>Budget vs. burn for the dashboard (design §6.2). "Spent" is realized/realizable
/// value — approved (WIP) plus invoiced; "pending" is open work not yet approved. Hours mirror
/// that split (billed hours once approved, worked hours while open). Overrun is only flagged,
/// never blocking (design rule 9). <see cref="Roles"/> carries the per-role hour budgets.</summary>
public record BudgetStatus(
    BudgetType Type,
    decimal? AmountBudget,
    decimal? HoursBudget,
    IReadOnlyList<int> AlertThresholds,
    decimal SpentValue,
    decimal PendingValue,
    decimal SpentHours,
    decimal PendingHours,
    IReadOnlyList<RoleBudget> Roles)
{
    public decimal? RemainingValue => AmountBudget is null ? null : AmountBudget.Value - SpentValue;
    public decimal? RemainingHours => HoursBudget is null ? null : HoursBudget.Value - SpentHours;

    /// <summary>Percent of the dollar budget spent, or null when there is no positive dollar
    /// budget to measure against. Can exceed 100 on overrun.</summary>
    public double? PercentValue => AmountBudget is > 0 ? (double)(SpentValue / AmountBudget.Value) * 100 : null;
    public double? PercentHours => HoursBudget is > 0 ? (double)(SpentHours / HoursBudget.Value) * 100 : null;

    public bool IsOverValue => RemainingValue is < 0;
    public bool IsOverHours => RemainingHours is < 0;
    public bool IsOverBudget => IsOverValue || IsOverHours;

    /// <summary>The higher of the two dimensions' burn — what alert thresholds are measured against.</summary>
    public double BurnPercent => Math.Max(PercentValue ?? 0, PercentHours ?? 0);

    public IReadOnlyList<int> ThresholdsCrossed => AlertThresholds.Where(t => BurnPercent >= t).ToList();
    public int? HighestThresholdCrossed => ThresholdsCrossed.Count > 0 ? ThresholdsCrossed[^1] : null;
}

public class ProjectDashboard
{
    public required Project Project { get; init; }
    public required string ClientName { get; init; }
    public required BillingProfile Billing { get; init; }
    public required DashboardTotals Totals { get; init; }
    public required IReadOnlyList<RoleBurn> ByRole { get; init; }
    public required IReadOnlyList<PersonBurn> ByPerson { get; init; }
    public required IReadOnlyList<AssignmentSummary> Team { get; init; }
    public required IReadOnlyList<RecentEntry> Recent { get; init; }

    /// <summary>The project's budget vs. burn, or null when no budget has been set.</summary>
    public BudgetStatus? Budget { get; init; }

    /// <summary>True when some billable entries have no rate card for their date, so the
    /// dollar figures understate the real value until a rate is added (design rule 3).</summary>
    public bool HasRateGaps { get; init; }
}

/// <summary>Read-only project burn dashboard (design §6.2). Reports hours and dollar value from
/// time entries and rate cards, split by status/role/person, and — when a budget is set — burn
/// against it (spent, remaining, % of threshold, overrun). Value bases: open entries on
/// hours_worked (pending), and approved/invoiced on hours_billed (the billing decision).</summary>
public class ProjectDashboardService(
    ICurrentUser currentUser,
    IProjectRepository projects,
    IClientRepository clients,
    IAssignmentRepository assignments,
    ITimeEntryRepository entries,
    IBudgetRepository budgets)
{
    public async Task<ProjectDashboard?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager, RoleNames.ProjectManager);

        var project = await projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var client = await clients.GetByIdAsync(project.ClientId, cancellationToken);
        var rows = await entries.GetBurnRowsAsync(projectId, cancellationToken);
        var team = await assignments.GetForProjectAsync(projectId, cancellationToken);

        var totals = new DashboardTotals(
            HoursWorked: rows.Sum(r => r.HoursWorked),
            HoursBilled: rows.Sum(r => r.HoursBilled),
            ApprovedValue: rows.Where(r => r.Status == TimeEntryStatus.Approved).Sum(BudgetBurn.RowValue),
            InvoicedValue: rows.Where(r => r.Status == TimeEntryStatus.Invoiced).Sum(BudgetBurn.RowValue),
            PendingValue: rows.Where(r => r.Status == TimeEntryStatus.Open).Sum(BudgetBurn.RowValue),
            OpenCount: rows.Count(r => r.Status == TimeEntryStatus.Open),
            ApprovedCount: rows.Count(r => r.Status == TimeEntryStatus.Approved),
            InvoicedCount: rows.Count(r => r.Status == TimeEntryStatus.Invoiced));

        var byRole = rows
            .GroupBy(r => r.RoleName)
            .Select(g => new RoleBurn(g.Key, g.Sum(r => r.HoursWorked), g.Sum(r => r.HoursBilled), g.Sum(BudgetBurn.RowValue)))
            .OrderByDescending(r => r.HoursWorked)
            .ToList();

        var byPerson = rows
            .GroupBy(r => r.EmployeeName)
            .Select(g => new PersonBurn(g.Key, g.Sum(r => r.HoursWorked), g.Sum(r => r.HoursBilled), g.Sum(BudgetBurn.RowValue)))
            .OrderByDescending(p => p.HoursWorked)
            .ToList();

        var recent = rows
            .OrderByDescending(r => r.EntryDate)
            .Take(10)
            .Select(r => new RecentEntry(r.EntryDate, r.EmployeeName, r.RoleName, r.HoursWorked, r.HoursBilled, r.Status, r.IsBillable))
            .ToList();

        var budget = await budgets.GetForProjectAsync(projectId, cancellationToken);
        var budgetStatus = budget is null ? null : BudgetBurn.Compute(budget, rows);

        return new ProjectDashboard
        {
            Project = project,
            ClientName = client?.Name ?? "—",
            Billing = ProjectBilling.Resolve(project, client),
            Totals = totals,
            ByRole = byRole,
            ByPerson = byPerson,
            Team = team,
            Recent = recent,
            Budget = budgetStatus,
            HasRateGaps = rows.Any(r => r is { IsBillable: true, ResolvedRate: null }),
        };
    }
}
