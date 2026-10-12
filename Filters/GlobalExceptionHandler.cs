using Microsoft.AspNetCore.Diagnostics;

namespace HRManagement.Filters
{
    /// <summary>
    /// Turns exceptions that escape a controller into a JSON { message, traceId } response.
    /// Services throw the exception types below with a message meant for the user, so that message is returned;
    /// anything else is unexpected, gets logged, and the client only sees a generic message.
    /// </summary>
    public class GlobalExceptionHandler : IExceptionHandler
    {
        private const string GenericMessage = "Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau.";

        private readonly ILogger<GlobalExceptionHandler> _logger;

        public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
        {
            _logger = logger;
        }

        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            // The client went away; nobody is left to read a response
            if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
                return true;

            var (status, message) = exception switch
            {
                KeyNotFoundException => (StatusCodes.Status404NotFound, exception.Message),
                // 403, not 401: the client treats 401 as an expired session and redirects to the login page
                UnauthorizedAccessException => (StatusCodes.Status403Forbidden, exception.Message),
                ArgumentException or InvalidOperationException => (StatusCodes.Status400BadRequest, exception.Message),
                BadHttpRequestException badRequest => (badRequest.StatusCode, "Yêu cầu không hợp lệ."),
                _ => (StatusCodes.Status500InternalServerError, GenericMessage)
            };

            if (status >= StatusCodes.Status500InternalServerError)
                _logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            else
                _logger.LogInformation("{ExceptionType} on {Method} {Path}: {Message}",
                    exception.GetType().Name, httpContext.Request.Method, httpContext.Request.Path, exception.Message);

            httpContext.Response.StatusCode = status;
            await httpContext.Response.WriteAsJsonAsync(new { message, traceId = httpContext.TraceIdentifier }, cancellationToken);
            return true;
        }
    }
}
