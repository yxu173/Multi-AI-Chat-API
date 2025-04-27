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
    public class MessageRepositoryTests
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
        public async Task AddGetById_ShouldReturnMessage()
        {
            // Arrange
            var context = CreateContext(nameof(AddGetById_ShouldReturnMessage));
            var repo = new MessageRepository(context);
            var userId = Guid.NewGuid();
            var chat = ChatSession.Create(userId, Guid.NewGuid());
            context.ChatSessions.Add(chat);
            await context.SaveChangesAsync();
            var message = Message.CreateUserMessage(userId, chat.Id, "Hello");

            // Act
            await repo.AddAsync(message, CancellationToken.None);
            var fetched = await repo.GetByIdAsync(message.Id, CancellationToken.None);

            // Assert
            fetched.Should().NotBeNull();
            fetched!.Content.Should().Be("Hello");
            fetched.IsFromAi.Should().BeFalse();
        }

        [Fact]
        public async Task Update_ShouldModifyMessage()
        {
            // Arrange
            var context = CreateContext(nameof(Update_ShouldModifyMessage));
            var userId = Guid.NewGuid();
            var chat = ChatSession.Create(userId, Guid.NewGuid());
            context.ChatSessions.Add(chat);
            await context.SaveChangesAsync();
            var repo = new MessageRepository(context);
            var msg = Message.CreateUserMessage(userId, chat.Id, "Hi");
            await repo.AddAsync(msg, CancellationToken.None);

            // Act
            msg.UpdateContent("Hello world");
            await repo.UpdateAsync(msg, CancellationToken.None);
            var updated = await repo.GetByIdAsync(msg.Id, CancellationToken.None);

            // Assert
            updated!.Content.Should().Be("Hello world");
        }

        [Fact]
        public async Task Delete_ShouldRemoveMessage()
        {
            // Arrange
            var context = CreateContext(nameof(Delete_ShouldRemoveMessage));
            var userId = Guid.NewGuid();
            var chat = ChatSession.Create(userId, Guid.NewGuid());
            context.ChatSessions.Add(chat);
            await context.SaveChangesAsync();
            var repo = new MessageRepository(context);
            var msg = Message.CreateUserMessage(userId, chat.Id, "Test");
            await repo.AddAsync(msg, CancellationToken.None);

            // Act
            await repo.DeleteAsync(msg.Id, CancellationToken.None);
            var fetched = await repo.GetByIdAsync(msg.Id, CancellationToken.None);

            // Assert
            fetched.Should().BeNull();
        }

        [Fact]
        public async Task GetLatestAiMessageForChat_ShouldReturnLatestStreaming()
        {
            // Arrange
            var context = CreateContext(nameof(GetLatestAiMessageForChat_ShouldReturnLatestStreaming));
            var userId = Guid.NewGuid();
            var chat = ChatSession.Create(userId, Guid.NewGuid());
            context.ChatSessions.Add(chat);
            await context.SaveChangesAsync();
            var repo = new MessageRepository(context);
            var m1 = Message.CreateAiMessage(userId, chat.Id);
            await repo.AddAsync(m1, CancellationToken.None);
            var m2 = Message.CreateAiMessage(userId, chat.Id);
            await repo.AddAsync(m2, CancellationToken.None);
            m1.CompleteMessage();
            await repo.UpdateAsync(m1, CancellationToken.None);

            // Act
            var latest = await repo.GetLatestAiMessageForChatAsync(chat.Id, CancellationToken.None);

            // Assert
            latest!.Id.Should().Be(m2.Id);
        }
    }
} 