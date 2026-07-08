namespace ProjectTango.Domain.Entities;

/// <summary>Grant of a company role to an employee. Permissions are the UNION of held roles.
/// Grants/revocations are also written to the audit log.</summary>
public class EmployeeRole
{
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public Guid RoleId { get; set; }
    public Role? Role { get; set; }

    public Guid GrantedById { get; set; }
    public Employee? GrantedBy { get; set; }

    public DateTimeOffset GrantedAt { get; set; }
}
