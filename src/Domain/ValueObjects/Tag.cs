using Domain.Common;

namespace Domain.ValueObjects;

public record Tag : ValueObject
{
    public string Name { get; init; }

    private Tag()
    {
    }

    public static Tag Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tag name cannot be empty", nameof(name));

        return new Tag
        {
            Name = name.Trim().ToLowerInvariant()
        };
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Name;
    }
}