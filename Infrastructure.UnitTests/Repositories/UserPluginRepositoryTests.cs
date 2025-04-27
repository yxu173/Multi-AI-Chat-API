using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Infrastructure.Database;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Domain.Aggregates.Users;

namespace Infrastructure.UnitTests.Repositories
{
    public class UserPluginRepositoryTests
    {
        private ApplicationDbContext CreateContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;
            var context = new ApplicationDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        [Fact]
        public async Task AddAndGetByUserIdAsync_ShouldReturnPluginPreferences()
        {
            // Arrange
            var context = CreateContext(nameof(AddAndGetByUserIdAsync_ShouldReturnPluginPreferences));
            var repo = new UserPluginRepository(context);
            var userId = Guid.NewGuid();
            var pluginId = Guid.NewGuid();
            var preference = UserPlugin.Create(userId, pluginId, true);

            // Act
            await repo.AddAsync(preference);
            var list = await repo.GetAllByUserIdAsync(userId);

            // Assert
            list.Should().ContainSingle().Which.PluginId.Should().Be(pluginId);
        }

        [Fact]
        public async Task GetByUserIdAndPluginIdAsync_ShouldReturnSinglePreference()
        {
            // Arrange
            var context = CreateContext(nameof(GetByUserIdAndPluginIdAsync_ShouldReturnSinglePreference));
            var repo = new UserPluginRepository(context);
            var userId = Guid.NewGuid();
            var pluginId = Guid.NewGuid();
            var pref = UserPlugin.Create(userId, pluginId, false);
            await repo.AddAsync(pref);

            // Act
            var fetched = await repo.GetByUserIdAndPluginIdAsync(userId, pluginId);

            // Assert
            fetched.Should().NotBeNull();
            fetched.PluginId.Should().Be(pluginId);
            fetched.IsEnabled.Should().BeFalse();
        }

        [Fact]
        public async Task UpdateAsync_ShouldModifyPreference()
        {
            // Arrange
            var context = CreateContext(nameof(UpdateAsync_ShouldModifyPreference));
            var repo = new UserPluginRepository(context);
            var pref = UserPlugin.Create(Guid.NewGuid(), Guid.NewGuid(), true);
            await repo.AddAsync(pref);

            // Act
            pref.SetEnabled(false);
            await repo.UpdateAsync(pref);
            var fetched = await repo.GetByUserIdAndPluginIdAsync(pref.UserId, pref.PluginId);

            // Assert
            fetched.IsEnabled.Should().BeFalse();
        }
    }
} 