using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectTango.Application.Projects;
using ProjectTango.Domain;
using ProjectTango.Domain.Enums;
using ProjectTango.Web.Models;

namespace ProjectTango.Web.Controllers;

[Authorize(Roles = $"{RoleNames.Admin},{RoleNames.OperationsManager},{RoleNames.ProjectManager}")]
public class ProjectsController(
    ProjectAdminService projectAdmin,
    RateCardService rateCardService,
    AssignmentService assignmentService,
    BudgetService budgetService,
    ProjectDashboardService dashboardService) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var projects = await projectAdmin.ListAsync(cancellationToken);
        return View(projects);
    }

    [HttpGet]
    public async Task<IActionResult> Dashboard(Guid id, CancellationToken cancellationToken)
    {
        var dashboard = await dashboardService.GetAsync(id, cancellationToken);
        return dashboard is null ? NotFound() : View(dashboard);
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
                model.StartDate, model.EndDate, model.ToBillingInput(), cancellationToken);
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
        var page = await BuildManagePageAsync(id, form: null, cancellationToken);
        return page is null ? NotFound() : View(page);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ProjectFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var page = await BuildManagePageAsync(id, model, cancellationToken);
            return page is null ? NotFound() : View(page);
        }

        try
        {
            await projectAdmin.UpdateAsync(
                id, model.ClientId!.Value, model.Name!, model.Code!, model.ProjectManagerId!.Value,
                model.StartDate, model.EndDate, model.ToBillingInput(), cancellationToken);
            return RedirectToAction(nameof(Index));
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var page = await BuildManagePageAsync(id, model, cancellationToken);
            return page is null ? NotFound() : View(page);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(Guid id, ProjectStatus status, CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () => projectAdmin.SetStatusAsync(id, status, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRate(
        Guid id, Guid roleId, decimal hourlyRate, DateOnly effectiveFrom, CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            rateCardService.SetRateAsync(id, roleId, hourlyRate, effectiveFrom, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CorrectRate(
        Guid id, Guid rateCardId, decimal hourlyRate, DateOnly effectiveFrom, CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            rateCardService.CorrectRateAsync(id, rateCardId, hourlyRate, effectiveFrom, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRate(Guid id, Guid rateCardId, CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            rateCardService.DeleteRateAsync(id, rateCardId, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetBudget(
        Guid id, BudgetType type, decimal? amount, decimal? hours, string? thresholds, string? reason,
        CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            budgetService.SetBudgetAsync(id, type, amount, hours, ParseThresholds(thresholds), reason, cancellationToken));
    }

    /// <summary>Parses the comma/space-separated threshold input (e.g. "50, 75, 90"). Null or
    /// blank falls through to the service default; the service also validates and clamps.</summary>
    private static int[]? ParseThresholds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return raw
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var value) ? value : (int?)null)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(
        Guid id, Guid employeeId, Guid? defaultBillingRoleId, DateOnly? startDate, CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            assignmentService.AssignAsync(id, employeeId, defaultBillingRoleId, startDate, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndAssignment(
        Guid id, Guid assignmentId, DateOnly? endDate, CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            assignmentService.EndAsync(assignmentId, endDate ?? DateOnly.FromDateTime(DateTime.Today), cancellationToken));
    }

    private async Task<IActionResult> RunAndReturnToManage(Guid projectId, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Edit), new { id = projectId });
    }

    private async Task<ProjectManageViewModel?> BuildManagePageAsync(
        Guid id, ProjectFormViewModel? form, CancellationToken cancellationToken)
    {
        var project = await projectAdmin.GetAsync(id, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var billableRoles = await projectAdmin.GetBillableRoleOptionsAsync(cancellationToken);

        return new ProjectManageViewModel
        {
            Project = project,
            Form = await WithOptionsAsync(form ?? ProjectFormViewModel.From(project), cancellationToken),
            Rates = await rateCardService.ListForProjectAsync(id, cancellationToken),
            Assignments = await assignmentService.ListForProjectAsync(id, cancellationToken),
            Budget = await budgetService.GetAsync(id, cancellationToken),
            BudgetRevisions = await budgetService.GetRevisionsAsync(id, cancellationToken),
            BillableRoleOptions = billableRoles
                .Select(r => new SelectListItem(r.DisplayName, r.Id.ToString()))
                .ToList(),
            EmployeeOptions = (await projectAdmin.GetManagerOptionsAsync(cancellationToken))
                .Select(e => new SelectListItem(e.DisplayName, e.Id.ToString()))
                .ToList(),
        };
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
