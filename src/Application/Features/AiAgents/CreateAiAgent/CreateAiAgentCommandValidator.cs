using FluentValidation;

namespace Application.Features.AiAgents.CreateAiAgent;

public class CreateAiAgentCommandValidator : AbstractValidator<CreateAiAgentCommand>
{
    public CreateAiAgentCommandValidator()
    {
        RuleFor(command => command.UserId)
            .NotEmpty().WithMessage("User ID is required.");

        RuleFor(command => command.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters.");

        RuleFor(command => command.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters.");

        RuleFor(command => command.SystemPrompt)
            .NotEmpty().WithMessage("System prompt is required.")
            .MaximumLength(2000).WithMessage("System prompt cannot exceed 2000 characters.");

        RuleFor(command => command.AiModelId)
            .NotEmpty().WithMessage("AI Model ID is required.");

        RuleFor(command => command.Categories)
            .Must(categories => categories == null || categories.Count <= 5)
            .WithMessage("You can select up to 5 categories.");

        RuleFor(command => command.ModelParameters)
            .Must((command, parameters) => !command.AssignCustomModelParameters || !string.IsNullOrWhiteSpace(parameters))
            .When(command => command.AssignCustomModelParameters)
            .WithMessage("Model parameters must be provided when custom parameters are enabled.");

        RuleFor(command => command.Plugins)
            .Must(plugins => plugins == null || plugins.Count <= 10)
            .WithMessage("You can add up to 10 plugins.");
    }
}
