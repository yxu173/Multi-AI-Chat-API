using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;
using DotnetGeminiSDK.Client.Interfaces;
using DotnetGeminiSDK.Model.Request;

namespace Infrastructure.Services;

public class GeminiService : IAiModelService
{
    private readonly IGeminiClient _geminiClient;

    public GeminiService(IGeminiClient geminiClient)
    {
        _geminiClient = geminiClient;
    }

    public IAsyncEnumerable<string> StreamResponseAsync(IEnumerable<MessageDto> history)
    {
        throw new NotImplementedException();
    }
}