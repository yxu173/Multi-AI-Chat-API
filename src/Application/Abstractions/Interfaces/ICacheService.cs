namespace Application.Abstractions.Interfaces;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);


    Task<bool> SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken = default);


    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);


    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    Task<T> GetOrSetAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan expiry,
        CancellationToken ct = default);

    Task EvictByPatternAsync(string pattern, CancellationToken ct = default);
}