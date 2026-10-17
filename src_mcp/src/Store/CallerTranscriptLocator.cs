using System.Text.Json;

namespace CoaiMcp.Store;

/// <summary>
/// Where the Claude CLI keeps what this reader needs: its transcripts and its session registry.
/// </summary>
/// <remarks>
/// <para><c>CLAUDE_CONFIG_DIR</c> is NOT honoured in this epic — <see cref="AgentLog"/> does not honour it either, and two readers
/// disagreeing about where the CLI keeps its files is worse than both being wrong the same way.</para>
/// <para><see cref="Default"/> is one value for the process. A test run points it, through the single seam
/// <see cref="ReplaceDefault"/>, at an empty temp folder (<c>TestClaudeHome</c>), so neither this reader nor <see cref="AgentLog"/>
/// opens a developer's transcripts (parent §16 D17).</para>
/// </remarks>
public sealed record ClaudeHome(string ProjectsDir, string SessionsDir)
{
    private static ClaudeHome _default = FromProfile();

    /// <summary><c>&lt;UserProfile&gt;/.claude/projects</c> and <c>/sessions</c>, unless a test replaced it.</summary>
    public static ClaudeHome Default => Volatile.Read(ref _default);

    /// <summary>The one seam for tests. Never reached from product code.</summary>
    internal static void ReplaceDefault(ClaudeHome home) => Volatile.Write(ref _default, home);

    /// <summary>
    /// The same home with the transcripts moved to <paramref name="agentLogDir"/> when it is non-empty — which is what
    /// <c>PanelSettings.AgentLogDir</c> (<c>COAI_AGENT_LOG_DIR</c>) means. The registry folder does not move.
    /// </summary>
    public ClaudeHome With(string agentLogDir) => string.IsNullOrWhiteSpace(agentLogDir) ? this : this with { ProjectsDir = agentLogDir };

    private static ClaudeHome FromProfile()
    {
        var claude = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

        return new ClaudeHome(Path.Combine(claude, "projects"), Path.Combine(claude, "sessions"));
    }
}

/// <summary>
/// A Claude session id and WHICH source it came from. <see cref="None"/> has an empty id; a <c>default</c> value is not one.
/// </summary>
public readonly record struct CallerSessionId(string Id, string Source)
{
    public const string Registry = "registry";
    public const string Environment = "environment";
    public const string Stored = "stored";

    public static CallerSessionId None => new(string.Empty, string.Empty);
}

/// <summary>
/// Either a located transcript or a named reason it cannot be observed. Closed: the two nested records are the only cases.
/// </summary>
public abstract record CallerTranscriptLocation
{
    public const string NoSessionId = "no session id";

    /// <summary>The id came from <c>COAI_CALLER_SESSION</c>: an operator's string, which names no transcript (parent §16 D9).</summary>
    public const string StatedId = "stated id";
    public const string TranscriptNotFound = "transcript not found";
    public const string TranscriptAmbiguous = "transcript in two folders";

    private CallerTranscriptLocation()
    {
    }

    public static string NoReaderFor(string vendor) => "no reader for " + vendor;

    /// <summary><see cref="SessionFolder"/> is <c>&lt;project folder&gt;/&lt;id&gt;</c> — it holds the subagents, and may not exist.</summary>
    public sealed record Found(CallerSessionId Session, string MainFile, string SessionFolder) : CallerTranscriptLocation;

    public sealed record NotObservable(CallerSessionId Session, string Reason) : CallerTranscriptLocation;
}

/// <summary>
/// Which session is calling, and which file is its transcript (E3.S2).
/// </summary>
/// <remarks>
/// <para><b>The ladder</b> is the registry (<c>&lt;SessionsDir&gt;/&lt;CLAUDE_PID&gt;.json</c>, read BY NAME and never by listing —
/// a <c>&lt;pid&gt;.&lt;hash&gt;.key</c> sits beside every registry file), then <c>CLAUDE_CODE_SESSION_ID</c>, then a stored id.
/// It is NOT <c>CallerIdentity.From</c>: that puts <c>COAI_CALLER_SESSION</c> first, and here a stated id alone is
/// <c>unobserved: stated id</c>. The registry and the variable disagreeing is the <c>/clear</c> case (the variable is inherited
/// stale): the registry wins and the source says so; nothing is recorded about the loser.</para>
/// <para><b>An id never reaches a path unvalidated.</b> Valid means a GUID in the <c>D</c> form, and it is normalised to lower
/// case before it is used, so <c>../x</c> is <c>no session id</c> and an upper-case id finds the lower-case file.</para>
/// <para><b>The lookup</b> is one non-recursive listing of the projects folder and one <c>File.Exists</c> per project folder: no
/// slug is computed from a <c>cwd</c>, which is the thing that goes wrong when the caller changed directory.</para>
/// </remarks>
public static class CallerTranscriptLocator
{
    private const int MaxRegistryBytes = 64 * 1024;
    private const int MaxVendorWord = 20;

    public static CallerTranscriptLocation Locate(string vendor, Func<string, string?> env, ClaudeHome home)
    {
        if (!ReadsClaude(vendor))
        {
            return Refused(CallerSessionId.None, CallerTranscriptLocation.NoReaderFor(VendorWord(vendor)));
        }

        var session = SessionFor(env, home);

        return session.Id.Length > 0 ? Lookup(session, home) : Refused(CallerSessionId.None, Unnamed(env));
    }

    /// <summary>
    /// The same validation and lookup for an id stored on a round (<c>rounds.caller</c>) — E7's entry. A <c>repo:…</c> value is
    /// not a session id.
    /// </summary>
    public static CallerTranscriptLocation LocateStored(string id, ClaudeHome home)
    {
        var session = Named(id, CallerSessionId.Stored);

        return session.Id.Length > 0 ? Lookup(session, home) : Refused(CallerSessionId.None, CallerTranscriptLocation.NoSessionId);
    }

    private static CallerTranscriptLocation.NotObservable Refused(CallerSessionId session, string reason) => new(session, reason);

    /// <summary>Claude, unknown and an empty vendor are read; any other vendor has no reader here.</summary>
    private static bool ReadsClaude(string vendor) => vendor is "claude" or "unknown" or "";

    /// <summary>The vendor word goes into a reason, so it is used only in its closed shape <c>^[a-z][a-z-]{0,19}$</c>.</summary>
    private static string VendorWord(string vendor) => IsVendorWord(vendor) ? vendor : "this vendor";

    private static bool IsVendorWord(string vendor) =>
        vendor.Length is > 0 and <= MaxVendorWord && char.IsAsciiLetterLower(vendor[0]) && vendor.All(IsVendorChar);

    private static bool IsVendorChar(char c) => char.IsAsciiLetterLower(c) || c == '-';

    private static CallerSessionId SessionFor(Func<string, string?> env, ClaudeHome home)
    {
        var fromRegistry = FromRegistry(env("CLAUDE_PID") ?? string.Empty, home);

        return fromRegistry.Id.Length > 0 ? fromRegistry : Named(env("CLAUDE_CODE_SESSION_ID"), CallerSessionId.Environment);
    }

    /// <summary>What an absent id means: a stated one is its own state, anything else is nothing named.</summary>
    private static string Unnamed(Func<string, string?> env) =>
        string.IsNullOrWhiteSpace(env("COAI_CALLER_SESSION")) ? CallerTranscriptLocation.NoSessionId : CallerTranscriptLocation.StatedId;

    private static CallerSessionId Named(string? raw, string source) =>
        Guid.TryParseExact((raw ?? string.Empty).Trim(), "D", out var guid) ? new CallerSessionId(guid.ToString("D"), source) : CallerSessionId.None;

    private static CallerSessionId FromRegistry(string pid, ClaudeHome home) =>
        IsPid(pid) ? ReadRegistry(Path.Combine(home.SessionsDir, pid + ".json")) : CallerSessionId.None;

    private static bool IsPid(string pid) => pid.Length is >= 1 and <= 10 && pid.All(char.IsAsciiDigit);

    /// <summary>One shared read of one named file, refused over 64 KiB, taking <c>sessionId</c> and nothing else.</summary>
    private static CallerSessionId ReadRegistry(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            return ReadRegistry(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return CallerSessionId.None;
        }
    }

    /// <summary>
    /// The registry entry's <c>sessionId</c>, from at most 64 KiB + 1 bytes actually READ. The bound is on what is read, not on
    /// <c>Length</c>: a file appended to after its length was measured would otherwise be parsed whole. Full means over the cap.
    /// </summary>
    internal static CallerSessionId ReadRegistry(Stream stream)
    {
        try
        {
            var buffer = new byte[MaxRegistryBytes + 1];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

            return read > MaxRegistryBytes ? CallerSessionId.None : SessionIdIn(buffer.AsMemory(0, read));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return CallerSessionId.None;
        }
    }

    private static CallerSessionId SessionIdIn(ReadOnlyMemory<byte> json)
    {
        using var document = JsonDocument.Parse(json);

        return Named(JsonText.StringProperty(document.RootElement, "sessionId"), CallerSessionId.Registry);
    }

    private static CallerTranscriptLocation Lookup(CallerSessionId session, ClaudeHome home)
    {
        var holders = HoldersOf(session.Id, home);

        return holders.Count switch
        {
            1 => new CallerTranscriptLocation.Found(session, Path.Combine(holders[0], session.Id + ".jsonl"), Path.Combine(holders[0], session.Id)),
            0 => Refused(session, CallerTranscriptLocation.TranscriptNotFound),
            _ => Refused(session, CallerTranscriptLocation.TranscriptAmbiguous),
        };
    }

    /// <summary>The project folders (top level only) that hold <c>&lt;id&gt;.jsonl</c>. A listing that fails is no holder.</summary>
    private static List<string> HoldersOf(string id, ClaudeHome home)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(home.ProjectsDir).Where(dir => File.Exists(Path.Combine(dir, id + ".jsonl")))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
