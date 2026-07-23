using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;

namespace Crosscheck.Application.Projects;

/// <summary><paramref name="HasTimeEntries"/> is true when any time has been logged on the
/// project — such a project can never be deleted.</summary>
public record ProjectSummary(
    Project Project, string ClientName, string ProjectManagerName, bool HasTimeEntries = false);

public interface IProjectRepository
{
    Task<IReadOnlyList<ProjectSummary>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Project?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Finds a project by (client, code). Codes are unique per client, so this is
    /// the lookup used to enforce that uniqueness on create/edit.</summary>
    Task<Project?> GetByClientAndCodeAsync(Guid clientId, string code, CancellationToken cancellationToken = default);

    Task AddAsync(Project project, CancellationToken cancellationToken = default);

    Task UpdateAsync(Project project, CancellationToken cancellationToken = default);

    Task SetStatusAsync(Guid projectId, ProjectStatus status, CancellationToken cancellationToken = default);

    Task<bool> HasTimeEntriesAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes the project and its setup data (rate cards, assignments, budget,
    /// modules). Callers must first verify no time has been logged — see
    /// <see cref="HasTimeEntriesAsync"/>.</summary>
    Task DeleteAsync(Guid projectId, CancellationToken cancellationToken = default);
}
