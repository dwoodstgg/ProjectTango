namespace ProjectTango.Domain.Entities;

/// <summary>An hour allocation for one billing role under a project budget (e.g. Lead
/// Developer 300h). Allocations are in hours; the dollar value derives from the rate card.</summary>
public class BudgetRoleAllocation
{
    public Guid Id { get; set; }
    public Guid BudgetId { get; set; }
    public Guid RoleId { get; set; }
    public decimal Hours { get; set; }

    /// <summary>Role display label, populated by the repository for UI/reporting — not persisted
    /// on this row (it lives on <c>roles</c>).</summary>
    public string? RoleName { get; set; }
}
