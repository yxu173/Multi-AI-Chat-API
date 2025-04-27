using System;
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
    public class ChatSessionRepositoryTests
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
        public async Task AddAndGetById_ShouldReturnSavedChatSession()
        {
            // Arrange
            var context = CreateContext(nameof(AddAndGetById_ShouldReturnSavedChatSession));
            var repo = new ChatSessionRepository(context);
            var userId = Guid.NewGuid();
            var modelId = Guid.NewGuid();
            var chat = ChatSession.Create(userId, modelId);

            // Act
            await repo.AddAsync(chat, CancellationToken.None);
            var fetched = await repo.GetByIdAsync(chat.Id);

            // Assert
            fetched.Should().NotBeNull();
            fetched.Id.Should().Be(chat.Id);
            fetched.UserId.Should().Be(userId);
        }

        [Fact]
        public async Task Delete_NonExisting_ShouldReturnFailure()
        {
            // Arrange
            var context = CreateContext(nameof(Delete_NonExisting_ShouldReturnFailure));
            var repo = new ChatSessionRepository(context);

            // Act
            var result = await repo.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task Delete_Existing_ShouldReturnSuccess()
        {
            // Arrange
            var context = CreateContext(nameof(Delete_Existing_ShouldReturnSuccess));
            var repo = new ChatSessionRepository(context);
            var chat = ChatSession.Create(Guid.NewGuid(), Guid.NewGuid());
            await repo.AddAsync(chat, CancellationToken.None);

            // Act
            var result = await repo.DeleteAsync(chat.Id, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
        }
    }
} 