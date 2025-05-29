using Application.Abstractions.Messaging;
using Domain.Aggregates.Admin;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.Admin.SubscriptionPlans.CreateSubscriptionPlan;

internal sealed class CreateSubscriptionPlanCommandHandler : ICommandHandler<CreateSubscriptionPlanCommand, System.Guid>
{
    private readonly ISubscriptionPlanRepository _subscriptionPlanRepository;

    public CreateSubscriptionPlanCommandHandler(ISubscriptionPlanRepository subscriptionPlanRepository)
    {
        _subscriptionPlanRepository = subscriptionPlanRepository;
    }

    public async Task<Result<System.Guid>> ExecuteAsync(CreateSubscriptionPlanCommand command, CancellationToken ct)
    {
        var subscriptionPlan = SubscriptionPlan.Create(
            command.Name,
            command.Description,
            command.MaxRequestsPerDay,
            command.MaxTokensPerRequest,
            command.MonthlyPrice);

        await _subscriptionPlanRepository.AddAsync(subscriptionPlan, ct);

        return Result.Success(subscriptionPlan.Id);
    }
}
