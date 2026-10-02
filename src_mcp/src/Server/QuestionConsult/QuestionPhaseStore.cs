using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoaiMcp.Runners.Context;

namespace CoaiMcp.Server;

/// <summary>
/// What a phase record is keyed by (<c>todo/PLAN_question_consultant.md</c> D14 (b)): the repository's common dir
/// and the plan's file name when a plan is known, else the caller session. A closed union.
/// </summary>
public abstract record QuestionPhaseKey
{
    /// <param name="RepoId">The git common dir, as the cadence record is keyed — one record across worktrees and branches.</param>
    /// <param name="PlanKey">The plan's file name, lower-cased (<c>EpicRef.PlanKey</c>), so promotion to <c>research/</c> keeps it.</param>
    public sealed record Plan(string RepoId, string PlanKey) : QuestionPhaseKey;

    /// <summary>Unplanned work has no plan key: the caller session is the unit the person was asked under.</summary>
    public sealed record Caller(string CallerId) : QuestionPhaseKey;

    private QuestionPhaseKey() { }

    /// <summary>One spelling per identity — length-prefixed, the cadence store's lesson: a <c>#</c> inside either part cannot make two identities one.</summary>
    public string Spelled => this switch
    {
        Plan plan => $"plan:{plan.RepoId.Length}:{RepositoryIdentity.Normalised(plan.RepoId)}#{plan.PlanKey.ToLowerInvariant()}",
        Caller caller => $"caller:{caller.CallerId}",
        _ => throw new InvalidOperationException("the union is closed"),
    };
}

/// <summary>One record on disk: which questions reached the person since the plan's proceed, and whether the release was seen.</summary>
/// <remarks>Every member normalises null in its accessor, for the reason <c>QuestionConsultRecord</c> gives.</remarks>
public sealed record QuestionPhaseState
{
    public string Key { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>The escalation ids that reached the person — ids, so a retry after a lost reply is not a second batch.</summary>
    public IReadOnlyList<string> Batches { get => field ?? []; init; } = [];

    /// <summary>When the release was first observed; empty until then. A record seen un-released after it starts over.</summary>
    public string ReleasedUtc { get => field ?? string.Empty; init; } = string.Empty;

    public string UpdatedUtc { get => field ?? string.Empty; init; } = string.Empty;
}

/// <summary>What reading a phase record came to: the count, or that the record exists and could not be read (unreadable → the gate allows).</summary>
public sealed record QuestionPhaseRead(int Batches, bool Readable, string Why);

/// <summary>
/// Where the batches that reached the person are counted: one JSON per key under <c>&lt;dataDir&gt;/qphase/</c>
/// (<c>todo/PLAN_question_consultant.md</c> §4, D14 (b), §5).
/// </summary>
/// <remarks>
/// <para><b>Idempotent by escalation id</b>: <see cref="Count"/> records a question once however often it is asked
/// about. <b>Unreadable → allow</b>: a record that exists and cannot be read is said (<see cref="QuestionPhaseRead.Readable"/>),
/// never read as zero and never turned into a refusal — the cadence store fails closed because a gate that waved
/// a group through would skip a consultation somebody owed; this one fails open because a gate that refused on
/// a torn file would stop an AI asking a person, which is the worse failure.</para>
/// <para><b>A plan key reused after its release starts a fresh count</b> (D14 (b)): the release is stamped on the
/// record when <see cref="Observe"/> first sees it, and a record seen un-released afterwards is a new piece of
/// work under an old name — its batches start over.</para>
/// <para>The turn is held across the read–modify–write (the cadence store's lesson). Growth (§5): one small file
/// per plan or caller, deleted <see cref="Retention"/> after its last write by the startup sweep and the one-minute beat.</para>
/// </remarks>
public sealed class QuestionPhaseStore(string dataDir)
{
    /// <summary>How long a phase record outlives its last write (§5: <c>qphase/</c> deleted after 30 days).</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private const string Prefix = "qphase-";

    public string Directory => Path.Combine(dataDir, "qphase");

    public string FileFor(QuestionPhaseKey key)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key.Spelled)))[..16];

        return Path.Combine(Directory, $"{Prefix}{digest}.json");
    }

    /// <summary>The count under this key, with the release observed: stamped when first seen, the count reset when a stamped record is seen un-released.</summary>
    public QuestionPhaseRead Observe(QuestionPhaseKey key, bool released, DateTime nowUtc)
    {
        var file = FileFor(key);
        using var turn = SessionTurn.Take(file);
        var (state, why) = Read(file, key);
        if (why.Length > 0)
        {
            return new QuestionPhaseRead(0, Readable: false, why);
        }

        var next = Transitioned(state, released, nowUtc);
        if (!ReferenceEquals(next, state))
        {
            Write(file, next);
        }

        return new QuestionPhaseRead(next.Batches.Count, Readable: true, string.Empty);
    }

    /// <summary>One more question that reached the person; false when the id was already counted or the record cannot be read. Throws when it cannot be written.</summary>
    public bool Count(QuestionPhaseKey key, string escalationId, DateTime nowUtc)
    {
        var file = FileFor(key);
        System.IO.Directory.CreateDirectory(Directory);
        using var turn = SessionTurn.Take(file);
        var (state, why) = Read(file, key);
        if (why.Length > 0 || state.Batches.Contains(escalationId, StringComparer.Ordinal))
        {
            return false;
        }

        Write(file, state with { Batches = [.. state.Batches, escalationId], UpdatedUtc = Stamp(nowUtc) });

        return true;
    }

    /// <summary>Records whose last write is older than <see cref="Retention"/> go; how many went.</summary>
    public int Sweep(DateTime nowUtc)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return 0;
        }

        return System.IO.Directory.EnumerateFiles(Directory, Prefix + "*.json")
            .Where(file => nowUtc - File.GetLastWriteTimeUtc(file) > Retention)
            .Count(Deleted);
    }

    private static QuestionPhaseState Transitioned(QuestionPhaseState state, bool released, DateTime nowUtc)
    {
        if (released && state.ReleasedUtc.Length == 0)
        {
            return state with { ReleasedUtc = Stamp(nowUtc), UpdatedUtc = Stamp(nowUtc) };
        }

        return !released && state.ReleasedUtc.Length > 0
            ? state with { Batches = [], ReleasedUtc = string.Empty, UpdatedUtc = Stamp(nowUtc) }
            : state;
    }

    /// <summary>The record, or a fresh one when none exists — and the sentence when one exists and cannot be read.</summary>
    private static (QuestionPhaseState State, string Why) Read(string file, QuestionPhaseKey key)
    {
        if (!File.Exists(file))
        {
            return (new QuestionPhaseState { Key = key.Spelled }, string.Empty);
        }

        try
        {
            var state = JsonSerializer.Deserialize(SharedRead.Text(file), QuestionPhaseJsonContext.Default.QuestionPhaseState);

            return state is null
                ? (new QuestionPhaseState { Key = key.Spelled }, $"the question-phase record {Path.GetFileName(file)} is empty")
                : (state, string.Empty);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return (new QuestionPhaseState { Key = key.Spelled }, $"the question-phase record {Path.GetFileName(file)} could not be read ({e.Message})");
        }
    }

    private static void Write(string file, QuestionPhaseState state)
    {
        // The directory, here and not only in Count: the first thing written under a key can be the release stamp
        // Observe makes — found by the after-the-release test, red with DirectoryNotFoundException.
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        // Under the turn the caller already holds (Observe, Count) — never AtomicJson.Write, which would take it again.
        AtomicJson.WriteUnderTurn(file, JsonSerializer.Serialize(state, QuestionPhaseJsonContext.Default.QuestionPhaseState));
    }

    private static bool Deleted(string file)
    {
        try
        {
            File.Delete(file);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false; // the next sweep tries again
        }
    }

    private static string Stamp(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(QuestionPhaseState))]
internal sealed partial class QuestionPhaseJsonContext : JsonSerializerContext;
