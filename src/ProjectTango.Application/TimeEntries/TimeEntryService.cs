using ProjectTango.Application.Clients;
using ProjectTango.Application.Common;
using ProjectTango.Application.Projects;
using ProjectTango.Application.Roles;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;

namespace ProjectTango.Application.TimeEntries;

/// <summary>Owner-side time entry: create/edit the single entry for a (project, day) cell
/// while its semi-monthly window is open (design rules 1, 5, 6, 7). No submission step;
/// back-dating within an open window is allowed. Entries are <b>auto-approved on save</b>
/// (the small-shop default) so hours flow straight to billable without a manual gate; a
/// billable entry with no rate card yet stays <c>open</c> until one is added. The owner keeps
/// editing an entry until its window closes or it is invoiced. hours_worked is owner-only;
/// hours_billed tracks worked unless an approver later adjusts it via <see cref="ApprovalService"/>.</summary>
public class TimeEntryService(
    ICurrentUser currentUser,
    IProjectRepository projects,
    IClientRepository clients,
    IAssignmentRepository assignments,
    IRoleRepository roles,
    ITimeEntryRepository entries,
    IRateCardRepository rateCards,
    ITimesheetPeriodRepository periods)
{
    /// <summary>Records <paramref name="hours"/> for the cell, creating or updating the open
    /// entry. Zero hours clears the cell (removes the open entry). Returns the entry, or null
    /// when the cell was cleared.</summary>
    public async Task<TimeEntry?> SaveHoursAsync(
        Guid projectId, DateOnly date, decimal hours, Guid billingRoleId, string? notes,
        Guid? employeeId = null, CancellationToken cancellationToken = default)
    {
        var ownerId = ResolveOwner(employeeId);
        ValidateHours(hours);

        var existing = await entries.GetByCellAsync(ownerId, projectId, date, cancellationToken);

        if (hours == 0)
        {
            if (existing is not null)
            {
                await RemoveInternalAsync(existing, cancellationToken);
            }

            return null;
        }

        if (existing is { Status: TimeEntryStatus.Invoiced })
        {
            throw new DomainException(
                "This entry is on an invoice — the invoice must be voided before it can change.");
        }

        var project = await projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");
        RequireProjectAcceptsTime(project);
        await RequireOpenWindowAsync(date, cancellationToken);
        await RequireActiveAssignmentAsync(project.Id, ownerId, date, cancellationToken);

        var role = await roles.GetByIdAsync(billingRoleId, cancellationToken)
            ?? throw new DomainException("Unknown billing role.");
        if (!role.IsBillable)
        {
            throw new DomainException($"{role.Name} is not a billable role — it cannot be an entry's billing role.");
        }

        var client = await clients.GetByIdAsync(project.ClientId, cancellationToken)
            ?? throw new DomainException("Unknown client.");
        var isBillable = !client.IsInternal;

        // Billable time must carry a work description (it lands on the invoice). Internal
        // leave/admin time is exempt.
        if (isBillable && string.IsNullOrWhiteSpace(notes))
        {
            throw new DescriptionRequiredException();
        }

        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        if (existing is not null)
        {
            existing.HoursWorked = hours;
            existing.HoursBilled = hours; // stays equal to worked until an approver adjusts it
            existing.BillingRoleId = billingRoleId;
            existing.Notes = notes;
            existing.IsBillable = isBillable;
            await AutoApproveAsync(existing, cancellationToken);
            await entries.UpdateAsync(existing, cancellationToken);
            return existing;
        }

        var entry = new TimeEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            EmployeeId = ownerId,
            BillingRoleId = billingRoleId,
            EntryDate = date,
            HoursWorked = hours,
            HoursBilled = hours,
            Notes = notes,
            IsBillable = isBillable,
            Status = TimeEntryStatus.Open,
        };
        await AutoApproveAsync(entry, cancellationToken);
        await entries.AddAsync(entry, cancellationToken);
        return entry;
    }

    /// <summary>Auto-approves the entry on save — the small-shop default that removes the manual
    /// approval step. Approval is a billing decision, so a billable entry can only auto-approve
    /// once a rate card covers its (project, billing role, date) (design rule 3); until then it
    /// stays <c>open</c> and shows up in the approval queue. Non-billable (internal/leave) time
    /// always auto-approves. The manual path (<see cref="ApprovalService"/>) stays available for
    /// adjusting hours_billed (worked 8, bill 6) or returning an entry to open.</summary>
    private async Task AutoApproveAsync(TimeEntry entry, CancellationToken cancellationToken)
    {
        if (entry.IsBillable)
        {
            var rate = await rateCards.ResolveAsync(entry.ProjectId, entry.BillingRoleId, entry.EntryDate, cancellationToken);
            if (rate is null)
            {
                entry.Status = TimeEntryStatus.Open;
                entry.ApprovedById = null;
                entry.ApprovedAt = null;
                return;
            }
        }

        entry.Status = TimeEntryStatus.Approved;
        entry.ApprovedById = currentUser.EmployeeId;
        entry.ApprovedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Removes an open entry (clearing a cell). Owner-only edit rules apply.</summary>
    public async Task DeleteAsync(Guid entryId, CancellationToken cancellationToken = default)
    {
        var entry = await entries.GetAsync(entryId, cancellationToken)
            ?? throw new DomainException("Unknown time entry.");
        ResolveOwner(entry.EmployeeId);
        await RemoveInternalAsync(entry, cancellationToken);
    }

    private async Task RemoveInternalAsync(TimeEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Status == TimeEntryStatus.Invoiced)
        {
            throw new DomainException("This entry is on an invoice — the invoice must be voided before it can be removed.");
        }

        await RequireOpenWindowAsync(entry.EntryDate, cancellationToken);
        await entries.DeleteAsync(entry.Id, cancellationToken);
    }

    /// <summary>The owner is the current user; Ops/Admin may enter time on someone's behalf.</summary>
    private Guid ResolveOwner(Guid? employeeId)
    {
        var me = currentUser.EmployeeId ?? throw new UnauthorizedAccessException("No signed-in employee.");
        var ownerId = employeeId ?? me;
        if (ownerId != me)
        {
            currentUser.RequireAny(RoleNames.OperationsManager);
        }

        return ownerId;
    }

    private static void RequireProjectAcceptsTime(Project project)
    {
        if (project.Status is ProjectStatus.Closed or ProjectStatus.Archived)
        {
            throw new DomainException($"Project {project.Code} is {DbStatus(project.Status)} — it no longer accepts new time.");
        }
    }

    private async Task RequireOpenWindowAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var window = SemiMonthlyPeriod.Containing(date);
        var period = await periods.GetByStartAsync(window.Start, cancellationToken);
        if (period is { Status: TimesheetPeriodStatus.Closed })
        {
            throw new DomainException(
                $"The {window.Start:yyyy-MM-dd}–{window.End:yyyy-MM-dd} timesheet window is closed. " +
                "Ops must reopen it before this day can change.");
        }
    }

    private async Task RequireActiveAssignmentAsync(Guid projectId, Guid employeeId, DateOnly date, CancellationToken cancellationToken)
    {
        var assignment = await assignments.GetByProjectAndEmployeeAsync(projectId, employeeId, cancellationToken);
        if (assignment is null || !assignment.IsActiveOn(date))
        {
            throw new DomainException("Time can only be logged on a project the employee is actively assigned to on that date.");
        }
    }

    private static void ValidateHours(decimal hours)
    {
        if (hours < 0)
        {
            throw new DomainException("Hours cannot be negative.");
        }

        if (hours > 24)
        {
            throw new DomainException("A single entry cannot exceed 24 hours.");
        }

        if (hours * 4m != Math.Truncate(hours * 4m))
        {
            throw new DomainException("Hours must be in quarter-hour (0.25) increments.");
        }
    }

    private static string DbStatus(ProjectStatus status) => status.ToString().ToLowerInvariant();
}
