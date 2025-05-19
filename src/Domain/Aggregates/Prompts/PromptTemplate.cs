using Domain.Aggregates.Users;
using Domain.Common;
using Domain.DomainErrors;
using Domain.ValueObjects;
using SharedKernal;

namespace Domain.Aggregates.Prompts;

public class PromptTemplate : BaseEntity
{
    private readonly List<Tag> _tags = new();
    public Guid UserId { get; private set; }
    public User User { get; private set; }
    public string Title { get; private set; }
    public string Content { get; private set; }
    public string Description { get; private set; }
    public IReadOnlyCollection<Tag> Tags => _tags.AsReadOnly();

    private PromptTemplate()
    {
    }

    private PromptTemplate(Guid userId, string title, string content, string description, IEnumerable<Tag> tags)
    {
        UserId = userId;
        Title = title;
        Content = content;
        Description = description;
        _tags.AddRange(tags);
    }

    public static PromptTemplate Create(Guid userId, string title, string description, string content,
        IEnumerable<Tag> tags)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be empty", nameof(title));

        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty", nameof(content));

        return new PromptTemplate(userId, title, content, description, tags);
    }

    public void Update(string title, string content,string description)
    {
        Title = title;
        Content = content;
        Description = description;
    }

    public Result AddTag(string name)
    {
        var tag = Tag.Create(name);
        if (_tags.Any(t => t.Equals(tag)))
            return Result.Failure(PromptTemplateErrors.TagIsAlreadyExist);

        _tags.Add(tag);
        return Result.Success();
    }

    public Result RemoveTag(string name)
    {
        var tag = Tag.Create(name);
        var removed = _tags.RemoveAll(t => t.Equals(tag)) > 0;
        return removed ? Result.Success() : Result.Failure(PromptTemplateErrors.TagNotFound);
    }

    public Result AddTags(IEnumerable<string> tagNames)
    {
        foreach (var name in tagNames)
        {
            var result = AddTag(name);
            if (result.IsFailure)
                return result;
        }

        return Result.Success();
    }

    public Result RemoveTags(IEnumerable<string> tagNames)
    {
        foreach (var name in tagNames)
        {
            var result = RemoveTag(name);
            if (result.IsFailure)
                return result;
        }

        return Result.Success();
    }

    public void UpdateTags(IEnumerable<Tag> newTags)
    {
        if (newTags == null)
            return;

        // Create a set of new tags for efficient lookup
        var newTagSet = new HashSet<Tag>(newTags);

        // Remove tags that are not in the new list
        var tagsToRemove = _tags.Where(t => !newTagSet.Contains(t)).ToList();
        foreach (var tag in tagsToRemove)
        {
            _tags.Remove(tag);
        }

        // Add tags that are not already in the current list
        var tagsToAdd = newTagSet.Where(t => !_tags.Contains(t)).ToList();
        foreach (var tag in tagsToAdd)
        {
            _tags.Add(tag);
        }
    }
}