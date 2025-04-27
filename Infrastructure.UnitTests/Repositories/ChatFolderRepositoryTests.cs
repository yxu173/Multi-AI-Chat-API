using System;
using System.Linq;
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
    public class ChatFolderRepositoryTests
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
        public async Task AddAndGetById_ShouldReturnFolder()
        {
            // Arrange
            var context = CreateContext(nameof(AddAndGetById_ShouldReturnFolder));
            var repo = new ChatFolderRepository(context);
            var folder = ChatFolder.Create(Guid.NewGuid(), "MyFolder", "Desc");

            // Act
            var added = await repo.AddAsync(folder, CancellationToken.None);
            var fetched = await repo.GetByIdAsync(added.Id, CancellationToken.None);

            // Assert
            fetched.Should().NotBeNull();
            fetched!.Name.Should().Be("MyFolder");
        }

        [Fact]
        public async Task GetByUserId_ShouldReturnAllFoldersForUser()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var context = CreateContext(nameof(GetByUserId_ShouldReturnAllFoldersForUser));
            var repo = new ChatFolderRepository(context);
            var f1 = ChatFolder.Create(userId, "F1");
            var f2 = ChatFolder.Create(userId, "F2");
            await repo.AddAsync(f1, CancellationToken.None);
            await repo.AddAsync(f2, CancellationToken.None);

            // Act
            var list = await repo.GetByUserIdAsync(userId, CancellationToken.None);

            // Assert
            list.Should().HaveCount(2).And.
                Contain(x => x.Name == "F1").And.
                Contain(x => x.Name == "F2");
        }

        [Fact]
        public async Task Update_ShouldModifyFolderName()
        {
            // Arrange
            var context = CreateContext(nameof(Update_ShouldModifyFolderName));
            var repo = new ChatFolderRepository(context);
            var folder = ChatFolder.Create(Guid.NewGuid(), "OldName");
            await repo.AddAsync(folder, CancellationToken.None);

            // Act
            folder.UpdateDetails("NewName", "UpdatedDesc");
            var updated = await repo.UpdateAsync(folder, CancellationToken.None);
            var fetched = await repo.GetByIdAsync(folder.Id, CancellationToken.None);

            // Assert
            fetched!.Name.Should().Be("NewName");
        }

        [Fact]
        public async Task Delete_ShouldRemoveFolder()
        {
            // Arrange
            var context = CreateContext(nameof(Delete_ShouldRemoveFolder));
            var repo = new ChatFolderRepository(context);
            var folder = ChatFolder.Create(Guid.NewGuid(), "ToDelete");
            await repo.AddAsync(folder, CancellationToken.None);

            // Act
            await repo.DeleteAsync(folder.Id, CancellationToken.None);
            var fetched = await repo.GetByIdAsync(folder.Id, CancellationToken.None);

            // Assert
            fetched.Should().BeNull();
        }
    }
} 