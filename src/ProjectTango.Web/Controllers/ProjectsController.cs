using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectTango.Application.Projects;
using ProjectTango.Domain;
using ProjectTango.Domain.Enums;
using ProjectTango.Web.Models;

namespace ProjectTango.Web.Controllers;

[Authorize(Roles = $"{RoleNames.Admin},{RoleNames.OperationsManager},{RoleNames.ProjectManager}")]
public class ProjectsController(ProjectAdminService projectAdmin) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var projects = await projectAdmin.ListAsync(cancellationToken);
        return View(projects);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        return View(await WithOptionsAsync(new ProjectFormViewModel(), cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProjectFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(await WithOptionsAsync(model, cancellationToken));
        }

        try
        {
            await projectAdmin.CreateAsync(
                model.ClientId!.Value, model.Name!, model.Code!, model.ProjectManagerId!.Value,
                model.StartDate, model.EndDate, cancellationToken);
            return RedirectToAction(nameof(Index));
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(await WithOptionsAsync(model, cancellationToken));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var project = await projectAdmin.GetAsync(id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        ViewBag.Project = project;
        return View(await WithOptionsAsync(ProjectFormViewModel.From(project), cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ProjectFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Project = await projectAdmin.GetAsync(id, cancellationToken);
            return View(await WithOptionsAsync(model, cancellationToken));
        }

        try
        {
            await projectAdmin.UpdateAsync(
                id, model.ClientId!.Value, model.Name!, model.Code!, model.ProjectManagerId!.Value,
                model.StartDate, model.EndDate, cancellationToken);
            return RedirectToAction(nameof(Index));
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewBag.Project = await projectAdmin.GetAsync(id, cancellationToken);
            return View(await WithOptionsAsync(model, cancellationToken));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(Guid id, ProjectStatus status, CancellationToken cancellationToken)
    {
        try
        {
            await projectAdmin.SetStatusAsync(id, status, cancellationToken);
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task<ProjectFormViewModel> WithOptionsAsync(ProjectFormViewModel model, CancellationToken cancellationToken)
    {
        model.ClientOptions = (await projectAdmin.GetClientOptionsAsync(cancellationToken))
            .Select(c => new SelectListItem(c.Name, c.Id.ToString()))
            .ToList();
        model.ManagerOptions = (await projectAdmin.GetManagerOptionsAsync(cancellationToken))
            .Select(e => new SelectListItem(e.DisplayName, e.Id.ToString()))
            .ToList();
        return model;
    }
}
