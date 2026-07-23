using Crosscheck.Application.Clients;
using Crosscheck.Application.Common;
using Crosscheck.Application.Employees;
using Crosscheck.Domain;
using Crosscheck.Domain.Entities;
using Crosscheck.Domain.Enums;

namespace Crosscheck.Application.Projects;

/// <summary>Project management. Ops manages all projects; a PM manages only projects
/// where they are the assigned PM; Admin bypasses (audit-flagged). Close-out/reopen
/// are Phase 2 — Phase 1 transitions stay within draft/active/on_hold.</summary>
public class ProjectAdminService(
    ICurrentUser currentUser,
    IProjectRepository projects,
    IClientRepository clients,
    IEmployeeRepository employees,
    Roles.IRoleRepository roles,
    IAuditLog audit)
{
    private static readonly Dictionary<ProjectStatus, ProjectStatus[]> AllowedTransitions = new()
    {
        [ProjectStatus.Draft] = [ProjectStatus.Active],
        [ProjectStatus.Active] = [ProjectStatus.OnHold],
        [ProjectStatus.OnHold] = [ProjectStatus.Active],
    };

    public async Task<IReadOnlyList<ProjectSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager, RoleNames.ProjectManager);
        return await projects.GetAllAsync(cancellationToken);
    }

    public async Task<Project?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager, RoleNames.ProjectManager);
        return await projects.GetByIdAsync(projectId, cancellationToken);
    }

    public async Task<IReadOnlyList<Client>> GetClientOptionsAsync(CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager, RoleNames.ProjectManager);
        return (await clients.GetAllAsync(cancellationToken)).Where(c => c.IsActive).ToList();
    }

    public async Task<IReadOnlyList<Employee>> GetManagerOptionsAsync(CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager, RoleNames.ProjectManager);
        return (await employees.GetAllAsync(cancellationToken))
            .Select(s => s.Employee)
            .Where(e => e.IsActive)
            .ToList();
    }

    public async Task<IReadOnlyList<Role>> GetBillableRoleOptionsAsync(CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager, RoleNames.ProjectManager);
        return (await roles.GetAllAsync(cancellationToken)).Where(r => r.IsBillable).ToList();
    }

    public async Task<Project> CreateAsync(
        Guid clientId, string name, string code, Guid projectManagerId, ProjectType type,
        DateOnly? startDate, DateOnly? endDate, ProjectBillingInput? billing = null,
        BreakdownLabel breakdownLabel = BreakdownLabel.Module,
        CancellationToken cancellationToken = default)
    {
        var adminOverride = currentUser.RequireAny(RoleNames.OperationsManager, RoleNames.ProjectManager);

        await ValidateAsync(clientId, projectManagerId, startDate, endDate, cancellationToken);

        code = code.Trim().ToUpperInvariant();
        if (await projects.GetByClientAndCodeAsync(clientId, code, cancellationToken) is not null)
        {
            throw new DomainException($"Project code {code} is already in use for this client.");
        }

        var project = new Project
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            Name = name.Trim(),
            Code = code,
            ProjectManagerId = projectManagerId,
            Type = type,
            StartDate = startDate,
            EndDate = endDate,
            BreakdownLabel = breakdownLabel,
        };
        ApplyBilling(project, billing);
        await projects.AddAsync(project, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "project.created", "project", project.Id,
            new { project.Code, project.Name, adminOverride }), cancellationToken);

        return project;
    }

    public async Task UpdateAsync(
        Guid projectId, Guid clientId, string name, string code, Guid projectManagerId, ProjectType type,
        DateOnly? startDate, DateOnly? endDate, ProjectBillingInput? billing = null,
        CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");

        var adminOverride = RequireCanManage(project);

        await ValidateAsync(clientId, projectManagerId, startDate, endDate, cancellationToken);

        code = code.Trim().ToUpperInvariant();
        var byCode = await projects.GetByClientAndCodeAsync(clientId, code, cancellationToken);
        if (byCode is not null && byCode.Id != projectId)
        {
            throw new DomainException($"Project code {code} is already in use for this client.");
        }

        project.ClientId = clientId;
        project.Name = name.Trim();
        project.Code = code;
        project.ProjectManagerId = projectManagerId;
        project.Type = type;
        project.StartDate = startDate;
        project.EndDate = endDate;
        ApplyBilling(project, billing);
        await projects.UpdateAsync(project, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "project.updated", "project", project.Id,
            new { project.Code, project.Name, Type = project.Type.ToString(), adminOverride }), cancellationToken);
    }

    /// <summary>Renames what the project calls its budget breakdown ("modules" vs
    /// "milestones"). Display only — applied on its own so the wording flips immediately,
    /// independent of the Details/Budget save.</summary>
    public async Task SetBreakdownLabelAsync(
        Guid projectId, BreakdownLabel breakdownLabel, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");

        RequireCanManage(project);

        if (project.BreakdownLabel == breakdownLabel)
        {
            return;
        }

        project.BreakdownLabel = breakdownLabel;
        await projects.UpdateAsync(project, cancellationToken);
    }

    /// <summary>Hard-deletes a project that was created by mistake — allowed only while no time
    /// has ever been logged on it (mirrors how a no-time assignment is hard-deleted rather than
    /// soft-deactivated). Setup data (rate cards, assignments, budget, modules) goes with it.</summary>
    public async Task DeleteAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");

        var adminOverride = RequireCanManage(project);

        if (await projects.HasTimeEntriesAsync(projectId, cancellationToken))
        {
            throw new DomainException(
                "Time has been logged on this project, so it cannot be deleted. Close it out instead.");
        }

        await projects.DeleteAsync(projectId, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "project.deleted", "project", projectId,
            new { project.Code, project.Name, adminOverride }), cancellationToken);
    }

    public async Task SetStatusAsync(Guid projectId, ProjectStatus target, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");

        var adminOverride = RequireCanManage(project);

        if (target is ProjectStatus.Closed or ProjectStatus.Archived)
        {
            throw new DomainException("Close-out and archiving ship in Phase 2 (with the pre-close checklist).");
        }

        if (!AllowedTransitions.TryGetValue(project.Status, out var allowed) || !allowed.Contains(target))
        {
            throw new DomainException($"Cannot move a {project.Status} project to {target}.");
        }

        var from = project.Status;
        await projects.SetStatusAsync(projectId, target, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "project.status_changed", "project", projectId,
            new { From = from.ToString(), To = target.ToString(), adminOverride }), cancellationToken);
    }

    private bool RequireCanManage(Project project) => currentUser.RequireCanManage(project);

    /// <summary>Copies the project's billing overrides. Null/blank fields stay null so they
    /// inherit the client's default (design decision 18). Terms are validated non-negative.</summary>
    private static void ApplyBilling(Project project, ProjectBillingInput? billing)
    {
        if (billing?.PaymentTermsDays is < 0)
        {
            throw new DomainException("Payment terms cannot be negative.");
        }

        project.BillingContactName = Normalize(billing?.ContactName);
        project.BillingContactEmail = Normalize(billing?.ContactEmail);
        project.BillingAddress = billing?.Address;
        project.PaymentTermsDays = billing?.PaymentTermsDays;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task ValidateAsync(
        Guid clientId, Guid projectManagerId, DateOnly? startDate, DateOnly? endDate, CancellationToken cancellationToken)
    {
        var client = await clients.GetByIdAsync(clientId, cancellationToken);
        if (client is null || !client.IsActive)
        {
            throw new DomainException("Client must exist and be active.");
        }

        var manager = await employees.GetByIdAsync(projectManagerId, cancellationToken);
        if (manager is null || !manager.IsActive)
        {
            throw new DomainException("Project manager must be an active employee.");
        }

        if (startDate is not null && endDate is not null && endDate < startDate)
        {
            throw new DomainException("End date cannot be before start date.");
        }
    }
}
