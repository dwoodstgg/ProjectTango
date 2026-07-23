namespace Crosscheck.Domain.Enums;

/// <summary>How a project is contracted and billed. Stored as snake_case text
/// ('hourly', 'fixed_rate', 'service_contract', 'internal') with a CHECK constraint.
/// Drives what the budget form asks for — the budget row mirrors the project's type
/// at save time.</summary>
public enum ProjectType
{
    /// <summary>Billed for hours worked at the project's rates. Budget (optional) is a
    /// dollar cap, an hours cap, or both.</summary>
    Hourly,

    /// <summary>An agreed price for the work. The budget is the contract amount; hours
    /// are internal effort budgeting.</summary>
    FixedRate,

    /// <summary>Runs for a set timeframe (project start–end) and bills monthly. Some pay
    /// a fixed monthly amount regardless of hours — the budget stores that monthly figure
    /// and sizes the contract total from it (monthly × months), so burn can swing month to
    /// month and average out over the contract.</summary>
    ServiceContract,

    /// <summary>Internal work — random assigned tasks. Never billed, carries no budget
    /// and no dollar amount; entries are non-billable and auto-approve without a rate.</summary>
    Internal,
}
