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
public sealed record AccountSlot(
    string Vendor,
    string Name,
    string Directory,
    DateTimeOffset LastUsedUtc,
    DateTimeOffset? CooldownUntilUtc,
    bool NeedsSignIn,
    string Note,
    int ConsecutiveCooldowns)
{
    /// <summary>True when this account could take work at <paramref name="nowUtc"/>.</summary>
    public bool IsReady(DateTimeOffset nowUtc) =>
        !NeedsSignIn && (CooldownUntilUtc is null || CooldownUntilUtc <= nowUtc);

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

        return soonest is { } until
            ? $"every account is rate-limited; the first comes back at {until:u}"
            : $"every account is signed out — run `coai-server login {slots.First().Vendor} <slot>` on the server";
    }
}
