using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProjectTango.Web.Controllers.Api.V1;

[ApiController]
[Route("api/v1/meta")]
public class MetaController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get() => Ok(new
    {
        name = "ProjectTango",
        apiVersion = "v1",
    });
}
