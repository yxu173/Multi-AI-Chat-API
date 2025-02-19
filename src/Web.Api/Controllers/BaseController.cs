using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Web.Api.Controllers;
[ApiController]
[Route("api/[controller]")]
public class BaseController : ControllerBase
{
    private IMediator _mediatorInstance;
    protected IMediator _mediator => _mediatorInstance ??= HttpContext.RequestServices.GetService<IMediator>();
    protected string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
}