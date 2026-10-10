namespace CoaiServer;

/// <summary>Who has signed in — the admin's roster, so a Team server tab can show a name and a last seen.</summary>
/// <remarks>
/// <para>Story 2.1 of <c>PLAN_team_usage_by_person.md</c>. The ledger knows who SPENT; only the sessions
/// know who is signed in, and until this route nothing listed them — so "last seen" and "signed in but
/// spent nothing" could not be shown.</para>
/// <para><b>Admin-only, through the ONE decision the company branch of <c>/api/usage</c> also consults</b>
/// (<see cref="AdminOnly"/>), registered with <see cref="RouteHandlerBuilderExtensions.RequireAdmin"/> so
/// the handler runs for nobody else and the same 403 names the setting that would open it. A wrong
/// check here hands every colleague's email and activity to any caller, which is the risk the plan
/// names first; the 403 is the first test, and it was two identical inline checks until the code
/// round on E1+E2 asked for one.</para>
/// <para><b>"People with an unexpired session this server still serves"</b>, which the page says in those
/// words: a caller on a raw identity-provider token has no session file and is not here, and a session
/// whose domain has since left <c>Coai:AllowedDomains</c> is refused on every request, so it is not
/// here either (<see cref="CallerFilter.Admits"/>). Read from the files directly — never through
/// <see cref="SessionStore.Validate"/>, which writes and deletes.</para>
/// </remarks>
public static class PeopleEndpoints
{
    public static void MapPeopleEndpoints(this WebApplication app, SessionStore sessions, CallerFilter gate)
    {
        app.MapGet("/api/people", () => Results.Json(
                People.From(sessions.Active(DateTimeOffset.UtcNow).Where(s => gate.Admits(s.Email))),
                ServerJsonContext.Default.IReadOnlyListPersonDto))
            .RequireAdmin(gate, "/api/people");
    }
}

/// <summary>Sessions in, people out. Pure functions, like <see cref="UsageTotals"/>.</summary>
public static class People
{
    /// <summary>One row per person, sorted by email.</summary>
    /// <remarks>
    /// Case-insensitive, for the reason <see cref="UsageTotals.ByPerson"/> is: an identity provider
    /// hands back <c>Alice@Example.com</c> on one sign-in and <c>alice@example.com</c> on the next, and
    /// two rows for one person is the company view lying about how many people there are (D2).
    /// </remarks>
    public static IReadOnlyList<PersonDto> From(IEnumerable<SessionRecord> sessions) =>
        [.. sessions
            .GroupBy(s => s.Email, StringComparer.OrdinalIgnoreCase)
            .Select(Person)
            .OrderBy(p => p.Email, StringComparer.OrdinalIgnoreCase)];

    /// <summary>The person one group of sessions describes.</summary>
    /// <remarks>
    /// <para>"Latest" is by issue: a person who was renamed signs in again and the NEWEST session carries
    /// the new name, while an older one still in use carries the old. The newest session's casing of
    /// the email is used for the same reason. Last used is the maximum over every session — not the
    /// newest session's — because whichever window they used last is when they were last here, and a
    /// laptop that never signs out is older than the phone that signed in yesterday.</para>
    /// <para>The name is tested with <see cref="string.IsNullOrEmpty"/>, not <c>Length</c>: the record
    /// declares it non-null and <see cref="SessionStore.Issue"/> writes it so, but a file written by hand
    /// or by an older build deserialises <c>"name": null</c> as exactly that (doctrine §4a), and one such
    /// file must not turn the whole roster into a 500.</para>
    /// </remarks>
    private static PersonDto Person(IGrouping<string, SessionRecord> sessions)
    {
        var newestFirst = sessions.OrderByDescending(s => s.CreatedUtc).ToList();

        return new PersonDto(
            newestFirst[0].Email,
            newestFirst.Select(s => s.Name).FirstOrDefault(name => !string.IsNullOrEmpty(name), string.Empty),
            newestFirst.Max(s => s.LastUsedUtc));
    }
}
