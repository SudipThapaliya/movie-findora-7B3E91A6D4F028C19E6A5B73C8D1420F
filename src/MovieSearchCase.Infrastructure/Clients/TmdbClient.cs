using System.Net.Http.Json;
using MovieSearchCase.Domain.Entities;
using MovieSearchCase.Domain.Exceptions;
using MovieSearchCase.Domain.Interfaces.Clients;

namespace MovieSearchCase.Infrastructure.Clients;

public class TmdbClient : ITmdbClient
{
    private readonly HttpClient _httpClient;

    public TmdbClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<Movie>> GetTrendingMoviesAsync(CancellationToken cancellationToken)
    {
        TmdbPagedResponse<TmdbMovie>? response;

        try
        {
            response = await _httpClient.GetFromJsonAsync<TmdbPagedResponse<TmdbMovie>>(
                "trending/movie/week",
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new MovieException(
                MovieException.ExceptionTitle,
                ErrorType.UpstreamServiceUnavailable,
                "TMDB did not return trending movies.");
        }

        if (response is null)
        {
            throw new MovieException(
                MovieException.ExceptionTitle,
                ErrorType.UpstreamServiceUnavailable,
                "TMDB returned an empty trending movies response.");
        }

        return response.Results.Select(TmdbMovieMapper.ToDomainModel).ToList();
    }

    public async Task<PagedResult<Movie>> SearchMoviesAsync(string query, int page, CancellationToken ct)
    {
        TmdbPagedResponse<TmdbMovie>? response;

        try
        {
            string path = $"search/movie?query={Uri.EscapeDataString(query)}&page={page}";
            response = await _httpClient.GetFromJsonAsync<TmdbPagedResponse<TmdbMovie>>(path, ct);
        }
        catch (HttpRequestException)
        {
            throw new MovieException(MovieException.ExceptionTitle, ErrorType.UpstreamServiceUnavailable, "TMDB did not return search results.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new MovieException(MovieException.ExceptionTitle, ErrorType.UpstreamServiceUnavailable, "TMDB did not respond in time with search results.");
        }
        if (response is null)
        {
            throw new MovieException(MovieException.ExceptionTitle, ErrorType.UpstreamServiceUnavailable, "TMDB returned an empty search results response.");
        }

        return new PagedResult<Movie>
        {
            Page = response.Page,
            // TMDB returns total_pages = 1 when nothing matches; the contract wants 0.
            TotalPages = response.TotalResults == 0 ? 0 : response.TotalPages,
            TotalResults = response.TotalResults,
            Results = response.Results.Select(TmdbMovieMapper.ToDomainModel).ToList(),
        };
    }
}
