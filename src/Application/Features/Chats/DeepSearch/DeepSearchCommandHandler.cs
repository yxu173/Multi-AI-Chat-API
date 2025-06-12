using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Application.Abstractions.Messaging;
using Application.Features.Chats.SendMessage;
using Application.Notifications;
using Application.Services.Plugins;
using FastEndpoints;
using Microsoft.Extensions.Logging;
using SharedKernal;

namespace Application.Features.Chats.DeepSearch;

public class DeepSearchCommandHandler : Application.Abstractions.Messaging.ICommandHandler<DeepSearchCommand>
{
    private readonly PluginService _pluginService;
    private readonly ILogger<DeepSearchCommandHandler> _logger;

    public DeepSearchCommandHandler(PluginService pluginService, ILogger<DeepSearchCommandHandler> logger)
    {
        _pluginService = pluginService;
        _logger = logger;
    }

    public async Task<Result> ExecuteAsync(DeepSearchCommand command, CancellationToken cancellationToken)
    {
        try
        {
            await new DeepSearchStartedNotification(command.ChatSessionId, "Starting deep search...").PublishAsync(Mode.WaitForNone, cancellationToken);

            var pluginId = new Guid("235979c5-cec1-4af2-9d61-6c1079c80be5"); // JinaDeepSearchPlugin ID
            var arguments = new JsonObject
            {
                ["query"] = command.Content
            };

            var pluginResult = await _pluginService.ExecutePluginByIdAsync(pluginId, arguments, cancellationToken);

            string contentToSend = command.Content;

            if (pluginResult.Success)
            {
                await new DeepSearchResultsNotification(command.ChatSessionId, pluginResult.Result).PublishAsync(Mode.WaitForNone, cancellationToken);
                contentToSend = $"{command.Content}\n\nDeep Search Results:\n{pluginResult.Result}";
            }
            else
            {
                var errorMessage = $"Deep search failed: {pluginResult.ErrorMessage}";
                await new DeepSearchErrorNotification(command.ChatSessionId, errorMessage).PublishAsync(Mode.WaitForNone, cancellationToken);
            }
            
            var sendMessageCommand = new SendMessageCommand(
                command.ChatSessionId,
                command.UserId,
                contentToSend,
                command.EnableThinking,
                command.ImageSize,
                command.NumImages,
                command.OutputFormat,
                command.EnableSafetyChecker,
                command.SafetyTolerance
            );

            await sendMessageCommand.ExecuteAsync(cancellationToken);
            
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing deep search for chat {ChatSessionId}", command.ChatSessionId);
            await new DeepSearchErrorNotification(command.ChatSessionId, "Deep search execution failed").PublishAsync(Mode.WaitForNone, cancellationToken);
            
            var sendMessageCommand = new SendMessageCommand(
                command.ChatSessionId,
                command.UserId,
                command.Content, // Send original content on failure
                command.EnableThinking,
                command.ImageSize,
                command.NumImages,
                command.OutputFormat,
                command.EnableSafetyChecker,
                command.SafetyTolerance
            );
            
            await sendMessageCommand.ExecuteAsync(cancellationToken);
            
            // We re-throw because the underlying SendMessageCommand will handle the actual failure.
            // This handler's job is just to orchestrate the deep search.
            // However, since we already sent a message, we might just return a failure result.
            // For now, let's just return success as a message was sent.
            return Result.Failure(Error.Failure("DeepSearch.Failed", ex.Message));
        }
    }
} 