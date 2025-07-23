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
           
            var sendMessageCommand = new SendMessageCommand(
                command.ChatSessionId,
                command.UserId,
                command.Content,
                null, 
                null, 
                command.EnableThinking,
                command.ImageSize,
                command.NumImages,
                command.OutputFormat,
                true // EnableDeepSearch
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
                null, 
                null, 
                command.EnableThinking,
                command.ImageSize,
                command.NumImages,
                command.OutputFormat,
                true // EnableDeepSearch
            );
            await sendMessageCommand.ExecuteAsync(cancellationToken);
            return Result.Failure(Error.Failure("DeepSearch.Failed", ex.Message));
        }
    }
} 