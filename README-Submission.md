# Submission notes — Movie Search backend

Setup, configuration and run instructions are in [README.md](README.md#getting-started).
In short: put a TMDB **API Read Access Token (v4)** in
`src/MovieSearchCase.WebApi/appsettings.Development.local.json` (git-ignored), then:

```bash
dotnet run --project src/MovieSearchCase.WebApi   # http://localhost:5080, Swagger at /swagger
dotnet test
```

## What I built

### P0 — `GET /api/v1/movies/search?query=&page=`

The endpoint follows the existing trending flow, layer by layer:

| Layer | Change |
|---|---|
| Controller | `MoviesController.Search` — one-liner that resolves a handler from the factory, like `GetTrending`. |
| Factory | `SearchMovies(string query, int page)` on `IMoviesRequestHandlerFactory` / `MoviesRequestHandlerFactory`. |
| Handler | `SearchMoviesHandler` — validates the parameters, calls the service, maps to the API model. |
| Service | `IMovieService.SearchAsync` → `MovieService` passes through to the client. |
| Client | `ITmdbClient.SearchMoviesAsync` → `TmdbClient` calls TMDB `search/movie`, same try/catch + mapping pattern as trending. |
| Models | `PagedResult<T>` in Domain; `PagedResultDto<T>` and `MovieDto` in WebApi (`page`, `totalPages`, `totalResults`, `results`), mapped in `MovieMapper`, following the existing Domain/API model split. I renamed the API models with a `Dto` suffix so they can't be confused with the Domain types of the same name. |

**Validation happens in the handler, before TMDB is called.** I checked TMDB's behaviour directly:

- a blank or spaces-only `query` returns `200` with no results, so without our own check the API would silently return `200` instead of `400`;
- `page=0` or `page=501` returns TMDB `400`, which `HttpClient` raises as `HttpRequestException`, so the API would report `503` ("TMDB is down") for what is really a client error.

Rules, matching the contract:

- `query` is required; empty or whitespace → `400` "The 'query' parameter is required.". The query is trimmed before it is sent.
- `page` defaults to `1` and must be from 1 to 500; out of range → `400` "The 'page' parameter must be a whole number from 1 to 500.".
- A non-integer `page` (`abc`, `1.5`, empty) fails model binding → `400` with the same problem shape.
- The query is URL-encoded (`Uri.EscapeDataString`), so `&`, `#` and `+` in a title don't break the TMDB request.

**Pagination.** TMDB returns `total_pages: 1` (not 0) when nothing matches. The contract requires `totalPages: 0`, so `TmdbClient` normalises it. The adjustment sits in the client because it is a TMDB quirk, and the rest of the app shouldn't need to know about it.

### Error format fixes (required by the contract)

The contract says every error uses `application/problem+json` with `"type": "about:blank"`. Running the original API showed two mismatches in the existing middleware:

| | Contract | Original behaviour |
|---|---|---|
| `type` | `about:blank` | `https://tools.ietf.org/html/rfc9110#section-15.6.4` (`ProblemDetailsFactory` fills in RFC links by default) |
| `Content-Type` | `application/problem+json` | `application/json` (`WriteAsJsonAsync` overwrites the header that was set before it) |

Fixes:

- `RequestLoggingMiddleware` passes `type: "about:blank"` explicitly, and passes the content type into `WriteAsJsonAsync`.
- The model-binding `400` (`InvalidModelStateResponseFactory`) used `ValidationProblemDetails` (different title, `errors` object, no `detail`). It now produces the same shape as the other errors. It returns a `JsonResult`, because the controllers' `[Produces("application/json")]` filter overwrites an `ObjectResult`'s content type.
- A TMDB timeout during search now maps to `503`. `HttpClient` reports its own timeout as `TaskCanceledException`, which the original pattern did not catch (it became a `500`). Cancellation by the caller is not caught and still propagates.
- **Security:** unexpected `500` errors no longer return `exception.Message` to the client. The full exception is still logged.

I treated these as contract fixes rather than restructuring: no files moved, and the middleware and handler/factory patterns are unchanged.

### P1 — Tests

20 tests in total (2 existing, 18 new), using the existing xUnit, Moq and FluentAssertions setup:

- `SearchMoviesHandlerTests` — mapping to the contract shape, query trimming, blank-query and page-range rejection (the service is not called), and the page boundaries 1 and 500.
- `MovieServiceTests` — search passes through to the client.
- `TmdbClientTests` — new, using a stub `HttpMessageHandler`: request URL and escaping, field mapping (nulls, invalid release date), the `totalPages: 0` normalisation, TMDB failures (`500`, `503`, `401`) → `503`, timeout → `503`, and caller cancellation propagating.

I also checked every contract case by hand against the running API, with real TMDB data and with TMDB unreachable.

## What I skipped, and why

- **P2 (`/movies/{id}`, `/movies/{id}/videos`) and P3 (TV search, caching).** I put the time into making P0 exactly match the contract and covering it with tests, rather than adding more endpoints with less verification.
- **Trending is unchanged.** It benefits from the middleware fixes, but a TMDB timeout on trending still returns `500` rather than `503`, because changing trending wasn't part of the task. The fix is the same `catch` used in search.
- **No integration tests over HTTP.** The error shape is checked manually. An automated test would need `Microsoft.AspNetCore.Mvc.Testing`, and I didn't add a package for it.
- **The `HttpClient` timeout stays at the default 100 s.** For a search box, I would set it lower (around 10 s) so the user gets a `503` quickly.
- **Accessibility (WCAG 2.2).** This is a backend-only task, so accessibility is mostly the frontend's job. The API supports it by returning a plain-English `detail` in every error, which the frontend can show or announce to screen readers.

## Contract points I found unclear or would change

1. **`totalPages: 0` for an empty result.** I implemented it as specified. `page: 1` together with `totalPages: 0` is slightly contradictory, but it lets the frontend check `totalPages === 0` to show "no results". I would keep it and just note that it differs from TMDB.
2. **`page` beyond the last page** (e.g. `page=50` when there is 1 page). The contract doesn't say. The API returns `200` with `results: []` and echoes `page: 50`, which is what TMDB does. A `400` would also be defensible, but it would require an extra call.
3. **Extra fields in error responses.** Errors also include `traceId` (useful for support) and `instance: "about:blank"`, both from the existing code. Clients normally ignore unknown fields, but the contract says "exactly", so I'd like that confirmed. `instance: "about:blank"` also looks like a mix-up in `ProblemDetailsException` between `type` and `instance`; I would remove it.
4. **TMDB rejecting our token returns `503`.** Any non-2xx response from TMDB, including `401`, becomes "TMDB is down". A bad token is our misconfiguration, not an outage, so I would map it to `500`.
5. **`query` has no maximum length.** I would add a reasonable limit (e.g. 200 characters) to protect the upstream call.
6. **`/movies/{id}/videos` for an unknown movie.** The contract specifies `404` for `/movies/{id}` but not for its videos. I would make both return `404` for consistency.
7. **Non-integer `page`** (`abc`, `1.5`). I return `400`, reading "whole number" strictly.

TMDB caps search at 500 pages and 10,000 results, which matches the 1–500 page rule, so `totalResults` never implies more pages than a client can request.
