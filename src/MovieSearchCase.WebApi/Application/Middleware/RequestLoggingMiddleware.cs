using Microsoft.AspNetCore.Mvc.Infrastructure;
using MovieSearchCase.Domain.Exceptions;
using MovieSearchCase.WebApi.Application.ProblemDetails;

namespace MovieSearchCase.WebApi.Application.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext httpContext, ProblemDetailsFactory problemDetailsFactory)
    {
        try
        {
            await _next(httpContext);
        }
        catch (Exception exception)
        {
            var (statusCode, title, detail, instance) = exception switch
            {
                ProblemDetailsException problemDetailsException => (
                    problemDetailsException.ErrorCode.ToStatusCode(),
                    problemDetailsException.Title,
                    problemDetailsException.Message,
                    problemDetailsException.Instance.ToString()),
                HttpRequestException => (
                    StatusCodes.Status503ServiceUnavailable,
                    "Upstream service unavailable",
                    exception.Message,
                    httpContext.Request.Path.ToString()),
                _ => (
                    StatusCodes.Status500InternalServerError,
                    "An unexpected error occurred",
                    // Unexpected exception messages can leak internals; the log keeps the details.
                    "The request could not be completed.",
                    httpContext.Request.Path.ToString()),
            };

            _logger.LogError(exception, "Request to {Path} failed with status {StatusCode}", httpContext.Request.Path, statusCode);

            var problemDetails = problemDetailsFactory.CreateProblemDetails(
                httpContext,
                statusCode,
                title,
                type: ProblemDetailsExtensions.DefaultType,
                instance: instance,
                detail: detail);

            httpContext.Response.StatusCode = statusCode;
            // contentType must be passed here: WriteAsJsonAsync otherwise overwrites it with application/json.
            await httpContext.Response.WriteAsJsonAsync(
                problemDetails,
                options: null,
                contentType: ProblemDetailsExtensions.ContentType);
        }
    }
}

public static class RequestLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestLoggingMiddleware>();
}
