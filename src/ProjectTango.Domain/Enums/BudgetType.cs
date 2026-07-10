namespace ProjectTango.Domain.Enums;

/// <summary>What a project budget constrains (design-doc §5.2). Stored as snake_case text
/// ('fixed_fee', 'time_and_materials_cap', 'hours_cap') with a CHECK constraint.</summary>
public enum BudgetType
{
    /// <summary>A fixed price for the engagement; the dollar amount is the ceiling.</summary>
    FixedFee,

    /// <summary>Time &amp; materials with a not-to-exceed dollar cap.</summary>
    TimeAndMaterialsCap,

    /// <summary>A cap on hours rather than dollars.</summary>
    HoursCap,
}
