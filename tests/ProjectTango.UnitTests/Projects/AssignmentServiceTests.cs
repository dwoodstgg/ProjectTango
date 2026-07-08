using ProjectTango.Application.Projects;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.UnitTests.Fakes;

namespace ProjectTango.UnitTests.Projects;

public class AssignmentServiceTests
{
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeAssignmentRepository _assignments = new();
    private readonly FakeRoleRepository _roles = new();
    private readonly FakeEmployeeRepository _employees;
    private readonly FakeAuditLog _audit = new();
    private readonly AssignmentService _service;

    private readonly Project _project;
    private readonly Employee _dev;

    public AssignmentServiceTests()
    {
        _employees = new FakeEmployeeRepository(_roles);
        _service = new AssignmentService(_currentUser, _projects, _assignments, _employees, _roles, _audit);

        _project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Name = "P",
            Code = "GEO-001",
            ProjectManagerId = _currentUser.EmployeeId!.Value,
        };
        _projects.Projects.Add(_project);
        _dev = new Employee { Id = Guid.NewGuid(), Email = "dev@x", DisplayName = "Dev" };
        _employees.Employees.Add(_dev);
        _currentUser.Roles.Add(RoleNames.ProjectManager);
    }

    [Fact]
    public async Task Assign_creates_row_and_audits()
    {
        await _service.AssignAsync(_project.Id, _dev.Id, null, new DateOnly(2026, 7, 1));

        var assignment = Assert.Single(_assignments.Assignments);
        Assert.Equal(_dev.Id, assignment.EmployeeId);
        Assert.Null(assignment.EndDate);
        Assert.Single(_audit.Events, e => e.Action == "assignment.added");
    }

    [Fact]
    public async Task Duplicate_active_assignment_is_rejected()
    {
        await _service.AssignAsync(_project.Id, _dev.Id, null, null);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _service.AssignAsync(_project.Id, _dev.Id, null, null));

        Assert.Contains("already assigned", ex.Message);
    }

    [Fact]
    public async Task Reassigning_after_end_reopens_the_same_row()
    {
        await _service.AssignAsync(_project.Id, _dev.Id, null, null);
        var assignment = _assignments.Assignments.Single();
        await _service.EndAsync(assignment.Id, new DateOnly(2026, 7, 8));

        await _service.AssignAsync(_project.Id, _dev.Id, null, null);

        Assert.Single(_assignments.Assignments); // still one row
        Assert.Null(assignment.EndDate);
        Assert.Single(_audit.Events, e => e.Action == "assignment.reopened");
    }

    [Fact]
    public async Task End_before_start_is_rejected()
    {
        await _service.AssignAsync(_project.Id, _dev.Id, null, new DateOnly(2026, 7, 1));
        var assignment = _assignments.Assignments.Single();

        await Assert.ThrowsAsync<DomainException>(() =>
            _service.EndAsync(assignment.Id, new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public async Task Inactive_employee_cannot_be_assigned()
    {
        _dev.IsActive = false;

        await Assert.ThrowsAsync<DomainException>(() =>
            _service.AssignAsync(_project.Id, _dev.Id, null, null));
    }

    [Fact]
    public async Task Pm_of_other_project_cannot_assign()
    {
        _project.ProjectManagerId = Guid.NewGuid();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.AssignAsync(_project.Id, _dev.Id, null, null));
    }
}
