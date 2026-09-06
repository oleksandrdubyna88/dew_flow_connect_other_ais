namespace CoaiServer;

/// <summary>Submit a review, watch it, give up on it.</summary>
public static class ReviewEndpoints
{
    /// <summary>The longest a poll may hold the connection open.</summary>
    /// <remarks>
    /// Twenty-five seconds: long enough that a client watching a review makes roughly two requests a
    /// minute — which is why polling never reaches the rate limiter and the limiter's window can stay
    /// tight for everything else — and short enough to sit inside every proxy's idle timeout. The
    /// wait is a <see cref="TaskCompletionSource"/>, so an open poll occupies no thread.
    /// </remarks>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(25);

    public static void MapReviewEndpoints(
        this WebApplication app, JobStore jobs, VendorCatalogHost catalog, CallerFilter gate, TimeSpan? queueWait = null)
    {
        app.MapPost("/api/reviews", async (HttpContext ctx, ReviewRequestDto request) =>
        {
            var caller = ctx.CallerOf();
            var current = catalog.Current;

            if (Refusal(current, request) is { } refusal)
            {
                return Results.Json(new ErrorDto(refusal), ServerJsonContext.Default.ErrorDto,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var now = DateTimeOffset.UtcNow;
            var budget = TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 30, 1800));
            var (job, refused, position) = jobs.Submit(new JobRecord(
                JobId.New(),
                caller.Email,
                request.Vendor,
                request.Model,
                request.Role ?? string.Empty,
                request.Prompt,
                JobStatus.Queued,
                now,
                now + (queueWait ?? JobTransitions.DefaultQueueWait),
                budget));

            if (refused == SubmitRefusal.TooManyQueued)
            {
                // A queue nobody drains is just a way to hold other people's turn, so the refusal is
                // a 429 with a time — not a silent accept that never runs.
                ctx.Response.Headers.RetryAfter = "30";

                return Results.Json(
                    new ErrorDto(
                        $"you already have {jobs.PerCallerQueued} reviews waiting. They run as accounts "
                        + "free up; this one was not accepted so it cannot sit behind them."),
                    ServerJsonContext.Default.ErrorDto,
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            await Task.CompletedTask;

            return Results.Json(
                new ReviewAcceptedDto(job!.Id, position),
                ServerJsonContext.Default.ReviewAcceptedDto,
                statusCode: StatusCodes.Status202Accepted);
        }).RequireCaller(gate);

        app.MapGet("/api/reviews/{id}", async (HttpContext ctx, string id, int? wait) =>
        {
            var caller = ctx.CallerOf();
            var job = jobs.Find(id, caller.Email);

            // A finished job answers AT ONCE, whatever `wait` says. Waiting for a "change" that has
            // already happened is how a client that reconnects after a blip sits for 25 seconds
            // staring at an answer the server already has. (Plan round.)
            if (job is not null && !job.IsTerminal && wait is > 0)
            {
                var budget = TimeSpan.FromSeconds(Math.Min(wait.Value, MaxWait.TotalSeconds));
                await jobs.WaitForChangeAsync(budget, ctx.RequestAborted);
                job = jobs.Find(id, caller.Email);
            }

            return job is null
                ? Missing(id, caller.Email, jobs)
                : Results.Json(Describe(job, jobs), ServerJsonContext.Default.ReviewStatusDto);
        }).RequireCaller(gate);

        app.MapDelete("/api/reviews/{id}", (HttpContext ctx, string id) =>
        {
            var caller = ctx.CallerOf();

            return jobs.Cancel(id, caller.Email, DateTimeOffset.UtcNow) is not null
                ? Results.NoContent()
                : Missing(id, caller.Email, jobs);
        }).RequireCaller(gate);
    }

    /// <summary>Why this submission cannot be accepted, or null.</summary>
    /// <remarks>
    /// The allowlist is checked HERE, before anything is queued, and the refusal NAMES what is
    /// allowed. A caller who asked for a model the company does not pay for should not have to guess
    /// which of the two names was wrong.
    /// </remarks>
    private static string? Refusal(VendorCatalog catalog, ReviewRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return "a review needs a prompt";
        }

        if (catalog.Find(request.Vendor ?? string.Empty) is not { } vendor)
        {
            return catalog.Vendors.Count == 0
                ? "this server has no vendors configured yet"
                : $"'{request.Vendor}' is not a vendor here. Available: "
                    + string.Join(", ", catalog.Vendors.Select(v => v.Id));
        }

        return catalog.Allows(vendor.Id, request.Model ?? string.Empty)
            ? null
            : $"'{request.Model}' is not allowed for {vendor.Id}. Allowed: " + string.Join(", ", vendor.Models);
    }

    /// <summary>
    /// The answer for an id this caller cannot read.
    /// </summary>
    /// <remarks>
    /// <b>403 when it exists and belongs to somebody else</b>, 404 otherwise. The first draft hid
    /// existence behind a 404 for both, and two reviewers said the same thing about it: every caller
    /// here is an authenticated member of one company and a job id is an unguessable
    /// <c>&lt;epoch&gt;-&lt;guid&gt;</c>, so confirming existence to a colleague leaks nothing worth
    /// having — while the debugging story it created was real. Somebody chasing a missing review
    /// could not tell "I mistyped the id" from "the server dropped it", and the two lead in opposite
    /// directions.
    /// </remarks>
    private static IResult Missing(string id, string email, JobStore jobs)
    {
        if (jobs.BelongsToSomeoneElse(id, email))
        {
            return Results.Json(
                new ErrorDto("that review belongs to somebody else on this server"),
                ServerJsonContext.Default.ErrorDto,
                statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Json(
            new ErrorDto(JobId.Explain(JobId.Classify(id, JobId.Epoch))),
            ServerJsonContext.Default.ErrorDto,
            statusCode: StatusCodes.Status404NotFound);
    }

    private static ReviewStatusDto Describe(JobRecord job, JobStore jobs) =>
        new(
            job.Id,
            job.Status.ToString().ToLowerInvariant(),
            jobs.PositionOf(job),
            job.Answer,
            Math.Round(job.Elapsed.TotalSeconds, 1),
            job.TokensIn,
            job.TokensOut,
            job.Failure == FailureKind.None ? string.Empty : Wire(job.Failure),
            job.Reason);

    /// <summary>The failure name a client already renders.</summary>
    private static string Wire(FailureKind kind) => kind switch
    {
        FailureKind.NonZeroExit => "non_zero_exit",
        FailureKind.TimedOut => "timed_out",
        FailureKind.UnparseableByVendor => "unparseable_by_vendor",
        FailureKind.RateLimited => "rate_limited",
        FailureKind.NotStarted => "not_started",
        FailureKind.Cancelled => "cancelled",
        FailureKind.Lost => "lost",
        _ => string.Empty,
    };
}
