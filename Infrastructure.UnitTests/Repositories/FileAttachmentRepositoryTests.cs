using FluentAssertions;
using Infrastructure.Database;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Domain.Aggregates.Chats;
using Domain.Enums;

namespace Infrastructure.UnitTests.Repositories
{
    public class FileAttachmentRepositoryTests
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
        public async Task AddAndGetById_ShouldReturnAttachment()
        {
            // Arrange
            var context = CreateContext(nameof(AddAndGetById_ShouldReturnAttachment));
            var repo = new FileAttachmentRepository(context);
            var attachment = FileAttachment.Create("file.txt", "/path/file.txt", "text/plain", 123);

            // Act
            await repo.AddAsync(attachment, CancellationToken.None);
            var fetched = await repo.GetByIdAsync(attachment.Id, CancellationToken.None);

            // Assert
            fetched.Should().NotBeNull();
            fetched!.FileName.Should().Be("file.txt");
        }

        [Fact]
        public async Task GetByMessageId_ShouldReturnAttachmentsForMessage()
        {
            // Arrange
            var context = CreateContext(nameof(GetByMessageId_ShouldReturnAttachmentsForMessage));
            var chat = ChatSession.Create(Guid.NewGuid(), Guid.NewGuid(),ChatType.Text.ToString());
            context.ChatSessions.Add(chat);
            await context.SaveChangesAsync();
            var msg = Message.CreateUserMessage(chat.UserId, chat.Id, "hi");
            context.Messages.Add(msg);
            await context.SaveChangesAsync();
            var repo = new FileAttachmentRepository(context);
            var attachment = FileAttachment.Create("img.png", "/img/img.png", "image/png", 456, msg.Id);

            // Act
            await repo.AddAsync(attachment, CancellationToken.None);
            var list = await repo.GetByMessageIdAsync(msg.Id, CancellationToken.None);

            // Assert
            list.Should().ContainSingle().Which.Id.Should().Be(attachment.Id);
        }

        [Fact]
        public async Task GetByChatSessionId_ShouldReturnAttachmentsForChat()
        {
            // Arrange
            var context = CreateContext(nameof(GetByChatSessionId_ShouldReturnAttachmentsForChat));
            var chat = ChatSession.Create(Guid.NewGuid(), Guid.NewGuid(),ChatType.Text.ToString());
            context.ChatSessions.Add(chat);
            await context.SaveChangesAsync();
            var msg = Message.CreateUserMessage(chat.UserId, chat.Id, "msg");
            context.Messages.Add(msg);
            await context.SaveChangesAsync();
            var repo = new FileAttachmentRepository(context);
            var attachment = FileAttachment.Create("doc.pdf", "/docs/doc.pdf", "application/pdf", 789, msg.Id);

            // Act
            await repo.AddAsync(attachment, CancellationToken.None);
            var list = await repo.GetByChatSessionIdAsync(chat.Id, CancellationToken.None);

            // Assert
            list.Should().ContainSingle().Which.Id.Should().Be(attachment.Id);
        }

        [Fact]
        public async Task Delete_ShouldRemoveAttachment()
        {
            // Arrange
            var context = CreateContext(nameof(Delete_ShouldRemoveAttachment));
            var repo = new FileAttachmentRepository(context);
            var attachment = FileAttachment.Create("del.txt", "/del.txt", "text/plain", 10);
            await repo.AddAsync(attachment, CancellationToken.None);

            // Act
            await repo.DeleteAsync(attachment.Id, CancellationToken.None);
            var fetched = await repo.GetByIdAsync(attachment.Id, CancellationToken.None);

            // Assert
            fetched.Should().BeNull();
        }

        [Theory]
        [InlineData("application/pdf", FileType.PDF)]
        [InlineData("text/csv", FileType.CSV)]
        [InlineData("application/csv", FileType.CSV)]
        [InlineData("text/plain", FileType.Text)]
        [InlineData("image/jpeg", FileType.Image)]
        [InlineData("image/png", FileType.Image)]
        [InlineData("image/gif", FileType.Image)]
        [InlineData("image/webp", FileType.Image)]
        [InlineData("text/html", FileType.Text)]
        [InlineData("application/json", FileType.Other)]
        public void FileType_ShouldBeCorrectlyDetermined(string contentType, FileType expectedFileType)
        {
            // Arrange & Act
            var attachment = FileAttachment.Create("test.file", "/test.file", contentType, 100);

            // Assert
            attachment.FileType.Should().Be(expectedFileType);
        }
    }
} 