using System.Text.Json;
using System.Text.Json.Serialization;
using CoaiBench.Model;

namespace CoaiBench.Store;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<RunRecord>))]
[JsonSerializable(typeof(List<Case>))]
internal sealed partial class BenchJson : JsonSerializerContext;

/// <summary>
/// Where a run is kept, whole.
/// </summary>
/// <remarks>
/// Every finding is written with its text, not a count. A bench that stores its own summary can only
/// answer the question somebody thought of first — and the one measurement that mattered most in
/// this repository was rescued precisely because the raw answers were still there when the metric
/// turned out to be wrong.
/// </remarks>
public static class RunStore
{
    /// <summary>
    /// The whole file, replaced in one step.
    /// </summary>
    /// <remarks>
    /// Written beside the file and moved over it, because a judgement now saves after every run and
    /// a reader — the table verb, a person watching a campaign — that arrives mid-write would
    /// otherwise read half a JSON array and call the file corrupt. A move is one operation; a write
    /// of six hundred kilobytes is not.
    /// </remarks>
    public static async Task SaveAsync(string file, IReadOnlyList<RunRecord> runs, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file) ?? ".");
        var beside = file + ".writing";
        await File.WriteAllTextAsync(
            beside, JsonSerializer.Serialize(runs.ToList(), BenchJson.Default.ListRunRecord), ct);
        File.Move(beside, file, overwrite: true);
    }

    public static async Task<IReadOnlyList<RunRecord>> LoadAsync(string file, CancellationToken ct) =>
        JsonSerializer.Deserialize(
            await File.ReadAllTextAsync(file, ct), BenchJson.Default.ListRunRecord) ?? [];

    /// <summary>
    /// The corpus: plan-and-commit pairs, named.
    /// </summary>
    /// <remarks>
    /// A file rather than flags, because the same pairs are wanted again next month and a command
    /// line is not somewhere anybody keeps them.
    /// </remarks>
    public static async Task<IReadOnlyList<Case>> LoadCorpusAsync(string file, CancellationToken ct) =>
        JsonSerializer.Deserialize(
            await File.ReadAllTextAsync(file, ct), BenchJson.Default.ListCase) ?? [];
}
