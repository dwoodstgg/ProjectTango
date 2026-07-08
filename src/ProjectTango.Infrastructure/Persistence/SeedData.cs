using ProjectTango.Domain;

namespace ProjectTango.Infrastructure.Persistence;

/// <summary>Fixed identifiers for rows seeded by SQL scripts (Scripts/0002_seed_phase1.sql).
/// Code that needs a well-known row (bootstrap Admin, internal client, INT-LEAVE) references these.</summary>
public static class SeedData
{
    // Roles
    public static readonly Guid DeveloperRoleId = new("a0000000-0000-0000-0000-000000000001");
    public static readonly Guid ProjectManagerRoleId = new("a0000000-0000-0000-0000-000000000002");
    public static readonly Guid OperationsManagerRoleId = new("a0000000-0000-0000-0000-000000000003");
    public static readonly Guid AdminRoleId = new("a0000000-0000-0000-0000-000000000004");

    // Bootstrap Admin (entra_oid linked on first sign-in; email is the bootstrap key)
    public static readonly Guid AdminEmployeeId = new("b0000000-0000-0000-0000-000000000001");
    public const string AdminEmail = "dwoods@thegeospatialgroup.com";

    // Internal client + leave project (never invoiced)
    public static readonly Guid InternalClientId = new("c0000000-0000-0000-0000-000000000001");
    public const string InternalClientName = "The Geospatial Group";
    public static readonly Guid LeaveProjectId = new("d0000000-0000-0000-0000-000000000001");
    public const string LeaveProjectCode = "INT-LEAVE";

    /// <summary>Stable timestamp for seeded rows — HasData must not produce a new value per migration diff.</summary>
    public static readonly DateTimeOffset SeededAt = new(2026, 7, 8, 0, 0, 0, TimeSpan.Zero);

    public static readonly (Guid Id, string Name, bool IsBillable, bool IsSystemAdmin)[] Roles =
    [
        (DeveloperRoleId, RoleNames.Developer, true, false),
        (ProjectManagerRoleId, RoleNames.ProjectManager, true, false),
        (OperationsManagerRoleId, RoleNames.OperationsManager, true, false),
        (AdminRoleId, RoleNames.Admin, false, true),
    ];
}
