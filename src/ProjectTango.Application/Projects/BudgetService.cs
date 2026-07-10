using ProjectTango.Application.Common;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;

namespace ProjectTango.Application.Projects;

/// <summary>Project budget management (design-doc §5.2, §6.2). A project has at most one
/// budget; setting it again updates it in place and records a <see cref="BudgetRevision"/>
/// (who, when, old → new, reason) plus an audit event. A budget never changes project status
/// — overrun is expected and only flagged on the dashboard (design rule 9).</summary>
public class BudgetService(
    ICurrentUser currentUser,
    IProjectRepository projects,
    IBudgetRepository budgets,
    IAuditLog audit)
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

        switch (type)
        {
            case BudgetType.FixedFee when amount is null:
                throw new DomainException("A fixed-fee budget needs a dollar amount.");
            case BudgetType.TimeAndMaterialsCap when amount is null:
                throw new DomainException("A time-and-materials cap needs a dollar amount.");
            case BudgetType.HoursCap when hours is null:
                throw new DomainException("An hours-cap budget needs an hours figure.");
        }

        if (amount is null && hours is null)
        {
            throw new DomainException("A budget must set a dollar amount, an hours cap, or both.");
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
            ToHours = hours,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        };

        budget.Type = type;
        budget.Amount = amount;
        budget.Hours = hours;
        budget.AlertThresholds = thresholds;
        budget.UpdatedAt = now;

        await budgets.SaveAsync(budget, revision, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, existing is null ? "budget.set" : "budget.revise", "project", projectId,
            new
            {
                Type = type.ToString(),
                Amount = amount,
                Hours = hours,
                Thresholds = thresholds,
                FromAmount = existing?.Amount,
                FromHours = existing?.Hours,
                revision.Reason,
                adminOverride,
            }), cancellationToken);
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
}
