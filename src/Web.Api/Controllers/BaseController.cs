using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Web.Api.Controllers;
[ApiController]
[Route("api/[controller]")]
public class BaseController : ControllerBase
{
    protected Guid UserId 
    { 
        get 
        {
            var input = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (input == null) { throw new ArgumentNullException(nameof(input)); }
            if (!Guid.TryParse(input, out Guid parsedGuid)) { throw new FormatException("Invalid GUID format."); }
            return parsedGuid;
        } 
    }
}