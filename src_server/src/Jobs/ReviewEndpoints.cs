using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;

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

    /// <summary>The narrowest and widest vendor budget a caller may ask for.</summary>
    /// <remarks>
    /// Below thirty seconds a CLI has barely started; above half an hour one review can hold an
    /// account longer than most people will wait for the whole round. A request outside the range is
    /// REFUSED naming it rather than silently clamped — somebody who asked for five seconds and got
    /// thirty would draw the wrong conclusion from the result. (gemini, code round.)
    /// </remarks>
    public static readonly TimeSpan MinBudget = TimeSpan.FromSeconds(30);

    /// <inheritdoc cref="MinBudget"/>
    public static readonly TimeSpan MaxBudget = TimeSpan.FromMinutes(30);

    /// <summary>What a caller refused for a full queue is told to wait.</summary>
    private const string RetryAfterSeconds = "30";

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
            var key = request.IdempotencyKey?.Trim() ?? string.Empty;
            var (job, refused, position) = jobs.Submit(
                Accepted(caller, request, key, now, queueWait), now);

            if (Rejected(ctx, jobs, refused, key) is { } rejection)
            {
                return rejection;
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
            // A READ that writes, deliberately: this is the only evidence the server has that anybody
            // is still listening. A job nobody polls holds a slot on a shared account and answers into
            // nothing — the client that submitted it can die without saying so, and the cancel on tab
            // close is best-effort by construction. `Polled` also decides the boundary, so a poll
            // arriving after the window cannot revive what the sweep was entitled to drop.
            var job = jobs.Polled(id, caller.Email, DateTimeOffset.UtcNow);

            // A finished job answers AT ONCE, whatever `wait` says. Waiting for a "change" that has
            // already happened is how a client that reconnects after a blip sits for 25 seconds
            // staring at an answer the server already has. (Plan round.)
            if (job is not null && !job.IsTerminal && wait is > 0)
            {
                var deadline = DateTimeOffset.UtcNow
                    + TimeSpan.FromSeconds(Math.Min(wait.Value, MaxWait.TotalSeconds));
                var was = job.Status;

                // Loops until THIS job moves or the budget is gone. One wake is not enough: the
                // waiter can be signalled while the job is still in the same state (a requeue onto
                // another account is a change, but not the one the caller is waiting for), and
                // returning then would turn a long poll into a busy poll.
                while (job is not null && !job.IsTerminal && job.Status == was && DateTimeOffset.UtcNow < deadline)
                {
                    await jobs.WaitForChangeAsync(id, deadline - DateTimeOffset.UtcNow, ctx.RequestAborted);
                    // Stamped again on every wake, not only when the request arrived: a long poll IS
                    // somebody listening for as long as it is open, and a client holding one for
                    // twenty-five seconds must not age towards abandonment while it does.
                    job = jobs.Polled(id, caller.Email, DateTimeOffset.UtcNow);
                }
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

    /// <summary>
    /// The job a well-formed request becomes.
    /// </summary>
    /// <remarks>
    /// Out of the handler because it is a dozen lines of construction and none of it is a decision —
    /// which is what made the handler read as though it had more branches than it has. (SonarCloud
    /// S3776: 19 against the 15 this repository allows.)
    /// </remarks>
    private static JobRecord Accepted(
        Caller caller, ReviewRequestDto request, string key, DateTimeOffset now, TimeSpan? queueWait)
    {
        // Its shape was checked by `Refusal` before anything reached here, so this cannot fail.
        JobKinds.TryRead(request.Kind, out var kind);

        // The CATALOG's spelling, not the client's. `Enum.TryParse(ignoreCase: true)` used to
        // canonicalise this on its way in, so a job sent as `architecture` was recorded, billed and
        // answered as `Architecture`; keeping that is what stops one role becoming two rows in the
        // usage view the day two clients spell it differently.
        var role = RoleCatalog.Builtin.ById(request.Role ?? string.Empty)?.Id
            ?? request.Role
            ?? string.Empty;

        return new JobRecord(
            JobId.New(),
            caller.Email,
            request.Vendor,
            request.Model,
            role,
            request.Prompt,
            JobStatus.Queued,
            now,
            now + (queueWait ?? JobTransitions.DefaultQueueWait),
            TimeSpan.FromSeconds(request.TimeoutSeconds),
            kind,
            IdempotencyKey: key,
            Fingerprint: key.Length == 0
                ? string.Empty
                : Idempotency.Fingerprint(
                    caller.Email, request.Vendor, request.Model, role, request.Prompt, kind,
                    request.TimeoutSeconds));
    }

    /// <summary>
    /// What the STORE refused, as an answer — or null when it refused nothing.
    /// </summary>
    /// <remarks>
    /// Two refusals, and they are different kinds of wrong. A 409 says the request is well formed and
    /// so is the key: what disagrees is the two of them against something this server already
    /// accepted, and the fix is a new key, so the message names the one that collided. A 429 says the
    /// person is holding as many waiting reviews as they may, and it carries a time — a queue nobody
    /// drains is just a way to hold other people's turn, and a silent accept that never runs is worse
    /// than being told.
    /// </remarks>
    private static IResult? Rejected(HttpContext ctx, JobStore jobs, SubmitRefusal refused, string key)
    {
        if (refused == SubmitRefusal.KeyUsedForSomethingElse)
        {
            return Results.Json(
                new ErrorDto(
                    $"the idempotency key '{key}' was already used for a different request. A repeat "
                    + "must carry the same vendor, model, role, kind, prompt and timeout; use a new key "
                    + "for a new question."),
                ServerJsonContext.Default.ErrorDto,
                statusCode: StatusCodes.Status409Conflict);
        }

        if (refused == SubmitRefusal.TooManyQueued)
        {
            ctx.Response.Headers.RetryAfter = RetryAfterSeconds;

            return Results.Json(
                new ErrorDto(
                    $"you already have {jobs.PerCallerQueued} reviews waiting. They run as accounts "
                    + "free up; this one was not accepted so it cannot sit behind them."),
                ServerJsonContext.Default.ErrorDto,
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        return null;
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

        var seconds = request.TimeoutSeconds;
        if (seconds < MinBudget.TotalSeconds || seconds > MaxBudget.TotalSeconds)
        {
            return $"timeoutSeconds must be between {MinBudget.TotalSeconds:0} and "
                + $"{MaxBudget.TotalSeconds:0}; you asked for {seconds}";
        }

        // An unknown role fails NAMING the legal values rather than quietly becoming the first one.
        // Silently substituting a role means a reviewer runs with instructions nobody asked for and
        // the answer looks like an ordinary one. (Two reviewers, code round.)
        if (!string.IsNullOrWhiteSpace(request.Role)
            && RoleCatalog.Builtin.ById(request.Role) is null)
        {
            return $"'{request.Role}' is not a review role. Allowed: "
                + string.Join(", ", RoleCatalog.Builtin.Roles.Select(r => r.Id));
        }

        // What this job IS, checked against the role it carries. The whole table is in `JobKinds`,
        // where it can be read as a table rather than reconstructed from branches.
        if (JobKinds.Refusal(request.Kind, request.Role) is { } wrongKind)
        {
            return wrongKind;
        }

        if (Idempotency.Refusal(request.IdempotencyKey) is { } badKey)
        {
            return badKey;
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
