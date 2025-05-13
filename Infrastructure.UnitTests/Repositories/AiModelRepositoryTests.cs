using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Infrastructure.Database;
using Infrastructure.Repositories;
using Infrastructure.UnitTests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Domain.Aggregates.Chats;
using Domain.Enums;

namespace Infrastructure.UnitTests.Repositories
{
    public class AiModelRepositoryTests
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

        private AiModelRepository CreateRepo(ApplicationDbContext db, TestCacheService cache)
            => new AiModelRepository(db, cache);

        private AiProvider CreateProvider(ApplicationDbContext db)
        {
            var provider = AiProvider.Create("TestProv", "Desc");
            db.AiProviders.Add(provider);
            db.SaveChanges();
            return provider;
        }

        [Fact]
        public async Task AddAndGetById_ShouldReturnModel()
        {
            // Arrange
            var db = CreateContext(nameof(AddAndGetById_ShouldReturnModel));
            var cache = new TestCacheService();
            var repo = CreateRepo(db, cache);
            var prov = CreateProvider(db);
            var model = AiModel.Create(
                name: "M1", modelType: ModelType.OpenAi.ToString(), aiProviderId: prov.Id,
                inputTokenPricePer1M: 1, outputTokenPricePer1M: 2, modelCode: "code",128000);

            // Act
            await repo.AddAsync(model);
            var fetched = await repo.GetByIdAsync(model.Id);

            // Assert
            fetched.Should().NotBeNull();
            fetched!.Id.Should().Be(model.Id);
            fetched.Name.Should().Be("M1");
        }

        [Fact]
        public async Task GetAll_ShouldReturnAllModels()
        {
            // Arrange
            var db = CreateContext(nameof(GetAll_ShouldReturnAllModels));
            var cache = new TestCacheService();
            var repo = CreateRepo(db, cache);
            var prov = CreateProvider(db);
            var m1 = AiModel.Create("M1", ModelType.OpenAi.ToString(), prov.Id, 1, 2, "c1", 128000);
            var m2 = AiModel.Create("M2", ModelType.OpenAi.ToString(), prov.Id, 1, 2, "c2", 128000);
            await repo.AddAsync(m1);
            await repo.AddAsync(m2);

            // Act
            var all = await repo.GetAllAsync();

            // Assert
            all.Should().HaveCount(2)
                .And.Contain(x => x.Name == "M1")
                .And.Contain(x => x.Name == "M2");
        }

        [Fact]
        public async Task Delete_NonExisting_ShouldReturnFalse()
        {
            // Arrange
            var db = CreateContext(nameof(Delete_NonExisting_ShouldReturnFalse));
            var cache = new TestCacheService();
            var repo = CreateRepo(db, cache);

            // Act
            var res = await repo.DeleteAsync(Guid.NewGuid());

            // Assert
            res.Should().BeFalse();
        }

        [Fact]
        public async Task Delete_Existing_ShouldRemoveAndReturnTrue()
        {
            // Arrange
            var db = CreateContext(nameof(Delete_Existing_ShouldRemoveAndReturnTrue));
            var cache = new TestCacheService();
            var repo = CreateRepo(db, cache);
            var prov = CreateProvider(db);
            var model = AiModel.Create("M", ModelType.OpenAi.ToString(), prov.Id, 1, 2, "c", 128000);
            await repo.AddAsync(model);

            // Act
            var res = await repo.DeleteAsync(model.Id);
            var fetched = await repo.GetByIdAsync(model.Id);

            // Assert
            res.Should().BeTrue();
            fetched.Should().BeNull();
        }

        [Fact]
        public async Task ExistsAsync_ShouldReflectPresence()
        {
            // Arrange
            var db = CreateContext(nameof(ExistsAsync_ShouldReflectPresence));
            var cache = new TestCacheService();
            var repo = CreateRepo(db, cache);
            var prov = CreateProvider(db);
            var model = AiModel.Create("M", ModelType.Gemini.ToString(), prov.Id, 1, 2, "c", 128000);
            await repo.AddAsync(model);

            // Act
            var exists = await repo.ExistsAsync(model.Id);
            var notExists = await repo.ExistsAsync(Guid.NewGuid());

            // Assert
            exists.Should().BeTrue();
            notExists.Should().BeFalse();
        }
    }
} 