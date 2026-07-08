namespace ProjectTango.Domain.Entities;

/// <summary>Project membership. One row per person per project — ending sets
/// EndDate (soft); re-adding reopens the row. Time entries require an active
/// assignment on their date.</summary>
public class ProjectAssignment
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>UI pre-selection only — the authoritative billing role is chosen per time entry.</summary>
    public Guid? DefaultBillingRoleId { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public bool IsActiveOn(DateOnly date) =>
        (StartDate is null || StartDate <= date) && (EndDate is null || EndDate >= date);
}
