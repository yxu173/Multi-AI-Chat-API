using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Infrastructure.Database;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Domain.Aggregates.Chats;

namespace Infrastructure.UnitTests.Repositories
{
    public class ChatSessionPluginRepositoryTests
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
        public async Task GetActivatedPlugins_ShouldReturnOnlyActivePlugins()
        {
            // Arrange
            var context = CreateContext(nameof(GetActivatedPlugins_ShouldReturnOnlyActivePlugins));
            var repo = new ChatSessionPluginRepository(context);
            var session = ChatSession.Create(Guid.NewGuid(), Guid.NewGuid());
            context.ChatSessions.Add(session);
            await context.SaveChangesAsync();

            // Seed Plugin entities for relationships
            var plugin1 = Plugin.Create("Plugin1", "Description1");
            var plugin2 = Plugin.Create("Plugin2", "Description2");
            context.Plugins.AddRange(plugin1, plugin2);
            await context.SaveChangesAsync();

            // Create session-plugin relationships
            var active = ChatSessionPlugin.Create(session.Id, plugin1.Id, true);
            var inactive = ChatSessionPlugin.Create(session.Id, plugin2.Id, false);
            context.ChatSessionPlugins.AddRange(active, inactive);
            await context.SaveChangesAsync();

            // Act
            var result = await repo.GetActivatedPluginsAsync(session.Id, CancellationToken.None);

            // Assert
            result.Should().ContainSingle().Which.PluginId.Should().Be(active.PluginId);
        }

        [Fact]
        public async Task AddAsync_ShouldInsertPluginEntry()
        {
            // Arrange
            var context = CreateContext(nameof(AddAsync_ShouldInsertPluginEntry));
            var repo = new ChatSessionPluginRepository(context);
            var session = ChatSession.Create(Guid.NewGuid(), Guid.NewGuid());
            context.ChatSessions.Add(session);
            await context.SaveChangesAsync();
            var plugin = ChatSessionPlugin.Create(session.Id, Guid.NewGuid());

            // Act
            await repo.AddAsync(plugin, CancellationToken.None);
            var persisted = await context.ChatSessionPlugins.FirstOrDefaultAsync(p => p.Id == plugin.Id);

            // Assert
            persisted.Should().NotBeNull();
            persisted!.ChatSessionId.Should().Be(session.Id);
            persisted.IsActive.Should().BeTrue();
        }
    }
} 