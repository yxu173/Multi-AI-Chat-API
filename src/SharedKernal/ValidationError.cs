namespace SharedKernel;

public sealed record ValidationError : Error
{
    public IReadOnlyDictionary<string, Error[]> ErrorsByProperty { get; }

    public ValidationError(Error[] errors, Dictionary<string, Error[]> errorsByProperty) 
        : base("Validation.General", "One or more validation errors occurred", ErrorType.Validation)
    {
        ErrorsByProperty = errorsByProperty;
    }
}