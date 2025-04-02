using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Infrastructure.Services.AiProvidersServices.Base;

public class DeepSeekService : BaseAiService
{
    private const string BaseUrl = "https://api.deepseek.com/v1/";

    public DeepSeekService(IHttpClientFactory httpClientFactory, string apiKey, string modelCode)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl)
    {
    }

    protected override void ConfigureHttpClient()
    {
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
    }

    protected override string GetEndpointPath() => "chat/completions";

    public override async IAsyncEnumerable<AiRawStreamChunk> StreamResponseAsync(
        AiRequestPayload requestPayload,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = CreateRequest(requestPayload);
        HttpResponseMessage? response = null;

        try
        {
            response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException httpEx)
        {
            await HandleApiErrorAsync(response ?? new HttpResponseMessage(httpEx.StatusCode ?? System.Net.HttpStatusCode.InternalServerError), "DeepSeek");
            yield break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending request to DeepSeek: {ex.Message}");
            throw;
        }

        try
        {
            await foreach (var jsonChunk in ReadStreamAsync(response, cancellationToken)
                                .WithCancellation(cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested) break;

                bool isCompletion = false;
                try
                {
                    using var doc = JsonDocument.Parse(jsonChunk);
                    if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                    { 
                         if(choices[0].TryGetProperty("finish_reason", out var finishReason) && finishReason.ValueKind != JsonValueKind.Null)
                         {
                            isCompletion = true;
                         }
                    }
                }
                catch (JsonException) { /* Ignore parse errors, yield raw chunk anyway */ }

                yield return new AiRawStreamChunk(jsonChunk, isCompletion);
            }
        }
        finally
        {
            response.Dispose();
        }
    }
}