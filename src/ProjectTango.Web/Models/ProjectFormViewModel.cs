using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Web.Models;

public class ProjectFormViewModel
{
    [Required]
    [Display(Name = "Client")]
    public Guid? ClientId { get; set; }

    [Required]
    [Display(Name = "Project name")]
    public string? Name { get; set; }

    [Required]
    [Display(Name = "Code")]
    [RegularExpression(@"^[A-Za-z0-9\-]{2,20}$", ErrorMessage = "2–20 letters, digits, or dashes (e.g. GEO-014).")]
    public string? Code { get; set; }

    [Required]
    [Display(Name = "Project manager")]
    public Guid? ProjectManagerId { get; set; }

    [Display(Name = "Start date")]
    [DataType(DataType.Date)]
    public DateOnly? StartDate { get; set; }

    [Display(Name = "End date")]
    [DataType(DataType.Date)]
    public DateOnly? EndDate { get; set; }

    public List<SelectListItem> ClientOptions { get; set; } = [];
    public List<SelectListItem> ManagerOptions { get; set; } = [];

    public static ProjectFormViewModel From(Project project) => new()
    {
        ClientId = project.ClientId,
        Name = project.Name,
        Code = project.Code,
        ProjectManagerId = project.ProjectManagerId,
        StartDate = project.StartDate,
        EndDate = project.EndDate,
    };
}
