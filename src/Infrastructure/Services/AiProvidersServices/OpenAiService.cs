using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Infrastructure.Services.AiProvidersServices.Base;
using System.IO;
using Application.Services;

public class OpenAiService : BaseAiService
{
    private const string BaseUrl = "https://api.openai.com/v1/";

    public OpenAiService(IHttpClientFactory httpClientFactory, string apiKey, string modelCode)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl)
    {
    }

    protected override void ConfigureHttpClient()
    {
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
        HttpClient.DefaultRequestHeaders.Add("Accept", "text/event-stream");
    }

    protected override string GetEndpointPath() => "responses";

    // Override ReadStreamAsync for Assistants API v2 SSE format
    protected override async IAsyncEnumerable<string> ReadStreamAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        string? eventName = null;

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;

            if (string.IsNullOrWhiteSpace(line))
            {
                // Blank line indicates end of event - reset event name
                eventName = null;
                continue;
            }

            if (line.StartsWith("event:"))
            {
                eventName = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:"))
            {
                var jsonData = line["data:".Length..].Trim();
                
                // We just yield the JSON data part. The parser will handle the event type.
                // Don't check for [DONE] here, the completion event is handled by the parser.
                if (!string.IsNullOrEmpty(jsonData))
                {
                     yield return jsonData;
                }
            }
            // Ignore other lines (like comments starting with ':')
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
            await HandleApiErrorAsync(response ?? new HttpResponseMessage(httpEx.StatusCode ?? System.Net.HttpStatusCode.InternalServerError), "OpenAI");
            yield break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending request to OpenAI: {ex.Message}");
            throw;
        }
        finally
        {
        }
        
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