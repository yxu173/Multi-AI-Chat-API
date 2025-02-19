namespace Web.Api.Contracts.Identity;

public record RegisterCreate(
    string FirstName,
    string LastName,
    string Email,
    string UserName,
    string Password);