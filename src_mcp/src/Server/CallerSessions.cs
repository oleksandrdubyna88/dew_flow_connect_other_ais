using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CoaiMcp.Server;

/// <summary>
/// Who is calling us — the AI's OWN session, which is not the same thing as ours.
/// </summary>
/// <remarks>
/// <para>Our session is keyed by repo+branch and its plan stage happens ONCE: after a plan proceeds
/// the stage advances and a second <c>review_plan</c> on that session is refused outright. So the
/// epics produced by a split come back on their own branches, as their own sessions — which is
/// exactly why a per-session memory cannot see them, and why the caller has to be identified.</para>
/// <para>Claude Code exports <c>CLAUDE_CODE_SESSION_ID</c> to the children it spawns, and an MCP
/// server on stdio is one of those children. Nothing has to be passed, remembered or trusted to the
/// model's own bookkeeping. <c>COAI_CALLER_SESSION</c> is first so a client without an id of its own
/// can still be given one.</para>
/// </remarks>
public static class CallerIdentity
{
    private static readonly string[] Variables =
    [
        "COAI_CALLER_SESSION",
        "CLAUDE_CODE_SESSION_ID",
        "CODEX_SESSION_ID",
        "GEMINI_CLI_SESSION_ID",
    ];

    /// <summary>The first of them that is set, or empty when this client identifies itself at all.</summary>
    public static string From(Func<string, string?> read)
    {
        foreach (var name in Variables)
        {
            var value = read(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    public static string Current() => From(Environment.GetEnvironmentVariable);
}

/// <summary>
/// What one caller has already been told, so it is not told again.
/// </summary>
/// <remarks>
/// <para><b>The loop this exists to stop.</b> The gate tells an AI to split its plan into epics.
/// The AI does, and brings each epic back for its own plan review — which is the right thing to do.
/// The gate, with no memory of the first order, tells each epic to split into epics. Epics of epics,
/// for ever. Raised by the operator before it could happen, and it is why the order is given once
/// per CALLER rather than once per round.</para>
/// <para>One small file per caller, written only when a split is actually ordered: two servers
/// sharing a data directory never write the same file, and a caller that never triggers a split
/// leaves nothing behind.</para>
/// </remarks>
public sealed class CallerSessions(string dataDir)
{
    /// <summary>
    /// How long a caller is remembered as "already split".
    /// </summary>
    /// <remarks>
    /// A deliberate compromise, stated rather than hidden: a Claude session is normally one task,
    /// but a very long one can be two. Forgetting after a day means the second task is ordered to
    /// split again; not forgetting at all would mean it never is. The failure on the other side is
    /// soft either way — <c>AlreadySplitCommand</c> tells the AI to say so if the piece is really
    /// too big, rather than to build something oversized in silence.
    /// </remarks>
    public static readonly TimeSpan Remembers = TimeSpan.FromHours(24);

    private string Dir => Path.Combine(dataDir, "callers");

    /// <summary>Pure half: what a stored stamp means at a given moment.</summary>
    public static bool StillRemembered(DateTime? orderedUtc, DateTime nowUtc) =>
        orderedUtc is { } t && nowUtc - t < Remembers && nowUtc >= t;

    /// <summary>Has this caller already been ordered to split something? A pure question.</summary>
    public bool SplitAlreadyOrdered(string caller, DateTime nowUtc) =>
        StillRemembered(ReadStamp(caller), nowUtc);

    /// <summary>
    /// Take this caller's ONE split order, atomically. True means it is yours to give.
    /// </summary>
    /// <remarks>
    /// <para>A claim rather than a read followed by a write, because two servers share this data
    /// directory as a matter of course — one per MCP client on this machine — and a read-then-write
    /// pair lets both of them see an unclaimed caller and both issue the order. Raised by codex in
    /// this change's plan round.</para>
    /// <para><b>The whole decision happens while the file is held.</b> The first fix used
    /// <c>CreateNew</c> and deleted an EXPIRED claim first — and three reviewers, independently,
    /// found the same hole in it: two processes can both pass the existence check, and the second
    /// one's delete removes the claim the first has just written, so both return true. There is no
    /// ordering of delete-then-create that closes that. <c>FileShare.None</c> does: one process
    /// holds the handle, reads the stamp, decides and writes without ever letting go, and every
    /// other process is refused at the door rather than racing it.</para>
    /// <para><b>It fails OPEN.</b> If the claim cannot be written at all — an unwritable data
    /// directory, a full disk — the order is given rather than withheld, and a warning is the only
    /// signal. Failing closed would silently turn the whole feature off, which is worse than the
    /// duplicate it prevents: the duplicate costs one repeated instruction, the silence costs every
    /// instruction.</para>
    /// </remarks>
    public bool TryClaimSplitOrder(string caller, DateTime nowUtc, Action<string>? warn = null)
    {
        // An empty caller would be one shared bucket for everybody who has no identity, which is the
        // opposite of what the key is for. Unreachable from the server — `CallerFor` always
        // substitutes the checkout — so a contract violation rather than a runtime condition.
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);
        var file = FileFor(caller);
        try
        {
            Directory.CreateDirectory(Dir);
            using var stream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            using var reader = new StreamReader(stream, leaveOpen: true);
            if (StillRemembered(StampIn(reader.ReadToEnd()), nowUtc))
            {
                return false;
            }

            // Truncated and rewritten in place: an expired claim is REPLACED without the file ever
            // ceasing to exist, which is what makes the delete race impossible rather than unlikely.
            stream.SetLength(0);
            using var writer = new StreamWriter(stream);
            writer.Write($"{nowUtc.ToString("o", CultureInfo.InvariantCulture)}\t{caller}\n");
            return true;
        }
        catch (IOException) when (File.Exists(file))
        {
            return false; // another process is holding it — which is another process claiming it
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn?.Invoke($"the split-order memory at {Dir} could not be written ({e.Message}) — "
                + "the order is being given, and may be given again");

            return true;
        }
    }

    private DateTime? ReadStamp(string caller)
    {
        var file = FileFor(caller);
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            return StampIn(File.ReadAllText(file));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Including the case where a claim is being written right now: FileShare.None refuses
            // this read, and "nobody has claimed it" is the only honest answer a reader can give.
            return null;
        }
    }

    /// <summary>The stamp a claim file carries, or null for an empty, torn or unparseable one.</summary>
    private static DateTime? StampIn(string content) =>
        DateTime.TryParse(
            content.Split('\t')[0].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    // A session id is somebody else's string: it can hold slashes, colons, anything. Hashed rather
    // than escaped, because the file name is never read by a person and the id is kept INSIDE the
    // file for when one has to be. The digest is used WHOLE — sixteen hex characters is 64 bits, and
    // a caller silently inheriting another's claim is not a failure worth saving 48 characters for.
    private string FileFor(string caller) =>
        Path.Combine(Dir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(caller))) + ".txt");
}
