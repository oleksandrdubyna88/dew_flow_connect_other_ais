using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A reader of the job claim sees a whole claim or no file — never an empty one.
/// </summary>
/// <remarks>
/// <para><b>What this is for.</b> The claim file is the ONLY thing that survives a killed shim, and
/// the parent reads it to cancel a job that is still costing money on a Team server. `Claim` wrote
/// it with `File.WriteAllText`, which truncates first and writes second: a process killed between
/// those two steps leaves a file that exists and names nothing. `ReadClaim` then returns
/// `RemoteClaim.None`, `CancelAbandonedAsync` gives up, and the job runs to its queue deadline while
/// somebody is billed for a review nobody can explain — the exact loss `Claim`'s own docstring says
/// must not happen silently.</para>
/// <para><b>How it was found.</b> Not by reading: `AKilledShimLEAVESAClaimTheParentCanActOn` failed
/// on a win-x64 release runner and nowhere else, expecting `job-77` and getting `""`. The test waited
/// for the file to EXIST and then killed the shim, which on a slower machine is a kill inside the
/// write. The flake and the defect are the same fact.</para>
/// </remarks>
public sealed class ClaimIsWrittenAtomicallyTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"coai-claim-{Guid.NewGuid():N}.json");

    /// <summary>
    /// The write leaves nothing beside the claim, whatever happened.
    /// </summary>
    /// <remarks>
    /// A create-then-rename writer earns its atomicity by leaving a sibling around for an instant,
    /// and the failure mode of that shape is litter: one abandoned `.writing-` file per crashed
    /// shim, in a directory nobody sweeps.
    /// </remarks>
    [Fact]
    public void TheWriteLeavesNoTemporaryFileBehind()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"coai-claimdir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var jobFile = Path.Combine(dir, "job.json");

        for (var i = 0; i < 5; i++)
        {
            RemoteRuntime.Claim(jobFile, "https://coai.example.com", $"job-{i}", "t.json").Should().BeEmpty();
        }

        Directory.EnumerateFiles(dir).Should().Equal([jobFile]);
        RemoteRuntime.ReadClaim(jobFile).JobId.Should().Be("job-4");
        Directory.Delete(dir, recursive: true);
    }

    /// <summary>
    /// The write goes THROUGH the sibling — asserted deterministically, with no seam in the product.
    /// </summary>
    /// <remarks>
    /// <para>The gate asked for this twice and was right both times: the kill test reproduces the CI
    /// failure but cannot be made to fail on a fast machine, so a return to a direct
    /// `File.WriteAllText` could have slipped past every other test here. A synchronisation point
    /// injected into the shim would have proved it, at the cost of test-only machinery on a path that
    /// runs in production.</para>
    /// <para>The filesystem is the seam instead. Block the SIBLING's path — put a directory where the
    /// `.writing` file must go — and the two implementations answer differently and deterministically:
    /// a create-then-rename writer cannot write and reports why, while a direct writer neither needs
    /// the sibling nor notices it and publishes the claim happily. Verified by reverting the fix: this
    /// test goes red with `Claim` returning empty and a claim on disk.</para>
    /// </remarks>
    [Fact]
    public void WithTheSiblingsPathBlocked_NoClaimIsPublished()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"coai-blocked-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var jobFile = Path.Combine(dir, "job.json");
        // A directory cannot be opened for writing, so the sibling write is refused.
        Directory.CreateDirectory(jobFile + ".writing");

        var failure = RemoteRuntime.Claim(jobFile, "https://coai.example.com", "job-77", "t.json");

        failure.Should().NotBeEmpty("the write could not reach its sibling and must say so");
        File.Exists(jobFile).Should().BeFalse(
            "a claim that never went through the sibling was written straight at the destination, "
            + "which is the truncate-then-write this whole change removed");
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void AClaimSurvivesARoundTrip()
    {
        // The guard on the other side: making the write atomic must not change what it writes.
        var jobFile = TempFile();

        RemoteRuntime.Claim(jobFile, "https://coai.remsoft.dev/", "job-77", "t.json").Should().BeEmpty();

        var claim = RemoteRuntime.ReadClaim(jobFile);
        claim.JobId.Should().Be("job-77");
        claim.TokenFile.Should().Be("t.json");
        claim.Server.Should().Be(TeamServerAuth.Normalise("https://coai.remsoft.dev/"));
        File.Delete(jobFile);
    }

    [Fact]
    public void AClaimIntoAMissingDirectory_ReportsWhyRatherThanThrowing()
    {
        var nowhere = Path.Combine(Path.GetTempPath(), $"coai-absent-{Guid.NewGuid():N}", "deeper", "job.json");

        RemoteRuntime.Claim(nowhere, "https://coai.example.com", "job-1", "t.json")
            .Should().NotBeEmpty("losing the claim costs a cancellation, so it is returned rather than swallowed");
    }
}
