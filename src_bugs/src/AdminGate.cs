using System.Globalization;

namespace CoaiBugs;

/// <summary>
/// The gate in front of <c>/admin/*</c>: the bearer key and the admin rate limit, before any body.
/// </summary>
/// <remarks>
/// <para><b>The same shape as <see cref="IngestGate"/>, and separate on purpose.</b> The common half
/// is four lines — read a bearer header, authenticate, admit, or refuse — and the halves that differ
/// are the ones that matter: a different credential store, a different limiter subject, and a
/// failure mode that must not disclose anything. Extracting a shared base would put the one
/// interesting difference behind a virtual call and make the disclosure rule harder to read at the
/// point it applies, so the header parsing is shared (it is in `IngestGate`, `internal`, and reused)
/// and the decisions are not. Where that was weighed: the reuse-first rule's step 2, "widen the
/// existing thing" — rejected because the widening is a seam through an authentication boundary.
/// </para>
/// <para><b>Absent and invalid are ONE answer.</b> A caller must not be able to tell "no
/// administrators are configured" from "you are not one of them": the first would make this endpoint
/// an oracle for whether administration is enabled here, answerable by anybody who asks. Same status,
/// same body, same work — <see cref="AdminKeys.Match"/> walks its whole list either way, and when
/// the list is empty there is nothing to walk and nothing to time. The operator learns the variable
/// is missing from the startup log, which is on the host.</para>
/// <para><b>And it never admits an administrator to the contributor limiter.</b> The subjects carry
/// different prefixes so the buckets cannot collide, and an admin route is limited by
/// <c>COAI_BUGS_ADMIN_RATE_PER_MINUTE</c> — a separate setting, because the contributor number is
/// flood control on a public endpoint and using it here makes the Users tab rate-limit itself after
/// ten pages.</para>
/// <para>Constructed by hand and wired with <c>app.Use</c> rather than resolved: <c>UseMiddleware</c>
/// finds <c>InvokeAsync</c> by reflection, and this binary is Native AOT.</para>
/// </remarks>
internal sealed class AdminGate(AdminKeys admins, RateLimiter limiter, ServerSecret secret)
{
    /// <summary>Where the authenticated administrator waits for the endpoint.</summary>
    public const string AdminItem = "coai-bugs.admin";

    public async Task InvokeAsync(HttpContext http, RequestDelegate next)
    {
        if (!IsAdmin(http.Request))
        {
            await next(http);

            return;
        }

        if (admins.Match(IngestGate.Presented(http.Request), secret.Value)
            is not AdminKeys.Presented.Administrator administrator)
        {
            // ONE answer for "no admins configured" and "not an admin". See the remarks: telling
            // them apart is a disclosure, not a courtesy.
            await RefusedAsync(http);

            return;
        }

        var admission = limiter.Admit(LimiterSubject.Administrator(administrator.Id));
        if (!admission.Admitted)
        {
            await TooManyAsync(http, admission);

            return;
        }

        http.Items[AdminItem] = administrator.Id;
        await next(http);
    }

    /// <summary>The administrator the gate let through. A missing one is a wiring defect, said so.</summary>
    public static AdminId Of(HttpContext http) =>
        http.Items.TryGetValue(AdminItem, out var who) && who is AdminId id
            ? id
            : throw new InvalidOperationException(
                "an /admin route was reached without passing the gate; the middleware order is wrong");

    /// <summary>Whether this request is for the admin surface at all.</summary>
    /// <remarks>
    /// A prefix match, so a route added later is gated by default rather than by being remembered —
    /// the failure this shape prevents is a new admin endpoint that nobody thought to protect.
    /// </remarks>
    private static bool IsAdmin(HttpRequest request) =>
        request.Path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase);

    /// <summary>401, with a body that says nothing about why.</summary>
    private static Task RefusedAsync(HttpContext http)
    {
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;

        return http.Response.WriteAsJsonAsync(
            new Problem("an administrator's key is required"),
            BugsJson.Default.Problem,
            cancellationToken: http.RequestAborted);
    }

    /// <summary>429, with <c>Retry-After</c> and a body naming the limit that was reached.</summary>
    private async Task TooManyAsync(HttpContext http, Admission admission)
    {
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers.RetryAfter = admission.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        await http.Response.WriteAsJsonAsync(
            new Problem(
                $"at most {limiter.Limit.Value} requests a minute per administrator; try again in "
                + $"{admission.RetryAfterSeconds} s"),
            BugsJson.Default.Problem,
            cancellationToken: http.RequestAborted);
    }
}
