using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ProjectTango.Application.TimeEntries;

namespace ProjectTango.Web.Controllers.Api.V1;

/// <summary>Read-side timesheet API for the signed-in employee — the "time entry on the go" slice
/// a mobile client needs (roadmap Phase 4). Reads only the caller's own data, so no role is
/// required; identity comes from the bearer token via <see cref="ApiControllerBase"/>. Calls the
/// same <see cref="TimesheetService"/> the Razor UI uses, so nothing is API-only.</summary>
[Route("api/v1/timesheet")]
public sealed class TimesheetApiController(TimesheetService timesheet) : ApiControllerBase
{
    /// <summary>The signed-in employee's timesheet rows and entries for a date range.</summary>
    [HttpGet]
    public async Task<ActionResult<MyTimesheetResponse>> GetMine(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid range",
                detail: "'to' must be on or after 'from'.");
        }

        var grid = await timesheet.GetMyRangeAsync(from, to, cancellationToken);
        return Ok(MyTimesheetResponse.From(grid));
    }
}
