using ProjectTango.Application.Common;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;
using ProjectTango.Domain.Enums;

namespace ProjectTango.Application.TimeEntries;

/// <summary>Ops/Admin control of the semi-monthly edit windows (design-doc §6.1). Closing a
/// window locks owner edits for its dates; reopening restores them. Both are audited. A
/// window is identified by any date it contains — the service snaps to its bounds.</summary>
public class TimesheetPeriodService(
    ICurrentUser currentUser,
    ITimesheetPeriodRepository periods,
    IAuditLog audit)
{
    public async Task<IReadOnlyList<TimesheetPeriod>> ListInRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await periods.GetInRangeAsync(from, to, cancellationToken);

    /// <summary>Is the window covering <paramref name="date"/> closed?</summary>
    public async Task<bool> IsClosedAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var window = SemiMonthlyPeriod.Containing(date);
        var period = await periods.GetByStartAsync(window.Start, cancellationToken);
        return period is { Status: TimesheetPeriodStatus.Closed };
    }

    public Task CloseAsync(DateOnly dateInWindow, CancellationToken cancellationToken = default) =>
        SetStatusAsync(dateInWindow, TimesheetPeriodStatus.Closed, cancellationToken);

    public Task ReopenAsync(DateOnly dateInWindow, CancellationToken cancellationToken = default) =>
        SetStatusAsync(dateInWindow, TimesheetPeriodStatus.Open, cancellationToken);

    private async Task SetStatusAsync(DateOnly dateInWindow, TimesheetPeriodStatus target, CancellationToken cancellationToken)
    {
        var adminOverride = currentUser.RequireAny(RoleNames.OperationsManager);
        var window = SemiMonthlyPeriod.Containing(dateInWindow);
        var period = await periods.GetByStartAsync(window.Start, cancellationToken);

        if (period is not null && period.Status == target)
        {
            throw new DomainException($"The {window.Start:yyyy-MM-dd}–{window.End:yyyy-MM-dd} window is already {DbStatus(target)}.");
        }

        var closing = target == TimesheetPeriodStatus.Closed;
        period ??= new TimesheetPeriod { Id = Guid.NewGuid(), PeriodStart = window.Start, PeriodEnd = window.End };
        period.Status = target;
        period.ClosedById = closing ? currentUser.EmployeeId : null;
        period.ClosedAt = closing ? DateTimeOffset.UtcNow : null;
        await periods.UpsertAsync(period, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, closing ? "timesheet_period.closed" : "timesheet_period.reopened",
            "timesheet_period", period.Id,
            new
            {
                PeriodStart = window.Start.ToString("yyyy-MM-dd"),
                PeriodEnd = window.End.ToString("yyyy-MM-dd"),
                adminOverride,
            }), cancellationToken);
    }

    private static string DbStatus(TimesheetPeriodStatus status) => status.ToString().ToLowerInvariant();
}
