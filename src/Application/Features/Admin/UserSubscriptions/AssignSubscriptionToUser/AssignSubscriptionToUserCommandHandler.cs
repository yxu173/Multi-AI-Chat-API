using Application.Abstractions.Messaging;
using Domain.Aggregates.Admin;
using Domain.Repositories;
using SharedKernal;

namespace Application.Features.Admin.UserSubscriptions.AssignSubscriptionToUser;

internal sealed class AssignSubscriptionToUserCommandHandler : ICommandHandler<AssignSubscriptionToUserCommand, System.Guid>
{
    private readonly IUserSubscriptionRepository _userSubscriptionRepository;
    private readonly ISubscriptionPlanRepository _subscriptionPlanRepository;
    private readonly IUserRepository _userRepository;

    public AssignSubscriptionToUserCommandHandler(
        IUserSubscriptionRepository userSubscriptionRepository,
        ISubscriptionPlanRepository subscriptionPlanRepository,
        IUserRepository userRepository)
    {
        _userSubscriptionRepository = userSubscriptionRepository;
        _subscriptionPlanRepository = subscriptionPlanRepository;
        _userRepository = userRepository;
    }

    public async Task<Result<System.Guid>> ExecuteAsync(AssignSubscriptionToUserCommand command, CancellationToken ct)
    {
        var user = await _userRepository.GetByIdAsync(command.UserId);
        if (user == null)
        {
            return Result.Failure<System.Guid>(Error.NotFound(
                "UserSubscription.UserNotFound", 
                $"User with ID {command.UserId} does not exist"));
        }

        var plan = await _subscriptionPlanRepository.GetByIdAsync(command.SubscriptionPlanId, ct);
        if (plan == null)
        {
            return Result.Failure<System.Guid>(Error.NotFound(
                "UserSubscription.PlanNotFound", 
                $"Subscription plan with ID {command.SubscriptionPlanId} does not exist"));
        }

        if (!plan.IsActive)
        {
            return Result.Failure<System.Guid>(Error.Problem(
                "UserSubscription.PlanInactive", 
                $"Subscription plan '{plan.Name}' is currently inactive"));
        }

        var startDate = command.StartDate ?? DateTime.UtcNow;
        var expiryDate = startDate.AddMonths(command.DurationMonths);

        var existingSubscription = await _userSubscriptionRepository.GetActiveSubscriptionAsync(command.UserId, ct);
        if (existingSubscription != null)
        {
            existingSubscription.SetActive(false);
            await _userSubscriptionRepository.UpdateAsync(existingSubscription, ct);
        }

        var userSubscription = UserSubscription.Create(
            command.UserId,
            command.SubscriptionPlanId,
            startDate,
            expiryDate,
            command.PaymentReference);

        await _userSubscriptionRepository.AddAsync(userSubscription, ct);

        return Result.Success(userSubscription.Id);
    }
}
