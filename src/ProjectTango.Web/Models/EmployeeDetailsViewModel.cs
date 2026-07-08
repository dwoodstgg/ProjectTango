using System.ComponentModel.DataAnnotations;
using ProjectTango.Application.Employees;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;

namespace ProjectTango.Web.Models;

public record EmployeeDetailsViewModel(
    EmployeeSummary Summary,
    IReadOnlyList<Role> AllRoles,
    IReadOnlySet<Guid> HeldRoleIds,
    EmployeeProfileViewModel Profile);

public class EmployeeProfileViewModel
{
    [Required]
    [Display(Name = "Display name")]
    public string? DisplayName { get; set; }

    [Display(Name = "Employment type")]
    public EmploymentType EmploymentType { get; set; } = EmploymentType.Employee;
}
