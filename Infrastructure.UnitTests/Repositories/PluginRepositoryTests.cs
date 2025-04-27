using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Infrastructure.Database;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Domain.Aggregates.Chats;

namespace Infrastructure.UnitTests.Repositories
{
    public class PluginRepositoryTests
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
        public async Task AddAndGetById_ShouldReturnPlugin()
        {
            // Arrange
            var context = CreateContext(nameof(AddAndGetById_ShouldReturnPlugin));
            var repo = new PluginRepository(context);
            var plugin = Plugin.Create("P1", "Desc1", "/icon1.png");

            // Act
            await repo.AddAsync(plugin);
            var fetched = await repo.GetByIdAsync(plugin.Id);

            // Assert
            fetched.Should().NotBeNull();
            fetched!.Name.Should().Be("P1");
        }

        [Fact]
        public async Task GetAllAndGetByIds_ShouldReturnCorrectPlugins()
        {
            // Arrange
            var context = CreateContext(nameof(GetAllAndGetByIds_ShouldReturnCorrectPlugins));
            var repo = new PluginRepository(context);
            var p1 = Plugin.Create("P1", "D1");
            var p2 = Plugin.Create("P2", "D2");
            await repo.AddAsync(p1);
            await repo.AddAsync(p2);

            // Act
            var all = (await repo.GetAllAsync()).ToList();
            var subset = (await repo.GetByIdsAsync(new [] { p2.Id })).ToList();

            // Assert
            all.Should().HaveCount(2);
            subset.Should().ContainSingle().Which.Id.Should().Be(p2.Id);
        }

        [Fact]
        public async Task Update_ShouldModifyPlugin()
        {
            // Arrange
            var context = CreateContext(nameof(Update_ShouldModifyPlugin));
            var repo = new PluginRepository(context);
            var plugin = Plugin.Create("Old", "OldDesc");
            await repo.AddAsync(plugin);

            // Act
            plugin.Update("New", "NewDesc", "/icon2.png");
            await repo.UpdateAsync(plugin);
            var fetched = await repo.GetByIdAsync(plugin.Id);

            // Assert
            fetched!.Name.Should().Be("New");
            fetched.Description.Should().Be("NewDesc");
        }
    }
} 