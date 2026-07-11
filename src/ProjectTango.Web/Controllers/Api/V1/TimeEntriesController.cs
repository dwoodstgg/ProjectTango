using Microsoft.AspNetCore.Mvc;
using ProjectTango.Application.TimeEntries;

namespace ProjectTango.Web.Controllers.Api.V1;

/// <summary>Write-side time entry API for the signed-in employee (design rules 5–7). Delegates to
/// the same <see cref="TimeEntryService"/> the Razor timesheet grid uses — auto-approval,
/// open-window and assignment checks all apply identically. Domain-rule violations surface as
/// problem+json via <see cref="ApiExceptionFilterAttribute"/>.</summary>
[Route("api/v1/time-entries")]
public sealed class TimeEntriesController(TimeEntryService timeEntries) : ApiControllerBase
{
    /// <summary>Records hours for one (project, day) cell for the signed-in employee, creating or
    /// updating the entry. Zero hours clears the cell and returns 204.</summary>
    [HttpPost]
    public async Task<ActionResult<TimeEntryDto>> Save(
        SaveTimeEntryRequest request, CancellationToken cancellationToken)
    {
        var entry = await timeEntries.SaveHoursAsync(
            request.ProjectId, request.Date, request.Hours, request.BillingRoleId, request.Notes,
            cancellationToken: cancellationToken);

        return entry is null ? NoContent() : Ok(TimeEntryDto.From(entry));
    }
}
