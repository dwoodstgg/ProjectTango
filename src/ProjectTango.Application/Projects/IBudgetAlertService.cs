namespace ProjectTango.Application.Projects;

/// <summary>Fires budget threshold/overrun emails as burn changes. Both methods are
/// best-effort — they never throw — so a mail failure can't roll back a time entry or a
/// budget edit.</summary>
public interface IBudgetAlertService
{
    /// <summary>Re-checks the project's budget burn and emails any newly-crossed thresholds
    /// (each fires once). No-op when the project has no budget.</summary>
    Task EvaluateAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Called after a budget is created or revised: re-arms the thresholds (so a raised
    /// budget can alert again) and immediately re-evaluates (so a lowered budget flags at once).</summary>
    Task OnBudgetChangedAsync(Guid projectId, CancellationToken cancellationToken = default);
}
