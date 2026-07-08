using ProjectTango.Domain.Enums;

namespace ProjectTango.Domain.Entities;

public class Employee
{
    public Guid Id { get; set; }

    /// <summary>Entra object ID — the stable identity key. Null until first sign-in
    /// for records created ahead of time (imports, manual provisioning).</summary>
    public string? EntraOid { get; set; }

    /// <summary>Tenant email; bootstrap key that links a pre-created record to Entra on first sign-in.</summary>
    public required string Email { get; set; }

    public required string DisplayName { get; set; }

    public EmploymentType EmploymentType { get; set; } = EmploymentType.Employee;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<EmployeeRole> Roles { get; set; } = [];
}
