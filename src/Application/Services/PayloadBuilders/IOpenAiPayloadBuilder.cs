using Application.Services;
using Domain.Enums;

namespace Application.Services.PayloadBuilders;

public interface IOpenAiPayloadBuilder
{
    AiRequestPayload PreparePayload(AiRequestContext context, List<object>? toolDefinitions);
} 