using System.Text;
using System.Text.Json;
using Application.Abstractions.Interfaces;
using Application.Services;

namespace Infrastructure.Services.AiProvidersServices.Base;

public abstract class BaseAiService : IAiModelService
{
    protected readonly HttpClient HttpClient;
    protected readonly string ApiKey;
    protected readonly string ModelCode;

    protected BaseAiService(
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string modelCode,
        string baseUrl)
    {
        HttpClient = httpClientFactory.CreateClient();
        HttpClient.BaseAddress = new Uri(baseUrl);
        ApiKey = apiKey;
        ModelCode = modelCode;

        ConfigureHttpClient();
    }

    /// <summary>
    /// Configures HTTP client headers and other settings specific to the AI provider
    /// </summary>
    protected abstract void ConfigureHttpClient();

    /// <summary>
    /// Creates the request body for the AI provider API
    /// </summary>
    protected abstract object CreateRequestBody(IEnumerable<MessageDto> history);

    /// <summary>
    /// Creates the HTTP request with appropriate headers and endpoint
    /// </summary>
    protected virtual HttpRequestMessage CreateRequest(object requestBody)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, GetEndpointPath());
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");
        return request;
    }

    /// <summary>
    /// Gets the endpoint path for the AI provider API
    /// </summary>
    protected abstract string GetEndpointPath();

    /// <summary>
    /// Processes the streaming response from the AI provider
    /// </summary>
    public abstract IAsyncEnumerable<StreamResponse> StreamResponseAsync(
        IEnumerable<MessageDto> history);

    /// <summary>
    /// Common method to handle API errors
    /// </summary>
    protected async Task HandleApiErrorAsync(HttpResponseMessage response, string providerName)
    {
        var errorContent = await response.Content.ReadAsStringAsync();
        throw new Exception($"{providerName} API Error: {response.StatusCode} - {errorContent}");
    }
}