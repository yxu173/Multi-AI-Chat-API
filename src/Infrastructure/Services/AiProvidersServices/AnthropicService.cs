using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Interfaces;
using Application.Services;
using Domain.Aggregates.Chats;
using Domain.Aggregates.Users;
using Domain.ValueObjects;
using Infrastructure.Services.AiProvidersServices.Base;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Application.Services.AI;

public class AnthropicService : BaseAiService
{
    private const string BaseUrl = "https://api.anthropic.com/v1/";
    private const string AnthropicVersion = "2023-06-01";

    public AnthropicService(IHttpClientFactory httpClientFactory, string apiKey, string modelCode)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl)
    {
    }

    protected override void ConfigureHttpClient()
    {
        HttpClient.DefaultRequestHeaders.Clear();
        HttpClient.DefaultRequestHeaders.Add("x-api-key", ApiKey);
        HttpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
        HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    protected override string GetEndpointPath() => "messages";

    protected override async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        StringBuilder dataBuffer = new StringBuilder();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;

            if (string.IsNullOrWhiteSpace(line))
            {
                // Blank line indicates end of event
                if (dataBuffer.Length > 0)
                {
                    var completeJsonData = dataBuffer.ToString();
                    dataBuffer.Clear();
                    if (!string.IsNullOrWhiteSpace(completeJsonData))
                    {
                        yield return completeJsonData;
                    }
                }
            }
            else if (line.StartsWith("data:"))
            {
                   var dataContent = line.Length > 5 ? line.Substring(5).TrimStart() : string.Empty;
                dataBuffer.Append(dataContent);
            }
        }
        
        if (dataBuffer.Length > 0)
        {
             var finalJsonData = dataBuffer.ToString();
             if (!string.IsNullOrWhiteSpace(finalJsonData))
             {
                 yield return finalJsonData;
             }
        }
    }

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
            await HandleApiErrorAsync(response ?? new HttpResponseMessage(httpEx.StatusCode ?? System.Net.HttpStatusCode.InternalServerError), "Anthropic");
            yield break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending request to Anthropic: {ex.Message}");
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
                    if (doc.RootElement.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
                    {
                        isCompletion = typeElement.GetString() == "message_stop";
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