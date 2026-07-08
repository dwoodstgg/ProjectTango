namespace ProjectTango.Domain.Entities;

public class Client
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public string? BillingContactName { get; set; }
    public string? BillingContactEmail { get; set; }
    public BillingAddress? BillingAddress { get; set; }

    /// <summary>Net terms, e.g. 30 for Net-30. Due date = issued + terms.</summary>
    public int PaymentTermsDays { get; set; } = 30;

    /// <summary>True only for The Geospatial Group itself — owns internal non-billable
    /// projects (leave, admin time) that are never invoiced.</summary>
    public bool IsInternal { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Project> Projects { get; set; } = [];
}

/// <summary>Stored as jsonb on clients.billing_address.</summary>
public record BillingAddress
{
    public string? Line1 { get; init; }
    public string? Line2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
}
