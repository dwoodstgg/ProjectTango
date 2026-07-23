using Crosscheck.Application.TimeEntries;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;

namespace Crosscheck.Application.Projects;

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

    /// <summary>Non-modular overload — kept so callers without module context keep working.</summary>
    public static BudgetStatus Compute(Budget budget, IReadOnlyList<BurnRow> rows) =>
        Compute(budget, [], rows);

    /// <summary>Assembles a <see cref="BudgetStatus"/> from the budget, the project's live
    /// modules, and its burn rows. With live modules (design decision #21) the hours budget and
    /// the per-role breakdown roll up from the modules — the budget row only contributes the
    /// dollar figure and thresholds; without modules, behavior is exactly the pre-module one.</summary>
    public static BudgetStatus Compute(
        Budget budget, IReadOnlyList<ProjectModule> liveModules, IReadOnlyList<BurnRow> rows)
    {
        var spentValue = rows.Where(r => r.Status != TimeEntryStatus.Open).Sum(RowValue);
        var pendingValue = rows.Where(r => r.Status == TimeEntryStatus.Open).Sum(RowValue);
        // Hours mirror the value split: billed hours once approved/invoiced, worked while open.
        var spentHours = rows.Where(r => r.Status != TimeEntryStatus.Open).Sum(r => r.HoursBilled);
        var pendingHours = rows.Where(r => r.Status == TimeEntryStatus.Open).Sum(r => r.HoursWorked);

        var modular = liveModules.Count > 0;
        var hoursBudget = modular ? liveModules.Sum(m => m.EffectiveHours) : budget.Hours;

        // Per-role project totals: from the budget's own allocations, or — when modular —
        // summed across the modules' allocations.
        var roleAllocations = modular
            ? liveModules
                .SelectMany(m => m.Allocations)
                .GroupBy(a => a.RoleId)
                .Select(g => (RoleId: g.Key, RoleName: g.First().RoleName, Hours: g.Sum(a => a.Hours)))
                .ToList()
            : budget.RoleAllocations.Select(a => (a.RoleId, a.RoleName, a.Hours)).ToList();

        var roles = roleAllocations
            .Select(a => RoleBudgetFor(a.RoleId, a.RoleName, a.Hours, rows))
            .OrderBy(r => r.RoleName)
            .ToList();

        return new BudgetStatus(
            budget.Type, budget.Amount, hoursBudget, budget.AlertThresholds,
            spentValue, pendingValue, spentHours, pendingHours, roles,
            ComputeModules(liveModules, rows));
    }

    /// <summary>Per-module burn: one line per live module, plus lines for soft-deleted modules
    /// that still have entries, plus an "Unassigned" bucket for pre-module entries on a modular
    /// project. Non-modular projects report an empty list. Public so the dashboard can show
    /// module burn even before any budget row exists.</summary>
    public static IReadOnlyList<ModuleBudget> ComputeModules(
        IReadOnlyList<ProjectModule> liveModules, IReadOnlyList<BurnRow> rows)
    {
        var result = new List<ModuleBudget>();
        foreach (var module in liveModules.OrderBy(m => m.SortOrder).ThenBy(m => m.Name))
        {
            var moduleRows = rows.Where(r => r.ModuleId == module.Id).ToList();
            var roles = module.Allocations
                .Select(a => RoleBudgetFor(a.RoleId, a.RoleName, a.Hours, moduleRows))
                .OrderBy(r => r.RoleName)
                .ToList();
            result.Add(ModuleBudgetFor(module.Id, module.Name, isDeleted: false,
                module.EffectiveHours, module.Amount, moduleRows, roles));
        }

        // Entries on since-removed modules: no allocation to measure against, but the spend
        // still belongs to the project's history.
        var liveIds = liveModules.Select(m => m.Id).ToHashSet();
        foreach (var group in rows
            .Where(r => r.ModuleId is { } mid && !liveIds.Contains(mid))
            .GroupBy(r => r.ModuleId!.Value))
        {
            var moduleRows = group.ToList();
            result.Add(ModuleBudgetFor(group.Key, moduleRows[0].ModuleName ?? "?", isDeleted: true,
                allocatedHours: 0m, amount: null, moduleRows, Roles: []));
        }

        if (liveModules.Count > 0)
        {
            var unassigned = rows.Where(r => r.ModuleId is null).ToList();
            if (unassigned.Count > 0)
            {
                result.Add(ModuleBudgetFor(null, "Unassigned", isDeleted: false,
                    allocatedHours: 0m, amount: null, unassigned, Roles: []));
            }
        }

        return result;
    }

    private static RoleBudget RoleBudgetFor(Guid roleId, string? roleName, decimal hours, IReadOnlyList<BurnRow> rows) =>
        new(roleId,
            roleName ?? "—",
            hours,
            SpentHours: rows.Where(r => r.BillingRoleId == roleId && r.Status != TimeEntryStatus.Open).Sum(r => r.HoursBilled),
            PendingHours: rows.Where(r => r.BillingRoleId == roleId && r.Status == TimeEntryStatus.Open).Sum(r => r.HoursWorked));

    private static ModuleBudget ModuleBudgetFor(
        Guid? moduleId, string moduleName, bool isDeleted, decimal allocatedHours, decimal? amount,
        IReadOnlyList<BurnRow> moduleRows, IReadOnlyList<RoleBudget> Roles) =>
        new(moduleId, moduleName, isDeleted, allocatedHours, amount,
            SpentHours: moduleRows.Where(r => r.Status != TimeEntryStatus.Open).Sum(r => r.HoursBilled),
            PendingHours: moduleRows.Where(r => r.Status == TimeEntryStatus.Open).Sum(r => r.HoursWorked),
            SpentValue: moduleRows.Where(r => r.Status != TimeEntryStatus.Open).Sum(RowValue),
            PendingValue: moduleRows.Where(r => r.Status == TimeEntryStatus.Open).Sum(RowValue),
            Roles);
}
