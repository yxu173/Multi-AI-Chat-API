using System.Linq.Expressions;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Infrastructure;

public interface IBulkOperationsService
{
    Task<int> BulkUpdateAsync<T>(IQueryable<T> query, Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>> updateExpression, CancellationToken cancellationToken = default) where T : class;
    Task<int> BulkDeleteAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default) where T : class;
    Task BulkInsertAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : class;
    Task BulkInsertWithIdentityAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : class;
}

public class BulkOperationsService : IBulkOperationsService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<BulkOperationsService> _logger;

    public BulkOperationsService(ApplicationDbContext context, ILogger<BulkOperationsService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<int> BulkUpdateAsync<T>(IQueryable<T> query, Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>> updateExpression, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            _logger.LogInformation("Starting bulk update for {EntityType}", typeof(T).Name);
            
            // Optimize change tracking for bulk operations
            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            
            var result = await query.ExecuteUpdateAsync(updateExpression, cancellationToken);
            
            _logger.LogInformation("Completed bulk update for {EntityType}. Updated {Count} records", typeof(T).Name, result);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk update for {EntityType}", typeof(T).Name);
            throw;
        }
        finally
        {
            _context.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    public async Task<int> BulkDeleteAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            _logger.LogInformation("Starting bulk delete for {EntityType}", typeof(T).Name);
            
            // Optimize change tracking for bulk operations
            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            
            var result = await query.ExecuteDeleteAsync(cancellationToken);
            
            _logger.LogInformation("Completed bulk delete for {EntityType}. Deleted {Count} records", typeof(T).Name, result);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk delete for {EntityType}", typeof(T).Name);
            throw;
        }
        finally
        {
            _context.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    public async Task BulkInsertAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            _logger.LogInformation("Starting bulk insert for {EntityType}. Count: {Count}", typeof(T).Name, entities.Count());
            
            // Optimize change tracking for bulk operations
            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            
            await _context.Set<T>().AddRangeAsync(entities, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Completed bulk insert for {EntityType}", typeof(T).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk insert for {EntityType}", typeof(T).Name);
            throw;
        }
        finally
        {
            _context.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    public async Task BulkInsertWithIdentityAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            _logger.LogInformation("Starting bulk insert with identity for {EntityType}. Count: {Count}", typeof(T).Name, entities.Count());
            
            // Use AddRange for better performance with identity columns
            _context.Set<T>().AddRange(entities);
            await _context.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Completed bulk insert with identity for {EntityType}", typeof(T).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk insert with identity for {EntityType}", typeof(T).Name);
            throw;
        }
    }
} 