using Dapper;
using Npgsql;
using ProjectTango.Domain;
using ProjectTango.Domain.Enums;
using ProjectTango.Infrastructure.Persistence;
using ProjectTango.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ProjectTango.IntegrationTests;

public sealed class RoleRenameAndProfileTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private NpgsqlDataSource _dataSource = null!;
    private RoleRepository _roles = null!;
    private EmployeeRepository _employees = null!;

    public async Task InitializeAsync()
    {
        DapperConfig.Apply();
        await _postgres.StartAsync();
        DatabaseMigrator.MigrateToLatest(_postgres.GetConnectionString());
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        _roles = new RoleRepository(_dataSource);
        _employees = new EmployeeRepository(_dataSource);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Seeded_roles_have_display_name_equal_to_name()
    {
        var all = await _roles.GetAllAsync();

        Assert.All(all, r => Assert.Equal(r.Name, r.DisplayName));
    }

    [Fact]
    public async Task Rename_updates_display_name_but_not_the_stable_key()
    {
        await _roles.RenameAsync(SeedData.DeveloperRoleId, "GIS Analyst");

        var role = await _roles.GetByIdAsync(SeedData.DeveloperRoleId);
        Assert.NotNull(role);
        Assert.Equal("GIS Analyst", role.DisplayName);
        Assert.Equal(RoleNames.Developer, role.Name); // auth key unchanged

        // Auth claim source keeps returning the stable name; display source returns the new label.
        var stable = await _employees.GetRoleNamesAsync(SeedData.AdminEmployeeId); // Admin only
        Assert.DoesNotContain("GIS Analyst", stable);
    }

    [Fact]
    public async Task Renamed_role_shows_new_label_in_employee_display_badges()
    {
        // Give the seeded Admin the Developer role too, then rename it.
        await _employees.GrantRoleAsync(SeedData.AdminEmployeeId, SeedData.DeveloperRoleId, SeedData.AdminEmployeeId);
        await _roles.RenameAsync(SeedData.DeveloperRoleId, "GIS Analyst");

        var displayNames = await _employees.GetRoleDisplayNamesAsync(SeedData.AdminEmployeeId);
        var stableNames = await _employees.GetRoleNamesAsync(SeedData.AdminEmployeeId);

        Assert.Contains("GIS Analyst", displayNames);
        Assert.Contains(RoleNames.Developer, stableNames);
        Assert.DoesNotContain("GIS Analyst", stableNames);
    }

    [Fact]
    public async Task UpdateProfile_persists_name_and_type()
    {
        await _employees.UpdateProfileAsync(SeedData.AdminEmployeeId, "Donald Woods", EmploymentType.Subcontractor);

        var employee = await _employees.GetByIdAsync(SeedData.AdminEmployeeId);
        Assert.NotNull(employee);
        Assert.Equal("Donald Woods", employee.DisplayName);
        Assert.Equal(EmploymentType.Subcontractor, employee.EmploymentType);
    }

    [Fact]
    public async Task GetRoleIds_returns_held_roles()
    {
        var ids = await _employees.GetRoleIdsAsync(SeedData.AdminEmployeeId);

        Assert.Contains(SeedData.AdminRoleId, ids);
    }
}
