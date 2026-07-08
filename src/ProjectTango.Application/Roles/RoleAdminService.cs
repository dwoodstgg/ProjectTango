using ProjectTango.Application.Common;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Roles;

/// <summary>Role definition management — Admin only (role administration per design §2).
/// Only the display label is editable; the stable authorization key never changes.</summary>
public class RoleAdminService(ICurrentUser currentUser, IRoleRepository roles, IAuditLog audit)
{
    public async Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        return await roles.GetAllAsync(cancellationToken);
    }

    public async Task RenameAsync(Guid roleId, string displayName, CancellationToken cancellationToken = default)
    {
        RequireAdmin();

        displayName = displayName?.Trim() ?? "";
        if (displayName.Length == 0)
        {
            throw new DomainException("Role name cannot be empty.");
        }

        var all = await roles.GetAllAsync(cancellationToken);
        var role = all.FirstOrDefault(r => r.Id == roleId)
            ?? throw new DomainException("Unknown role.");

        if (all.Any(r => r.Id != roleId && string.Equals(r.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"Another role is already named \"{displayName}\".");
        }

        if (string.Equals(role.DisplayName, displayName, StringComparison.Ordinal))
        {
            return;
        }

        var previous = role.DisplayName;
        await roles.RenameAsync(roleId, displayName, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "role.renamed", "role", roleId,
            new { From = previous, To = displayName }), cancellationToken);
    }

    private void RequireAdmin()
    {
        if (!currentUser.IsInRole(RoleNames.Admin))
        {
            throw new UnauthorizedAccessException("Role administration requires the Admin role.");
        }
    }
}
