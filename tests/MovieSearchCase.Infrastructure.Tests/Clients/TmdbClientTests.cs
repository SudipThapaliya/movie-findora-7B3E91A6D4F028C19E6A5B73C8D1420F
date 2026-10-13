using System.Net;
using System.Text;
using FluentAssertions;
using MovieSearchCase.Domain.Exceptions;
using MovieSearchCase.Infrastructure.Clients;
using Xunit;

namespace MovieSearchCase.Infrastructure.Tests.Clients;

public class TmdbClientTests
{
    private const string BaseUrl = "https://api.themoviedb.org/3/";

    [Fact]
    public async Task SearchMoviesAsyncShouldCallSearchEndpointWithEscapedQueryAndPage()
    {
        var handler = new StubHttpMessageHandler(_ => Json("""{ "page": 2, "results": [], "total_pages": 0, "total_results": 0 }"""));

        await CreateClient(handler).SearchMoviesAsync("tom & jerry #1", 2, CancellationToken.None);

        handler.LastRequest!.RequestUri!.AbsoluteUri.Should()
            .Be($"{BaseUrl}search/movie?query=tom%20%26%20jerry%20%231&page=2");
    }

    [Fact]
    public async Task SearchMoviesAsyncShouldMapPageAndMovies()
    {
        var handler = new StubHttpMessageHandler(_ => Json("""
            {
              "page": 1,
              "total_pages": 12,
              "total_results": 231,
              "results": [
                {
                  "id": 268,
                  "title": "Batman",
                  "overview": null,
                  "poster_path": "/cij4dd21v2Rk2YtUQbV5kW69WB2.jpg",
                  "backdrop_path": null,
                  "vote_average": 7.2,
                  "release_date": "1989-06-21"
                },
                {
                  "id": 1,
                  "title": "No Release Date",
                  "vote_average": 0,
                  "release_date": ""
                }
              ]
            }
            """));

        var result = await CreateClient(handler).SearchMoviesAsync("batman", 1, CancellationToken.None);

        result.Page.Should().Be(1);
        result.TotalPages.Should().Be(12);
        result.TotalResults.Should().Be(231);
        result.Results.Should().HaveCount(2);
        result.Results[0].Id.Should().Be(268);
        result.Results[0].PosterPath.Should().Be("/cij4dd21v2Rk2YtUQbV5kW69WB2.jpg");
        result.Results[0].BackdropPath.Should().BeNull();
        result.Results[0].ReleaseDate.Should().Be(new DateOnly(1989, 6, 21));
        result.Results[1].ReleaseDate.Should().BeNull();
    }

    [Fact]
    public async Task SearchMoviesAsyncShouldReportZeroPagesWhenNothingMatches()
    {
        // Real TMDB response for a query with no matches: total_pages is 1, not 0.
        var handler = new StubHttpMessageHandler(_ => Json("""{ "page": 1, "results": [], "total_pages": 1, "total_results": 0 }"""));

        var result = await CreateClient(handler).SearchMoviesAsync("zzqxv", 1, CancellationToken.None);

        result.TotalPages.Should().Be(0);
        result.TotalResults.Should().Be(0);
        result.Results.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task SearchMoviesAsyncShouldThrowUpstreamUnavailableWhenTmdbFails(HttpStatusCode statusCode)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(statusCode));

        var act = () => CreateClient(handler).SearchMoviesAsync("batman", 1, CancellationToken.None);

        (await act.Should().ThrowAsync<MovieException>())
            .Which.ErrorCode.Should().Be(ErrorType.UpstreamServiceUnavailable);
    }

    [Fact]
    public async Task SearchMoviesAsyncShouldThrowUpstreamUnavailableWhenTmdbTimesOut()
    {
        var handler = new StubHttpMessageHandler(_ => throw new TaskCanceledException("timeout"));

        var act = () => CreateClient(handler).SearchMoviesAsync("batman", 1, CancellationToken.None);

        (await act.Should().ThrowAsync<MovieException>())
            .Which.ErrorCode.Should().Be(ErrorType.UpstreamServiceUnavailable);
    }

    [Fact]
    public async Task SearchMoviesAsyncShouldLetCallerCancellationPropagate()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = new StubHttpMessageHandler(_ => Json("""{ "page": 1, "results": [], "total_pages": 0, "total_results": 0 }"""));

        var act = () => CreateClient(handler).SearchMoviesAsync("batman", 1, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static TmdbClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) });

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            return Task.FromResult(_respond(request));
        }
    }
}
