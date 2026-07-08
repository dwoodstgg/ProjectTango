using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectTango.Application.Roles;
using ProjectTango.Domain;

namespace ProjectTango.Web.Controllers;

[Authorize(Roles = RoleNames.Admin)]
public class RolesController(RoleAdminService roleAdmin) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var roles = await roleAdmin.ListAsync(cancellationToken);
        return View(roles);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(Guid id, string displayName, CancellationToken cancellationToken)
    {
        try
        {
            await roleAdmin.RenameAsync(id, displayName, cancellationToken);
            return Json(new { ok = true, displayName = (displayName ?? "").Trim() });
        }
        catch (DomainException ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }
}
