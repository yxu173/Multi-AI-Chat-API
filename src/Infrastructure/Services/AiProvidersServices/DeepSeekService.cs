using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
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
                string? toolCallsFinishReason = null;
                
                try
                {
                    using var doc = JsonDocument.Parse(jsonChunk);
                    if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                    { 
                         if(choices[0].TryGetProperty("finish_reason", out var finishReason) && finishReason.ValueKind != JsonValueKind.Null)
                         {
                            string? finishReasonValue = finishReason.GetString();
                            isCompletion = true;
                            
                            // Check for tool calls specifically (like OpenAI's API)
                            if (finishReasonValue == "tool_calls" || finishReasonValue == "function_call")
                            {
                                toolCallsFinishReason = finishReasonValue;
                                Console.WriteLine($"DeepSeek stream detected tool call completion with reason: {finishReasonValue}");
                            }
                         }
                         
                         // Check for tool calls in delta (similar to OpenAI's format)
                         if (choices[0].TryGetProperty("delta", out var delta) && 
                             delta.TryGetProperty("tool_calls", out var toolCalls) && 
                             toolCalls.ValueKind == JsonValueKind.Array && 
                             toolCalls.GetArrayLength() > 0)
                         {
                             Console.WriteLine($"DeepSeek stream chunk contains tool_calls data");
                             
                             // If we have a chunk with tool call data but no finish reason
                             // and this is the completion chunk, we should ensure it's marked appropriately
                             if (isCompletion && toolCallsFinishReason == null)
                             {
                                 Console.WriteLine("Setting completion reason to tool_calls based on content");
                                 toolCallsFinishReason = "tool_calls";
                             }
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