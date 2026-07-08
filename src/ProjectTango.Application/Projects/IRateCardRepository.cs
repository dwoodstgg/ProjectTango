using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Projects;

public record RateCardSummary(ProjectRateCard RateCard, string RoleName);

public interface IRateCardRepository
{
    Task<IReadOnlyList<RateCardSummary>> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectRateCard>> GetForRoleAsync(Guid projectId, Guid roleId, CancellationToken cancellationToken = default);

    Task AddAsync(ProjectRateCard rateCard, CancellationToken cancellationToken = default);

    /// <summary>Closes an open-ended row by setting effective_to. The one sanctioned
    /// mutation — everything else about a rate row is immutable.</summary>
    Task CloseAsync(Guid rateCardId, DateOnly effectiveTo, CancellationToken cancellationToken = default);

    /// <summary>Rate resolution (design rule): the rate for (project, billing role) effective on a date.</summary>
    Task<decimal?> ResolveAsync(Guid projectId, Guid roleId, DateOnly date, CancellationToken cancellationToken = default);
}
