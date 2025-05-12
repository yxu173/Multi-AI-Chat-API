using Application.Abstractions.Messaging;
using Domain.Repositories;
using SharedKernel;

namespace Application.Features.Admin.UserSubscriptions.CancelUserSubscription;

internal sealed class CancelUserSubscriptionCommandHandler : ICommandHandler<CancelUserSubscriptionCommand, bool>
{
    private readonly IUserSubscriptionRepository _userSubscriptionRepository;

    public CancelUserSubscriptionCommandHandler(IUserSubscriptionRepository userSubscriptionRepository)
    {
        _userSubscriptionRepository = userSubscriptionRepository;
    }

    public async Task<Result<bool>> ExecuteAsync(CancelUserSubscriptionCommand command, CancellationToken ct)
    {
        var subscription = await _userSubscriptionRepository.GetByIdAsync(command.SubscriptionId, ct);
        if (subscription == null)
        {
            return Result.Failure<bool>(Error.NotFound(
                "UserSubscription.NotFound",
                $"Subscription with ID {command.SubscriptionId} does not exist"));
        }

        subscription.SetActive(false);
        await _userSubscriptionRepository.UpdateAsync(subscription, ct);

        return Result.Success(true);
    }
}
