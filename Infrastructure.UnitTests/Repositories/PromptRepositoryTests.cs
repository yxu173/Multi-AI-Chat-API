using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Infrastructure.Database;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Xunit;
using Domain.Aggregates.Prompts;
using Domain.DomainErrors;
using Domain.ValueObjects;

namespace Infrastructure.UnitTests.Repositories
{
    public class PromptRepositoryTests
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
        public async Task GetById_NonExisting_ShouldReturnFailure()
        {
            var context = CreateContext(nameof(GetById_NonExisting_ShouldReturnFailure));
            var repo = new PromptRepository(context);

            var result = await repo.GetByIdAsync(Guid.NewGuid());

            result.IsFailure.Should().BeTrue();
            result.Error.Should().Be(PromptTemplateErrors.PromptTemplateNotFound);
        }

        [Fact]
        public async Task AddAndGetById_ShouldReturnPrompt()
        {
            var context = CreateContext(nameof(AddAndGetById_ShouldReturnPrompt));
            var repo = new PromptRepository(context);
            var userId = Guid.NewGuid();
            var prompt = PromptTemplate.Create(userId, "Title", "Desc", "Content", new List<Tag>());

            var addResult = await repo.AddAsync(prompt);
            addResult.IsSuccess.Should().BeTrue();

            var getResult = await repo.GetByIdAsync(prompt.Id);
            getResult.IsSuccess.Should().BeTrue();
            getResult.Value.Should().NotBeNull().And.Subject.As<PromptTemplate>().Title.Should().Be("Title");
        }

        [Fact]
        public async Task Update_ShouldModifyPrompt()
        {
            var context = CreateContext(nameof(Update_ShouldModifyPrompt));
            var repo = new PromptRepository(context);
            var userId = Guid.NewGuid();
            var prompt = PromptTemplate.Create(userId, "T1", "Desc", "Content", new List<Tag>());
            await repo.AddAsync(prompt);

            prompt.Update("T2", "Content2", "Desc2");
            var updateResult = await repo.UpdateAsync(prompt);
            updateResult.IsSuccess.Should().BeTrue();

            var getResult = await repo.GetByIdAsync(prompt.Id);
            getResult.Value.Should().NotBeNull().And.Subject.As<PromptTemplate>().Title.Should().Be("T2");
        }

        [Fact]
        public async Task Delete_NonExisting_ShouldReturnFailure()
        {
            var context = CreateContext(nameof(Delete_NonExisting_ShouldReturnFailure));
            var repo = new PromptRepository(context);

            var deleteResult = await repo.DeleteAsync(Guid.NewGuid());
            deleteResult.IsFailure.Should().BeTrue();
        }

        [Fact]
        public async Task Delete_Existing_ShouldReturnSuccess()
        {
            var context = CreateContext(nameof(Delete_Existing_ShouldReturnSuccess));
            var repo = new PromptRepository(context);
            var prompt = PromptTemplate.Create(Guid.NewGuid(), "T", "D", "C", new List<Tag>());
            await repo.AddAsync(prompt);

            var deleteResult = await repo.DeleteAsync(prompt.Id);
            deleteResult.IsSuccess.Should().BeTrue().And.Subject.As<bool>().Should().BeTrue();

            var getResult = await repo.GetByIdAsync(prompt.Id);
            getResult.IsFailure.Should().BeTrue();
        }

        [Fact]
        public async Task GetAllPromptsByUserId_ShouldReturnOnlyUserPrompts()
        {
            var context = CreateContext(nameof(GetAllPromptsByUserId_ShouldReturnOnlyUserPrompts));
            var repo = new PromptRepository(context);
            var u1 = Guid.NewGuid();
            var u2 = Guid.NewGuid();
            var p1 = PromptTemplate.Create(u1, "A", "D", "C", new List<Tag>());
            var p2 = PromptTemplate.Create(u2, "B", "D", "C", new List<Tag>());
            await repo.AddAsync(p1);
            await repo.AddAsync(p2);

            var list = await repo.GetAllPromptsByUserId(u1);
            list.Should().ContainSingle().Which.UserId.Should().Be(u1);
        }

        [Fact]
        public async Task GetAllPrompts_ShouldReturnAllPrompts()
        {
            var context = CreateContext(nameof(GetAllPrompts_ShouldReturnAllPrompts));
            var repo = new PromptRepository(context);
            var p1 = PromptTemplate.Create(Guid.NewGuid(), "A", "D", "C", new List<Tag>());
            var p2 = PromptTemplate.Create(Guid.NewGuid(), "B", "D", "C", new List<Tag>());
            await repo.AddAsync(p1);
            await repo.AddAsync(p2);

            var list = await repo.GetAllPrompts();
            list.Should().HaveCount(2);
        }
    }
} 