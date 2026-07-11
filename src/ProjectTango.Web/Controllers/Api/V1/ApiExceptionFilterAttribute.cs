using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ProjectTango.Domain;

namespace ProjectTango.Web.Controllers.Api.V1;

/// <summary>Translates service-layer exceptions into RFC 7807 problem+json for API clients
/// (design-doc §7). The MVC/Razor controllers catch <see cref="DomainException"/> themselves and
/// re-render a view, so this filter is applied only to <see cref="ApiControllerBase"/>.
/// <see cref="DomainException"/> messages are user-safe by contract; authorization failures return
/// a generic 403 so policy details are not leaked to the caller.</summary>
public sealed class ApiExceptionFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        ProblemDetails? problem = context.Exception switch
        {
            UnauthorizedAccessException => new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = "You do not have permission to perform this action.",
            },
            DomainException ex => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Request could not be completed",
                Detail = ex.Message,
            },
            _ => null,
        };

        if (problem is null)
        {
            return; // Not a known service-layer failure — let the default handler deal with it.
        }

        problem.Instance = context.HttpContext.Request.Path;
        context.Result = new ObjectResult(problem)
        {
            StatusCode = problem.Status,
            ContentTypes = { "application/problem+json" },
        };
        context.ExceptionHandled = true;
    }
}
