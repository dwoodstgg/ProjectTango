using ProjectTango.Application.Clients;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.UnitTests.Fakes;

namespace ProjectTango.UnitTests.Clients;

public class ClientAdminServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeClientRepository _clients = new();
    private readonly FakeAuditLog _audit = new();
    private readonly ClientAdminService _service;

    public ClientAdminServiceTests()
    {
        _service = new ClientAdminService(_currentUser, _clients, _audit);
        _currentUser.Roles.Add(RoleNames.OperationsManager);
    }

    [Fact]
    public async Task Create_normalizes_input_and_writes_audit()
    {
        var client = await _service.CreateAsync("  MDEQ ", "  ", "billing@mdeq.example", null, 30);

        Assert.Equal("MDEQ", client.Name);
        Assert.Null(client.BillingContactName);
        Assert.True(client.IsActive);
        Assert.False(client.IsInternal);
        Assert.Single(_audit.Events, e => e.Action == "client.created");
    }

    [Fact]
    public async Task Developer_cannot_manage_clients()
    {
        _currentUser.Roles.Clear();
        _currentUser.Roles.Add(RoleNames.Developer);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.CreateAsync("X", null, null, null, 30));
    }

    [Fact]
    public async Task Admin_without_ops_role_is_flagged_as_override()
    {
        _currentUser.Roles.Clear();
        _currentUser.Roles.Add(RoleNames.Admin);

        await _service.CreateAsync("Via Admin", null, null, null, 30);

        var evt = Assert.Single(_audit.Events, e => e.Action == "client.created");
        var adminOverride = (bool)evt.Details!.GetType().GetProperty("adminOverride")!.GetValue(evt.Details)!;
        Assert.True(adminOverride);
    }

    [Fact]
    public async Task Deactivate_and_reactivate_are_audited()
    {
        var client = new Client { Id = Guid.NewGuid(), Name = "C" };
        _clients.Clients.Add(client);

        await _service.SetActiveAsync(client.Id, false);
        Assert.False(client.IsActive);
        Assert.Single(_audit.Events, e => e.Action == "client.deactivated");

        await _service.SetActiveAsync(client.Id, true);
        Assert.True(client.IsActive);
        Assert.Single(_audit.Events, e => e.Action == "client.reactivated");
    }
}
