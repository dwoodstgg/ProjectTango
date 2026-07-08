using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectTango.Application.Projects;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Web.Models;

public class ProjectManageViewModel
{
    public required Project Project { get; init; }
    public required ProjectFormViewModel Form { get; init; }
    public required IReadOnlyList<RateCardSummary> Rates { get; init; }
    public required IReadOnlyList<AssignmentSummary> Assignments { get; init; }
    public List<SelectListItem> BillableRoleOptions { get; init; } = [];
    public List<SelectListItem> EmployeeOptions { get; init; } = [];
}
