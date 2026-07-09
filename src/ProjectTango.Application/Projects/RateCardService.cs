using ProjectTango.Application.Common;
using ProjectTango.Application.Roles;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Projects;

/// <summary>Rate card management. A rate change closes the current open row the day
/// before the new rate starts and inserts a new row — history is never edited, so
/// past time entries are never re-priced.</summary>
public class RateCardService(
    ICurrentUser currentUser,
    IProjectRepository projects,
    IRateCardRepository rateCards,
    IRoleRepository roles,
    IAuditLog audit)
{
    public async Task<IReadOnlyList<RateCardSummary>> ListForProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        currentUser.RequireAny(RoleNames.OperationsManager, RoleNames.ProjectManager);
        return await rateCards.GetForProjectAsync(projectId, cancellationToken);
    }

    public async Task SetRateAsync(
        Guid projectId, Guid roleId, decimal hourlyRate, DateOnly effectiveFrom,
        CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");
        var adminOverride = currentUser.RequireCanManage(project);

        var role = await roles.GetByIdAsync(roleId, cancellationToken)
            ?? throw new DomainException("Unknown role.");
        if (!role.IsBillable)
        {
            throw new DomainException($"{role.Name} is not a billable role — rates only apply to billable roles.");
        }

        if (hourlyRate < 0)
        {
            throw new DomainException("Hourly rate cannot be negative.");
        }

        var existing = await rateCards.GetForRoleAsync(projectId, roleId, cancellationToken);
        var latest = existing.MaxBy(r => r.EffectiveFrom);
        if (latest is not null && effectiveFrom <= latest.EffectiveFrom)
        {
            throw new DomainException(
                $"A rate starting {latest.EffectiveFrom:yyyy-MM-dd} already exists — rate changes must start after it. " +
                "Historical entries are never re-priced.");
        }

        if (latest is { EffectiveTo: null })
        {
            await rateCards.CloseAsync(latest.Id, effectiveFrom.AddDays(-1), cancellationToken);
        }

        var rateCard = new ProjectRateCard
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            RoleId = roleId,
            HourlyRate = hourlyRate,
            EffectiveFrom = effectiveFrom,
        };
        await rateCards.AddAsync(rateCard, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "rate.set", "project", projectId,
            new
            {
                Role = role.Name,
                HourlyRate = hourlyRate,
                EffectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
                Superseded = latest?.HourlyRate,
                adminOverride,
            }), cancellationToken);
    }

    /// <summary>Fixes a data-entry mistake on an existing rate row (wrong amount and/or
    /// start date) in place. This is NOT a rate change — it only touches a row that has not
    /// yet priced any invoiced time, so no billed history is ever re-priced.</summary>
    public async Task CorrectRateAsync(
        Guid projectId, Guid rateCardId, decimal hourlyRate, DateOnly effectiveFrom,
        CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");
        var adminOverride = currentUser.RequireCanManage(project);

        if (hourlyRate < 0)
        {
            throw new DomainException("Hourly rate cannot be negative.");
        }

        var rate = await rateCards.GetByIdAsync(rateCardId, cancellationToken);
        if (rate is null || rate.ProjectId != projectId)
        {
            throw new DomainException("Unknown rate.");
        }

        if (await rateCards.HasInvoicedTimeAsync(projectId, rate.RoleId, rate.EffectiveFrom, rate.EffectiveTo, cancellationToken))
        {
            throw new DomainException(
                "This rate has already priced invoiced time and can no longer be corrected — void the invoice instead.");
        }

        var ordered = (await rateCards.GetForRoleAsync(projectId, rate.RoleId, cancellationToken))
            .OrderBy(r => r.EffectiveFrom).ToList();
        var index = ordered.FindIndex(r => r.Id == rate.Id);
        var prior = index > 0 ? ordered[index - 1] : null;
        var next = index >= 0 && index < ordered.Count - 1 ? ordered[index + 1] : null;

        if (prior is not null && effectiveFrom <= prior.EffectiveFrom)
        {
            throw new DomainException(
                $"Effective date must be after the previous rate ({prior.EffectiveFrom:yyyy-MM-dd}).");
        }
        if (next is not null && effectiveFrom >= next.EffectiveFrom)
        {
            throw new DomainException(
                $"Effective date must be before the next rate ({next.EffectiveFrom:yyyy-MM-dd}).");
        }
        if (rate.EffectiveTo is not null && effectiveFrom > rate.EffectiveTo)
        {
            throw new DomainException("Effective date must be on or before this rate's end date.");
        }

        // Keep history contiguous: if the predecessor was closed exactly to abut this row,
        // move its end so it still sits just before the new start.
        Guid? priorRowId = null;
        DateOnly? priorEffectiveTo = null;
        if (prior is not null && effectiveFrom != rate.EffectiveFrom
            && prior.EffectiveTo == rate.EffectiveFrom.AddDays(-1))
        {
            priorRowId = prior.Id;
            priorEffectiveTo = effectiveFrom.AddDays(-1);
        }

        await rateCards.CorrectAsync(rate.Id, hourlyRate, effectiveFrom, priorRowId, priorEffectiveTo, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "rate.correct", "project", projectId,
            new
            {
                RateCardId = rate.Id,
                FromRate = rate.HourlyRate,
                ToRate = hourlyRate,
                FromEffective = rate.EffectiveFrom.ToString("yyyy-MM-dd"),
                ToEffective = effectiveFrom.ToString("yyyy-MM-dd"),
                adminOverride,
            }), cancellationToken);
    }

    /// <summary>Soft-deletes a mistaken rate row (as long as it hasn't priced invoiced
    /// time). If it was the current open row, the predecessor it closed is reopened.</summary>
    public async Task DeleteRateAsync(Guid projectId, Guid rateCardId, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken)
            ?? throw new DomainException("Unknown project.");
        var adminOverride = currentUser.RequireCanManage(project);

        var rate = await rateCards.GetByIdAsync(rateCardId, cancellationToken);
        if (rate is null || rate.ProjectId != projectId)
        {
            throw new DomainException("Unknown rate.");
        }

        if (await rateCards.HasInvoicedTimeAsync(projectId, rate.RoleId, rate.EffectiveFrom, rate.EffectiveTo, cancellationToken))
        {
            throw new DomainException(
                "This rate has already priced invoiced time and can no longer be removed — void the invoice instead.");
        }

        var ordered = (await rateCards.GetForRoleAsync(projectId, rate.RoleId, cancellationToken))
            .OrderBy(r => r.EffectiveFrom).ToList();
        var index = ordered.FindIndex(r => r.Id == rate.Id);
        var prior = index > 0 ? ordered[index - 1] : null;

        // Removing the current open row leaves the predecessor closed — reopen it so the
        // role keeps an active rate.
        Guid? reopenPriorRowId = null;
        if (rate.EffectiveTo is null && prior is not null && prior.EffectiveTo == rate.EffectiveFrom.AddDays(-1))
        {
            reopenPriorRowId = prior.Id;
        }

        await rateCards.SoftDeleteAsync(rate.Id, reopenPriorRowId, cancellationToken);

        await audit.WriteAsync(new AuditEvent(
            currentUser.EmployeeId, "rate.delete", "project", projectId,
            new
            {
                RateCardId = rate.Id,
                rate.HourlyRate,
                EffectiveFrom = rate.EffectiveFrom.ToString("yyyy-MM-dd"),
                ReopenedPrior = reopenPriorRowId,
                adminOverride,
            }), cancellationToken);
    }
}
