namespace ProjectTango.Domain.Entities;

/// <summary>An effective-dated hourly rate for a (project, billing role) pair.
/// Rate changes create new rows — existing rows are closed, never edited.</summary>
public class ProjectRateCard
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RoleId { get; set; }
    public decimal HourlyRate { get; set; }
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Null = open-ended until superseded.</summary>
    public DateOnly? EffectiveTo { get; set; }

    public bool IsEffectiveOn(DateOnly date) =>
        EffectiveFrom <= date && (EffectiveTo is null || EffectiveTo >= date);
}
