using FastEndpoints;
using FluentValidation;
using SharedKernel;

namespace Application.Abstractions.PreProcessors;

public class ValidationPreProcessor<TRequest> : IPreProcessor<TRequest>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationPreProcessor(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task PreProcessAsync(IPreProcessorContext<TRequest> ctx, CancellationToken ct)
    {
        if (!_validators.Any())
            return;

        var context = new FluentValidation.ValidationContext<TRequest>(ctx.Request);
        
        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, ct)));

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

            // For FastEndpoints, we need to throw an exception or use ThrowIfAnyErrors to stop processing
            ctx.HttpContext.Response.StatusCode = 400;
            
            // Store validation error in HttpContext to be used in an endpoint
            ctx.HttpContext.Items["ValidationError"] = validationError;
            
            // Set ValidationFailed flag to let FastEndpoints know to abort the request
            ctx.ValidationFailures.Add(new() { ErrorMessage = "Validation failed", PropertyName = "_" });
        }
    }
}
