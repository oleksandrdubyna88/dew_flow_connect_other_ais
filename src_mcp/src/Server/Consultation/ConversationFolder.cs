using System.Globalization;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Server;

/// <summary>
/// What a conversation's folder says about itself: WHICH store made it, and when its last turn started, may last until and
/// ended — written beside its home and snapshot (D20 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>, R1 and R4 of the S3
/// risk consultation).
/// </summary>
/// <param name="Store">The data directory whose record store owns the conversation (<see cref="ConversationFolder.StoreOf"/>); empty when nobody wrote one.</param>
/// <param name="StartedUtc">When the last turn started.</param>
/// <param name="Ceiling">How long that turn may run at most — the consult turn's own deadline (<c>ConsultationDeadline.For</c>).</param>
/// <param name="Ended">The turn ended in the process that ran it — its child was read, so nothing of it still runs.</param>
public sealed record ConversationStamp(string Store, DateTime StartedUtc, TimeSpan Ceiling, bool Ended)
{
    /// <summary>A folder with no stamp — made by nothing this build knows, or left before its stamp was written.</summary>
    public static ConversationStamp Unowned { get; } = new(string.Empty, DateTime.MinValue, TimeSpan.Zero, Ended: true);

    /// <summary>
    /// Whether the last turn can no longer be running: it ended where it ran, or its ceiling has passed. A server killed
    /// mid-turn never writes <see cref="Ended"/>, so its Vibe child — bounded by its own <c>--time-limit</c>, under the
    /// launch's timeout, under this ceiling — is past its life once the ceiling is (R4).
    /// </summary>
    public bool TurnIsOver(DateTime nowUtc) => Ended || nowUtc - StartedUtc > Ceiling;
}

/// <summary>The stamp and the cross-process turn lock of a conversation's folder.</summary>
/// <remarks>
/// <para><b>The lock</b> is a file held open exclusively (<see cref="HeldFile"/>) inside the folder for a whole turn, and
/// taken — with no wait — by the home sweep before it deletes anything: a SECOND server on the same machine, which the
/// in-process <c>VibeHomeTurns</c> cannot see, then cannot delete a live conversation's home (R1). The kernel lets go of it
/// when its holder dies. On Unix .NET's exclusive open is an advisory <c>flock</c>, honoured between .NET processes — every
/// holder here is one.</para>
/// <para><b>The stamp</b> is a few <c>key=value</c> lines — no serialiser, nothing a reflection-free build has to know.</para>
/// </remarks>
public static class ConversationFolder
{
    /// <summary>The stamp's file name inside the folder.</summary>
    public const string StampName = "coai-conversation.txt";

    /// <summary>The turn lock's file name inside the folder.</summary>
    public const string LockName = "coai-turn.lock";

    /// <summary>How a data directory is named as an owner: its full path, its case folded on Windows only (the <c>RepositoryLock</c> rule).</summary>
    public static string StoreOf(string dataDir)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDir));

        return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
    }

    /// <summary>The stamp for a turn that starts now — its owner, its start and its ceiling.</summary>
    public static void Begin(string folder, string store, DateTime nowUtc, TimeSpan ceiling) =>
        Write(folder, new ConversationStamp(store, nowUtc, ceiling, Ended: false));

    /// <summary>The same stamp, marked ended — the turn's child was read in this process.</summary>
    public static void End(string folder)
    {
        if (Read(folder) is { Store.Length: > 0 } stamp)
        {
            Write(folder, stamp with { Ended = true });
        }
    }

    /// <summary>The folder's stamp, or <see cref="ConversationStamp.Unowned"/> when there is none or it does not read.</summary>
    public static ConversationStamp Read(string folder)
    {
        try
        {
            var path = Path.Combine(folder, StampName);

            return File.Exists(path) ? Parsed(File.ReadAllLines(path)) : ConversationStamp.Unowned;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ConversationStamp.Unowned;
        }
    }

    /// <summary>The folder's turn lock, held — or why not (<see cref="Hold.Busy"/>: somebody runs a turn in it).</summary>
    public static Hold TakeTurn(string folder) => HeldFile.Take(Path.Combine(folder, LockName));

    private static void Write(string folder, ConversationStamp stamp) =>
        AtomicFile.Write(Path.Combine(folder, StampName), string.Join('\n',
            $"store={stamp.Store}",
            $"started={stamp.StartedUtc.ToString("O", CultureInfo.InvariantCulture)}",
            $"ceilingSeconds={stamp.Ceiling.TotalSeconds.ToString(CultureInfo.InvariantCulture)}",
            $"ended={(stamp.Ended ? "true" : "false")}") + "\n");

    private static ConversationStamp Parsed(IEnumerable<string> lines)
    {
        var values = lines.Select(line => line.Split('=', 2)).Where(pair => pair.Length == 2).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
        var started = DateTime.TryParse(values.GetValueOrDefault("started"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when);
        var ceiling = double.TryParse(values.GetValueOrDefault("ceilingSeconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds);

        return started && ceiling && values.GetValueOrDefault("store") is { Length: > 0 } store
            ? new ConversationStamp(store, when.ToUniversalTime(), TimeSpan.FromSeconds(seconds), values.GetValueOrDefault("ended") == "true")
            : ConversationStamp.Unowned;
    }
}
