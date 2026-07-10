using ProjectTango.Application.Common;
using ProjectTango.Application.Projects;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;

namespace ProjectTango.Application.TimeEntries;

/// <summary>Approver-side workflow. Approval is a billing decision: the approver may set
/// hours_billed (worked 8, bill 6) but never touches hours_worked (design rules 3, 6). A
/// billable entry cannot be approved until its (project, billing role, date) has a rate
/// card row (design rule 3). Un-approval returns an entry to <c>open</c> for correction.</summary>
public class ApprovalService(
    ICurrentUser currentUser,
    IProjectRepository projects,
    ITimeEntryRepository entries,
    IRateCardRepository rateCards,
    IAuditLog audit,
    IBudgetAlertService budgetAlerts)
{
    public async Task<IReadOnlyList<ApprovalEntry>> ListForApprovalAsync(
        Guid projectId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");
        currentUser.RequireCanManage(project);
        return await entries.GetForProjectRangeAsync(projectId, from, to, cancellationToken);
    }

    /// <summary>Approves an open entry, optionally overriding hours_billed. Billable entries
    /// require a rate card for (project, billing role, date) first.</summary>
    public async Task ApproveAsync(
        Guid entryId, decimal? billedHours, CancellationToken cancellationToken = default)
    {
        var (entry, project) = await LoadForManageAsync(entryId, cancellationToken);
        var adminOverride = currentUser.RequireCanManage(project);

        if (entry.Status != TimeEntryStatus.Open)
        {
            throw new DomainException("Only open entries can be approved.");
        }

        var hoursBilled = billedHours ?? entry.HoursBilled;
        ValidateBilled(hoursBilled, entry.HoursWorked);

        if (entry.IsBillable)
        {
            var rate = await rateCards.ResolveAsync(entry.ProjectId, entry.BillingRoleId, entry.EntryDate, cancellationToken);
            if (rate is null)
            {
                throw new DomainException(
                    "No rate card covers this entry's (project, billing role) on its date — add a rate before approving.");
            }
        }

        entry.HoursBilled = hoursBilled;
        entry.Status = TimeEntryStatus.Approved;
        entry.ApprovedById = currentUser.EmployeeId;
        entry.ApprovedAt = DateTimeOffset.UtcNow;
        await entries.UpdateAsync(entry, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "timeentry.approved", "time_entry", entry.Id,
            new
            {
                entry.ProjectId,
                entry.EmployeeId,
                Date = entry.EntryDate.ToString("yyyy-MM-dd"),
                entry.HoursWorked,
                HoursBilled = hoursBilled,
                adminOverride,
            }), cancellationToken);

        // Approving raises billable burn — re-check budget thresholds.
        await budgetAlerts.EvaluateAsync(entry.ProjectId, cancellationToken);
    }

    /// <summary>Approves several open entries at their current hours_billed (bulk per period).
    /// Returns the count approved.</summary>
    public async Task<int> ApproveManyAsync(IEnumerable<Guid> entryIds, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var id in entryIds)
        {
            await ApproveAsync(id, billedHours: null, cancellationToken);
            count++;
        }

        return count;
    }

    /// <summary>Returns an approved entry to <c>open</c> (the "return for correction" path;
    /// the comment is recorded in the audit log). Invoiced entries cannot be un-approved —
    /// void the invoice instead.</summary>
    public async Task UnapproveAsync(Guid entryId, string? comment = null, CancellationToken cancellationToken = default)
    {
        var (entry, project) = await LoadForManageAsync(entryId, cancellationToken);
        var adminOverride = currentUser.RequireCanManage(project);

        if (entry.Status == TimeEntryStatus.Invoiced)
        {
            throw new DomainException("Invoiced entries cannot be un-approved — void the invoice instead.");
        }

        if (entry.Status != TimeEntryStatus.Approved)
        {
            throw new DomainException("Only approved entries can be un-approved.");
        }

        entry.Status = TimeEntryStatus.Open;
        entry.ApprovedById = null;
        entry.ApprovedAt = null;
        await entries.UpdateAsync(entry, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "timeentry.unapproved", "time_entry", entry.Id,
            new { entry.ProjectId, entry.EmployeeId, Comment = comment, adminOverride }), cancellationToken);
    }

    private async Task<(TimeEntry Entry, Project Project)> LoadForManageAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var entry = await entries.GetAsync(entryId, cancellationToken)
            ?? throw new DomainException("Unknown time entry.");
        var project = await projects.GetByIdAsync(entry.ProjectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");
        return (entry, project);
    }

    private static void ValidateBilled(decimal billed, decimal worked)
    {
        if (billed < 0)
        {
            throw new DomainException("Billed hours cannot be negative.");
        }

        if (billed > worked)
        {
            throw new DomainException("Billed hours cannot exceed hours worked.");
        }

        if (billed * 4m != Math.Truncate(billed * 4m))
        {
            throw new DomainException("Billed hours must be in quarter-hour (0.25) increments.");
        }
    }
}
