using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Infrastructure.Database;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Domain.Aggregates.Users;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Llms;

namespace Infrastructure.UnitTests.Repositories
{
    public class UserApiKeyRepositoryTests
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
        public async Task AddAndGetByUserAndProvider_ShouldReturnApiKey()
        {
            var context = CreateContext(nameof(AddAndGetByUserAndProvider_ShouldReturnApiKey));
            var repo = new UserApiKeyRepository(context);
            var user = User.Create("test@example.com","testuser").Value;
            var prov = AiProvider.Create("P","D");
            context.Users.Add(user);
            context.AiProviders.Add(prov);
            await context.SaveChangesAsync();

            var key = UserApiKey.Create(user.Id, prov.Id, "abc123");
            await repo.AddAsync(key);

            var fetched = await repo.GetByUserAndProviderAsync(user.Id, prov.Id);
            fetched.Should().NotBeNull();
            fetched.ApiKey.Should().Be("abc123");
        }

        [Fact]
        public async Task GetByUserId_ShouldReturnAllKeysForUser()
        {
            var context = CreateContext(nameof(GetByUserId_ShouldReturnAllKeysForUser));
            var repo = new UserApiKeyRepository(context);
            var user = User.Create("test@example.com","testuser").Value;
            var p1 = AiProvider.Create("A","D");
            var p2 = AiProvider.Create("B","D");
            context.Users.Add(user);
            context.AiProviders.AddRange(p1, p2);
            await context.SaveChangesAsync();

            var k1 = UserApiKey.Create(user.Id, p1.Id, "k1");
            var k2 = UserApiKey.Create(user.Id, p2.Id, "k2");
            await repo.AddAsync(k1);
            await repo.AddAsync(k2);

            var list = await repo.GetByUserIdAsync(user.Id);
            list.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetByProviderId_ShouldReturnAllKeysForProvider()
        {
            var context = CreateContext(nameof(GetByProviderId_ShouldReturnAllKeysForProvider));
            var repo = new UserApiKeyRepository(context);
            var user1 = User.Create("u1@example.com","user1").Value;
            var user2 = User.Create("u2@example.com","user2").Value;
            var prov = AiProvider.Create("P","D");
            context.Users.AddRange(user1, user2);
            context.AiProviders.Add(prov);
            await context.SaveChangesAsync();

            var k1 = UserApiKey.Create(user1.Id, prov.Id, "k1");
            var k2 = UserApiKey.Create(user2.Id, prov.Id, "k2");
            await repo.AddAsync(k1);
            await repo.AddAsync(k2);

            var list = await repo.GetByProviderIdAsync(prov.Id);
            list.Should().HaveCount(2);
        }

        [Fact]
        public async Task Delete_NonExisting_ShouldReturnFalse()
        {
            var context = CreateContext(nameof(Delete_NonExisting_ShouldReturnFalse));
            var repo = new UserApiKeyRepository(context);

            var res = await repo.DeleteAsync(Guid.NewGuid());
            res.Should().BeFalse();
        }

        [Fact]
        public async Task Delete_Existing_ShouldReturnTrue()
        {
            var context = CreateContext(nameof(Delete_Existing_ShouldReturnTrue));
            var repo = new UserApiKeyRepository(context);
            var user = User.Create("x@x.com","userX").Value;
            var prov = AiProvider.Create("P","D");
            context.Users.Add(user);
            context.AiProviders.Add(prov);
            await context.SaveChangesAsync();

            var key = UserApiKey.Create(user.Id, prov.Id, "k");
            await repo.AddAsync(key);

            var res = await repo.DeleteAsync(key.Id);
            res.Should().BeTrue();
            var fetched = await repo.GetByIdAsync(key.Id);
            fetched.Should().BeNull();
        }
    }
} 