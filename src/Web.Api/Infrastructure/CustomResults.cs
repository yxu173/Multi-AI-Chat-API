using SharedKernel;

namespace Web.Api.Infrastructure;

public static class CustomResults
{
    public static IResult Problem(Result result)
    {
        var statusCode = result.Error.Code switch
        {
            "Users.NotFoundByEmail" => StatusCodes.Status404NotFound,
            "Users.InvalidCredentials" => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status400BadRequest
        };

        return Results.Problem(
            statusCode: statusCode,
            title: result.Error.Code,
            detail: result.Error.Description,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.4"
        );
    }

    public static IResult Problem<T>(Result<T> result)
    {
        return Problem(result as Result);
    }
}