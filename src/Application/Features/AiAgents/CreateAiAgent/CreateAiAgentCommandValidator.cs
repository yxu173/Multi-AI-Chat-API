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

       
        RuleFor(command => command.AiModelId)
            .NotEmpty().WithMessage("AI Model ID is required.");

        RuleFor(command => command.Categories)
            .Must(categories => categories == null || categories.Count <= 5)
            .WithMessage("You can select up to 5 categories.");
        
        RuleFor(command => command.Plugins)
            .Must(plugins => plugins == null || plugins.Count <= 10)
            .WithMessage("You can add up to 10 plugins.");
    }
}
