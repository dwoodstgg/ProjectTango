using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectTango.Application.Employees;
using ProjectTango.Domain;
using ProjectTango.Web.Models;

namespace ProjectTango.Web.Controllers;

[Authorize(Roles = $"{RoleNames.Admin},{RoleNames.OperationsManager}")]
public class EmployeesController(EmployeeAdminService employeeAdmin) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var employees = await employeeAdmin.ListAsync(cancellationToken);
        return View(employees);
    }

    [HttpGet]
    public IActionResult Create() => View(new CreateEmployeeViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateEmployeeViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var employee = await employeeAdmin.CreateAsync(
                model.Email!, model.DisplayName!, model.EmploymentType, cancellationToken);
            return RedirectToAction(nameof(Details), new { id = employee.Id });
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var page = await BuildDetailsAsync(id, profile: null, cancellationToken);
        return page is null ? NotFound() : View(page);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile(Guid id, EmployeeProfileViewModel profile, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var invalid = await BuildDetailsAsync(id, profile, cancellationToken);
            return invalid is null ? NotFound() : View(nameof(Details), invalid);
        }

        try
        {
            await employeeAdmin.UpdateProfileAsync(id, profile.DisplayName!, profile.EmploymentType, cancellationToken);
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var page = await BuildDetailsAsync(id, profile, cancellationToken);
            return page is null ? NotFound() : View(nameof(Details), page);
        }
    }

    private async Task<EmployeeDetailsViewModel?> BuildDetailsAsync(
        Guid id, EmployeeProfileViewModel? profile, CancellationToken cancellationToken)
    {
        var summary = await employeeAdmin.GetAsync(id, cancellationToken);
        if (summary is null)
        {
            return null;
        }

        var allRoles = await employeeAdmin.ListRolesAsync(cancellationToken);
        var heldRoleIds = await employeeAdmin.GetHeldRoleIdsAsync(id, cancellationToken);

        return new EmployeeDetailsViewModel(summary, allRoles, heldRoleIds, profile ?? new EmployeeProfileViewModel
        {
            DisplayName = summary.Employee.DisplayName,
            EmploymentType = summary.Employee.EmploymentType,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GrantRole(Guid id, Guid roleId, CancellationToken cancellationToken)
    {
        return await RunAndRedirect(id, () => employeeAdmin.GrantRoleAsync(id, roleId, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeRole(Guid id, Guid roleId, CancellationToken cancellationToken)
    {
        return await RunAndRedirect(id, () => employeeAdmin.RevokeRoleAsync(id, roleId, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        return await RunAndRedirect(id, () => employeeAdmin.SetActiveAsync(id, isActive, cancellationToken));
    }

    private async Task<IActionResult> RunAndRedirect(Guid employeeId, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = employeeId });
    }
}
