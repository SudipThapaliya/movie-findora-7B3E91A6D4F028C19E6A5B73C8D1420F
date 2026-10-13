using MovieSearchCase.Domain.Entities;
using MovieSearchCase.WebApi.Models.Movies;

namespace MovieSearchCase.WebApi.Mappers;

internal static class MovieMapper
{
    public static MovieDto ToApiModel(this Movie movie) => new()
    {
        Id = movie.Id,
        Title = movie.Title,
        Overview = movie.Overview,
        PosterPath = movie.PosterPath,
        BackdropPath = movie.BackdropPath,
        VoteAverage = movie.VoteAverage,
        ReleaseDate = movie.ReleaseDate,
    };

    public static PagedResultDto<MovieDto> ToApiModel(this PagedResult<Movie> pagedResult) => new()
    {
        Page = pagedResult.Page,
        TotalPages = pagedResult.TotalPages,
        TotalResults = pagedResult.TotalResults,
        Results = pagedResult.Results.Select(movie => movie.ToApiModel()),
    };
}
