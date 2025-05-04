using System.Runtime.CompilerServices;
using Application.Abstractions.Interfaces;
using Application.Services;
using Application.Services.AI;
using Infrastructure.Services.AiProvidersServices.Base;

namespace Infrastructure.Services.AiProvidersServices;

public class QwenService : BaseAiService
{
    private const string QwenBaseUrl = "https://api.aimlapi.com/v1/";

    public QwenService(IHttpClientFactory httpClientFactory, string? apiKey, string modelCode)
        : base(httpClientFactory, apiKey, modelCode, QwenBaseUrl)
    {
    }

    protected override void ConfigureHttpClient()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Qwen API key is missing.");
        }
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
    }

    protected override string GetEndpointPath() => "chat/completions";

    public override async IAsyncEnumerable<AiRawStreamChunk> StreamResponseAsync(
        AiRequestPayload requestPayload,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        
        try
        {
            var request = CreateRequest(requestPayload);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
            
            response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException httpEx)
        {
            await HandleApiErrorAsync(response ?? new HttpResponseMessage(httpEx.StatusCode ?? System.Net.HttpStatusCode.InternalServerError), "Qwen");
            yield break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending request to Qwen: {ex.Message}");
            throw;
        }

        if (response != null)
        {
            try
            {
                await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken)
                                   .WithCancellation(cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    yield return new AiRawStreamChunk(jsonChunk);
                }
            }
            finally
            {
                response.Dispose();
            }
        }
    }
} 