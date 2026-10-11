namespace CoaiServer;

/// <summary>One signed-in account of one vendor, and what is known about it right now.</summary>
/// <param name="Vendor">The vendor id this account belongs to.</param>
/// <param name="Name">The slot name from <c>vendors.json</c> — also its directory name.</param>
/// <param name="Directory">The absolute path used as <c>HOME</c> for launches on this account.</param>
/// <param name="CooldownUntilUtc">
/// When the vendor said it would serve this account again, or null. Temporary by nature: it clears
/// itself by the passage of time and needs no human.
/// </param>
/// <param name="NeedsSignIn">
/// The account is signed out. NOT a cooldown: it never clears by itself, and no amount of waiting
/// helps — a human runs <c>coai-server login</c>. Keeping the two apart is the difference between
/// "try again in an hour" and "nobody will ever try again".
/// </param>
/// <param name="ConsecutiveCooldowns">
/// How many times in a row this account has been rate-limited without a success in between. It is
/// what makes the fallback back off instead of hammering an exhausted account every thirty minutes.
/// </param>
/// <param name="Blocked">
/// A <see cref="BlockedReasons"/> code while the account is UNAVAILABLE until recovery proves it safe — a gauge reader
/// of a crashed server that could not be named, or one that would not die (story 6.1a of
/// todo/PLAN_every_round_is_counted.md). Empty otherwise. Neither a cooldown (no clock clears it) nor needs-sign-in
/// (the credentials are fine); every later acquisition reconciles it again, and a human may clear the marker.
/// </param>
/// <param name="GaugeSuspended">
/// A <see cref="GaugeCodes"/> code while gauge capture on this account is suspended — the read met an authentication
/// error or a refresh request — or empty. Its OWN state: the account still takes jobs, and a job that succeeds lifts it.
/// </param>
/// <param name="Gauge">The latest reading of the vendor's own gauge on this account, or null when none was ever captured.</param>
public sealed record AccountSlot(
    string Vendor,
    string Name,
    string Directory,
    DateTimeOffset LastUsedUtc,
    DateTimeOffset? CooldownUntilUtc,
    bool NeedsSignIn,
    string Note,
    int ConsecutiveCooldowns,
    string Blocked = "",
    string GaugeSuspended = "",
    GaugeReadingDto? Gauge = null)
{
    /// <summary>True when this account could take work at <paramref name="nowUtc"/>.</summary>
    public bool IsReady(DateTimeOffset nowUtc) =>
        Blocked.Length == 0 && !NeedsSignIn && (CooldownUntilUtc is null || CooldownUntilUtc <= nowUtc);

    /// <summary>True when it is only WAITING — rate-limited, and it will come back on its own.</summary>
    public bool IsCoolingDown(DateTimeOffset nowUtc) =>
        !NeedsSignIn && CooldownUntilUtc is { } until && until > nowUtc;
}

/// <summary>Which account to run the next job on.</summary>
/// <remarks>
/// Pure, and separate from <see cref="SlotRegistry"/> on purpose: choosing is a decision worth
/// testing exhaustively, and it should not need a filesystem to test. The registry does the part
/// that cannot be pure — taking the lock.
/// </remarks>
public static class SlotSelector
{
    /// <summary>
    /// The least-recently-used account that is ready, or null when none is.
    /// </summary>
    /// <remarks>
    /// Least-recently-used rather than first-listed so that several accounts of one vendor actually
    /// share the load. First-listed would drive account <c>a</c> into its rate limit while <c>b</c>
    /// sat idle, which is the exact opposite of why an operator adds a second account.
    /// </remarks>
    public static AccountSlot? Pick(IEnumerable<AccountSlot> slots, DateTimeOffset nowUtc) =>
        slots.Where(s => s.IsReady(nowUtc))
             .OrderBy(s => s.LastUsedUtc)
             .ThenBy(s => s.Name, StringComparer.Ordinal)
             .FirstOrDefault();

    /// <summary>
    /// Why nothing could be picked, phrased for the person who has to fix it.
    /// </summary>
    /// <remarks>
    /// A bare "no slot available" makes an operator go and look; these two states have completely
    /// different cures and the message says which one applies. Only called when
    /// <see cref="Pick"/> returned null.
    /// </remarks>
    public static string Explain(IReadOnlyCollection<AccountSlot> slots, DateTimeOffset nowUtc)
    {
        if (slots.Count == 0)
        {
            return "this vendor has no account slots configured";
        }

        var soonest = slots.Where(s => s.IsCoolingDown(nowUtc)).Min(s => s.CooldownUntilUtc);
        if (soonest is { } until)
        {
            return $"every account is rate-limited; the first comes back at {until:u}";
        }

        // Blocked before signed-out: a blocked account's credentials are fine, and `login` would be the wrong cure —
        // it meets the same barrier. The cure is on the box: the server log names the code.
        return slots.Any(s => s.Blocked.Length > 0 && !s.NeedsSignIn)
            ? "every account is unavailable — a gauge reader of a crashed server could not be proved gone; see the server log"
            : $"every account is signed out — run `coai-server login {slots.First().Vendor} <slot>` on the server";
    }
}
