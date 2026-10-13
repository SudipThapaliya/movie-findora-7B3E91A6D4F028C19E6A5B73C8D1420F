using Microsoft.AspNetCore.Mvc;
using MovieSearchCase.Domain.Exceptions;
using MovieSearchCase.Domain.Interfaces.Services;
using MovieSearchCase.Shared;
using MovieSearchCase.WebApi.Mappers;

namespace MovieSearchCase.WebApi.Handlers.Movies;

public class SearchMoviesHandler : IRequestHandlerAsync
{
    // TMDB rejects pages outside 1..500, so the contract uses the same range.
    public const int MinPage = 1;
    public const int MaxPage = 500;

    private readonly IMovieService _movieService;
    private readonly string _query;
    private readonly int _page;

    public SearchMoviesHandler(IMovieService movieService, string query, int page)
    {
        _movieService = movieService;
        _query = query;
        _page = page;
    }

    public async Task<IActionResult> HandleAsync(HttpRequest request)
    {
        // Validated here rather than left to TMDB: TMDB returns 200 for a blank query
        // and 400 for a bad page, which the client would surface as 503.
        if (string.IsNullOrWhiteSpace(_query))
        {
            throw InvalidRequest("The 'query' parameter is required.");
        }

        if (_page is < MinPage or > MaxPage)
        {
            throw InvalidRequest($"The 'page' parameter must be a whole number from {MinPage} to {MaxPage}.");
        }

        var result = await _movieService.SearchAsync(_query.Trim(), _page, request.HttpContext.RequestAborted);

        return new OkObjectResult(result.ToApiModel());
    }

    private static MovieException InvalidRequest(string detail)
    {
        return new(ProblemDetailsException.InvalidRequestTitle, ErrorType.InvalidRequest, detail);
    }
}
