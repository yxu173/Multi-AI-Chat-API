using Domain.Aggregates.Users;
using Domain.Common;
using Domain.DomainErrors;
using SharedKernel;

namespace Domain.Aggregates.Prompts;

public sealed class PromptTemplate : BaseEntity
{
    private readonly List<Tag> _tags = new();
    public Guid UserId { get; private set; }
    public User User { get; private set; }
    public string Title { get; private set; }
    public string Description { get; private set; }
    public string Content { get; private set; }
    public ICollection<PromptTemplateTag> PromptTemplateTags { get; set; }
    public IReadOnlyList<Tag> Tags => _tags.AsReadOnly();

    private PromptTemplate()
    {
    }


    public static Result<PromptTemplate> Create(Guid userId, string title, string description, string content)
    {
        return Result.Success(new PromptTemplate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title.Trim(),
            Description = description.Trim(),
            Content = content.Trim()
        });
    }

    public Result AddTag(string name)
    {
        var tag = Tag.Create(name);

        if (_tags.Exists(t => t.Name.Equals(tag.Name, StringComparison.OrdinalIgnoreCase)))
            return Result.Failure(PromptTemplateErrors.TagIsAlreadyExist);

        _tags.Add(tag);
        return Result.Success();
    }

    public Result RemoveTag(string name)
    {
        var tag = _tags.Find(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (tag == null)
            return Result.Failure(PromptTemplateErrors.TagNotFound);

        _tags.Remove(tag);
        return Result.Success();
    }
}