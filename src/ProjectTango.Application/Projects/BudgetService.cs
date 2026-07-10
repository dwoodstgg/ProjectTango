using ProjectTango.Application.Common;
using ProjectTango.Application.Roles;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;

namespace ProjectTango.Application.Projects;

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

    /// <summary>Creates or replaces the project's budget. The dollar amount is required for
    /// fixed-fee and T&amp;M-cap budgets; the hours cap is required for an hours budget; either
    /// type may also carry the other dimension.</summary>
    public async Task SetBudgetAsync(
        Guid projectId, BudgetType type, decimal? amount, decimal? hours,
        int[]? alertThresholds, string? reason,
        IReadOnlyList<RoleHourInput>? roleAllocations = null,
        CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");
        var adminOverride = currentUser.RequireCanManage(project);

        if (amount is < 0)
        {
            throw new DomainException("Budget amount cannot be negative.");
        }

        if (hours is < 0)
        {
            throw new DomainException("Budget hours cannot be negative.");
        }

        var allocations = await BuildAllocationsAsync(roleAllocations, cancellationToken);

        // Overall hours default to the sum of the role allocations when not set explicitly, so a
        // per-role hour budget yields a project total automatically.
        var effectiveHours = hours ?? (allocations.Count > 0 ? allocations.Sum(a => a.Hours) : null);

        switch (type)
        {
            case BudgetType.FixedFee when amount is null:
                throw new DomainException("A fixed-fee budget needs a dollar amount.");
            case BudgetType.TimeAndMaterialsCap when amount is null:
                throw new DomainException("A time-and-materials cap needs a dollar amount.");
            case BudgetType.HoursCap when effectiveHours is null:
                throw new DomainException("An hours-cap budget needs an hours figure or per-role hours.");
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

    /// <summary>Keeps thresholds sane: whole percents in 1..100, de-duplicated and sorted.
    /// A null/empty list falls back to the standard {50, 75, 90}.</summary>
    private static int[] NormalizeThresholds(int[]? thresholds)
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
