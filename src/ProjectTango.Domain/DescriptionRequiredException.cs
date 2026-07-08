namespace ProjectTango.Domain;

/// <summary>A billable time entry was saved without a work description. Distinct from a plain
/// <see cref="DomainException"/> so the UI can respond by prompting for the description rather
/// than just showing an error.</summary>
public sealed class DescriptionRequiredException()
    : DomainException("A work description is required for billable time.");
