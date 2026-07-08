using System.Security.Claims;

namespace ProjectTango.Web;

/// <summary>App-specific claims stamped onto the auth cookie at sign-in.</summary>
public static class TangoClaims
{
    public const string EmployeeId = "tango:employee_id";

    public static Guid? GetEmployeeId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(EmployeeId), out var id) ? id : null;

    /// <summary>The user's full display name from the Entra <c>name</c> claim (e.g. "Don Woods").
    /// <see cref="System.Security.Principal.IIdentity.Name"/> is the UPN/email here, so prefer the
    /// name claim and fall back only if it is absent (e.g. a bare JWT).</summary>
    public static string GetDisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue("name")
        ?? principal.Identity?.Name
        ?? "";
}
