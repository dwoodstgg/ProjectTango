using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Roles;

public interface IRoleRepository
{
    Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Updates the display label only. The stable <c>name</c> key is never changed.</summary>
    Task RenameAsync(Guid roleId, string displayName, CancellationToken cancellationToken = default);
}
