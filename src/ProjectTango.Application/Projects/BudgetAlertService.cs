using System.Globalization;
using Microsoft.Extensions.Logging;
using ProjectTango.Application.Common;
using ProjectTango.Application.Employees;
using ProjectTango.Application.TimeEntries;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Projects;

/// <summary>Evaluates project budget burn and emails alerts (design §6.2): the PM at each
/// configured threshold, and Operations Managers at 90%+ and on overrun. Each alert fires once
/// (deduped in <c>budget_alerts</c>) until the budget is revised, which re-arms them. Triggered
/// as a side effect of time-entry saves, approvals, and budget changes; failures are swallowed
/// and logged so notification never blocks the underlying action.</summary>
public class BudgetAlertService(
    IProjectRepository projects,
    IBudgetRepository budgets,
    IBudgetAlertRepository alerts,
    ITimeEntryRepository entries,
    IEmployeeRepository employees,
    IEmailSender email,
    ILogger<BudgetAlertService> logger) : IBudgetAlertService
{
    /// <summary>Ops is looped in from this burn level (design §6.2: "90%+ and overrun").</summary>
    private const int OpsThreshold = 90;

    private static readonly CultureInfo Usd = CultureInfo.GetCultureInfo("en-US");

    public async Task EvaluateAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            await EvaluateCoreAsync(projectId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Budget alert evaluation failed for project {ProjectId}.", projectId);
        }
    }

    public async Task OnBudgetChangedAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            var budget = await budgets.GetForProjectAsync(projectId, cancellationToken);
            if (budget is not null)
            {
                await alerts.ClearForBudgetAsync(budget.Id, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Re-arming budget alerts failed for project {ProjectId}.", projectId);
        }

        await EvaluateAsync(projectId, cancellationToken);
    }

    private async Task EvaluateCoreAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var budget = await budgets.GetForProjectAsync(projectId, cancellationToken);
        if (budget is null)
        {
            return; // nothing to measure against
        }

        var rows = await entries.GetBurnRowsAsync(projectId, cancellationToken);
        var status = BudgetBurn.Compute(budget, rows);

        var fired = await alerts.GetFiredKeysAsync(budget.Id, cancellationToken);
        var pending = new List<AlertIntent>();

        // Overall project burn (dollars and/or hours).
        foreach (var threshold in budget.AlertThresholds.OrderBy(t => t))
        {
            var key = $"pct:{threshold}";
            if (status.BurnPercent >= threshold && !fired.Contains(key))
            {
                pending.Add(new AlertIntent(key, threshold >= OpsThreshold, threshold, (decimal)status.BurnPercent, Role: null));
            }
        }

        if (status.IsOverBudget && !fired.Contains("overrun"))
        {
            pending.Add(new AlertIntent("overrun", NotifyOps: true, Threshold: null, (decimal)status.BurnPercent, Role: null));
        }

        // Per-role hours burn (e.g. Lead Developer past 90% of its 300h).
        foreach (var role in status.Roles.Where(r => r.AllocatedHours > 0))
        {
            foreach (var threshold in budget.AlertThresholds.OrderBy(t => t))
            {
                var key = $"role:{role.RoleId}:pct:{threshold}";
                if (role.PercentHours >= threshold && !fired.Contains(key))
                {
                    pending.Add(new AlertIntent(key, threshold >= OpsThreshold, threshold, (decimal)role.PercentHours, role));
                }
            }

            var overrunKey = $"role:{role.RoleId}:overrun";
            if (role.IsOver && !fired.Contains(overrunKey))
            {
                pending.Add(new AlertIntent(overrunKey, NotifyOps: true, Threshold: null, (decimal)role.PercentHours, role));
            }
        }

        if (pending.Count == 0)
        {
            return;
        }

        var project = await projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            return;
        }

        foreach (var intent in pending)
        {
            var recipients = await ResolveRecipientsAsync(project, intent.NotifyOps, cancellationToken);
            if (recipients.Count == 0)
            {
                // No one to notify yet (e.g. PM has no email). Leave it un-recorded so it can
                // fire once a recipient exists, rather than silently swallowing the alert.
                continue;
            }

            var message = intent.Role is null
                ? BuildOverallMessage(project, status, intent, recipients)
                : BuildRoleMessage(project, intent, recipients);
            await email.SendAsync(message, cancellationToken);

            await alerts.RecordAsync(new BudgetAlert
            {
                Id = Guid.NewGuid(),
                BudgetId = budget.Id,
                AlertKey = intent.Key,
                BurnPercent = intent.BurnPercent,
                NotifiedAt = DateTimeOffset.UtcNow,
            }, cancellationToken);
        }
    }

    /// <summary>One alert about to fire: its dedupe key, whether Ops is looped in, the threshold
    /// (null for overrun), the burn percent, and the role it concerns (null for project-overall).</summary>
    private sealed record AlertIntent(string Key, bool NotifyOps, int? Threshold, decimal BurnPercent, RoleBudget? Role);

    private async Task<IReadOnlyList<string>> ResolveRecipientsAsync(
        Project project, bool notifyOps, CancellationToken cancellationToken)
    {
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var pm = await employees.GetByIdAsync(project.ProjectManagerId, cancellationToken);
        if (pm is { IsActive: true } && !string.IsNullOrWhiteSpace(pm.Email))
        {
            recipients.Add(pm.Email);
        }

        if (notifyOps)
        {
            foreach (var opsEmail in await employees.GetActiveEmailsInRoleAsync(RoleNames.OperationsManager, cancellationToken))
            {
                recipients.Add(opsEmail);
            }
        }

        return recipients.ToList();
    }

    private static EmailMessage BuildOverallMessage(
        Project project, BudgetStatus status, AlertIntent intent, IReadOnlyList<string> recipients)
    {
        var headline = intent.Key == "overrun"
            ? $"over budget ({status.BurnPercent.ToString("0", Usd)}% burned)"
            : $"at {intent.Threshold}% of budget ({status.BurnPercent.ToString("0", Usd)}% burned)";

        var subject = $"[Budget] {project.Code} {headline}";

        var lines = new List<string> { $"{project.Code} — {project.Name} is {headline}.", "" };

        if (status.AmountBudget is not null)
        {
            var remaining = status.RemainingValue ?? 0m;
            lines.Add($"Dollars: {status.SpentValue.ToString("C0", Usd)} of {status.AmountBudget.Value.ToString("C0", Usd)} spent "
                + (remaining >= 0
                    ? $"({remaining.ToString("C0", Usd)} remaining)"
                    : $"({(-remaining).ToString("C0", Usd)} over)"));
        }

        if (status.HoursBudget is not null)
        {
            var remaining = status.RemainingHours ?? 0m;
            lines.Add($"Hours: {status.SpentHours:0.##} of {status.HoursBudget.Value:0.##} spent "
                + (remaining >= 0 ? $"({remaining:0.##} remaining)" : $"({-remaining:0.##} over)"));
        }

        if (status.PendingValue > 0)
        {
            lines.Add($"Plus {status.PendingValue.ToString("C0", Usd)} pending (open, not yet approved).");
        }

        return new EmailMessage(recipients, subject, string.Join("\n", lines));
    }

    private static EmailMessage BuildRoleMessage(
        Project project, AlertIntent intent, IReadOnlyList<string> recipients)
    {
        var role = intent.Role!;
        var headline = intent.Key.EndsWith(":overrun")
            ? $"over its hours budget ({role.PercentHours.ToString("0", Usd)}%)"
            : $"at {intent.Threshold}% of its hours budget";

        var subject = $"[Budget] {project.Code} — {role.RoleName} {headline}";

        var remaining = role.RemainingHours;
        var lines = new List<string>
        {
            $"{project.Code} — {project.Name}: the {role.RoleName} role is {headline}.",
            "",
            $"Hours: {role.SpentHours:0.##} of {role.AllocatedHours:0.##} spent "
                + (remaining >= 0 ? $"({remaining:0.##} remaining)" : $"({-remaining:0.##} over)"),
        };

        if (role.PendingHours > 0)
        {
            lines.Add($"Plus {role.PendingHours:0.##} hrs pending (open, not yet approved).");
        }

        return new EmailMessage(recipients, subject, string.Join("\n", lines));
    }
}
