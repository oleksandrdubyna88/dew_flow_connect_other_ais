using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// S15 of <c>research/PLAN_vibe_reads_a_folder_of_repositories.md</c> — the product flow: a REAL Vibe question-consultant row, the whole
/// <c>ask_consultants</c> service, git for real, the vibe CLI played in process, granted a PARENT folder. The pieces are pinned one by one
/// in <c>AVibeDiskRowExpandsAParentFolderTests</c> and <c>AVibeDiskRowSaysWhatItSkippedTests</c>; this is the only test that drives all
/// of them together from the person's side: where the launch stands, what it can read, what the prompt says, and what is left after it.
/// </summary>
/// <remarks>
/// A guard, green from the start: E1.2 and E1.3 landed before it was written, so there is no red to record. It is kept because it is the
/// one test over a parent folder through the service.
/// </remarks>
public sealed class AVibeDiskRowReadsAParentFolderThroughTheServiceTests : IDisposable
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private const string OneSkipped =
        "skipped 1 linked worktree whose main checkout is copied — its branch and uncommitted files were not read; grant a worktree directly to include it";

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-vibe-flow-");
    private readonly VibeQuestionRowFixture _rows;

    public AVibeDiskRowReadsAParentFolderThroughTheServiceTests() => _rows = new VibeQuestionRowFixture(_dir, _launcher);

    public void Dispose() => _dir.Dispose();

    /// <summary>The copy root a file's key stands under: <c>0/alpha/snapshot-&lt;id&gt;</c> — the grant index, the repository, the snapshot.</summary>
    private static string CopyRootOf(string key)
    {
        var parts = key.Split('/');

        return string.Join('/', parts.Take(Array.FindIndex(parts, part => part.StartsWith("snapshot-", StringComparison.Ordinal)) + 1));
    }

    /// <summary>A Windows path spelled the way WSL2 mounts it (<c>D:\x\y</c> is <c>/mnt/d/x/y</c>); a POSIX path is already that.</summary>
    private static string AsWslSeesIt(string path) =>
        path is [var drive, ':', ..] ? $"/mnt/{char.ToLowerInvariant(drive)}{path[2..].Replace('\\', '/')}" : path;

    [Theory]
    [InlineData(VibeFlowHost.Windows)]
    [InlineData(VibeFlowHost.Wsl2)]
    public async Task AParentOfTwoRepositories_OneWithVibeHooks_LaunchesInTheCopyTree_SeesBoth_SeesNoVibe_NamesNoOriginal_AndTheCopiesAreGone(string mode)
    {
        using var flow = VibeFlowHost.Enter(mode, _dir.At("flow"));
        using var noBudget = VibePathBudget.Using(VibePathBudget.None);
        var parent = Directory.CreateDirectory(_dir.At("parent")).FullName;
        var alpha = Path.Combine(parent, "alpha");
        var beta = Path.Combine(parent, "beta");
        await _rows.RepositoryAsync(alpha, ("src/Mailer.cs", "void Send() { }\n"), (".vibe/hooks.toml", "[[hooks]]\n"));
        await _rows.RepositoryAsync(beta, ("Retry.cs", "int Attempts = 3;\n"));
        var worktree = await _launcher.RunAsync(new ProcessRequest("git", ["worktree", "add", "-b", "wt", Path.Combine(parent, "alpha-wt")], alpha), TestContext.Current.CancellationToken);
        worktree.ExitCode.Should().Be(0, worktree.StdErr);
        await _rows.RepositoryAsync(_dir.At("asking"), ("readme.txt", "where the question is asked from"));
        var vibe = new PlayedVibe(_launcher, Advice);
        var service = _rows.ServiceFor(vibe, string.Empty, "glm-5-3", parent);

        var reply = await VibeQuestionRowFixture.AskAsync(service, _dir.At("asking"), TestContext.Current.CancellationToken);

        var row = VibeQuestionRowFixture.TheRow(reply);
        row.GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, reply.ToString());
        var turn = vibe.Turns.Should().ContainSingle().Subject;
        Path.GetFileName(turn.Request.WorkingDirectory).Should().Be(ConversationSnapshots.TreeName, "the launch stands in the row's copy tree, not in any folder of the person's");

        var keys = turn.Files.Keys.ToList();
        keys.Should().HaveCount(2, "the two repositories' files, and none of the .vibe/ the first one commits, nor the linked worktree's copy of them");
        keys.Should().Contain(key => key.StartsWith("0/alpha/snapshot-", StringComparison.Ordinal) && key.EndsWith("/src/Mailer.cs", StringComparison.Ordinal));
        keys.Should().Contain(key => key.StartsWith("0/beta/snapshot-", StringComparison.Ordinal) && key.EndsWith("/Retry.cs", StringComparison.Ordinal));
        keys.Should().NotContain(key => key.Contains(".vibe", StringComparison.Ordinal), "the snapshot leaves out what could configure Vibe (D6)");
        keys.Should().NotContain(key => key.Contains("alpha-wt", StringComparison.Ordinal), "the linked worktree is left out (D9)");

        row.GetProperty("note").GetString().Should().Contain(OneSkipped, "the person is told what was left out");
        var prompt = turn.Request.StdIn;
        prompt.Should().Contain(OneSkipped, "and so is the model, where the prompt names its copies");
        foreach (var root in keys.Select(CopyRootOf).Distinct())
        {
            prompt.Should().Contain(Path.Combine(turn.Request.WorkingDirectory, Path.Combine(root.Split('/'))), "the prompt names each copy Vibe can read");
        }

        foreach (var original in (string[])[parent, alpha, beta, Path.Combine(parent, "alpha-wt")])
        {
            prompt.Should().NotContain(original, "never an original path, which Vibe cannot read (M-V11)");
            prompt.Should().NotContain(AsWslSeesIt(original), "nor the /mnt/<drive>/… spelling of it that the WSL2 arm would name");
        }

        Directory.Exists(Path.GetDirectoryName(turn.Request.Environment[VibeLaunch.HomeVariable])!).Should().BeFalse("the row's home and every copy are gone once it answered");
        Directory.Exists(turn.Request.WorkingDirectory).Should().BeFalse("the copy tree went with the row's scratch");
        flow.AssertLinked(mode);
    }
}
