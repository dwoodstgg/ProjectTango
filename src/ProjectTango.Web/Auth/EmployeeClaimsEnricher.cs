using System.Security.Claims;
using Microsoft.Identity.Web;
using ProjectTango.Application.Employees;

namespace ProjectTango.Web.Auth;

/// <summary>Resolves an Entra identity on a validated token to the local employee, then stamps
/// the employee id and role claims onto the principal. Shared by both auth paths — the UI cookie
/// (OIDC sign-in) and the API JWT bearer (the path mobile/desktop clients use) — so that
/// <see cref="ProjectTango.Application.Common.ICurrentUser"/> resolves identically regardless of
/// how the caller authenticated. Without this on the bearer path, an API request would carry the
/// raw Entra token with no <c>tango:employee_id</c> or role claims (design-doc §4.1–4.3).</summary>
public static class EmployeeClaimsEnricher
{
    public static async Task EnrichAsync(ClaimsPrincipal principal, IServiceProvider services)
    {
        var entraOid = principal.GetObjectId()
            ?? throw new InvalidOperationException("Entra token is missing the oid claim.");
        var email = ResolveEmail(principal)
            ?? throw new InvalidOperationException("Entra token is missing a username/email claim.");
        var displayName = principal.FindFirstValue("name") ?? email;

        var provisioning = services.GetRequiredService<EmployeeProvisioningService>();
        var employees = services.GetRequiredService<IEmployeeRepository>();

        var employee = await provisioning.ProvisionSignInAsync(entraOid, email, displayName);
        var roleNames = await employees.GetRoleNamesAsync(employee.Id);

        var identity = (ClaimsIdentity)principal.Identity!;
        if (!identity.HasClaim(c => c.Type == TangoClaims.EmployeeId))
        {
            identity.AddClaim(new Claim(TangoClaims.EmployeeId, employee.Id.ToString()));
        }

        identity.AddClaims(roleNames.Select(r => new Claim(ClaimTypes.Role, r)));
    }

    // The UI id_token always carries preferred_username; API access tokens vary by how the client
    // requested scopes, so fall back through the common username/email claim shapes. Email is the
    // bootstrap key that links pre-created employee records (seed/import) on first sign-in (§4.2).
    private static string? ResolveEmail(ClaimsPrincipal principal) =>
        principal.FindFirstValue("preferred_username")
        ?? principal.FindFirstValue(ClaimTypes.Upn)
        ?? principal.FindFirstValue("upn")
        ?? principal.FindFirstValue(ClaimTypes.Email)
        ?? principal.FindFirstValue("email");
}
