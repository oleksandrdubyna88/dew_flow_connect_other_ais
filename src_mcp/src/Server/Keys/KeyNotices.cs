using CoaiMcp.Core.Notices;

namespace CoaiMcp.Server.Keys;

/// <summary>
/// The notices the pool and the role launch road write (KEY_POOL_SPEC.md sections 5 and 6): dead, exposed, exhausted, empty, person-sign-in-used. They name a key by its label and id and never by its text; the label of a stored key was checked against the redactor when it was added.
/// </summary>
internal static class KeyNotices
{
    private const string Source = "claude-keys";

    /// <summary>The window in which at most one <c>claude-keys-exhausted</c> notice is written.</summary>
    internal static readonly TimeSpan ExhaustedEvery = TimeSpan.FromMinutes(30);

    public static ServerNotice Dead(KeyRecord key, DateTime nowUtc) => new()
    {
        Utc = ServerNotice.Iso(nowUtc),
        Class = "failure",
        Source = Source,
        Code = ServerNoticeCodes.ClaudeKeyDead,
        Subject = key.Id,
        Title = $"Claude key '{key.Label}' ({key.Id}) was refused by the vendor and is dead",
        Detail = "The pool never picks a dead key. A network error, a server error or a crash never makes a key dead: only a refusal of the key itself does.",
        Cure = $"Check the key at the vendor. If it is gone, remove it (coai-mcp --keys remove --id {key.Id}); if it is good, enable it again (coai-mcp --keys enable --id {key.Id}).",
        Provider = "claude",
    };

    public static ServerNotice Exposed(KeyRecord key, DateTime nowUtc) => new()
    {
        Utc = ServerNotice.Iso(nowUtc),
        Class = "failure",
        Source = Source,
        Code = ServerNoticeCodes.ClaudeKeyExposed,
        Subject = key.Id,
        Title = $"Claude key '{key.Label}' ({key.Id}) was found in a launch's output or in the coder's files and is dead",
        Detail = "The exact text of the key was seen where it must never be. The pool never picks it again.",
        Cure = $"Revoke the key at the vendor now, then remove it (coai-mcp --keys remove --id {key.Id}) and add a new one.",
        Provider = "claude",
    };

    /// <summary>
    /// The exact text of a key was seen where it must never be, and the mark that makes the key dead could NOT be written (the pool was busy for a minute, or its folder refused): the key is STILL ENABLED and the pool may pick it again. The same code as
    /// <see cref="Exposed"/>, so it reaches the person the same way; the detail says what failed (a system sentence that names a path and never holds a key text) and the cure says what to do by hand.
    /// </summary>
    public static ServerNotice ExposedNotMarked(string keyId, string why, DateTime nowUtc) => new()
    {
        Utc = ServerNotice.Iso(nowUtc),
        Class = "failure",
        Source = Source,
        Code = ServerNoticeCodes.ClaudeKeyExposed,
        Subject = keyId,
        Title = $"Claude key {keyId} was found in a launch's output or in the coder's files, and could NOT be marked dead: it is still enabled",
        Detail = $"The exact text of the key was seen where it must never be, but the pool could not be written, so the key is still enabled and may be picked again. {Redaction.SafeText(why, 300)}",
        Cure = $"Revoke the key at the vendor now. Then disable it (coai-mcp --keys disable --id {keyId}) or remove it (coai-mcp --keys remove --id {keyId}) and add a new one.",
        Provider = "claude",
    };

    /// <summary>The pool holds no key at all. The subject is the UTC date: a repeat on the same day is the same notice on the page — one per day.</summary>
    public static ServerNotice Empty(DateTime nowUtc, bool switchIsSet) => new()
    {
        Utc = ServerNotice.Iso(nowUtc),
        Class = "failure",
        Source = Source,
        Code = ServerNoticeCodes.ClaudeKeysEmpty,
        Subject = DayOf(nowUtc),
        Title = "The claude key pool of this device is empty: role jobs do not run on claude here",
        Detail = switchIsSet
            ? "The pool holds no key. The switch COAI_ROLE_CLAUDE_PERSON_SIGN_IN is set, so a claude role job uses the person's own sign-in until a key is added."
            : "The pool holds no key, and role jobs do not use the person's own sign-in. A route with only claude models is refused; a route with another vendor runs on that vendor.",
        Cure = "Add a key: coai-mcp --keys add --label <name> (the token on stdin), or the Keys page. A device with no key yet can set COAI_ROLE_CLAUDE_PERSON_SIGN_IN=1 to run on the person's own sign-in until the first key is added.",
        Provider = "claude",
    };

    /// <summary>A claude role job ran on the person's own sign-in by the device switch. One per day, by the subject.</summary>
    public static ServerNotice PersonSignInUsed(DateTime nowUtc) => new()
    {
        Utc = ServerNotice.Iso(nowUtc),
        Class = "outcome",
        Source = Source,
        Code = ServerNoticeCodes.ClaudePersonSignInUsed,
        Subject = DayOf(nowUtc),
        Title = "A claude role job ran on the person's own sign-in (the device switch is set and the pool is empty)",
        Detail = "COAI_ROLE_CLAUDE_PERSON_SIGN_IN is set and the pool holds no key at all, so the role job used the sign-in file of the person, as role jobs did before the pool. With any key in the pool the switch does nothing.",
        Cure = "Add a key (coai-mcp --keys add --label <name>) and unset the switch.",
        Provider = "claude",
    };

    private static string DayOf(DateTime nowUtc) => nowUtc.ToUniversalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static ServerNotice Exhausted(KeyCounts counts, DateTime? earliestFreeUtc, DateTime nowUtc) => new()
    {
        Utc = ServerNotice.Iso(nowUtc),
        Class = "failure",
        Source = Source,
        Code = ServerNoticeCodes.ClaudeKeysExhausted,
        Subject = "pool",
        Title = "No usable claude key: " + counts.Sentence + (earliestFreeUtc is { } free ? $"; the earliest is free at {ServerNotice.Iso(free)} UTC" : string.Empty),
        Detail = "Role jobs do not wait for a key: a route with only claude models is refused, and a route with another vendor runs on that vendor.",
        Cure = "Add a key (coai-mcp --keys add --label <name>), enable one, or wait until the earliest key is free again.",
        Provider = "claude",
    };
}

/// <summary>The keys of a pool by what they are now: usable, cooling, disabled, dead, or with a file problem.</summary>
internal sealed record KeyCounts(int Usable, int Cooling, int Disabled, int Dead, int Broken)
{
    public string Sentence => $"{Usable} usable, {Cooling} cooling, {Disabled} disabled, {Dead} dead, {Broken} with a file problem";

    public static KeyCounts Of(IReadOnlyList<KeyEntry> entries, DateTime nowUtc) => new(
        entries.Count(entry => entry.Usable(nowUtc)),
        entries.Count(entry => entry.Record.State == KeyStates.Enabled && entry.Problem.Length == 0 && entry.Record.InCooldownAt(nowUtc)),
        entries.Count(entry => entry.Record.State == KeyStates.Disabled),
        entries.Count(entry => entry.Record.State == KeyStates.Dead),
        entries.Count(entry => entry.Record.State == KeyStates.Enabled && entry.Problem.Length > 0));
}
