using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// No Vibe home lives under TEMP any more (D1 of <c>todo/PLAN_vibe_live_round_fixes.md</c>, A6/A7): a TEMP of any length — the
/// person's, a CI runner's — cannot push Vibe's deepest write past Windows' path limit, because the homes live under the
/// homes root. A round's reservation is a folder holding its lock, and takes a read-only plugin folder with it when it ends.
/// </summary>
/// <remarks>
/// TEMP, TMP and TMPDIR are all pointed at a 150-character folder (<c>Path.GetTempPath</c> reads <c>TMP</c> first on Windows,
/// <c>TMPDIR</c> elsewhere) and restored in <c>finally</c>, so this class is in the non-parallel <c>fakecli-env</c> collection.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AVibeHomesNeverLiveUnderTempTests : IDisposable
{
    private static readonly string[] TempVariables = ["TEMP", "TMP", "TMPDIR"];

    private readonly TempDir _long = TempDir.For("testsToDelete-vh-longtemp-");
    private readonly TempDir _homes = TempDir.For("testsToDelete-vh-root-");
    private readonly Dictionary<string, string?> _before = TempVariables.ToDictionary(name => name, Environment.GetEnvironmentVariable);

    public void Dispose()
    {
        foreach (var (name, value) in _before)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        _long.Dispose();
        _homes.Dispose();
    }

    /// <summary>A TEMP of 150 characters, under this test's own folder.</summary>
    private string LongTemp()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_long, new string('t', Math.Max(1, 150 - _long.Path.Length - 1)))).FullName;
        folder.Length.Should().BeGreaterThanOrEqualTo(150);
        foreach (var name in TempVariables)
        {
            Environment.SetEnvironmentVariable(name, folder);
        }

        Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar).Should().Be(folder, "the fixture really moved TEMP");

        return folder;
    }

    [Fact]
    public void ARoundsHome_IsNotUnderATempThatIsTooLongForVibe()
    {
        var temp = LongTemp();

        using var scratch = RoundScratch.Reserve(_ => { }, _homes, keepsAHome: true);

        scratch.HomeFolder.Should().NotStartWith(temp).And.StartWith(Path.Combine(_homes, VibeHomes.OneShotFolder));
        (VibeRuntime.HomeFor(scratch.HomeFolder, "vibe", "PlanCritique").Length - _homes.Path.Length).Should().BeLessThanOrEqualTo(24,
            "what the layout costs beyond the root (`/coai-vh/<8>/<6>`) fits the Windows budget at the default root, whatever the root's length");
    }

    [Fact]
    public void ARoundsReservation_IsAFolderHoldingItsLock_AndItsDisposalTakesAReadOnlyPluginFolderWithIt()
    {
        var scratch = RoundScratch.Reserve(_ => { }, _homes, keepsAHome: true);
        var folder = scratch.HomeFolder;
        Path.GetDirectoryName(folder).Should().Be(Path.Combine(_homes, VibeHomes.OneShotFolder));
        Runners.Files.HeldFile.IsHeld(Path.Combine(folder, VibeHomes.LockName)).Should().BeTrue("the round holds its folder's lock for its life");
        var home = VibeRuntime.HomeFor(folder, "vibe", "PlanCritique");
        var plugin = Directory.CreateDirectory(Path.Combine(home, "logs", "session", "plugins", "packages", "6b", "x", "skills", "vibe")).FullName;
        File.WriteAllText(Path.Combine(plugin, "SKILL.md"), "x");
        File.SetAttributes(plugin, FileAttributes.ReadOnly);

        scratch.Dispose();

        Directory.Exists(folder).Should().BeFalse("the home and its read-only plugin folder went with the round");
        Directory.Exists(scratch.Directory).Should().BeFalse();
    }

    [Fact]
    public void ARoundThatNoVibeRowServes_ReservesNothing_AndASettingsWithNoRootKeepsTheScratch()
    {
        using var none = RoundScratch.Reserve(_ => { }, _homes, keepsAHome: false);
        using var noRoot = RoundScratch.Reserve(_ => { }, string.Empty, keepsAHome: true);

        none.Homes.Should().BeSameAs(OneShotReservation.None.Instance);
        Directory.Exists(Path.Combine(_homes, VibeHomes.OneShotFolder)).Should().BeFalse("a round with no Vibe row pays nothing");
        noRoot.HomeFolder.Should().Be(noRoot.Directory, "A12: settings built by hand keep today's home in the round's own scratch");
    }

    [Fact]
    public void ARefusedReservation_LeavesTheHomeKeepingRowsOutOfTheRound_WithItsSentence()
    {
        var target = TempDir.For("testsToDelete-vh-target-");
        SnapshotTestKit.LinkNow(Path.Combine(_homes, VibeHomes.OneShotFolder), target);
        using var scratch = RoundScratch.Reserve(_ => { }, _homes, keepsAHome: true);

        var place = RosterBuilder.PlaceFor(new VibeRuntime(), @"C:\work", hasCheckout: false, scratch);

        place.Should().BeOfType<LaunchPlace.Refused>().Which.Reason.Should().Contain("link");
        RosterBuilder.PlaceFor(new ReviewerRuntimeOfAnotherVendor(), @"C:\work", hasCheckout: false, scratch)
            .Should().BeOfType<LaunchPlace.At>("the other rows run");
        target.Dispose();
    }

    private sealed class ReviewerRuntimeOfAnotherVendor : Runners.Reviewers.IReviewerRuntime
    {
        public string Provider => "other";

        public string DefaultExecutable => "other";

        public Runners.Reviewers.ReviewerInvocation Build(string role, string prompt, string worktreePath, string schemaFilePath, string outputDir, Runners.Reviewers.ReviewerSettings settings) =>
            throw new NotSupportedException();

        public string? ReadAnswer(Runners.Reviewers.ReviewerInvocation invocation, ProcessResult result) => null;
    }
}
