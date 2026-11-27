using System.Diagnostics;
using CoaiMcp.Core.Roles;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Server.RoleJobs;
using CoaiMcp.Work;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.CodexScript;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3, pass 9, M20 and the shoulds of its family (S66 to S69): the host reads <c>.coai/toolchain.yaml</c> of the WORKTREE — a folder the coder's unit and every check unit can write — in every check round; a FIFO there must never be waited on. The ways of Fable's probe P9_3: (1) the base has no toolchain file and a command makes the
/// FIFO; (2) the base has one, turn 0 deletes it and a fix turn makes the FIFO; (3) the base has one, turn 0 deletes it and a CHECK makes the FIFO — no command of the model. Each ends the job with a stop word, never a wait. Made-up folders only; the rig cases go through the product's road with real units.
/// </summary>
/// <remarks>
/// The stop word is <c>checks-changed-tree</c>, not <c>coder-boundary</c>: a FIFO there proves no command of the model (a check can make it, way 3), so no boundary is said to have broken; it says that the tree the checks ran in is not what coai committed and expected, so what they say is not trusted — the word the existing rule uses for exactly that, which is never sent to the coder and never fails over.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AToolchainFileOfTheWorktreeTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);

    private const string ToolchainName = ".coai/toolchain.yaml";

    private static void MakeFifo(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var made = Process.Start(new ProcessStartInfo("mkfifo", [path]) { UseShellExecute = false })!;
        made.WaitForExit();
        made.ExitCode.Should().Be(0, "mkfifo makes the stand-in");
    }

    private static async Task<T?> WithinAsync<T>(Func<T> work, TimeSpan? time = null) where T : class
    {
        var task = Task.Run(work);

        return await Task.WhenAny(task, Task.Delay(time ?? Wait)) == task ? await task : null;
    }

    // ---------- the pure reads ----------

    [Fact]
    public async Task P9_2_TheWorktreesToolchainFile_ReadAsAFifo_GivesAnAnswer_InsteadOfWaiting()
    {
        using var temp = TempDir.For("coai-m20-");
        MakeFifo(Path.Combine(temp.Path, ".coai", "toolchain.yaml"));

        var read = await WithinAsync(() => new DiskWorkFolder(temp.Path).Read(ToolchainName, ToolchainTables.MaxBytes));

        read.Should().NotBeNull("the read of the worktree's toolchain file never waits on a FIFO");
        read.Should().NotBeOfType<WorkFile.Text>().And.NotBeOfType<WorkFile.Missing>("a FIFO is neither a text nor an absent file");
    }

    [Fact]
    public async Task S66_TheExpiryOfASignInCopy_ReadAsAFifo_IsNull_InsteadOfWaiting()
    {
        using var temp = TempDir.For("coai-s66-");
        var copy = Path.Combine(temp.Path, "auth.json");
        MakeFifo(copy);
        var recipe = (CoderSignInRecipe.CopyForJob)AgyCoderBed.Vendor().SignIn;

        var answered = await WithinAsync(() => (object?)SignInCopy.ExpiryOfFile(recipe, copy) ?? "none");

        answered.Should().Be("none", "a FIFO has no expiry, and the read of it never waits");
    }

    [Fact]
    public async Task S67_TheWalk_CountsEveryEntryItSees_TheFoldersItEnteredToo()
    {
        using var temp = TempDir.For("coai-s67-");
        var root = Path.Combine(temp.Path, "sessions");
        Directory.CreateDirectory(root);
        // Folders that are entered one after the other, so few wait at any time: 100 folders of 202 folders each is more entries than the limit.
        for (var outer = 0; outer < 100; outer++)
        {
            for (var inner = 0; inner < 202; inner++)
            {
                Directory.CreateDirectory(Path.Combine(root, "d" + outer.ToString(System.Globalization.CultureInfo.InvariantCulture), "e" + inner.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        var walked = await WithinAsync(() => UnitWrittenFile.Walk(root).Problem, TimeSpan.FromSeconds(60));

        walked.Should().NotBeNullOrEmpty("a walk past the limit of entries says so, whether the entries were files or folders that were entered");
    }

    [Fact]
    public async Task S68_ARolloutWithALineLongerThanTheLimit_IsNotARegularRollout_AndIsNotReadToItsEnd()
    {
        using var temp = TempDir.For("coai-s68-");
        var sessions = Path.Combine(temp.Path, "sessions");
        Directory.CreateDirectory(sessions);
        await File.WriteAllTextAsync(Path.Combine(sessions, $"rollout-2026-11-27T10-00-00-{ThreadId}.jsonl"), new string('x', 6 * 1024 * 1024), TestContext.Current.CancellationToken);

        var inspected = await WithinAsync(() => CodexTurnCheck.InspectSessions(sessions).Problem, TimeSpan.FromSeconds(60));

        inspected.Should().NotBeNullOrEmpty("a line over the limit is a file no CLI wrote");
    }

    [Fact]
    public void S69_ALinkAtTheSignInCopy_IsNotReadThrough_ByTheTokenRoad()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "a symbolic link needs a privilege on Windows");
        using var temp = TempDir.For("coai-s69-");
        var outside = Path.Combine(temp.Path, "outside.json");
        File.WriteAllText(outside, AgyCoderBed.SignInFile(DateTime.UtcNow.AddHours(1)));
        var copy = Path.Combine(temp.Path, "copy.json");
        File.CreateSymbolicLink(copy, outside);
        var recipe = (CoderSignInRecipe.CopyForJob)AgyCoderBed.Vendor().SignIn;

        SignInCopy.TokensOf(recipe, copy).Should().BeEmpty("the tokens of the file behind a link are never read as the copy's");
    }

    // ---------- the product road, with real units: Fable's three ways ----------

    public static TheoryData<string> Ways => new() { "base-has-none-and-a-command-makes-a-fifo", "delete-then-a-fix-turn-makes-a-fifo", "delete-then-a-check-makes-a-fifo" };

    [Theory]
    [MemberData(nameof(Ways))]
    public async Task M20_AFifoAtTheWorktreesToolchainFile_EndsTheJobWithAStopWord_NeverAWait(string way)
    {
        var toolchain = way switch
        {
            "base-has-none-and-a-command-makes-a-fifo" => string.Empty,
            "delete-then-a-check-makes-a-fifo" => ToolchainFixture.Shell(
                "if [ ! -e .coai/toolchain.yaml ]; then /usr/bin/mkfifo .coai/toolchain.yaml; fi; echo built",
                $"if grep -rq '{CoderRig.Red}' src 2>/dev/null; then echo 'FAILED thing: the state is red'; exit 1; fi; echo tested"),
            _ => null,
        };
        await using var world = await CodexRigWorld.CreateAsync(toolchain);
        var rig = world.Rig;
        var first = (Dictionary<string, object?>)Turn(Writes(way == "base-has-none-and-a-command-makes-a-fifo" ? CoderRig.Green : CoderRig.Red), Report());
        var second = (Dictionary<string, object?>)Turn(Writes(CoderRig.Green), Report());
        switch (way)
        {
            case "base-has-none-and-a-command-makes-a-fifo":
                first["cwdFifos"] = new[] { ToolchainName };
                break;
            case "delete-then-a-fix-turn-makes-a-fifo":
                first["cwdDelete"] = new[] { ToolchainName };
                second["cwdFifos"] = new[] { ToolchainName };
                break;
            default:
                first["cwdDelete"] = new[] { ToolchainName };
                break;
        }

        rig.SaysRaw("terra", first, second);
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job).WaitAsync(TimeSpan.FromSeconds(90), TestContext.Current.CancellationToken);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "checks-changed-tree"), $"{way}: {ended.Reason}");
    }

    [Fact]
    public async Task S69_AFifoMadeAtTheCodexSignInCopy_ThroughTheRig_EndsTheJobCoderBoundary_NeverGreen()
    {
        await using var world = await CodexRigWorld.CreateAsync();
        var rig = world.Rig;
        var turn = (Dictionary<string, object?>)Turn(Writes(CoderRig.Green), Report());
        turn["homeFifos"] = new[] { Path.Combine(".codex", "auth.json") };
        rig.SaysRaw("terra", turn);
        var job = rig.NewJob(route: ["codex/terra"]);

        var (_, ended) = await rig.RunAsync(job).WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
        ended.Reason.Should().Contain("regular file", "the sentence says what was wrong with the entry");
    }
}
