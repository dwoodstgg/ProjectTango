using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Crosscheck.Application.Projects;
using Crosscheck.Domain;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;
using Crosscheck.Web.Models;

namespace Crosscheck.Web.Controllers;

[Authorize(Roles = $"{RoleNames.Admin},{RoleNames.OperationsManager},{RoleNames.ProjectManager}")]
public class ProjectsController(
    ProjectAdminService projectAdmin,
    RateCardService rateCardService,
    AssignmentService assignmentService,
    BudgetService budgetService,
    ModuleService moduleService,
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
        if (dashboard is null)
        {
            return NotFound();
        }

        ViewBag.SwitcherProjects = await projectAdmin.ListAsync(cancellationToken);
        return View(dashboard);
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
                model.ProjectType, model.StartDate, model.EndDate, model.ToBillingInput(),
                cancellationToken: cancellationToken);
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
    public async Task<IActionResult> Edit(
        Guid id, ProjectFormViewModel model,
        decimal? budgetAmount, decimal? budgetHours,
        string? budgetThresholds, string? budgetReason,
        // Explicit name pins the binding prefix. Without it, a post with no roleHours[...]
        // fields (internal projects, modular/service-contract budgets) makes MVC fall back to
        // the empty prefix and try every form field name as a Guid dictionary key — each one
        // failing with "Unrecognized Guid format." in ModelState, which blocks the save.
        [FromForm(Name = "roleHours")] Dictionary<Guid, decimal?>? roleHours,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var page = await BuildManagePageAsync(id, model, cancellationToken);
            return page is null ? NotFound() : View(page);
        }

        var allocations = roleHours?
            .Where(kv => kv.Value is > 0)
            .Select(kv => new RoleHourInput(kv.Key, kv.Value!.Value))
            .ToList();

        try
        {
            await projectAdmin.UpdateAsync(
                id, model.ClientId!.Value, model.Name!, model.Code!, model.ProjectManagerId!.Value,
                model.ProjectType, model.StartDate, model.EndDate, model.ToBillingInput(), cancellationToken);

            // The budget shares this one Save. Only touch it when the submitted values actually
            // differ from the current budget — SetBudgetAsync writes a revision on every call, so
            // an unconditional call would append a no-op revision each time Details is saved.
            var project = await projectAdmin.GetAsync(id, cancellationToken);
            var current = await budgetService.GetAsync(id, cancellationToken);
            var thresholds = BudgetService.NormalizeThresholds(ParseThresholds(budgetThresholds));
            if (project is not null
                && BudgetChanged(project, current, budgetAmount, budgetHours, thresholds, allocations))
            {
                await budgetService.SetBudgetAsync(
                    id, budgetAmount, budgetHours, thresholds, budgetReason,
                    allocations, cancellationToken);
            }

            return RedirectToAction(nameof(Edit), new { id });
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var page = await BuildManagePageAsync(id, model, cancellationToken);
            return page is null ? NotFound() : View(page);
        }
    }

    /// <summary>True when the submitted budget both carries something (amount, hours, or
    /// per-role hours) and differs from what's stored — so we skip the revision when
    /// nothing changed and never create an empty budget. Mirrors
    /// <see cref="BudgetService.SetBudgetAsync"/>'s rules: overall hours default to the sum of
    /// role allocations; the reason never lives on the budget row so it isn't compared.</summary>
    private static bool BudgetChanged(
        Project project, Budget? current, decimal? amount, decimal? hours,
        int[] thresholds, IReadOnlyList<RoleHourInput>? allocations)
    {
        var allocDict = allocations?.ToDictionary(a => a.RoleId, a => a.Hours) ?? [];
        var effectiveHours = hours ?? (allocDict.Count > 0 ? allocDict.Values.Sum() : (decimal?)null);

        var provided = amount is not null || effectiveHours is not null || allocDict.Count > 0;
        if (!provided)
        {
            return false;
        }

        if (current is null)
        {
            return true;
        }

        if (project.Type != current.Type || amount != current.Amount
            || effectiveHours != current.Hours
            || !thresholds.SequenceEqual(current.AlertThresholds))
        {
            return true;
        }

        if (allocDict.Count != current.RoleAllocations.Count)
        {
            return true;
        }

        return current.RoleAllocations.Any(a =>
            !allocDict.TryGetValue(a.RoleId, out var h) || h != a.Hours);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await projectAdmin.DeleteAsync(id, cancellationToken);
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
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
        Guid id, Guid roleId, decimal hourlyRate, CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            rateCardService.SetRateAsync(id, roleId, hourlyRate, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CorrectRate(
        Guid id, Guid rateCardId, decimal hourlyRate, CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            rateCardService.CorrectRateAsync(id, rateCardId, hourlyRate, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRate(Guid id, Guid rateCardId, CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            rateCardService.DeleteRateAsync(id, rateCardId, cancellationToken));
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

    // Modules ------------------------------------------------------------------

    /// <summary>Instant-apply switch for what the project calls its breakdown sections —
    /// posted straight from the Modules/Milestones card header so the wording flips
    /// immediately instead of waiting on the page-level Save.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetBreakdownLabel(
        Guid id, BreakdownLabel breakdownLabel, CancellationToken cancellationToken)
    {
        try
        {
            await projectAdmin.SetBreakdownLabelAsync(id, breakdownLabel, cancellationToken);
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Edit), new { id });
        }

        // The card's script posts via fetch and swaps in the re-rendered card, so the rest
        // of the page keeps unsaved input and scroll position. Plain form posts (no JS)
        // still get the redirect.
        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            var page = await BuildManagePageAsync(id, form: null, cancellationToken);
            return page is null ? NotFound() : PartialView("_ModulesCard", page);
        }

        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddModule(
        Guid id, string name, decimal? moduleHours, decimal? moduleAmount,
        [FromForm(Name = "moduleRoleHours")] Dictionary<Guid, decimal?>? moduleRoleHours,
        CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            moduleService.CreateAsync(id, name, moduleHours, moduleAmount,
                ToModuleAllocations(moduleRoleHours), cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameModule(Guid id, Guid moduleId, string name, CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            moduleService.RenameAsync(id, moduleId, name, cancellationToken));
    }

    /// <summary>One save for a module's budget numbers: flat hours, per-role hours, and the
    /// agreed amount travel together from the module's edit form.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetModuleBudget(
        Guid id, Guid moduleId, decimal? moduleHours, decimal? moduleAmount,
        [FromForm(Name = "moduleRoleHours")] Dictionary<Guid, decimal?>? moduleRoleHours,
        CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, async () =>
        {
            await moduleService.SetHoursAsync(id, moduleId, moduleHours, cancellationToken);
            await moduleService.SetAllocationsAsync(id, moduleId,
                ToModuleAllocations(moduleRoleHours) ?? [], cancellationToken);
            await moduleService.SetAmountAsync(id, moduleId, moduleAmount, cancellationToken);
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteModule(Guid id, Guid moduleId, CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            moduleService.DeleteAsync(id, moduleId, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetModuleRate(
        Guid id, Guid moduleId, Guid? roleId, decimal hourlyRate, CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            moduleService.SetRateAsync(id, moduleId, roleId, hourlyRate, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CorrectModuleRate(
        Guid id, Guid moduleRateId, decimal hourlyRate, CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            moduleService.CorrectRateAsync(id, moduleRateId, hourlyRate, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteModuleRate(Guid id, Guid moduleRateId, CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            moduleService.DeleteRateAsync(id, moduleRateId, cancellationToken));
    }

    private static List<ModuleRoleHourInput>? ToModuleAllocations(Dictionary<Guid, decimal?>? moduleRoleHours) =>
        moduleRoleHours?
            .Where(kv => kv.Value is > 0)
            .Select(kv => new ModuleRoleHourInput(kv.Key, kv.Value!.Value))
            .ToList();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(
        Guid id, Guid employeeId, Guid? defaultBillingRoleId, CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            assignmentService.AssignAsync(id, employeeId, defaultBillingRoleId, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAssignment(Guid id, Guid assignmentId, CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            assignmentService.RemoveAsync(assignmentId, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReactivateAssignment(Guid id, Guid assignmentId, CancellationToken cancellationToken)
    {
        return await RunAndReturnToManage(id, () =>
            assignmentService.ReactivateAsync(assignmentId, cancellationToken));
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
        var rates = await rateCardService.ListForProjectAsync(id, cancellationToken);

        return new ProjectManageViewModel
        {
            Project = project,
            Form = await WithOptionsAsync(form ?? ProjectFormViewModel.From(project), cancellationToken),
            Rates = rates,
            Assignments = await assignmentService.ListForProjectAsync(id, cancellationToken),
            Budget = await budgetService.GetAsync(id, cancellationToken),
            BudgetRevisions = await budgetService.GetRevisionsAsync(id, cancellationToken),
            Modules = await moduleService.ListForProjectAsync(id, cancellationToken),
            BillableRoleOptions = billableRoles
                .Select(r => new SelectListItem(r.DisplayName, r.Id.ToString()))
                .ToList(),
            // Only roles priced on this project can be a team member's default billing role.
            RateCardRoleOptions = rates
                .GroupBy(r => r.RateCard.RoleId)
                .Select(g => new SelectListItem(g.First().RoleName, g.Key.ToString()))
                .OrderBy(o => o.Text)
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
