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
using Domain.Aggregates.Chats;
using Domain.ValueObjects;
using Domain.Enums;

namespace Infrastructure.UnitTests.Repositories
{
    public class AiAgentRepositoryTests
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
        public async Task AddAndGetById_ShouldReturnAgent()
        {
            // Arrange
            var context = CreateContext(nameof(AddAndGetById_ShouldReturnAgent));
            // Seed provider & model for FK
            var prov = AiProvider.Create("Prov1", "Desc1");
            context.AiProviders.Add(prov);
            await context.SaveChangesAsync();
            var model = AiModel.Create("M1", ModelType.OpenAi.ToString(), prov.Id, 1, 2, "code", apiType: "testApi");
            context.AiModels.Add(model);
            await context.SaveChangesAsync();
            var repo = new AiAgentRepository(context);
            var userId = Guid.NewGuid();
            // Create agent with existing model
            var agent = AiAgent.Create(userId, "Agent1", "Desc", null, false, null, null, model.Id);

            // Act
            var added = await repo.AddAsync(agent, CancellationToken.None);
            var fetched = await repo.GetByIdAsync(added.Id, CancellationToken.None);

            // Assert
            fetched.Should().NotBeNull();
            fetched!.Name.Should().Be("Agent1");
            fetched.AiModelId.Should().Be(model.Id);
        }

        [Fact]
        public async Task GetByUserId_ShouldReturnAgentsForUser()
        {
            // Arrange
            var context = CreateContext(nameof(GetByUserId_ShouldReturnAgentsForUser));
            // Seed provider & model
            var prov = AiProvider.Create("Prov2", "Desc2");
            context.AiProviders.Add(prov);
            await context.SaveChangesAsync();
            var model = AiModel.Create("M2", ModelType.Gemini.ToString(), prov.Id, 1, 2, "c", apiType: "testApi");
            context.AiModels.Add(model);
            await context.SaveChangesAsync();
            var repo = new AiAgentRepository(context);
            var u1 = Guid.NewGuid();
            var u2 = Guid.NewGuid();
            var a1 = AiAgent.Create(u1, "A1", "D", null, false, null, null, model.Id);
            var a2 = AiAgent.Create(u2, "A2", "D", null, false, null, null, model.Id);
            await repo.AddAsync(a1, CancellationToken.None);
            await repo.AddAsync(a2, CancellationToken.None);

            // Act
            var list = (await repo.GetByUserIdAsync(u1, CancellationToken.None)).ToList();

            // Assert
            list.Should().ContainSingle().Which.UserId.Should().Be(u1);
        }

        [Fact]
        public async Task Update_ShouldModifyAgentProperties()
        {
            // Arrange
            var context = CreateContext(nameof(Update_ShouldModifyAgentProperties));
            // Seed provider & model
            var prov = AiProvider.Create("Prov3", "Desc3");
            context.AiProviders.Add(prov);
            await context.SaveChangesAsync();
            var model = AiModel.Create("M3", ModelType.AimlFlux.ToString(), prov.Id, 1, 2, "mc", apiType: "testApi");
            context.AiModels.Add(model);
            await context.SaveChangesAsync();
            var repo = new AiAgentRepository(context);
            var userId = Guid.NewGuid();
            var agent = AiAgent.Create(userId, "OldName", "OldDesc", null, false, null, null, model.Id);
            await repo.AddAsync(agent, CancellationToken.None);

            // Act
            agent.Update("NewName", "NewDesc", null, true, ModelParameters.Create(), null);
            var updated = await repo.UpdateAsync(agent, CancellationToken.None);
            var fetched = await repo.GetByIdAsync(agent.Id, CancellationToken.None);

            // Assert
            fetched!.Name.Should().Be("NewName");
            fetched.Description.Should().Be("NewDesc");
        }

        [Fact]
        public async Task Delete_ShouldRemoveAgent()
        {
            // Arrange
            var context = CreateContext(nameof(Delete_ShouldRemoveAgent));
            // Seed provider & model
            var prov = AiProvider.Create("Prov4", "Desc4");
            context.AiProviders.Add(prov);
            await context.SaveChangesAsync();
            var model = AiModel.Create("M4", ModelType.AimlFlux.ToString(), prov.Id, 1, 2, "m4", apiType: "testApi");
            context.AiModels.Add(model);
            await context.SaveChangesAsync();
            var repo = new AiAgentRepository(context);
            var agent = AiAgent.Create(Guid.NewGuid(), "Name", "Desc", null, false, null, null, model.Id);
            await repo.AddAsync(agent, CancellationToken.None);

            // Act
            await repo.DeleteAsync(agent.Id, CancellationToken.None);
            var fetched = await repo.GetByIdAsync(agent.Id, CancellationToken.None);

            // Assert
            fetched.Should().BeNull();
        }
    }
} 