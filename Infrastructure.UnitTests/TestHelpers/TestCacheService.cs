using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Abstractions.Interfaces;

namespace Infrastructure.UnitTests.TestHelpers
{
    public class TestCacheService : ICacheService
    {
        private readonly Dictionary<string, object> _store = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_store.TryGetValue(key, out var value) ? (T?)value : default);

        public Task<bool> SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            _store[key] = value!;
            return Task.FromResult(true);
        }

        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            var removed = _store.Remove(key);
            return Task.FromResult(removed);
        }

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_store.ContainsKey(key));

        public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiry, CancellationToken ct = default)
        {
            if (_store.ContainsKey(key))
                return (T)_store[key]!;
            var value = await factory();
            _store[key] = value!;
            return value;
        }

        public Task EvictByPatternAsync(string pattern, CancellationToken ct = default)
        {
            var prefix = pattern.TrimEnd('*');
            var keys = new List<string>(_store.Keys);
            foreach (var key in keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                    _store.Remove(key);
            }
            return Task.CompletedTask;
        }
    }
} 