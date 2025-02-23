using Domain.Common;

namespace Domain.Aggregates.Prompts;

public sealed class Tag : BaseEntity
{
    public string Name { get; private set; }

    public ICollection<PromptTemplateTag> PromptTemplateTags { get; set; }

    private Tag()
    {
    }

    public static Tag Create(string name)
    {
        return new Tag  
        {
            Id = Guid.NewGuid(),
            Name = name
        };
    }
}