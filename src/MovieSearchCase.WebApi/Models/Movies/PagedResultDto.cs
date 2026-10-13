namespace MovieSearchCase.WebApi.Models.Movies;

/// <summary>
/// API response shape for a page of results, as defined by the API contract.
/// </summary>
public record PagedResultDto<T>
{
    public required int Page { get; init; }

    public required int TotalPages { get; init; }

    public required int TotalResults { get; init; }

    public required IEnumerable<T> Results { get; init; }
}
