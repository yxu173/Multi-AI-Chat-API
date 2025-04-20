namespace Web.Api.Middleware
{
    public class RequestContextLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestContextLoggingMiddleware> _logger;

        public RequestContextLoggingMiddleware(RequestDelegate next, ILogger<RequestContextLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            var request = context.Request;
            var requestPath = request.Path;
            var requestMethod = request.Method;
            var requestQueryString = request.QueryString;
            var requestBody = await GetRequestBody(request);

            _logger.LogInformation($"Request: {requestMethod} {requestPath}{requestQueryString} {requestBody}");

            await _next(context);
        }

        private async Task<string> GetRequestBody(HttpRequest request)
        {
            request.EnableBuffering();
            var body = await new StreamReader(request.Body).ReadToEndAsync();
            request.Body.Position = 0;
            return body;
        }
    }
}
