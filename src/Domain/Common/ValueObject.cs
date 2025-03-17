namespace Domain.Common;

public abstract record ValueObject
{
    protected abstract IEnumerable<object> GetEqualityComponents();

    public override int GetHashCode()
    {
        return GetEqualityComponents()
            .Select(x => x?.GetHashCode() ?? 0)
            .Aggregate((x, y) => x ^ y);
    }
}

public abstract record ValueObject<T> : ValueObject
{
    public T Value { get; }
    protected ValueObject(T value)
    {
        Value = value;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value!;
    }

    public override string ToString() => Value?.ToString() ?? string.Empty;
}