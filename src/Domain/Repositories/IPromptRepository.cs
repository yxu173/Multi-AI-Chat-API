using Domain.Aggregates.Prompts;
using SharedKernel;

namespace Domain.Repositories;

public interface IPromptRepository
{
    Task<Result<PromptTemplate>> GetByIdAsync(Guid id);
    Task<Result> AddAsync(PromptTemplate promptTemplate);
    Task<Result> UpdateAsync(PromptTemplate promptTemplate);
    Task<Result<bool>> DeleteAsync(Guid id);
    Task<IReadOnlyList<PromptTemplate>> GetAllPromptsByUserId(Guid userId);
    Task<IReadOnlyList<PromptTemplate>> GetAllPrompts();
}