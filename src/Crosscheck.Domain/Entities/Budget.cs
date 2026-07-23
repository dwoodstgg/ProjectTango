using Crosscheck.Domain.Enums;

namespace Crosscheck.Domain.Entities;

/// <summary>A project's budget (design-doc §5.2). Constrains a dollar <see cref="Amount"/>,
/// an <see cref="Hours"/> cap, or both. Budget rows are updated in place; every change is
/// recorded as a <see cref="BudgetRevision"/>. Hitting a budget never changes project status
/// — overrun is expected and only flagged (design rule 9).</summary>
public class Budget
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>The project's type when this budget was last saved — the budget row
    /// mirrors it so revision history reads correctly if the project changes type.</summary>
    public ProjectType Type { get; set; }

    /// <summary>Dollar ceiling. Null when the budget only caps hours. For a fixed-rate
    /// project this is the contract amount; for a service contract, the total contract
    /// amount over its timeframe (any monthly breakdown is derived at reporting time).</summary>
    public decimal? Amount { get; set; }

    /// <summary>Hours ceiling. Null when the budget only caps dollars.</summary>
    public decimal? Hours { get; set; }

    /// <summary>Percent-of-burn points that notify the PM, e.g. {50, 75, 90}.</summary>
    public int[] AlertThresholds { get; set; } = [50, 75, 90];

    /// <summary>Optional per-role hour allocations (e.g. Lead Developer 300h, PM 10h). When
    /// present and no overall <see cref="Hours"/> is set explicitly, the overall hours budget
    /// defaults to their sum.</summary>
    public List<BudgetRoleAllocation> RoleAllocations { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
