using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Web.Api.Controllers;
[ApiController]
[Route("api/[controller]")]
public class BaseController : ControllerBase
{
    protected Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
}