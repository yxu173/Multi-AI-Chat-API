using Domain.Common;

namespace Domain.Aggregates.Prompts;

public sealed class PromptTemplateTag : BaseEntity
{
    public Guid PromptTemplateId { get; private set; }
    public PromptTemplate PromptTemplate { get; private set; }

    public Guid TagId { get; private set; }
    public Tag Tag { get; private set; }
}