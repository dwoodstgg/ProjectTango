using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProjectTango.Web.Controllers.Api.V1;

/// <summary>Base for all <c>/api/v1</c> controllers. Authenticates via the Entra JWT bearer scheme
/// — the path mobile/desktop clients use (design-doc §4.1) — so the UI's cookie scheme is bypassed
/// and an unauthenticated call returns 401 rather than a login redirect. Returns JSON and maps
/// service-layer failures to problem+json via <see cref="ApiExceptionFilterAttribute"/>. Individual
/// controllers add their own <c>[Route("api/v1/...")]</c> and role requirements.</summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ApiExceptionFilter]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase;
