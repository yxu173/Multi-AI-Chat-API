using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Admin.SubscriptionPlans.UpdateSubscriptionPlan;

internal sealed class UpdateSubscriptionPlanCommandHandler : ICommandHandler<UpdateSubscriptionPlanCommand, bool>
{
    private readonly ISubscriptionPlanRepository _subscriptionPlanRepository;

    public UpdateSubscriptionPlanCommandHandler(ISubscriptionPlanRepository subscriptionPlanRepository)
    {
        _subscriptionPlanRepository = subscriptionPlanRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(UpdateSubscriptionPlanCommand command, CancellationToken ct)
    {
        var subscriptionPlan = await _subscriptionPlanRepository.GetByIdAsync(command.SubscriptionPlanId, ct);
        if (subscriptionPlan == null)
        {
            return Result.Failure<bool>(Error.NotFound(
                "SubscriptionPlan.NotFound",
                $"Subscription plan with ID {command.SubscriptionPlanId} does not exist"));
        }

        var name = command.Name ?? subscriptionPlan.Name;
        var description = command.Description ?? subscriptionPlan.Description;
        var maxRequestsPerDay = command.MaxRequestsPerDay ?? subscriptionPlan.MaxRequestsPerDay;
        var maxTokensPerRequest = command.MaxTokensPerRequest ?? subscriptionPlan.MaxTokensPerRequest;
        var monthlyPrice = command.MonthlyPrice ?? subscriptionPlan.MonthlyPrice;

        subscriptionPlan.Update(
            name, 
            description, 
            maxRequestsPerDay, 
            maxTokensPerRequest, 
            monthlyPrice);

        if (command.IsActive.HasValue)
        {
            subscriptionPlan.SetActive(command.IsActive.Value);
        }

        await _subscriptionPlanRepository.UpdateAsync(subscriptionPlan, ct);

        return Result.Success(true);
    }
}
