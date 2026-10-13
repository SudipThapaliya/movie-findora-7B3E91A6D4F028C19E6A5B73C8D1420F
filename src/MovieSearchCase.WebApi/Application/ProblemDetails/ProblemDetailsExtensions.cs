using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MovieSearchCase.Domain.Exceptions;

namespace MovieSearchCase.WebApi.Application.ProblemDetails;

public static class ProblemDetailsExtensions
{
    /// <summary>
    /// The contract uses "about:blank" for every error instead of the RFC links that
    /// <see cref="ProblemDetailsFactory"/> fills in by default.
    /// </summary>
    public const string DefaultType = "about:blank";

    public const string ContentType = "application/problem+json";

    public static IServiceCollection AddCustomProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails();

        services.Configure<ApiBehaviorOptions>(options =>
        {
            // Model binding failures (e.g. page=abc) use the same shape as every other error.
            options.InvalidModelStateResponseFactory = context =>
            {
                var problemDetailsFactory = context.HttpContext.RequestServices
                    .GetRequiredService<ProblemDetailsFactory>();

                var detail = string.Join(
                    " ",
                    context.ModelState
                        .Where(entry => entry.Value?.Errors.Count > 0)
                        .Select(entry => $"The '{entry.Key}' parameter is invalid. {entry.Value!.Errors[0].ErrorMessage}"));

                var problemDetails = problemDetailsFactory.CreateProblemDetails(
                    context.HttpContext,
                    StatusCodes.Status400BadRequest,
                    ProblemDetailsException.InvalidRequestTitle,
                    type: DefaultType,
                    detail: detail);

                // JsonResult rather than an ObjectResult: the controllers' [Produces("application/json")]
                // filter overwrites an ObjectResult's content type.
                return new JsonResult(problemDetails)
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ContentType = ContentType,
                };
            };
        });

        return services;
    }
}
