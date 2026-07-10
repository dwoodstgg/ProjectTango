namespace ProjectTango.Domain.Entities;

/// <summary>Records that a budget threshold alert has already fired, so it isn't re-sent on
/// every later save (design §6.2). <see cref="AlertKey"/> is <c>pct:&lt;n&gt;</c> for a
/// configured threshold or <c>overrun</c> for going over budget.</summary>
public class BudgetAlert
{
    public Guid Id { get; set; }
    public Guid BudgetId { get; set; }
    public required string AlertKey { get; set; }

    /// <summary>The burn percent at the moment the alert fired (for the email and audit).</summary>
    public decimal BurnPercent { get; set; }

    public DateTimeOffset NotifiedAt { get; set; }
}
