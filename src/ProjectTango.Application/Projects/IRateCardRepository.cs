using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Projects;

/// <summary><paramref name="HasBilledTime"/> is true when invoiced time has been billed
/// against this rate's window, which freezes it (no correcting or removing).</summary>
public record RateCardSummary(ProjectRateCard RateCard, string RoleName, bool HasBilledTime = false);

public interface IRateCardRepository
{
    Task<IReadOnlyList<RateCardSummary>> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<ProjectRateCard?> GetByIdAsync(Guid rateCardId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectRateCard>> GetForRoleAsync(Guid projectId, Guid roleId, CancellationToken cancellationToken = default);

    Task AddAsync(ProjectRateCard rateCard, CancellationToken cancellationToken = default);

    /// <summary>Closes an open-ended row by setting effective_to. The one sanctioned
    /// mutation for a real rate change — see <see cref="CorrectAsync"/> for fixing mistakes.</summary>
    Task CloseAsync(Guid rateCardId, DateOnly effectiveTo, CancellationToken cancellationToken = default);

    /// <summary>True if any INVOICED time entry for this (project, role) falls inside the
    /// given effective window — such a rate has priced real money and is frozen.</summary>
    Task<bool> HasInvoicedTimeAsync(
        Guid projectId, Guid roleId, DateOnly effectiveFrom, DateOnly? effectiveTo,
        CancellationToken cancellationToken = default);

    /// <summary>Corrects a mistaken row in place (new amount and/or start date), optionally
    /// re-closing its predecessor to stay contiguous. One transaction; NOT a rate change.</summary>
    Task CorrectAsync(
        Guid rateCardId, decimal hourlyRate, DateOnly effectiveFrom,
        Guid? priorRowId, DateOnly? priorEffectiveTo,
        CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a mistaken row, optionally reopening the predecessor it had
    /// closed (when the removed row was the current open one).</summary>
    Task SoftDeleteAsync(Guid rateCardId, Guid? reopenPriorRowId, CancellationToken cancellationToken = default);

    /// <summary>Rate resolution (design rule): the rate for (project, billing role) effective on a date.</summary>
    Task<decimal?> ResolveAsync(Guid projectId, Guid roleId, DateOnly date, CancellationToken cancellationToken = default);
}
