using ProjectTango.Domain.Enums;

namespace ProjectTango.Domain.Entities;

public class Project
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }
    public Client? Client { get; set; }

    public required string Name { get; set; }

    /// <summary>Short unique code for invoices/reports, e.g. GEO-014 or INT-LEAVE.</summary>
    public required string Code { get; set; }

    /// <summary>Status changes are always explicit user actions — never automatic
    /// (budget exhaustion must not close a project).</summary>
    public ProjectStatus Status { get; set; } = ProjectStatus.Draft;

    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedById { get; set; }
    public Employee? ClosedBy { get; set; }

    public Guid ProjectManagerId { get; set; }
    public Employee? ProjectManager { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    /// <summary>USD only in v1; column kept for the future.</summary>
    public string Currency { get; set; } = "USD";
}
