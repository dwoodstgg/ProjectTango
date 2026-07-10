using ProjectTango.Application.TimeEntries;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;

namespace ProjectTango.Application.Projects;

/// <summary>Shared burn maths for a project's time entries, used by both the dashboard and
/// the budget-alert evaluator so they never diverge. Value bases (design §6.2): open work on
/// hours_worked (an estimate, "pending"); approved/invoiced on hours_billed (the billing
/// decision, "spent").</summary>
public static class BudgetBurn
{
    /// <summary>Dollar value of one burn row. Non-billable work and rate-less entries are worth
    /// nothing; open rows value at worked hours, approved/invoiced at billed hours.</summary>
    public static decimal RowValue(BurnRow row)
    {
        if (!row.IsBillable || row.ResolvedRate is null)
        {
            return 0m;
        }

        var hours = row.Status == TimeEntryStatus.Open ? row.HoursWorked : row.HoursBilled;
        return hours * row.ResolvedRate.Value;
    }

    /// <summary>Assembles a <see cref="BudgetStatus"/> from the budget and the project's burn rows,
    /// including a per-role hours breakdown for each role allocation.</summary>
    public static BudgetStatus Compute(Budget budget, IReadOnlyList<BurnRow> rows)
    {
        var spentValue = rows.Where(r => r.Status != TimeEntryStatus.Open).Sum(RowValue);
        var pendingValue = rows.Where(r => r.Status == TimeEntryStatus.Open).Sum(RowValue);
        // Hours mirror the value split: billed hours once approved/invoiced, worked while open.
        var spentHours = rows.Where(r => r.Status != TimeEntryStatus.Open).Sum(r => r.HoursBilled);
        var pendingHours = rows.Where(r => r.Status == TimeEntryStatus.Open).Sum(r => r.HoursWorked);

        var roles = budget.RoleAllocations
            .Select(a => new RoleBudget(
                a.RoleId,
                a.RoleName ?? "—",
                a.Hours,
                SpentHours: rows.Where(r => r.BillingRoleId == a.RoleId && r.Status != TimeEntryStatus.Open).Sum(r => r.HoursBilled),
                PendingHours: rows.Where(r => r.BillingRoleId == a.RoleId && r.Status == TimeEntryStatus.Open).Sum(r => r.HoursWorked)))
            .OrderBy(r => r.RoleName)
            .ToList();

        return new BudgetStatus(
            budget.Type, budget.Amount, budget.Hours, budget.AlertThresholds,
            spentValue, pendingValue, spentHours, pendingHours, roles);
    }
}
