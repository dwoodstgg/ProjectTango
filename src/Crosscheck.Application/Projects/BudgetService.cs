using Crosscheck.Application.Common;
using Crosscheck.Application.Roles;
using Crosscheck.Domain;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;

namespace Crosscheck.Application.Projects;

/// <summary>An hour allocation to a billing role, as supplied by the caller when setting a budget.</summary>
public record RoleHourInput(Guid RoleId, decimal Hours);

/// <summary>Project budget management (design-doc §5.2, §6.2). A project has at most one
/// budget; setting it again updates it in place and records a <see cref="BudgetRevision"/>
/// (who, when, old → new, reason) plus an audit event. A budget never changes project status
/// — overrun is expected and only flagged on the dashboard (design rule 9).</summary>
public class BudgetService(
    ICurrentUser currentUser,
    IProjectRepository projects,
    IBudgetRepository budgets,
    IModuleRepository modules,
    IRoleRepository roles,
    IAuditLog audit,
    IBudgetAlertService budgetAlerts)
{
    public async Task<Budget?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager, RoleNames.ProjectManager);
        return await budgets.GetForProjectAsync(projectId, cancellationToken);
    }

    public async Task<IReadOnlyList<BudgetRevisionSummary>> GetRevisionsAsync(
        Guid projectId, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager, RoleNames.ProjectManager);
        var budget = await budgets.GetForProjectAsync(projectId, cancellationToken);
        return budget is null
            ? []
            : await budgets.GetRevisionsAsync(budget.Id, cancellationToken);
    }

    /// <summary>Creates or replaces the project's budget. What the budget means follows the
    /// project's type: an hourly project caps dollars and/or hours; a fixed-rate project's
    /// budget is its contract amount; a service contract's is its total contract amount over
    /// the project timeframe (a monthly breakdown is derived at reporting time only).</summary>
    public async Task SetBudgetAsync(
        Guid projectId, decimal? amount, decimal? hours,
        int[]? alertThresholds, string? reason,
        IReadOnlyList<RoleHourInput>? roleAllocations = null,
        CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");
        var adminOverride = currentUser.RequireCanManage(project);
        var type = project.Type;

        if (type == ProjectType.Internal)
        {
            throw new DomainException("An internal project is not billed and carries no budget.");
        }

        if (amount is < 0)
        {
            throw new DomainException("Budget amount cannot be negative.");
        }

        if (hours is < 0)
        {
            throw new DomainException("Budget hours cannot be negative.");
        }

        // With modules, per-role hours live on each module and the project totals roll up from
        // them — a budget-level allocation would create two competing sources of truth.
        if (roleAllocations is { Count: > 0 } && roleAllocations.Any(a => a.Hours > 0)
            && await modules.HasLiveModulesAsync(projectId, cancellationToken))
        {
            throw new DomainException(
                $"This project budgets hours per {Terminology.Singular(project.BreakdownLabel)} — " +
                $"set role hours on the {Terminology.Plural(project.BreakdownLabel)} instead.");
        }

        var allocations = await BuildAllocationsAsync(roleAllocations, cancellationToken);

        // Overall hours default to the sum of the role allocations when not set explicitly, so a
        // per-role hour budget yields a project total automatically.
        var effectiveHours = hours ?? (allocations.Count > 0 ? allocations.Sum(a => a.Hours) : null);

        if (type == ProjectType.FixedRate && amount is null)
        {
            throw new DomainException("A fixed-rate project's budget is its contract amount — enter the dollar figure.");
        }

        if (type == ProjectType.ServiceContract && amount is null)
        {
            throw new DomainException("A service contract's budget is its total contract amount — enter the dollar figure.");
        }

        if (amount is null && effectiveHours is null)
        {
            throw new DomainException("A budget must set a dollar amount, an hours figure, or per-role hours.");
        }

        var thresholds = NormalizeThresholds(alertThresholds);
        var now = DateTimeOffset.UtcNow;

        var existing = await budgets.GetForProjectAsync(projectId, cancellationToken);
        var budget = existing ?? new Budget
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            CreatedAt = now,
        };

        var revision = new BudgetRevision
        {
            Id = Guid.NewGuid(),
            BudgetId = budget.Id,
            RevisedById = currentUser.EmployeeId!.Value,
            RevisedAt = now,
            FromType = existing?.Type,
            FromAmount = existing?.Amount,
            FromHours = existing?.Hours,
            ToType = type,
            ToAmount = amount,
            ToHours = effectiveHours,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        };

        budget.Type = type;
        budget.Amount = amount;
        budget.Hours = effectiveHours;
        budget.AlertThresholds = thresholds;
        budget.RoleAllocations = allocations
            .Select(a => new BudgetRoleAllocation
            {
                Id = Guid.NewGuid(),
                BudgetId = budget.Id,
                RoleId = a.RoleId,
                Hours = a.Hours,
                RoleName = a.RoleName,
            })
            .ToList();
        budget.UpdatedAt = now;

        await budgets.SaveAsync(budget, revision, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, existing is null ? "budget.set" : "budget.revise", "project", projectId,
            new
            {
                Type = type.ToString(),
                Amount = amount,
                Hours = effectiveHours,
                Thresholds = thresholds,
                RoleHours = budget.RoleAllocations.ToDictionary(a => a.RoleName ?? a.RoleId.ToString(), a => a.Hours),
                FromAmount = existing?.Amount,
                FromHours = existing?.Hours,
                revision.Reason,
                adminOverride,
            }), cancellationToken);

        // Re-arm thresholds against the new budget and flag immediately if it's already breached.
        await budgetAlerts.OnBudgetChangedAsync(projectId, cancellationToken);
    }

    /// <summary>Whole months a service contract spans, endpoints inclusive by calendar month
    /// (Jan 1 – Dec 31 = 12; Jan 15 – Feb 1 = 2). Reporting uses this to break the total
    /// contract amount down by month.</summary>
    public static int ContractMonths(DateOnly start, DateOnly end) =>
        (end.Year - start.Year) * 12 + end.Month - start.Month + 1;

    /// <summary>Keeps thresholds sane: whole percents in 1..100, de-duplicated and sorted.
    /// A null/empty list falls back to the standard {50, 75, 90}. Public so callers can
    /// normalize the same way before comparing (e.g. to avoid writing a no-op revision).</summary>
    public static int[] NormalizeThresholds(int[]? thresholds)
    {
        if (thresholds is null || thresholds.Length == 0)
        {
            return [50, 75, 90];
        }

        var cleaned = thresholds
            .Where(t => t is >= 1 and <= 100)
            .Distinct()
            .OrderBy(t => t)
            .ToArray();

        return cleaned.Length == 0 ? [50, 75, 90] : cleaned;
    }

    /// <summary>Validates and normalizes the role hour allocations: only positive hours are
    /// kept, each role must be a real billable role, and a role may appear once.</summary>
    private async Task<IReadOnlyList<BudgetRoleAllocation>> BuildAllocationsAsync(
        IReadOnlyList<RoleHourInput>? inputs, CancellationToken cancellationToken)
    {
        if (inputs is null || inputs.Count == 0)
        {
            return [];
        }

        var result = new List<BudgetRoleAllocation>();
        var seen = new HashSet<Guid>();
        foreach (var input in inputs)
        {
            if (input.Hours <= 0)
            {
                continue; // a zero/blank allocation just means "not budgeted"
            }

            if (!seen.Add(input.RoleId))
            {
                throw new DomainException("A role can only have one hour allocation.");
            }

            var role = await roles.GetByIdAsync(input.RoleId, cancellationToken)
                ?? throw new DomainException("Unknown billing role in allocation.");
            if (!role.IsBillable)
            {
                throw new DomainException($"{role.Name} is not a billable role — it cannot carry an hour allocation.");
            }

            result.Add(new BudgetRoleAllocation { RoleId = role.Id, Hours = input.Hours, RoleName = role.DisplayName });
        }

        return result;
    }
}
