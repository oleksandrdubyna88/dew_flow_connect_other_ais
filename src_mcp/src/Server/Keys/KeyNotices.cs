using CoaiMcp.Core.Notices;

namespace CoaiMcp.Server.Keys;

/// <summary>
/// The three notices the pool writes (KEY_POOL_SPEC.md section 5). They name a key by its label and id and never by its text; the label of a stored key was checked against the redactor when it was added.
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
