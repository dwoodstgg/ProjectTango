using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Roles;

public interface IRoleRepository
{
    Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default);
}
