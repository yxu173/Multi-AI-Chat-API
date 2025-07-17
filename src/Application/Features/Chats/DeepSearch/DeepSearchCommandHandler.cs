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
using Application.Abstractions.Interfaces;

namespace Application.Features.Chats.DeepSearch;

public class DeepSearchCommandHandler : Application.Abstractions.Messaging.ICommandHandler<DeepSearchCommand>
{
    private readonly PluginService _pluginService;
    private readonly IPluginExecutorFactory _pluginExecutorFactory;
    private readonly ILogger<DeepSearchCommandHandler> _logger;

    public DeepSearchCommandHandler(PluginService pluginService, IPluginExecutorFactory pluginExecutorFactory, ILogger<DeepSearchCommandHandler> logger)
    {
        _pluginService = pluginService;
        _pluginExecutorFactory = pluginExecutorFactory;
        _logger = logger;
    }

    public async Task<Result> ExecuteAsync(DeepSearchCommand command, CancellationToken cancellationToken)
    {
        try
        {
            await new DeepSearchStartedNotification(command.ChatSessionId, "Starting deep search...").PublishAsync(Mode.WaitForNone, cancellationToken);

            var pluginId = new Guid("3d5ec31c-5e6c-437d-8494-2ca942c9e2fe"); // JinaDeepSearchPlugin ID
            var arguments = new JsonObject
            {
                ["query"] = command.Content
            };

            // Get the plugin instance and stream the result
            var plugin = _pluginExecutorFactory.GetPlugin(pluginId);
            var sb = new System.Text.StringBuilder();
            string fullResult = null;
            var jinaPlugin = plugin as dynamic; // Use dynamic to avoid direct reference
            if (jinaPlugin != null && jinaPlugin.GetType().Name == "JinaDeepSearchPlugin")
            {
                fullResult = await jinaPlugin.StreamWithNotificationAsync(
                    command.Content,
                    command.ChatSessionId,
                    (Func<string, Guid, Task>) (async (chunk, chatSessionId) => await new DeepSearchChunkReceivedNotification(chatSessionId, chunk)
                        .PublishAsync(Mode.WaitForNone, cancellationToken)),
                    cancellationToken);
            }
            else
            {
                var pluginResult = await plugin.ExecuteAsync(new JsonObject { ["query"] = command.Content }, cancellationToken);
                fullResult = pluginResult.Result;
            }
            await new DeepSearchResultsNotification(command.ChatSessionId, fullResult).PublishAsync(Mode.WaitForNone, cancellationToken);

            var contentToSend = $"{command.Content}\n\nDeep Search Results:\n{fullResult}";
            var sendMessageCommand = new SendMessageCommand(
                command.ChatSessionId,
                command.UserId,
                contentToSend,
                command.EnableThinking,
                command.ImageSize,
                command.NumImages,
                command.OutputFormat
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
                command.Content,
                command.EnableThinking,
                command.ImageSize,
                command.NumImages,
                command.OutputFormat
            );
            await sendMessageCommand.ExecuteAsync(cancellationToken);
            return Result.Failure(Error.Failure("DeepSearch.Failed", ex.Message));
        }
    }
} 