using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Projects;

/// <summary>A budget change paired with the display name of who made it, for history views.</summary>
public record BudgetRevisionSummary(BudgetRevision Revision, string RevisedByName);

public interface IBudgetRepository
{
    Task<Budget?> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates the project's single budget and records the revision in one
    /// transaction. The budget row carries the current values; the revision preserves history.</summary>
    Task SaveAsync(Budget budget, BudgetRevision revision, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BudgetRevisionSummary>> GetRevisionsAsync(Guid budgetId, CancellationToken cancellationToken = default);
}
