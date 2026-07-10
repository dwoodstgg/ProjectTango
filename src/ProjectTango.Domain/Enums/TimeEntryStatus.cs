namespace ProjectTango.Domain.Enums;

/// <summary>Time entry lifecycle (design-doc §5.2). No submission step. Entries are
/// auto-approved on save (small-shop default), so most reach <see cref="Approved"/>
/// immediately; a billable entry with no rate card yet stays <see cref="Open"/> until one is
/// added. Owners keep editing until the window closes or the entry is invoiced. Invoiced
/// entries are immutable — void the invoice to return them to <see cref="Approved"/>.</summary>
public enum TimeEntryStatus
{
    Open,
    Approved,
    Invoiced,
}
