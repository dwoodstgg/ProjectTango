using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Projects;

public interface IBudgetAlertRepository
{
    /// <summary>The alert keys already fired for this budget (so they aren't re-sent).</summary>
    Task<IReadOnlySet<string>> GetFiredKeysAsync(Guid budgetId, CancellationToken cancellationToken = default);

    /// <summary>Records a fired alert. Idempotent on (budget_id, alert_key).</summary>
    Task RecordAsync(BudgetAlert alert, CancellationToken cancellationToken = default);

    /// <summary>Clears every fired alert for a budget — re-arms thresholds after a budget change.</summary>
    Task ClearForBudgetAsync(Guid budgetId, CancellationToken cancellationToken = default);
}
