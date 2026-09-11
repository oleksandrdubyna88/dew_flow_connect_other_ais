using System.Globalization;
using System.Text.Json;

namespace CoaiServer;

/// <summary>One finished job, as the ledger recorded it.</summary>
/// <param name="Email">
/// Who spent it. Empty on a line written by a local run, which had no notion of a caller — those are
/// simply not anybody's on a Team server, and they are counted in the company totals and in nobody's
/// personal ones.
/// </param>
/// <param name="CostUsd">
/// Null when nothing priced it, which is the NORMAL case here: these are subscription CLIs, not metered
/// APIs. Never coerce it to zero — see <see cref="UsageTotals"/>.
/// </param>
public sealed record UsageLine(
    DateTimeOffset AtUtc,
    string Email,
    string Vendor,
    string Model,
    string Role,
    string Outcome,
    double Seconds,
    long TokensIn,
    long TokensOut,
    double? CostUsd,
    /// <summary>A review or a conversation. A line written before the field existed was a review.</summary>
    JobKind Kind = JobKinds.WhenNotSaid)
{
    /// <summary>True when this run produced no answer. It cost the same.</summary>
    public bool Failed => !Outcome.Equals("ok", StringComparison.OrdinalIgnoreCase);
}

/// <summary>What a read of the ledger produced, including what it could not read.</summary>
/// <param name="Unreadable">
/// Lines that would not parse. Reported rather than thrown: the file is appended by a process that can
/// be killed mid-write, so a torn last line is an expected state, and a usage page that answers 500
/// because of one bad line is worse than one that is honest about a gap.
/// </param>
public sealed record UsageScan(IReadOnlyList<UsageLine> Lines, int Unreadable);

/// <summary>
/// Reads <c>usage.jsonl</c>, one line at a time.
/// </summary>
/// <remarks>
/// <para><b>Opened for SHARED read and write.</b> The default share mode for a read is
/// <see cref="FileShare.Read"/>, which permits other readers and forbids writers — so a review
/// finishing while somebody looked at the usage page would fail to append its own line, and the
/// spending record would silently lose a row. The ledger's writer already opens with
/// <see cref="FileShare.ReadWrite"/> for the same reason; the reader has to agree. (gemini, plan round.)</para>
/// <para><b>Streamed, not materialised — which is a statement about MEMORY and not about time.</b>
/// Nothing here holds the whole file: the caller is handed a filtered, aggregated result, and a window
/// of one day never allocates a year.</para>
/// <para><b>It does read and parse the whole file, every time, and the sentence above used to read as
/// though it did not.</b> The window is applied after each line is deserialized, so the cost of
/// answering "today" is the cost of the entire history — linear in it, and multiplied by the number of
/// open panels, each asking about once a minute. Measured 2026-09-09 on a synthetic ledger, warm:
/// 1 000 old lines took 4–5 ms and 100 000 took <b>284–303 ms</b>, both returning zero rows. A local
/// microbenchmark rather than production latency; the slope is the point.</para>
/// <para><b>Left as it is, deliberately</b>, on 2026-09-11 — the operator's call. The live ledger holds
/// one line per reviewer per round, thousands rather than hundreds of thousands, so this is a few
/// milliseconds today and rewriting the reader to walk backwards from the end would be optimising
/// something that is not slow. The plan that would do it, with its measurement and its chunk-boundary
/// hazards, is <c>todo/PLAN_the_usage_page_reads_the_window_not_the_history.md</c>. What would make it
/// worth doing: a ledger past roughly 50 000 lines, or a usage page that feels slow to somebody.</para>
/// </remarks>
public sealed class UsageReader(string dataDir)
{
    private readonly string _path = Path.Combine(dataDir, "usage.jsonl");

    /// <summary>The lines inside <paramref name="range"/>, and how many would not parse.</summary>
    /// <remarks>
    /// The window is applied WHILE reading, so the returned list is the answer's size rather than the
    /// history's.
    /// </remarks>
    public UsageScan Read(UsageRange range)
    {
        if (!File.Exists(_path))
        {
            // No reviews have run yet. An empty report, not a fault.
            return new UsageScan([], 0);
        }

        var lines = new List<UsageLine>();
        var unreadable = 0;
        foreach (var text in ReadLines())
        {
            var parsed = Parse(text);
            if (parsed is null)
            {
                unreadable += text.Trim().Length > 0 ? 1 : 0;
                continue;
            }

            if (range.Contains(parsed.AtUtc))
            {
                lines.Add(parsed);
            }
        }

        return new UsageScan(lines, unreadable);
    }

    private IEnumerable<string> ReadLines()
    {
        using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(file);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    /// <summary>One line, or null when it will not parse.</summary>
    /// <remarks>
    /// A line from before the email column simply has an empty one — the field is trailing and
    /// defaulted in <c>UsageEntry</c>, so an old line is valid rather than unreadable, and saying
    /// otherwise would report every historical row as damage.
    /// </remarks>
    private static UsageLine? Parse(string text)
    {
        try
        {
            var entry = JsonSerializer.Deserialize(text, ServerJsonContext.Default.UsageEntryDto);
            if (entry is null)
            {
                return null;
            }

            // Invariant culture and RoundtripKind: the writer stamps ISO-8601 with "O", and parsing
            // that with the machine's own culture is how the same file reads differently on a
            // developer's box and on the VM. RoundtripKind keeps the offset instead of shifting it
            // into local time.
            if (!DateTimeOffset.TryParse(
                    entry.Utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
            {
                return null;
            }

            // A kind this build does not know is READ AS A REVIEW rather than refused. The line is
            // history: it already happened and it already cost money, and dropping it from a spending
            // report because a newer server wrote a word this one has not heard would hide spending —
            // which is the one thing this file must never do.
            var kind = JobKinds.TryRead(entry.Kind, out var said) ? said : JobKinds.WhenNotSaid;

            return new UsageLine(
                at.ToUniversalTime(),
                entry.Email ?? string.Empty,
                entry.Provider ?? string.Empty,
                entry.Model ?? string.Empty,
                entry.Role ?? string.Empty,
                entry.Outcome ?? string.Empty,
                entry.Seconds,
                entry.TokensIn,
                entry.TokensOut,
                entry.CostUsd,
                kind);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
