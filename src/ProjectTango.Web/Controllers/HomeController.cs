using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectTango.Domain;
using ProjectTango.Web.Models;

namespace ProjectTango.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        // The timesheet is the working home for everyone except Admin, who keeps the landing page.
        if (User.Identity?.IsAuthenticated == true && !User.IsInRole(RoleNames.Admin))
        {
            return RedirectToAction("Index", "Timesheet");
        }
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [Authorize]
    public IActionResult Me()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
