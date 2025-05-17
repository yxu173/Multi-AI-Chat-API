using System;
using System.Net;

namespace Application.Exceptions;

public class ProviderRateLimitException : Exception
{
    public Guid? ApiKeyIdUsed { get; }
    public TimeSpan? RetryAfter { get; }
    public HttpStatusCode StatusCode { get; }

    public ProviderRateLimitException(
        string message,
        HttpStatusCode statusCode,
        TimeSpan? retryAfter = null,
        Guid? apiKeyIdUsed = null,
        Exception? innerException = null) 
        : base(message, innerException)
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
        ApiKeyIdUsed = apiKeyIdUsed;
    }

    public ProviderRateLimitException(
        HttpStatusCode statusCode,
        TimeSpan? retryAfter = null,
        Guid? apiKeyIdUsed = null)
        : this($"The AI provider reported a rate limit error ({(int)statusCode}).", statusCode, retryAfter, apiKeyIdUsed)
    {
    }
} 