using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Infrastructure.Database;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Domain.Aggregates.Chats;
using System.Collections.Generic;
using System.Linq;
using Domain.Enums;

namespace Infrastructure.UnitTests.Repositories
{
    public class ChatTokenUsageRepositoryTests
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
        public async Task AddAndGetByChatSessionId_ShouldReturnUsage()
        {
            // Arrange
            var context = CreateContext(nameof(AddAndGetByChatSessionId_ShouldReturnUsage));
            var repo = new ChatTokenUsageRepository(context);
            var session = ChatSession.Create(Guid.NewGuid(), Guid.NewGuid(),ChatType.Text.ToString());
            context.ChatSessions.Add(session);
            await context.SaveChangesAsync();
            var usage = ChatTokenUsage.Create(session.Id, 10, 5, 1.5m);

            // Act
            var added = await repo.AddAsync(usage, CancellationToken.None);
            var fetched = await repo.GetByChatSessionIdAsync(session.Id);

            // Assert
            fetched.Should().NotBeNull();
            fetched.InputTokens.Should().Be(10);
            fetched.OutputTokens.Should().Be(5);
        }

        [Fact]
        public async Task Update_ShouldAdjustCountsAndCost()
        {
            // Arrange
            var context = CreateContext(nameof(Update_ShouldAdjustCountsAndCost));
            var repo = new ChatTokenUsageRepository(context);
            var session = ChatSession.Create(Guid.NewGuid(), Guid.NewGuid(),ChatType.Text.ToString());
            context.ChatSessions.Add(session);
            await context.SaveChangesAsync();
            var usage = ChatTokenUsage.Create(session.Id, 1, 2, 0.5m);
            var added = await repo.AddAsync(usage, CancellationToken.None);

            // Act
            added.UpdateTokenCountsAndCost(3, 4, 1.0m);
            await repo.UpdateAsync(added, CancellationToken.None);
            var fetched = await repo.GetByChatSessionIdAsync(session.Id);

            // Assert (initial 1+3 = 4, 2+4 = 6, cost 0.5+1.0 = 1.5)
            fetched.InputTokens.Should().Be(4);
            fetched.OutputTokens.Should().Be(6);
            fetched.TotalCost.Should().Be(1.5m);
        }

        [Fact]
        public async Task GetTotalTokens_ShouldSumAcrossSessions()
        {
            // Arrange
            var context = CreateContext(nameof(GetTotalTokens_ShouldSumAcrossSessions));
            var repo = new ChatTokenUsageRepository(context);
            var userId = Guid.NewGuid();
            var s1 = ChatSession.Create(userId, Guid.NewGuid(),ChatType.Text.ToString());
            var s2 = ChatSession.Create(userId, Guid.NewGuid(),ChatType.Text.ToString());
            context.ChatSessions.AddRange(s1, s2);
            await context.SaveChangesAsync();
            await repo.AddAsync(ChatTokenUsage.Create(s1.Id, 5, 7, 0.2m), CancellationToken.None);
            await repo.AddAsync(ChatTokenUsage.Create(s2.Id, 3, 2, 0.1m), CancellationToken.None);

            // Act
            var totalIn = await repo.GetTotalInputTokensForUserAsync(userId);
            var totalOut = await repo.GetTotalOutputTokensForUserAsync(userId);

            // Assert
            totalIn.Should().Be(8);
            totalOut.Should().Be(9);
        }
    }
} 