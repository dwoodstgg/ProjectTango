namespace ProjectTango.Domain.Entities;

/// <summary>A company role (permissions). Roles are data, not code — extensible beyond
/// the four seeded ones. The billing role on a time entry references this same table.</summary>
public class Role
{
    public Guid Id { get; set; }

    /// <summary>Stable authorization key — matches a <see cref="RoleNames"/> constant,
    /// used in claims and [Authorize]. Never edited. Display uses <see cref="DisplayName"/>.</summary>
    public required string Name { get; set; }

    /// <summary>Editable label shown throughout the UI (badges, dropdowns, rate cards).
    /// Renaming this never affects permissions. Seeded equal to <see cref="Name"/>.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>False for system roles (Admin) that are never a billing role.</summary>
    public bool IsBillable { get; set; } = true;

    /// <summary>True only for Admin — grants resource-level bypass semantics.</summary>
    public bool IsSystemAdmin { get; set; }
}
