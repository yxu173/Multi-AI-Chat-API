using Domain.Aggregates.Prompts;
using Domain.DomainErrors;
using Domain.Repositories;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernal;

namespace Infrastructure.Repositories;

public sealed class PromptRepository : IPromptRepository
{
    private readonly ApplicationDbContext _dbContext;

    public PromptRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PromptTemplate>> GetByIdAsync(Guid id)
    {
        var result = await _dbContext.PromptTemplates.FirstOrDefaultAsync(x => x.Id == id);
        return result == null
            ? Result.Failure<PromptTemplate>(PromptTemplateErrors.PromptTemplateNotFound)
            : Result.Success(result);
    }

    public async Task<Result> AddAsync(PromptTemplate promptTemplate)
    {
        await _dbContext.PromptTemplates.AddAsync(promptTemplate);
        var result = await _dbContext.SaveChangesAsync();
        if (result == 0)
            return Result.Failure(PromptTemplateErrors.PromptTemplateNotCreated);
        return Result.Success();
    }

    public async Task<Result> UpdateAsync(PromptTemplate promptTemplate)
    {
        _dbContext.PromptTemplates.Update(promptTemplate);
        var result = await _dbContext.SaveChangesAsync();
        if (result == 0)
            return Result.Failure(PromptTemplateErrors.PromptTemplateNotUpdated);
        return Result.Success();
    }

    public async Task<Result<bool>> DeleteAsync(Guid id)
    {
        var promptTemplate = await _dbContext.PromptTemplates.FindAsync(id);
        if (promptTemplate == null)
            return Result.Failure<bool>(PromptTemplateErrors.PromptTemplateNotFound);
        _dbContext.PromptTemplates.Remove(promptTemplate);
        var result = await _dbContext.SaveChangesAsync();
        if (result == 0)
            return Result.Failure<bool>(PromptTemplateErrors.PromptTemplateNotDeleted);
        return Result.Success(true);
    }

    public async Task<IReadOnlyList<PromptTemplate>> GetAllPromptsByUserId(Guid userId)
    {
        return await _dbContext.PromptTemplates
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<PromptTemplate>> GetAllPrompts()
    {
        return await _dbContext.PromptTemplates.AsNoTracking().ToListAsync();
    }
}