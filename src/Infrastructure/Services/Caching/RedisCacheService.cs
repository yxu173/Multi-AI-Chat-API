using System.Text.Json;
using Application.Abstractions.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Infrastructure.Services.Caching;

public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService>? _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IDatabase _db;
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 100;

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService>? logger = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        _db = redis.GetDatabase();
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var value = await _db.StringGetAsync(key);
                
                if (value.IsNullOrEmpty)
                {
                    return default;
                }
                
                return JsonSerializer.Deserialize<T>(value!, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error retrieving {Key} from Redis cache on attempt {Attempt}", key, attempt + 1);
                if (attempt == MaxRetries - 1) return default;
                await Task.Delay(RetryDelayMs, cancellationToken);
            }
        }
        return default;
    }

    public async Task<bool> SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var serialized = JsonSerializer.Serialize(value, _jsonOptions);
                
                return await _db.StringSetAsync(key, serialized, expiry);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error setting {Key} in Redis cache on attempt {Attempt}", key, attempt + 1);
                if (attempt == MaxRetries - 1) return false;
                await Task.Delay(RetryDelayMs, cancellationToken);
            }
        }
        return false;
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error removing {Key} from Redis cache", key);
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error checking if {Key} exists in Redis cache", key);
            return false;
        }
    }

    public async Task<T> GetOrSetAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan expiry,
        CancellationToken ct = default)
    {
        var existing = await GetAsync<T>(key, ct);
        if (existing is not null) return existing;

        var fresh = await factory();
        if (fresh is not null)
            await SetAsync(key, fresh, expiry, ct);
        return fresh;
    }

    public async Task EvictByPatternAsync(string pattern, CancellationToken ct = default)
    {
        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            foreach (var key in server.Keys(pattern: pattern))
            {
                await _db.KeyDeleteAsync(key);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error evicting keys by pattern {Pattern} from Redis cache", pattern);
        }
    }

    public async Task<Dictionary<string, T?>> GetBatchAsync<T>(IEnumerable<string> keys, CancellationToken ct = default)
    {
        var results = new Dictionary<string, T?>();
        var tasks = new List<Task>();

        foreach (var key in keys)
        {
            tasks.Add(GetAsync<T>(key, ct).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    results[key] = t.Result;
                }
                else
                {
                    results[key] = default;
                }
            }, ct));
        }

        await Task.WhenAll(tasks);
        return results;
    }

    public async Task<bool> SetBatchAsync<T>(Dictionary<string, T> keyValuePairs, TimeSpan expiry, CancellationToken ct = default)
    {
        var batch = _db.CreateBatch();
        var tasks = new List<Task<bool>>();

        foreach (var kvp in keyValuePairs)
        {
            var serialized = JsonSerializer.Serialize(kvp.Value, _jsonOptions);
            tasks.Add(Task.Run(() => batch.StringSetAsync(kvp.Key, serialized, expiry).GetAwaiter().GetResult()));
        }

        batch.Execute();
        var results = await Task.WhenAll(tasks);
        return results.All(r => r);
    }
}
