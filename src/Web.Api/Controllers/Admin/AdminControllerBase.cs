using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Api.Controllers;

namespace Web.Api.Controllers.Admin;

//[Authorize(Roles = "Admin")]
public abstract class AdminControllerBase : BaseController
{
}
