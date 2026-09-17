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
/// <para>Constructed by hand and wired with <c>app.Use</c> rather than resolved: <c>UseMiddleware</c>
/// finds <c>InvokeAsync</c> by reflection, and this binary is Native AOT.</para>
/// </remarks>
internal sealed class IngestGate(Corpus corpus, RateLimiter limiter, ServerSecret secret)
{
    /// <summary>Where the authenticated key waits for the endpoint.</summary>
    public const string KeyItem = "coai-bugs.key";

    public async Task InvokeAsync(HttpContext http, RequestDelegate next)
    {
        if (!IsIngest(http.Request))
        {
            await next(http);

            return;
        }

        var keyId = corpus.KeyFor(Presented(http.Request), secret.Value);
        if (keyId.Length == 0)
        {
            // A revoked key and one that never existed answer the same thing, so this cannot be used
            // to discover which keys are real.
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;

            return;
        }

        var admission = limiter.Admit(LimiterSubject.Contributor(keyId));
        if (!admission.Admitted)
        {
            await TooManyAsync(http, admission);

            return;
        }

        http.Items[KeyItem] = new KeyId(keyId);
        await next(http);
    }

    /// <summary>The key the gate let through, for the endpoint. A missing one is a wiring defect and is said so.</summary>
    public static KeyId KeyOf(HttpContext http) =>
        http.Items.TryGetValue(KeyItem, out var key) && key is KeyId id
            ? id
            : throw new InvalidOperationException(
                "/ingest was reached without passing the gate; the middleware order is wrong");

    private static bool IsIngest(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && request.Path.Equals("/ingest", StringComparison.OrdinalIgnoreCase);

    /// <summary>429, with <c>Retry-After</c> and a body naming the limit.</summary>
    /// <remarks>A 429 with no number is a client that retries immediately for ever.</remarks>
    private async Task TooManyAsync(HttpContext http, Admission admission)
    {
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers.RetryAfter = admission.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        await http.Response.WriteAsJsonAsync(
            new Problem(
                $"at most {limiter.Limit.Value} requests a minute per key; try again in "
                + $"{admission.RetryAfterSeconds} s"),
            BugsJson.Default.Problem,
            cancellationToken: http.RequestAborted);
    }

    /// <summary>The key a request presents, from the one header that carries it.</summary>
    /// <remarks>
    /// The scheme is matched case-insensitively, as RFC 6750 requires: a proxy that normalises the
    /// header to `bearer` is not an attacker, and a 401 for it is a morning somebody loses.
    /// </remarks>
    private static string Presented(HttpRequest http) =>
        http.Headers.Authorization.ToString() is { Length: > 7 } header
        && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header[7..]
            : string.Empty;
}
