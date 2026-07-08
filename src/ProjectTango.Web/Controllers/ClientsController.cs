using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectTango.Application.Clients;
using ProjectTango.Domain;
using ProjectTango.Web.Models;

namespace ProjectTango.Web.Controllers;

[Authorize(Roles = $"{RoleNames.Admin},{RoleNames.OperationsManager}")]
public class ClientsController(ClientAdminService clientAdmin) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var clients = await clientAdmin.ListAsync(cancellationToken);
        return View(clients);
    }

    [HttpGet]
    public IActionResult Create() => View(new ClientFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ClientFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await clientAdmin.CreateAsync(
                model.Name!, model.BillingContactName, model.BillingContactEmail,
                model.ToBillingAddress(), model.PaymentTermsDays, cancellationToken);
            return RedirectToAction(nameof(Index));
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var client = await clientAdmin.GetAsync(id, cancellationToken);
        if (client is null)
        {
            return NotFound();
        }

        ViewBag.Client = client;
        return View(ClientFormViewModel.From(client));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ClientFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Client = await clientAdmin.GetAsync(id, cancellationToken);
            return View(model);
        }

        try
        {
            await clientAdmin.UpdateAsync(
                id, model.Name!, model.BillingContactName, model.BillingContactEmail,
                model.ToBillingAddress(), model.PaymentTermsDays, cancellationToken);
            return RedirectToAction(nameof(Index));
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewBag.Client = await clientAdmin.GetAsync(id, cancellationToken);
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        try
        {
            await clientAdmin.SetActiveAsync(id, isActive, cancellationToken);
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
