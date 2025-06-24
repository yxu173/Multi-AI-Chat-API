using Domain.Aggregates.Chats;
using Domain.Aggregates.Llms;
using FluentAssertions;
using Infrastructure.Database;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Infrastructure.UnitTests.TestHelpers;

namespace Infrastructure.UnitTests.Repositories
{
    public class AiProviderRepositoryTests
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

        private AiProviderRepository CreateRepo(ApplicationDbContext db, TestCacheService cache)
            => new AiProviderRepository(db, cache);

        [Fact]
        public async Task AddAndGetById_ShouldReturnNewProvider()
        {
            var db = CreateContext(nameof(AddAndGetById_ShouldReturnNewProvider));
            var cache = new TestCacheService();
            var repo = CreateRepo(db, cache);
            var provider = AiProvider.Create("Test", "Desc");

            await repo.AddAsync(provider);
            var fetched = await repo.GetByIdAsync(provider.Id);

            fetched.Should().NotBeNull();
            fetched!.Id.Should().Be(provider.Id);
            fetched.Name.Should().Be("Test");
        }

        [Fact]
        public async Task GetAll_ShouldReturnAllProviders()
        {
            var db = CreateContext(nameof(GetAll_ShouldReturnAllProviders));
            var cache = new TestCacheService();
            var repo = CreateRepo(db, cache);
            var p1 = AiProvider.Create("A", "D1");
            var p2 = AiProvider.Create("B", "D2");
            await repo.AddAsync(p1);
            await repo.AddAsync(p2);

            var all = await repo.GetAllAsync();
            all.Should().HaveCount(2).And.
                Contain(x => x.Name == "A").And.
                Contain(x => x.Name == "B");
        }

        [Fact]
        public async Task Update_ShouldChangePropertiesAndInvalidateCache()
        {
            var db = CreateContext(nameof(Update_ShouldChangePropertiesAndInvalidateCache));
            var cache = new TestCacheService();
            var repo = CreateRepo(db, cache);
            var provider = AiProvider.Create("Old", "Desc");
            await repo.AddAsync(provider);

            provider.SetEnabled(false);
            await repo.UpdateAsync(provider);

            var fetched = await repo.GetByIdAsync(provider.Id);
            fetched.Should().NotBeNull().And.Subject.As<AiProvider>().IsEnabled.Should().BeFalse();
        }

        [Fact]
        public async Task Delete_NonExisting_ShouldReturnFalse()
        {
            var db = CreateContext(nameof(Delete_NonExisting_ShouldReturnFalse));
            var cache = new TestCacheService();
            var repo = CreateRepo(db, cache);
            var result = await repo.DeleteAsync(Guid.NewGuid());
            result.Should().BeFalse();
        }

        [Fact]
        public async Task Delete_Existing_ShouldRemoveAndReturnTrue()
        {
            var db = CreateContext(nameof(Delete_Existing_ShouldRemoveAndReturnTrue));
            var cache = new TestCacheService();
            var repo = CreateRepo(db, cache);
            var provider = AiProvider.Create("T", "D");
            await repo.AddAsync(provider);

            var result = await repo.DeleteAsync(provider.Id);
            result.Should().BeTrue();
            var fetched = await repo.GetByIdAsync(provider.Id);
            fetched.Should().BeNull();
        }

        [Fact]
        public async Task ExistsAsync_ShouldReflectPresence()
        {
            var db = CreateContext(nameof(ExistsAsync_ShouldReflectPresence));
            var cache = new TestCacheService();
            var repo = CreateRepo(db, cache);
            var provider = AiProvider.Create("T", "D");
            await repo.AddAsync(provider);

            var exists = await repo.ExistsAsync(provider.Id);
            exists.Should().BeTrue();

            var notExists = await repo.ExistsAsync(Guid.NewGuid());
            notExists.Should().BeFalse();
        }

        [Fact]
        public async Task ExistsByNameAsync_ShouldReturnCorrectValue()
        {
            var db = CreateContext(nameof(ExistsByNameAsync_ShouldReturnCorrectValue));
            var cache = new TestCacheService();
            var repo = CreateRepo(db, cache);
            var provider = AiProvider.Create("Unique", "D");
            await repo.AddAsync(provider);

            var exists = await repo.ExistsByNameAsync("Unique");
            exists.Should().BeTrue();

            var notExists = await repo.ExistsByNameAsync("Other");
            notExists.Should().BeFalse();
        }
    }
} 