using System.Buffers.Text;
using System.Text;
using System.Text.Json;

namespace CoaiServer;

/// <summary>When a codex slot's access token expires — the one claim read, and nothing kept.</summary>
public abstract record Expiry
{
    private Expiry()
    {
    }

    /// <summary>The access token's <c>exp</c>, as an instant.</summary>
    public sealed record Known(DateTimeOffset AtUtc) : Expiry;

    /// <summary>Nothing could be established, for this <see cref="CodexAuthExpiry"/> code — and an unknown expiry means no spawn.</summary>
    public sealed record Unknown(string Code) : Expiry;
}

/// <summary>
/// The pre-spawn expiry guard's reading of a slot's <c>auth.json</c>: the access token's JWT <c>exp</c> claim, decoded
/// and discarded — never the token, never logged, never kept (story 6.1a of todo/PLAN_every_round_is_counted.md).
/// </summary>
/// <remarks>
/// <para><b>What it guards, and what it does not.</b> codex 0.160.0 refreshes its token set PROACTIVELY before every
/// authenticated request when the access token expires within five minutes (<c>should_refresh_proactively</c> in
/// <c>codex-rs/login/src/auth/manager.rs</c>, inspected 2026-10-11 — see module_team_server.md). A refresh rotates the
/// refresh token at the vendor, so a reader that triggers one while any other process holds the same token set is the
/// company signing itself out. The guard keeps the read well clear of that window; it NARROWS the risk and is not the
/// safety boundary — that is the slot's lease, which nothing else uses while the reader runs.</para>
/// <para>The file is read whole and parsed with a document that is disposed on the way out. Only three facts leave this
/// class: the mode, whether a refresh has ever been recorded, and the expiry instant. The token's bytes are decoded into
/// a buffer that is dropped with the document.</para>
/// </remarks>
public static class CodexAuthExpiry
{
    public const string FileMissing = "auth-file-missing";
    public const string FileUnreadable = "auth-file-unreadable";
    public const string NotChatgptMode = "auth-mode-not-chatgpt";
    public const string NoAccessToken = "no-access-token";
    public const string NoLastRefresh = "no-last-refresh";
    public const string NotAJwt = "token-not-a-jwt";
    public const string NoExpiry = "exp-missing";

    // DateTimeOffset.FromUnixTimeSeconds accepts 0001-01-01 through 9999-12-31 only.
    private const long MinUnixSeconds = -62135596800;
    private const long MaxUnixSeconds = 253402300799;

    /// <summary>The slot's codex home, as <c>SlotEnvironment</c> sets <c>CODEX_HOME</c>.</summary>
    public static string AuthFileOf(string slotDirectory) => Path.Combine(slotDirectory, ".codex", "auth.json");

    /// <summary>The expiry of the access token in <paramref name="slotDirectory"/>'s codex home, or why it is unknown.</summary>
    public static Expiry Of(string slotDirectory)
    {
        var path = AuthFileOf(slotDirectory);
        if (!File.Exists(path))
        {
            return new Expiry.Unknown(FileMissing);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));

            return Read(document.RootElement);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Expiry.Unknown(FileUnreadable);
        }
    }

    private static Expiry Read(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object ? ReadMode(root) : new Expiry.Unknown(FileUnreadable);

    private static Expiry ReadMode(JsonElement root) =>
        root.TryGetProperty("auth_mode", out var mode) && mode.ValueKind == JsonValueKind.String && mode.GetString() == "chatgpt"
            ? ReadLastRefresh(root)
            : new Expiry.Unknown(NotChatgptMode);

    // codex's own precondition for a token-backed request: `get_token_data` refuses a file with no `last_refresh`.
    private static Expiry ReadLastRefresh(JsonElement root) =>
        root.TryGetProperty("last_refresh", out var refreshed) && refreshed.ValueKind == JsonValueKind.String
            ? ReadAccessToken(root)
            : new Expiry.Unknown(NoLastRefresh);

    private static Expiry ReadAccessToken(JsonElement root) =>
        root.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object
        && tokens.TryGetProperty("access_token", out var token) && token.ValueKind == JsonValueKind.String
            ? ExpiryOfJwt(token.GetString() ?? string.Empty)
            : new Expiry.Unknown(NoAccessToken);

    /// <summary>The <c>exp</c> claim of a JWT's payload — the second dot-separated part, base64url without padding.</summary>
    private static Expiry ExpiryOfJwt(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2 || !TryDecodeBase64Url(parts[1], out var payload))
        {
            return new Expiry.Unknown(NotAJwt);
        }

        try
        {
            using var claims = JsonDocument.Parse(payload);

            return ExpiryOfClaims(claims.RootElement);
        }
        catch (JsonException)
        {
            return new Expiry.Unknown(NotAJwt);
        }
    }

    private static Expiry ExpiryOfClaims(JsonElement claims) =>
        claims.ValueKind == JsonValueKind.Object && claims.TryGetProperty("exp", out var exp)
        && exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out var seconds) && InCalendar(seconds)
            ? new Expiry.Known(DateTimeOffset.FromUnixTimeSeconds(seconds))
            : new Expiry.Unknown(NoExpiry);

    private static bool InCalendar(long seconds) =>
        seconds is >= MinUnixSeconds and <= MaxUnixSeconds;

    private static bool TryDecodeBase64Url(string text, out byte[] bytes)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        var buffer = new byte[padded.Length];
        if (Convert.TryFromBase64String(padded, buffer, out var written))
        {
            bytes = buffer[..written];

            return true;
        }

        bytes = [];

        return false;
    }
}
