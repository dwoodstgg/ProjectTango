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
}
