using ProjectTango.Application.Employees;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;
using ProjectTango.UnitTests.Fakes;

namespace ProjectTango.UnitTests.Employees;

public class EmployeeAdminServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeEmployeeRepository _employees;
    private readonly FakeAuditLog _audit = new();
    private readonly EmployeeAdminService _service;

    private readonly Role _adminRole = new() { Id = Guid.NewGuid(), Name = RoleNames.Admin, IsBillable = false, IsSystemAdmin = true };
    private readonly Role _developerRole = new() { Id = Guid.NewGuid(), Name = RoleNames.Developer };
    private readonly Employee _don;

    public EmployeeAdminServiceTests()
    {
        _employees = new FakeEmployeeRepository(_roles);
        _service = new EmployeeAdminService(_currentUser, _employees, _roles, _audit);

        _roles.Roles.AddRange([_adminRole, _developerRole]);
        _don = AddEmployee("don@thegeospatialgroup.com", _adminRole.Id);
        _currentUser.Roles.Add(RoleNames.Admin);
        _currentUser.EmployeeId = _don.Id;
    }

    [Fact]
    public async Task Rejects_callers_without_admin_or_ops_role()
    {
        _currentUser.Roles.Clear();
        _currentUser.Roles.Add(RoleNames.Developer);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.ListAsync());
    }

    [Fact]
    public async Task Create_rejects_duplicate_email()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.CreateAsync("DON@thegeospatialgroup.com", "Duplicate Don", EmploymentType.Employee));

        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task UpdateProfile_changes_name_and_type_and_audits()
    {
        await _service.UpdateProfileAsync(_don.Id, "  Donald Woods ", EmploymentType.Subcontractor);

        Assert.Equal("Donald Woods", _don.DisplayName);
        Assert.Equal(EmploymentType.Subcontractor, _don.EmploymentType);
        Assert.Single(_audit.Events, e => e.Action == "employee.updated" && e.EntityId == _don.Id);
    }

    [Fact]
    public async Task UpdateProfile_rejects_empty_name()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.UpdateProfileAsync(_don.Id, "   ", EmploymentType.Employee));
    }

    [Fact]
    public async Task UpdateProfile_noop_when_unchanged_writes_no_audit()
    {
        await _service.UpdateProfileAsync(_don.Id, _don.DisplayName, _don.EmploymentType);

        Assert.DoesNotContain(_audit.Events, e => e.Action == "employee.updated");
    }

    [Fact]
    public async Task Create_trims_input_and_writes_audit()
    {
        var employee = await _service.CreateAsync("  new@thegeospatialgroup.com ", " New Person ", EmploymentType.Subcontractor);

        Assert.Equal("new@thegeospatialgroup.com", employee.Email);
        Assert.Equal("New Person", employee.DisplayName);
        var audit = Assert.Single(_audit.Events, e => e.Action == "employee.created");
        Assert.Equal(employee.Id, audit.EntityId);
        Assert.Equal(_don.Id, audit.ActorEmployeeId);
    }

    [Fact]
    public async Task Cannot_revoke_admin_from_last_active_admin()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.RevokeRoleAsync(_don.Id, _adminRole.Id));

        Assert.Contains("At least one active Admin", ex.Message);
        Assert.Contains(RoleNames.Admin, await _employees.GetRoleNamesAsync(_don.Id));
    }

    [Fact]
    public async Task Can_revoke_admin_when_another_active_admin_exists()
    {
        AddEmployee("second@thegeospatialgroup.com", _adminRole.Id);

        await _service.RevokeRoleAsync(_don.Id, _adminRole.Id);

        Assert.DoesNotContain(RoleNames.Admin, await _employees.GetRoleNamesAsync(_don.Id));
        Assert.Single(_audit.Events, e => e.Action == "role.revoked");
    }

    [Fact]
    public async Task Cannot_deactivate_last_active_admin()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => _service.SetActiveAsync(_don.Id, false));

        Assert.Contains("At least one active Admin", ex.Message);
        Assert.True(_don.IsActive);
    }

    [Fact]
    public async Task Deactivating_non_admin_works_and_is_audited()
    {
        var dev = AddEmployee("dev@thegeospatialgroup.com", _developerRole.Id);

        await _service.SetActiveAsync(dev.Id, false);

        Assert.False(dev.IsActive);
        Assert.Single(_audit.Events, e => e.Action == "employee.deactivated" && e.EntityId == dev.Id);
    }

    [Fact]
    public async Task Grant_and_duplicate_grant()
    {
        var dev = AddEmployee("dev@thegeospatialgroup.com");

        await _service.GrantRoleAsync(dev.Id, _developerRole.Id);
        Assert.Contains(RoleNames.Developer, await _employees.GetRoleNamesAsync(dev.Id));
        Assert.Single(_audit.Events, e => e.Action == "role.granted");

        await Assert.ThrowsAsync<DomainException>(() => _service.GrantRoleAsync(dev.Id, _developerRole.Id));
    }

    private Employee AddEmployee(string email, params Guid[] roleIds)
    {
        var employee = new Employee { Id = Guid.NewGuid(), Email = email, DisplayName = email };
        _employees.Employees.Add(employee);
        if (roleIds.Length > 0)
        {
            _employees.RoleIdsByEmployee[employee.Id] = [.. roleIds];
        }

        return employee;
    }
}
