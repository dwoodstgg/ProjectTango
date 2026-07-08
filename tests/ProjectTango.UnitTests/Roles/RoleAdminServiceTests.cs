using ProjectTango.Application.Roles;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.UnitTests.Fakes;

namespace ProjectTango.UnitTests.Roles;

public class RoleAdminServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeAuditLog _audit = new();
    private readonly RoleAdminService _service;

    private readonly Role _developer = new()
    {
        Id = Guid.NewGuid(), Name = RoleNames.Developer, DisplayName = "Developer",
    };
    private readonly Role _pm = new()
    {
        Id = Guid.NewGuid(), Name = RoleNames.ProjectManager, DisplayName = "Project Manager",
    };

    public RoleAdminServiceTests()
    {
        _service = new RoleAdminService(_currentUser, _roles, _audit);
        _roles.Roles.AddRange([_developer, _pm]);
        _currentUser.Roles.Add(RoleNames.Admin);
    }

    [Fact]
    public async Task Rename_changes_display_name_only_and_audits()
    {
        await _service.RenameAsync(_developer.Id, "  GIS Analyst ");

        Assert.Equal("GIS Analyst", _developer.DisplayName);
        Assert.Equal(RoleNames.Developer, _developer.Name); // stable key untouched
        var evt = Assert.Single(_audit.Events, e => e.Action == "role.renamed");
        Assert.Equal(_developer.Id, evt.EntityId);
    }

    [Fact]
    public async Task Rename_rejects_empty_name()
    {
        await Assert.ThrowsAsync<DomainException>(() => _service.RenameAsync(_developer.Id, "   "));
    }

    [Fact]
    public async Task Rename_rejects_duplicate_display_name()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.RenameAsync(_developer.Id, "project manager"));

        Assert.Contains("already named", ex.Message);
    }

    [Fact]
    public async Task Rename_to_same_value_is_a_noop()
    {
        await _service.RenameAsync(_developer.Id, "Developer");

        Assert.Empty(_audit.Events);
    }

    [Fact]
    public async Task Non_admin_cannot_manage_roles()
    {
        _currentUser.Roles.Clear();
        _currentUser.Roles.Add(RoleNames.OperationsManager);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.ListAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.RenameAsync(_developer.Id, "X"));
    }
}
