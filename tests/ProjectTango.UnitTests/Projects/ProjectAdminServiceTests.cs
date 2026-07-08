using ProjectTango.Application.Projects;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;
using ProjectTango.UnitTests.Fakes;

namespace ProjectTango.UnitTests.Projects;

public class ProjectAdminServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeClientRepository _clients = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeEmployeeRepository _employees;
    private readonly FakeAuditLog _audit = new();
    private readonly ProjectAdminService _service;

    private readonly Client _client = new() { Id = Guid.NewGuid(), Name = "MDEQ" };
    private readonly Employee _pm;
    private readonly Employee _otherPm;

    public ProjectAdminServiceTests()
    {
        _employees = new FakeEmployeeRepository(_roles);
        _service = new ProjectAdminService(_currentUser, _projects, _clients, _employees, _audit);

        _clients.Clients.Add(_client);
        _pm = AddEmployee("pm@thegeospatialgroup.com");
        _otherPm = AddEmployee("otherpm@thegeospatialgroup.com");

        _currentUser.Roles.Add(RoleNames.ProjectManager);
        _currentUser.EmployeeId = _pm.Id;
    }

    [Fact]
    public async Task Create_starts_in_draft_and_uppercases_code()
    {
        var project = await _service.CreateAsync(_client.Id, "Dashboard", "geo-001", _pm.Id, null, null);

        Assert.Equal(ProjectStatus.Draft, project.Status);
        Assert.Equal("GEO-001", project.Code);
        Assert.Single(_audit.Events, e => e.Action == "project.created");
    }

    [Fact]
    public async Task Create_rejects_duplicate_code_case_insensitively()
    {
        await _service.CreateAsync(_client.Id, "One", "GEO-001", _pm.Id, null, null);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.CreateAsync(_client.Id, "Two", "geo-001", _pm.Id, null, null));

        Assert.Contains("already in use", ex.Message);
    }

    [Fact]
    public async Task Create_rejects_end_before_start()
    {
        await Assert.ThrowsAsync<DomainException>(() => _service.CreateAsync(
            _client.Id, "Bad dates", "GEO-002", _pm.Id,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 1)));
    }

    [Fact]
    public async Task Pm_cannot_manage_another_pms_project()
    {
        var project = await _service.CreateAsync(_client.Id, "Other's", "GEO-003", _otherPm.Id, null, null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.SetStatusAsync(project.Id, ProjectStatus.Active));
    }

    [Fact]
    public async Task Admin_managing_someone_elses_project_is_flagged_as_override()
    {
        var project = await _service.CreateAsync(_client.Id, "Other's", "GEO-004", _otherPm.Id, null, null);
        _currentUser.Roles.Clear();
        _currentUser.Roles.Add(RoleNames.Admin);

        await _service.SetStatusAsync(project.Id, ProjectStatus.Active);

        var evt = Assert.Single(_audit.Events, e => e.Action == "project.status_changed");
        var adminOverride = (bool)evt.Details!.GetType().GetProperty("adminOverride")!.GetValue(evt.Details)!;
        Assert.True(adminOverride);
    }

    [Fact]
    public async Task Status_transitions_follow_phase1_rules()
    {
        var project = await _service.CreateAsync(_client.Id, "Mine", "GEO-005", _pm.Id, null, null);

        await _service.SetStatusAsync(project.Id, ProjectStatus.Active);
        Assert.Equal(ProjectStatus.Active, project.Status);

        await _service.SetStatusAsync(project.Id, ProjectStatus.OnHold);
        Assert.Equal(ProjectStatus.OnHold, project.Status);

        await _service.SetStatusAsync(project.Id, ProjectStatus.Active);
        Assert.Equal(ProjectStatus.Active, project.Status);

        // No skipping draft→on_hold, and close-out is Phase 2.
        var draft = await _service.CreateAsync(_client.Id, "Draft", "GEO-006", _pm.Id, null, null);
        await Assert.ThrowsAsync<DomainException>(() => _service.SetStatusAsync(draft.Id, ProjectStatus.OnHold));
        await Assert.ThrowsAsync<DomainException>(() => _service.SetStatusAsync(project.Id, ProjectStatus.Closed));
    }

    [Fact]
    public async Task Create_rejects_inactive_client_or_manager()
    {
        _client.IsActive = false;
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.CreateAsync(_client.Id, "X", "GEO-007", _pm.Id, null, null));

        _client.IsActive = true;
        _pm.IsActive = false;
        await Assert.ThrowsAsync<DomainException>(() =>
            _service.CreateAsync(_client.Id, "X", "GEO-007", _pm.Id, null, null));
    }

    private Employee AddEmployee(string email)
    {
        var employee = new Employee { Id = Guid.NewGuid(), Email = email, DisplayName = email };
        _employees.Employees.Add(employee);
        return employee;
    }
}
