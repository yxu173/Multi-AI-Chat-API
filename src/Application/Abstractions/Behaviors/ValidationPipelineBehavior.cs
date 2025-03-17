using FluentValidation;
using MediatR;
using SharedKernel;

namespace Application.Abstractions.Behaviors;

public sealed class ValidationPipelineBehavior<TRequest, TResponse> 
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationPipelineBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        
        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Count != 0)
        {
            // Group validation errors by property
            var errorsByProperty = failures
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => Error.Validation(
                        g.Key,
                        x.ErrorMessage
                    )).ToArray()
                );

            var validationErrors = errorsByProperty
                .SelectMany(x => x.Value)
                .ToArray();

            var validationError = new ValidationError(validationErrors, errorsByProperty);

            if (typeof(TResponse).IsGenericType)
            {
                var resultType = typeof(TResponse).GetGenericArguments()[0];
                return (TResponse)(object)typeof(Result<>)
                    .MakeGenericType(resultType)
                    .GetMethod(nameof(Result<object>.ValidationFailure))!
                    .Invoke(null, new object[] { validationError })!;
            }

            return (TResponse)(object)Result.Failure(validationError);
        }

        return await next();
    }
}