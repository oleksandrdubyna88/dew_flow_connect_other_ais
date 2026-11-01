using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CoaiMcp.Tests;

/// <summary>
/// Cuts a materialised fixture's transcript files at an instant: every line stamped AFTER it is gone, as if the read had happened then.
/// The live record of a round is attributed right after the round completes; this is what makes that read reproducible from E1's
/// sanitised transcripts, so the replay facts compare the live record with the backfill on real data (E4.S5, brief 4).
/// </summary>
/// <remarks>
/// It is a static helper and not a method of <see cref="CallerTranscriptFixture"/>: that class's public <c>void</c> methods are the mutation
/// helpers E3.S5 demands a sample call for. Each file keeps the terminator it had, and its last-write time is set to its own largest kept
/// stamp, so a read bounded by an instant sees the same file times as the original.
/// </remarks>
internal static class FixtureTruncation
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public static void After(CallerTranscriptFixture fixture, DateTime instant)
    {
        foreach (var path in Files(fixture))
        {
            var kept = File.ReadAllText(path).Split('\n').Where(line => line.Length > 0 && StampOf(line) <= instant).ToList();
            File.WriteAllText(path, kept.Count == 0 ? string.Empty : string.Join('\n', kept) + "\n", Utf8);
            var largest = kept.Select(StampOf).DefaultIfEmpty(DateTime.MinValue).Max();
            if (largest > DateTime.MinValue)
            {
                File.SetLastWriteTimeUtc(path, largest);
            }
        }
    }

    private static IEnumerable<string> Files(CallerTranscriptFixture fixture) =>
        fixture.MainFile.Length == 0 ? [] : [fixture.MainFile, .. fixture.AgentKeys.Select(key => Path.Combine(fixture.SessionFolder, "subagents", "agent-" + key + ".jsonl"))];

    /// <summary>The line's stamp; a line with none (an <c>ai-title</c>) is kept by reading as the earliest instant.</summary>
    private static DateTime StampOf(string line)
    {
        using var document = JsonDocument.Parse(line);

        return document.RootElement.TryGetProperty("timestamp", out var stamp)
            ? DateTime.Parse(stamp.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
            : DateTime.MinValue;
    }
}
