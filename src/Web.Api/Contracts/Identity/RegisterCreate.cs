namespace Web.Api.Contracts.Identity;

public record RegisterCreate(
    string Email,
    string UserName,
    string Password);