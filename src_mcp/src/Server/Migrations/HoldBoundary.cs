using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server.Migrations;

/// <summary>
/// The ONE boundary a held session crosses on its way out of its hold, used by both doors — the migration (<see cref="MigrationV2"/>) and the load of a session that is still
/// held when it is read (<see cref="SessionStore.Load"/>, SPEC §5.1): an answered hold is announced, and the announcement confirmed on disk, BEFORE the session is converted.
/// </summary>
/// <remarks>
/// <para><b>Why before.</b> After the conversion the session is no longer held, so no later run lists the answered hold again: a kill between the conversion and the notice
/// would lose the notice for good. Written first, a kill leaves a held session and a notice; the next run finds the notice (the card id is the key) and converts. The same
/// holds for the load: when its notice cannot be confirmed it leaves the session held — a v2 code path reads a held session as a normal one — and the next migration or load
/// tries again, so a door that cannot announce never converts what the other door would have announced.</para>
/// <para><b>Why read back.</b> The sink answers "accepted" — the writer's queue took it — and the append itself can still be refused by the disk; the writer only logs that.
/// The session is converted only when the line is there. An answer that v2 overrides is never overridden unannounced.</para>
/// <para><b>Reads are bounded.</b> The card ids that already have a notice are read once into a set (<see cref="Noticed"/>), and every "is it written?" before a write is
/// answered by the set. A write is confirmed by reading only the bytes appended after the file length recorded just before it (<see cref="ServerNotices.LinesFrom"/>), so the
/// cost of a notice does not grow with the history; the tail still has to contain the card's own line, which is what makes "accepted" insufficient.</para>
/// <para>One instance is one run's view: the migration keeps one for the whole apply, a load makes its own for the one session it converts, so a load never trusts a set that
/// another process may have outgrown.</para>
/// </remarks>
internal sealed class HoldBoundary(string dataDir, Func<ServerNotice, bool>? notice, TimeProvider clock)
{
    private readonly List<MigrateV2Notice> _written = [];

    private HashSet<string>? _noticed;

    /// <summary>The seam of the notice history's read: every line of <c>server-notices.jsonl</c> and its archive. A test counts how often it is read.</summary>
    internal Func<string, IEnumerable<string>> NoticeLines { get; init; } = ServerNotices.Lines;

    /// <summary>The seam of the confirm after a write: the lines of the live notices file from a byte offset. A test checks where it starts and how much it reads.</summary>
    internal Func<string, long, IEnumerable<string>> NoticeTail { get; init; } = ServerNotices.LinesFrom;

    /// <summary>The notices this instance wrote and read back, in order.</summary>
    public IReadOnlyList<MigrateV2Notice> Written => _written;

    /// <summary>Forgets the set: the next question reads the history afresh, once.</summary>
    public void Forget() => _noticed = null;

    public static MigrateV2Notice RecordOf(MigrateV2AnsweredHold hold, SessionState state) =>
        new(ServerNoticeCodes.V2ConvertedAnsweredHold, hold.Card, state.SessionId, hold.Decision);

    /// <summary>
    /// The session as it leaves its hold, or null when an answered hold's notice could not be written and confirmed — the session is then left exactly as it is.
    /// <paramref name="afterNotice"/> runs between the last notice and the conversion, and only when there was a hold to announce (the migration's fault seam).
    /// </summary>
    /// <param name="fresh">The session as read under its claim.</param>
    /// <param name="sessionFile">The session's file, which names it in a hold's record.</param>
    /// <param name="afterNotice">Called once the notices are on disk, before the conversion is returned.</param>
    public PersistedSession? Released(PersistedSession fresh, string sessionFile, Action? afterNotice = null)
    {
        var holds = AnsweredOf(sessionFile, fresh.State);
        if (!Announced(holds, fresh.State))
        {
            return null;
        }

        if (holds.Count > 0)
        {
            afterNotice?.Invoke();
        }

        return HoldConversion.Converted(fresh);
    }

    /// <summary>
    /// The session as v2 reads it WITHOUT being written: converted when nothing it overrides is unannounced (no answered hold, or each one's notice already on disk), otherwise
    /// as loaded. For a caller that cannot take the claim or cannot save — whoever saves the result must not persist a conversion whose notice nobody wrote.
    /// </summary>
    public PersistedSession InMemory(PersistedSession held, string sessionFile)
    {
        var holds = AnsweredOf(sessionFile, held.State);

        return holds.All(hold => NoticeWritten(hold.Card)) ? HoldConversion.Converted(held) : held;
    }

    /// <summary>The cards of one held session that a person ANSWERED: named in its <c>holdQuestions</c>, or carrying its session id.</summary>
    public List<MigrateV2AnsweredHold> AnsweredOf(string sessionFile, SessionState state)
    {
        var holds = new List<MigrateV2AnsweredHold>();
        var dir = MigrationV2.CardsDir(dataDir);
        if (!Directory.Exists(dir))
        {
            return holds;
        }

        var named = state.Unknown is { } bag && bag.TryGetValue("holdQuestions", out var questions) && questions.ValueKind == JsonValueKind.Array
            ? questions.EnumerateArray().Where(q => q.ValueKind == JsonValueKind.String).Select(q => q.GetString()!).ToHashSet()
            : [];
        var name = Path.GetFileName(sessionFile);
        foreach (var card in TopLevelCards(dir))
        {
            var id = Path.GetFileNameWithoutExtension(card);
            if ((named.Contains(id) || CardOfSession(card, state.SessionId)) && AnswerOf(dataDir, id) is { } decision)
            {
                holds.Add(new MigrateV2AnsweredHold(name, id, decision));
            }
        }

        return holds;
    }

    public static IEnumerable<string> TopLevelCards(string dir) =>
        Directory.EnumerateFiles(dir, "*.json").Where(f => !f.EndsWith(".answer.json", StringComparison.Ordinal)).Order(StringComparer.Ordinal);

    private static bool CardOfSession(string card, string sessionId)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(card)) is JsonObject json && json["sessionId"]?.GetValue<string>() == sessionId;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>The decision a person's non-empty answer carries (empty for a typed answer), or null when there is no answer — a missing, empty or malformed file is none.</summary>
    public static string? AnswerOf(string dataDir, string cardId)
    {
        try
        {
            using var answer = JsonDocument.Parse(File.ReadAllText(Path.Combine(MigrationV2.CardsDir(dataDir), cardId + ".answer.json")));
            var root = answer.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("answer", out var words) || words.ValueKind != JsonValueKind.String || words.GetString()!.Length == 0)
            {
                return null;
            }

            return root.TryGetProperty("decision", out var decision) && decision.ValueKind == JsonValueKind.String ? decision.GetString()! : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Makes sure every answered hold of this session has its notice on disk: true when each is there (already, or written now and confirmed), false when one could not be.
    /// </summary>
    public bool Announced(IReadOnlyList<MigrateV2AnsweredHold> holds, SessionState state)
    {
        foreach (var hold in holds)
        {
            if (NoticeWritten(hold.Card))
            {
                continue;
            }

            // The length BEFORE the write: the confirm reads from here, so what it reads is what the write (and any neighbour's append) added.
            var from = ServerNotices.Length(dataDir);
            if (notice is null || !notice(NoticeOf(hold, state)) || !Confirmed(hold.Card, from))
            {
                return false;
            }

            _written.Add(RecordOf(hold, state));
        }

        return true;
    }

    private ServerNotice NoticeOf(MigrateV2AnsweredHold hold, SessionState state) => new()
    {
        Utc = ServerNotice.Iso(clock.GetUtcNow()),
        Class = "outcome",
        Source = "migrate-v2",
        Code = ServerNoticeCodes.V2ConvertedAnsweredHold,
        Subject = hold.Card,
        Title = ServerNotice.Shortened(
            $"v2 released a held gate that a person had already answered{(hold.Decision.Length > 0 ? $" ('{hold.Decision}')" : string.Empty)} — v1 never applied the answer and v2 never waits, so the gate was released as good_enough"),
        Detail = $"session {state.SessionId} on branch {state.Branch}, card {hold.Card}",
        Cure = "if the decision still matters, review the session again and decide with resolve",
        Answer = hold.Decision.Length > 0 ? hold.Decision : null,
        Repo = state.RepoPath,
        Branch = state.Branch,
        Session = state.SessionId,
    };

    /// <summary>
    /// The card ids that already have a converted-hold notice in <c>server-notices.jsonl</c> (or its archive), read ONCE and kept; an instance that finds nothing to write never
    /// reads the file again. Fed by a read of the file only: the sink's "accepted" never adds to it.
    /// </summary>
    private HashSet<string> Noticed => _noticed ??= SubjectsIn(NoticeLines(dataDir));

    public bool NoticeWritten(string card) => Noticed.Contains(card);

    /// <summary>After a write: the tail from the recorded length must hold the card's own line. Every converted-hold subject it holds joins the set.</summary>
    private bool Confirmed(string card, long from)
    {
        var found = SubjectsIn(NoticeTail(dataDir, from));
        Noticed.UnionWith(found);

        return found.Contains(card);
    }

    private static HashSet<string> SubjectsIn(IEnumerable<string> lines)
    {
        var subjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (line.Contains(ServerNoticeCodes.V2ConvertedAnsweredHold, StringComparison.Ordinal) && SubjectOf(line) is { Length: > 0 } subject)
            {
                subjects.Add(subject);
            }
        }

        return subjects;
    }

    /// <summary>The subject of a converted-hold notice line, or empty for any other line (and for a torn one: somebody else's half-written append is not this card's notice).</summary>
    private static string SubjectOf(string line)
    {
        try
        {
            using var parsed = JsonDocument.Parse(line);
            var root = parsed.RootElement;

            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("code", out var code) && code.ValueEquals(ServerNoticeCodes.V2ConvertedAnsweredHold)
                && root.TryGetProperty("subject", out var subject) && subject.ValueKind == JsonValueKind.String
                    ? subject.GetString() ?? string.Empty
                    : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
