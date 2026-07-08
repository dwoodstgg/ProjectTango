using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;

namespace ProjectTango.Application.Projects;

public record ProjectSummary(Project Project, string ClientName, string ProjectManagerName);

public interface IProjectRepository
{
    Task<IReadOnlyList<ProjectSummary>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Project?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task AddAsync(Project project, CancellationToken cancellationToken = default);

    Task UpdateAsync(Project project, CancellationToken cancellationToken = default);

    Task SetStatusAsync(Guid projectId, ProjectStatus status, CancellationToken cancellationToken = default);
}
