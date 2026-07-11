using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using ProjectTango.Domain;
using ProjectTango.Web.Controllers.Api.V1;

namespace ProjectTango.IntegrationTests;

/// <summary>The API exception filter maps service-layer failures to RFC 7807 problem+json so
/// controllers stay thin. DomainException messages are user-safe and pass through; authorization
/// failures return a generic 403 without leaking the required roles.</summary>
public sealed class ApiExceptionFilterTests
{
    private static ExceptionContext Context(Exception exception)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/time-entries";
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ExceptionContext(actionContext, new List<IFilterMetadata>()) { Exception = exception };
    }

    [Fact]
    public void DomainException_becomes_400_problem_with_message()
    {
        var context = Context(new DomainException("Hours must be in quarter-hour increments."));

        new ApiExceptionFilterAttribute().OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("Hours must be in quarter-hour increments.", problem.Detail);
        Assert.True(context.ExceptionHandled);
    }

    [Fact]
    public void Unauthorized_becomes_403_problem_without_leaking_roles()
    {
        var context = Context(new UnauthorizedAccessException("Requires one of: Operations Manager, Admin."));

        new ApiExceptionFilterAttribute().OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.DoesNotContain("Admin", problem.Detail);
        Assert.True(context.ExceptionHandled);
    }

    [Fact]
    public void Unexpected_exception_is_left_for_the_default_handler()
    {
        var context = Context(new InvalidOperationException("boom"));

        new ApiExceptionFilterAttribute().OnException(context);

        Assert.Null(context.Result);
        Assert.False(context.ExceptionHandled);
    }
}
