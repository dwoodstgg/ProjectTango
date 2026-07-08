using ProjectTango.Domain;

namespace ProjectTango.Application.Common;

public static class Access
{
    /// <summary>Authorizes the current user if they hold any of <paramref name="roles"/>.
    /// Admin bypasses the check — the caller MUST record the returned override flag in the
    /// audit event for mutations (design rule: every Admin override is audit-logged).</summary>
    /// <returns>True when access was granted only via the Admin bypass.</returns>
    public static bool RequireAny(this ICurrentUser user, params string[] roles)
    {
        if (roles.Any(user.IsInRole))
        {
            return false;
        }

        if (user.IsInRole(RoleNames.Admin))
        {
            return true;
        }

        throw new UnauthorizedAccessException($"Requires one of: {string.Join(", ", roles)}.");
    }
}
