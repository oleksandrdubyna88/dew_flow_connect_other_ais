using System.Globalization;

namespace CoaiBugs;

/// <summary>
/// The gate in front of <c>/ingest</c>: the bearer key and the rate limit, BEFORE the body is read.
/// </summary>
/// <remarks>
/// <para>It was inside the handler, after model binding, and a code round did the arithmetic: the
/// framework deserialised every body before the handler ran, so a hundred one-megabyte bodies at a
/// limit of ten were all parsed before ninety of them got their 429, and a stranger's malformed body
/// got a framework 400 before the 401 it was owed. Middleware runs before routing binds anything, so
/// a request that fails here never has its body read.</para>
/// <para><b>The order is the contract: 401, then 429, then the endpoint</b> — 400 for a malformed
/// body, then the work. A revoked or unknown key never reaches the limiter and never gets a window.
/// The key that passed travels to the endpoint typed, in <see cref="HttpContext.Items"/>, and the
/// endpoint refuses to run without it: a route reached around this gate is a wiring defect, not a
/// request.</para>
/// <para><b>Two credentials are accepted here, and they are told apart.</b> A contributor key is
/// authenticated against <c>api_keys</c>; an administrator's is authenticated against
/// <see cref="AdminKeys"/>, admitted against the ADMIN limit, and stored down a path that counts
/// nothing — see <see cref="Uploader"/> for why they cannot be one case. A contributor key is tried
/// first because that is what nearly every request on this endpoint is.</para>
/// <para>Constructed by hand and wired with <c>app.Use</c> rather than resolved: <c>UseMiddleware</c>
/// finds <c>InvokeAsync</c> by reflection, and this binary is Native AOT.</para>
/// </remarks>
internal sealed class IngestGate(
    Corpus corpus,
    AdminKeys admins,
    RateLimiter contributors,
    RateLimiter administrators,
    ServerSecret secret)
{
    /// <summary>Where the authenticated caller waits for the endpoint.</summary>
    public const string KeyItem = "coai-bugs.key";

    public async Task InvokeAsync(HttpContext http, RequestDelegate next)
    {
        if (!IsIngest(http.Request))
        {
            await next(http);

            return;
        }

        var uploader = Who(http.Request);
        if (uploader is Uploader.Nobody)
        {
            // A revoked key, an unknown key and a key that is nobody's answer the same thing, so
            // this cannot be used to discover which keys are real.
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;

            return;
        }

        var limiter = Limiting(uploader);
        var admission = limiter.Admit(uploader.Subject);
        if (!admission.Admitted)
        {
            await TooManyAsync(http, admission, limiter, uploader);

            return;
        }

        http.Items[KeyItem] = uploader;
        await next(http);
    }

    /// <summary>The caller the gate let through. A missing one is a wiring defect and is said so.</summary>
    public static Uploader WhoOf(HttpContext http) =>
        http.Items.TryGetValue(KeyItem, out var who) && who is Uploader uploader
            ? uploader
            : throw new InvalidOperationException(
                "/ingest was reached without passing the gate; the middleware order is wrong");

    /// <summary>Which credential was presented, if either.</summary>
    /// <remarks>
    /// The contributor store is asked first, and an administrator's key can never be a row in it —
    /// nothing issues one — so the order changes no answer, only which lookup the common case pays
    /// for. Both lookups compare hashes in fixed time and neither says which it refused.
    /// </remarks>
    private Uploader Who(HttpRequest request)
    {
        var presented = Presented(request);
        if (corpus.KeyFor(presented, secret.Value) is { Length: > 0 } keyId)
        {
            // Typed once, here, and carried as a KeyId from this point on — the limiter and the
            // request item then take the same value and neither can be handed an unchecked string.
            return new Uploader.Contributor(new KeyId(keyId));
        }

        return admins.Match(presented, secret.Value) is AdminKeys.Presented.Administrator administrator
            ? new Uploader.Administrator(administrator.Id)
            : new Uploader.Nobody();
    }

    /// <summary>The limit that applies to this caller — its own setting, never the other's.</summary>
    private RateLimiter Limiting(Uploader uploader) =>
        uploader is Uploader.Administrator ? administrators : contributors;

    private static bool IsIngest(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && request.Path.Equals("/ingest", StringComparison.OrdinalIgnoreCase);

    /// <summary>429, with <c>Retry-After</c> and a body naming the limit that was reached.</summary>
    /// <remarks>
    /// <para>A 429 with no number is a client that retries immediately for ever.</para>
    /// <para>The limiter is passed in rather than read from the field, because which of the two
    /// applies depends on the caller: naming the contributor number to an administrator who hit the
    /// admin one would send them to the wrong environment variable.</para>
    /// </remarks>
    private static async Task TooManyAsync(
        HttpContext http, Admission admission, RateLimiter limiter, Uploader uploader)
    {
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers.RetryAfter = admission.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        await http.Response.WriteAsJsonAsync(
            new Problem(
                $"at most {limiter.Limit.Value} requests a minute per {uploader.Noun}; try again in "
                + $"{admission.RetryAfterSeconds} s"),
            BugsJson.Default.Problem,
            cancellationToken: http.RequestAborted);
    }

    /// <summary>The key a request presents, from the one header that carries it.</summary>
    /// <remarks>
    /// <para>The scheme is matched case-insensitively, as RFC 6750 requires: a proxy that normalises
    /// the header to `bearer` is not an attacker, and a 401 for it is a morning somebody loses.</para>
    /// <para><b>Shared with <see cref="AdminGate"/>, which is the one half of the two gates worth
    /// sharing.</b> RFC 6750 header parsing has exactly one correct answer and two copies of it
    /// would drift; the DECISIONS either gate makes are deliberately not shared.</para>
    /// </remarks>
    internal static string Presented(HttpRequest http) =>
        http.Headers.Authorization.ToString() is { Length: > 7 } header
        && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header[7..]
            : string.Empty;
}
