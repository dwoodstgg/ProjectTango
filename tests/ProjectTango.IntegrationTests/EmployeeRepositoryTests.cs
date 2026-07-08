using Dapper;
using Npgsql;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;
using ProjectTango.Infrastructure.Persistence;
using ProjectTango.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ProjectTango.IntegrationTests;

public sealed class EmployeeRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private NpgsqlDataSource _dataSource = null!;
    private EmployeeRepository _repository = null!;

    public async Task InitializeAsync()
    {
        DapperConfig.Apply();
        await _postgres.StartAsync();
        DatabaseMigrator.MigrateToLatest(_postgres.GetConnectionString());
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        _repository = new EmployeeRepository(_dataSource);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task GetByEmail_is_case_insensitive_and_maps_all_columns()
    {
        var employee = await _repository.GetByEmailAsync(SeedData.AdminEmail.ToUpperInvariant());

        Assert.NotNull(employee);
        Assert.Equal(SeedData.AdminEmployeeId, employee.Id);
        Assert.Null(employee.EntraOid);
        Assert.Equal("Don Woods", employee.DisplayName);
        Assert.Equal(EmploymentType.Employee, employee.EmploymentType);
        Assert.True(employee.IsActive);
    }

    [Fact]
    public async Task LinkEntraOid_makes_employee_findable_by_oid()
    {
        await _repository.LinkEntraOidAsync(SeedData.AdminEmployeeId, "test-oid-123");

        var byOid = await _repository.GetByEntraOidAsync("test-oid-123");

        Assert.NotNull(byOid);
        Assert.Equal(SeedData.AdminEmployeeId, byOid.Id);
    }

    [Fact]
    public async Task Add_roundtrips_a_subcontractor()
    {
        var subcontractor = new Employee
        {
            Id = Guid.NewGuid(),
            EntraOid = "sub-oid",
            Email = "sub@thegeospatialgroup.com",
            DisplayName = "Sub Contractor",
            EmploymentType = EmploymentType.Subcontractor,
        };

        await _repository.AddAsync(subcontractor);
        var fetched = await _repository.GetByEmailAsync("SUB@thegeospatialgroup.com");

        Assert.NotNull(fetched);
        Assert.Equal(EmploymentType.Subcontractor, fetched.EmploymentType);
    }

    [Fact]
    public async Task GetRoleNames_returns_admin_for_seeded_bootstrap_user()
    {
        var roles = await _repository.GetRoleNamesAsync(SeedData.AdminEmployeeId);

        Assert.Equal([RoleNames.Admin], roles);
    }

    [Fact]
    public async Task GetAll_includes_role_names()
    {
        var all = await _repository.GetAllAsync();

        var don = Assert.Single(all, s => s.Employee.Id == SeedData.AdminEmployeeId);
        Assert.Contains(RoleNames.Admin, don.RoleNames);
    }

    [Fact]
    public async Task Grant_and_revoke_role_roundtrip()
    {
        var granted = await _repository.GrantRoleAsync(
            SeedData.AdminEmployeeId, SeedData.DeveloperRoleId, SeedData.AdminEmployeeId);
        Assert.True(granted);

        var duplicate = await _repository.GrantRoleAsync(
            SeedData.AdminEmployeeId, SeedData.DeveloperRoleId, SeedData.AdminEmployeeId);
        Assert.False(duplicate);

        Assert.Contains(RoleNames.Developer, await _repository.GetRoleNamesAsync(SeedData.AdminEmployeeId));

        Assert.True(await _repository.RevokeRoleAsync(SeedData.AdminEmployeeId, SeedData.DeveloperRoleId));
        Assert.False(await _repository.RevokeRoleAsync(SeedData.AdminEmployeeId, SeedData.DeveloperRoleId));
    }

    [Fact]
    public async Task CountActiveAdmins_reflects_seed_and_deactivation()
    {
        Assert.Equal(1, await _repository.CountActiveAdminsAsync());

        await _repository.SetActiveAsync(SeedData.AdminEmployeeId, false);
        Assert.Equal(0, await _repository.CountActiveAdminsAsync());

        await _repository.SetActiveAsync(SeedData.AdminEmployeeId, true);
        Assert.Equal(1, await _repository.CountActiveAdminsAsync());
    }

    [Fact]
    public async Task AuditLog_writes_jsonb_details()
    {
        var audit = new AuditLogRepository(_dataSource);

        await audit.WriteAsync(new Application.Common.AuditEvent(
            SeedData.AdminEmployeeId, "role.granted", "employee", SeedData.AdminEmployeeId, new { Role = "Developer" }));

        await using var connection = await _dataSource.OpenConnectionAsync();
        var role = await connection.ExecuteScalarAsync<string>(
            "SELECT details->>'role' FROM audit_log WHERE action = 'role.granted'");
        Assert.Equal("Developer", role);
    }
}
