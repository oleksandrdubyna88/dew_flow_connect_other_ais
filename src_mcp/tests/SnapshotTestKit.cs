using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Snapshots;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the three snapshot test classes share: a budget no other test can fill, a snapshot asserted MADE, and a
/// refusal asserted as one — extracted rather than written three times (reuse-first, the second move).
/// </summary>
internal static class SnapshotTestKit
{
    /// <summary>A budget of this test's own, so a snapshot another test left undisposed cannot refuse this one.</summary>
    public static SnapshotBudget RoomyBudget() => new(long.MaxValue / 2, 0, static _ => long.MaxValue / 2);

    public static ReviewSnapshots Snapshots(IProcessLauncher launcher, SnapshotOptions? options = null, SnapshotBudget? budget = null) =>
        new(launcher, options ?? SnapshotOptions.Default, budget ?? RoomyBudget(), ReviewSnapshots.Quiet);

    /// <summary>The snapshot, or a failed assertion quoting the refusal.</summary>
    public static async Task<ReviewSnapshot> MadeAsync(ReviewSnapshots snapshots, SnapshotSource source, string scratch)
    {
        var outcome = await snapshots.MakeAsync(source, scratch, TestContext.Current.CancellationToken);

        return outcome switch
        {
            SnapshotOutcome.Made made => made.Snapshot,
            SnapshotOutcome.Refused refused => throw new Xunit.Sdk.XunitException($"the snapshot was refused: {refused.Reason}"),
            _ => throw new Xunit.Sdk.XunitException("the union is closed"),
        };
    }

    /// <summary>The refusal's sentence, or a failed assertion saying a snapshot was made.</summary>
    public static async Task<string> RefusedAsync(ReviewSnapshots snapshots, SnapshotSource source, string scratch)
    {
        var outcome = await snapshots.MakeAsync(source, scratch, TestContext.Current.CancellationToken);
        if (outcome is SnapshotOutcome.Made made)
        {
            made.Snapshot.Dispose();
            throw new Xunit.Sdk.XunitException("a snapshot was made where a refusal was expected");
        }

        return ((SnapshotOutcome.Refused)outcome).Reason;
    }

    /// <summary>No <c>snapshot-*</c> directory, published or partial, under the scratch directory.</summary>
    public static void NothingPublishedUnder(string scratch) =>
        Directory.GetDirectories(scratch, $"{ReviewSnapshots.Prefix}*").Should().BeEmpty("a refusal leaves no partial copy behind");

    /// <summary>Reads the file the snapshot holds at <paramref name="relative"/> (forward slashes).</summary>
    public static string TextAt(ReviewSnapshot snapshot, string relative) =>
        File.ReadAllText(Path.Combine(snapshot.Root, relative.Replace('/', Path.DirectorySeparatorChar)));

    public static bool Has(ReviewSnapshot snapshot, string relative)
    {
        var at = Path.Combine(snapshot.Root, relative.Replace('/', Path.DirectorySeparatorChar));

        return File.Exists(at) || Directory.Exists(at);
    }
}
