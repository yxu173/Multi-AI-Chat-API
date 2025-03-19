using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;
using Infrastructure.Services.AiProvidersServices.Base;

namespace Infrastructure.Services.AiProvidersServices;

public class OpenAiService : BaseAiService
{
    private const string BaseUrl = "https://api.openai.com/v1/";

    public OpenAiService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string modelCode)
        : base(httpClientFactory, apiKey, modelCode, BaseUrl)
    {
    }

    protected override void ConfigureHttpClient()
    {
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
    }

    protected override string GetEndpointPath() => "chat/completions";

    protected override object CreateRequestBody(IEnumerable<MessageDto> history)
    {
        var messages = new List<OpenAiMessage>
        {
            new("system", "Always respond using markdown formatting")
        };

        messages.AddRange(history
            .Where(m => !string.IsNullOrEmpty(m.Content))
            .Select(m => new OpenAiMessage(m.IsFromAi ? "assistant" : "user", m.Content)));

        return new
        {
            model = ModelCode,
            messages,
            max_tokens = 2000,
            stream = true
        };
    }

    public override async IAsyncEnumerable<StreamResponse> StreamResponseAsync(IEnumerable<MessageDto> history)
    {
        var tokenizer = Tiktoken.ModelToEncoder.For(ModelCode);
        var messages = ((List<OpenAiMessage>)((dynamic)CreateRequestBody(history)).messages);

        int inputTokens = 0;
        foreach (var msg in messages)
        {
            inputTokens += tokenizer.Encode(msg.content).Count;
        }

        var request = CreateRequest(CreateRequestBody(history));

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(response, "OpenAI");
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        int outputTokens = 0;
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("data: ") && line.Trim() != "data: [DONE]")
            {
                var json = line["data: ".Length..];

                var chunk = JsonSerializer.Deserialize<OpenAiResponse>(json);
                if (chunk?.choices is { Length: > 0 } choices)
                {
                    var delta = choices[0].delta;
                    if (!string.IsNullOrEmpty(delta?.content))
                    {
                        outputTokens += tokenizer.Encode(delta.content).Count;
                        yield return new StreamResponse(delta.content, inputTokens, outputTokens);
                    }
                }
            }
        }
    }

    private record OpenAiMessage(string role, string content);

    private record OpenAiResponse(
        string id,
        string @object,
        int created,
        string model,
        Choice[] choices
    );

    private record Choice(Delta delta, int index, string finish_reason);

    private record Delta(string content);
}