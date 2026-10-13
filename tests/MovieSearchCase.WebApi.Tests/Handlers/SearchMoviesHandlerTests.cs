using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MovieSearchCase.Domain.Entities;
using MovieSearchCase.Domain.Exceptions;
using MovieSearchCase.Domain.Interfaces.Services;
using MovieSearchCase.WebApi.Handlers.Movies;
using Xunit;
using ApiMovie = MovieSearchCase.WebApi.Models.Movies.MovieDto;
using ApiPagedResult = MovieSearchCase.WebApi.Models.Movies.PagedResultDto<MovieSearchCase.WebApi.Models.Movies.MovieDto>;

namespace MovieSearchCase.WebApi.Tests.Handlers;

public class SearchMoviesHandlerTests
{
    private readonly Mock<IMovieService> _movieServiceMock = new();

    [Fact]
    public async Task HandleShouldReturnOkWithMappedPageWhenServiceSucceeds()
    {
        var searchResult = new PagedResult<Movie>
        {
            Page = 2,
            TotalPages = 12,
            TotalResults = 231,
            Results = new List<Movie>
            {
                new()
                {
                    Id = 268,
                    Title = "Batman",
                    VoteAverage = 7.2,
                },
            },
        };

        _movieServiceMock
            .Setup(service => service.SearchAsync("batman", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);

        var result = await CreateHandler("batman", 2).HandleAsync(new DefaultHttpContext().Request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var page = okResult.Value.Should().BeOfType<ApiPagedResult>().Subject;
        page.Page.Should().Be(2);
        page.TotalPages.Should().Be(12);
        page.TotalResults.Should().Be(231);
        page.Results.Should().ContainSingle(movie => movie.Id == 268 && movie.Title == "Batman");
    }

    [Fact]
    public async Task HandleShouldTrimQueryBeforeCallingService()
    {
        _movieServiceMock
            .Setup(service => service.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<Movie> { Page = 1, TotalPages = 0, TotalResults = 0, Results = [] });

        await CreateHandler("  batman  ", 1).HandleAsync(new DefaultHttpContext().Request);

        _movieServiceMock.Verify(service => service.SearchAsync("batman", 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleShouldRejectEmptyOrBlankQuery(string query)
    {
        var act = () => CreateHandler(query, 1).HandleAsync(new DefaultHttpContext().Request);

        var exception = await act.Should().ThrowAsync<MovieException>();
        exception.Which.ErrorCode.Should().Be(ErrorType.InvalidRequest);
        exception.Which.Message.Should().Be("The 'query' parameter is required.");
        VerifyServiceNotCalled();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(501)]
    public async Task HandleShouldRejectPageOutsideAllowedRange(int page)
    {
        var act = () => CreateHandler("batman", page).HandleAsync(new DefaultHttpContext().Request);

        var exception = await act.Should().ThrowAsync<MovieException>();
        exception.Which.ErrorCode.Should().Be(ErrorType.InvalidRequest);
        exception.Which.Message.Should().Be("The 'page' parameter must be a whole number from 1 to 500.");
        VerifyServiceNotCalled();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    public async Task HandleShouldAcceptPageBoundaries(int page)
    {
        _movieServiceMock
            .Setup(service => service.SearchAsync("batman", page, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<Movie> { Page = page, TotalPages = 0, TotalResults = 0, Results = [] });

        var result = await CreateHandler("batman", page).HandleAsync(new DefaultHttpContext().Request);

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<ApiPagedResult>()
            .Which.Results.Should().BeEmpty();
    }

    private SearchMoviesHandler CreateHandler(string query, int page) =>
        new(_movieServiceMock.Object, query, page);

    private void VerifyServiceNotCalled() =>
        _movieServiceMock.Verify(
            service => service.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
}
