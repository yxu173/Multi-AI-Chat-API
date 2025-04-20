using Domain.ValueObjects;

namespace Web.Api.Contracts.Prompts;

public sealed record CreatePromptRequest(
    string Title,
    string Description,
    string Content,
    IEnumerable<string> Tags);