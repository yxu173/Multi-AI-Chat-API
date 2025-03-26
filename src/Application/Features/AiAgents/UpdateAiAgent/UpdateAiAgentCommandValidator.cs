using FluentValidation;

namespace Application.Features.AiAgents.UpdateAiAgent;

public class UpdateAiAgentCommandValidator : AbstractValidator<UpdateAiAgentCommand>
{
    public UpdateAiAgentCommandValidator()
    {
        RuleFor(x => x.AiAgentId)
            .NotEmpty().WithMessage("AiAgentId is required");

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required")
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters");

        RuleFor(x => x.AiModelId)
            .NotEmpty().WithMessage("AiModelId is required");
            
        When(x => x.AssignCustomModelParameters == true, () =>
        {
            RuleFor(x => x.Temperature)
                .InclusiveBetween(0, 2).When(x => x.Temperature.HasValue)
                .WithMessage("Temperature must be between 0 and 2");

            RuleFor(x => x.TopP)
                .InclusiveBetween(0, 1).When(x => x.TopP.HasValue)
                .WithMessage("TopP must be between 0 and 1");
        });
    }
} 