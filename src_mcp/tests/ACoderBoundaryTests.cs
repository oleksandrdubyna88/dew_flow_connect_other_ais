using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s2 t5, DoD d1: the claude coder process ITSELF runs inside an OS layer of the checks' family — the main checkout, the coder root and the worktree's <c>.git</c> read-only, the worktree writable, no bus, no WSL interop, the coder's own home and
/// temp folders — and a fake CLI that writes into the main checkout or beside the worktree is STOPPED by that layer: the outcome names the path and is not green. What the layer lets the CLI have (the host network, ONE file of the real home, read-only) is
/// read off a real unit too.
/// </summary>
/// <remarks>
/// <para><b>The stop comes from the OS, not from the fake.</b> The fake (<c>write-files</c>) does not obey any flag: it tries each path and reports the ones the OS refused. The paths are checked on the disk afterwards — the main checkout hashed byte for byte,
/// a file of another job's worktree read back — so a layer that did nothing is seen as a write that landed, whatever the fake said.</para>
/// <para>A file full of negatives needs one positive: <see cref="AFakeCoderThatOnlyWritesItsWorktree_IsGreen_AndItsFileIsThere"/> makes something happen with the same fixture.</para>
/// <para>Real <c>systemd-run --user</c> units on temp folders only; the stand-in for the real home is a temp folder (<see cref="CoderBed"/>). Skipped, with the reason, where there is no user manager.</para>
/// </remarks>
public sealed class ACoderBoundaryTests
{
    private static void NeedAUserManager() => Assert.SkipUnless(SystemdUser.Works(out var why), why);

    [Fact]
    public async Task TheCodersUnit_CannotSeeAnotherJobsFolder_WhenTheJobFoldersLieInAFolderOfTheirOwn()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var others = bed.Inner.UseJobsFolderWithAnotherJob();
        bed.Job.JobCover.Should().Be(Path.GetDirectoryName(bed.Job.JobRoot));

        var run = await RunAsync(bed, bed.Shell($"if [ -e {others} ]; then echo sees-other-job; else echo hidden-other-job; fi; if {{ true > \"$HOME/from-unit.txt\"; }} 2>/dev/null; then echo wrote-home; else echo refused-home; fi"));

        run.Result.ExitCode.Should().Be(0, run.Result.StdErr);
        run.Result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().Contain(["hidden-other-job", "wrote-home"], "another job's coder home is not in this coder's unit, and its own home is writable");
        File.Exists(others).Should().BeTrue("a positive control: the file is there on the host");
    }

    [Fact]
    public async Task TheFakeCoder_WritesInItsWorktree_AndIsStoppedInTheMainCheckout_BesideTheWorktree_AndInTheGitPointer_TheOutcomeNamesEveryPath_AndIsNotGreen()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var anotherJob = Path.Combine(bed.CoderRoot, "coai-coder-someone-elses-job");
        Directory.CreateDirectory(anotherJob);
        await File.WriteAllTextAsync(Path.Combine(anotherJob, "theirs.txt"), "theirs\n");
        var mine = Path.Combine(bed.Worktree, "from-the-coder.txt");
        string[] stopped =
        [
            Path.Combine(bed.Main, "new-in-main.txt"),
            Path.Combine(bed.Main, "a.txt"),
            Path.Combine(bed.Main, ".git", "coai-t5-probe"),
            Path.Combine(bed.CoderRoot, "beside-the-worktree.txt"),
            Path.Combine(anotherJob, "theirs.txt"),
            bed.Inner.Tree.GitEntry,
        ];
        var mainBefore = await bed.MainBytesAsync();
        var pointerBefore = await File.ReadAllTextAsync(bed.Inner.Tree.GitEntry);

        var outcome = await bed.Turns().RunAsync(bed.Job, bed.Fake(["write-files", mine, .. stopped]), "c1");

        var hit = outcome.Should().BeOfType<CoderTurnOutcome.WriteStopped>(outcome.ToString()).Subject;
        hit.Green.Should().BeFalse("a coder that was stopped from writing did not do what it set out to do, whatever its exit code");
        hit.Run.Result.ExitCode.Should().Be(0, "the fake carries on after a refused write: it is the OS layer that stops the launch from being a result");
        hit.Lines.Should().HaveCount(stopped.Length);
        foreach (var path in stopped)
        {
            hit.Sentence.Should().Contain(path, "the answer names the path the OS refused");
        }

        hit.Sentence.Should().NotContain(mine, "a write the layer allowed is not named as stopped");
        File.ReadAllText(mine).Should().Be("written by the fake coder\n", "the worktree is the one writable source folder");
        (await bed.MainBytesAsync()).Should().BeEquivalentTo(mainBefore, "not one byte of the main checkout, its .git included, changed");
        File.Exists(Path.Combine(bed.CoderRoot, "beside-the-worktree.txt")).Should().BeFalse();
        File.ReadAllText(Path.Combine(anotherJob, "theirs.txt")).Should().Be("theirs\n", "another job's worktree is beside this one, and read-only to this coder");
        File.ReadAllText(bed.Inner.Tree.GitEntry).Should().Be(pointerBefore, "the .git pointer of the worktree was not touched");
        hit.Run.Gone.Should().BeTrue();
        hit.Run.Record.Boundary.Should().Be(CoaiMcp.Core.Toolchain.BoundaryRecord.UnitBoundary);
    }

    [Fact]
    public async Task AFakeCoderThatOnlyWritesItsWorktree_IsGreen_AndItsFileIsThere()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var mine = Path.Combine(bed.Worktree, "from-the-coder.txt");
        var mainBefore = await bed.MainBytesAsync();

        var outcome = await bed.Turns().RunAsync(bed.Job, bed.Fake("write-files", mine), "c1");

        var done = outcome.Should().BeOfType<CoderTurnOutcome.Finished>(outcome.ToString()).Subject;
        done.Green.Should().BeTrue();
        done.Run.Result.StdOut.Should().Contain($"wrote {mine}");
        File.ReadAllText(mine).Should().Be("written by the fake coder\n");
        (await bed.MainBytesAsync()).Should().BeEquivalentTo(mainBefore);
        done.Run.Unit.Should().Be($"coai-coder-{bed.JobId}-c1", "a coder's unit is named for the job, and is not a check's");
        done.Run.Gone.Should().BeTrue();
    }

    [Fact]
    public async Task TheCodersUnit_HasItsOwnHome_ThePersonsHomeIsClosed_AndTheSignInFileIsTheOneFileOfItBoundReadOnly_ReadOffTheRealFileSystem()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var script = $$"""
            w() { if { true >> "$1"; } 2>/dev/null; then echo "wrote $2"; else echo "refused $2"; fi; }
            w {{bed.Worktree}}/from-coder.txt worktree
            w {{bed.Main}}/from-coder.txt main
            w {{bed.Main}}/.git/from-coder.txt main-git
            w {{bed.Worktree}}/.git worktree-git-pointer
            w {{bed.CoderRoot}}/from-coder.txt coder-root
            w "$HOME/from-coder.txt" home
            w "$HOME/.claude/from-coder.txt" claude-folder-of-the-coders-home
            w "$HOME/.claude/.credentials.json" sign-in-file
            w {{BoundaryUnit.UnitTemp}}/from-coder.txt tmp
            w {{BoundaryUnit.UnitVarTemp}}/from-coder.txt vartmp
            w {{bed.RealHome}}/from-coder.txt real-home
            w {{bed.RealHome}}/.claude/from-coder.txt real-claude-folder
            if [ "$(cat "$HOME/.claude/.credentials.json")" = "{{CoderBed.SignInLine}}" ]; then echo "reads sign-in-line"; else echo "cannot read sign-in-line"; fi
            if [ -e {{bed.RealHome}}/.claude.json ] || [ -e {{bed.RealHome}}/.bashrc ]; then echo "sees real-home-files"; else echo "hidden real-home-files"; fi
            if [ -e {{bed.Job.HomeFolder}} ] || [ -e {{bed.Job.NuGetFolder}} ]; then echo "sees the-checks-folders"; else echo "hidden the-checks-folders"; fi
            echo "home=$HOME"
            """;

        var run = await RunAsync(bed, bed.Shell(script));

        run.Result.ExitCode.Should().Be(0, run.Result.StdErr);
        var said = run.Result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        said.Should().Contain(["wrote worktree", "wrote home", "wrote claude-folder-of-the-coders-home", "wrote tmp", "wrote vartmp"]);
        said.Should().Contain(["refused main", "refused main-git", "refused worktree-git-pointer", "refused coder-root", "refused sign-in-file", "refused real-home", "refused real-claude-folder"]);
        said.Should().Contain(["reads sign-in-line", "hidden real-home-files", "hidden the-checks-folders"], "the CLI can sign in with the one file it is given, and sees nothing else of the person's home or of the checks' folders");
        said.Should().Contain($"home={bed.Job.CoderHomeFolder}");
        bed.Job.CoderHomeFolder.Should().NotBe(bed.Job.HomeFolder, "what the coder writes must not be in the HOME the checks read");
        File.Exists(Path.Combine(bed.Worktree, "from-coder.txt")).Should().BeTrue();
        File.Exists(Path.Combine(bed.Job.CoderHomeFolder, "from-coder.txt")).Should().BeTrue();
        File.Exists(Path.Combine(bed.Job.CoderTempFolder, "from-coder.txt")).Should().BeTrue("the unit's temp folder is the coder's own");
        File.Exists(Path.Combine(bed.Main, "from-coder.txt")).Should().BeFalse();
        File.ReadAllText(bed.SignIn).Should().Be(CoderBed.SignInLine + "\n", "the person's sign-in file was read and never written");
        Directory.GetFileSystemEntries(bed.RealHome).Select(Path.GetFileName).Should().BeEquivalentTo([".claude", ".claude.json", ".bashrc"], "nothing was added to the stand-in for the real home");
        Directory.GetFileSystemEntries(Path.Combine(bed.RealHome, ".claude")).Select(Path.GetFileName).Should().BeEquivalentTo([".credentials.json"]);
    }

    [Fact]
    public async Task TheCodersEnvironment_IsTheAllowListAlone_AndTheRowsKeyTravelsOnStdin_NeverInAnArgvOrAUnitProperty()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var key = "sk-test-" + Guid.NewGuid().ToString("N");
        var request = bed.Request("/usr/bin/env") with
        {
            Environment = new Dictionary<string, string?> { ["ANTHROPIC_API_KEY"] = key },
            Passthrough = new HashSet<string>(),
        };

        var run = await RunAsync(bed, request);

        run.Result.ExitCode.Should().Be(0, run.Result.StdErr);
        var lines = run.Result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Select(line => line.Split('=')[0]).Should().BeEquivalentTo(["PATH", "HOME", "TMPDIR", "LANG", "TERM", "NO_COLOR", "GIT_TERMINAL_PROMPT", "ANTHROPIC_API_KEY"], "exactly the allow-list and the key of the row: no bus socket, no agent, no COAI_ name");
        lines.Should().Contain($"ANTHROPIC_API_KEY={key}", "the CLI gets its key");
        var started = bed.Launcher.Requests.Single(item => item.Arguments.Contains($"coai-coder-{bed.JobId}-c1"));
        started.Arguments.Should().NotContain(argument => argument.Contains(key, StringComparison.Ordinal), "no secret ever reaches an argv");
        started.Arguments.Should().NotContain(argument => argument.StartsWith("--setenv=ANTHROPIC_API_KEY", StringComparison.Ordinal), "a --setenv puts the value in the unit's Environment property and in a 0644 transient unit file: the row's key crosses on stdin, like a pool key");
        started.Environment.Values.Should().NotContain(value => value != null && value.Contains(key, StringComparison.Ordinal), "and it is not left in the environment of the systemd-run client either");
    }

    [Fact]
    public async Task TheCoder_CannotStartAUnit_OrReachTheBus_OrRunAWindowsProgram_FromInsideItsUnit()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        Assert.SkipWhen(bed.Host.WindowsProgram.Length == 0, "this host has no WSL interop, so there is no Windows program to try");
        var copy = Path.Combine(bed.Worktree, "tool.exe");
        File.Copy(bed.Host.WindowsProgram, copy);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var escape = $"coai-t5-escape-{Guid.NewGuid():N}"[..28];
        var script = $$"""
            if systemd-run --user --quiet --wait --collect --unit {{escape}} touch {{bed.Main}}/via-the-bus >/dev/null 2>&1; then echo "started a unit"; else echo "no unit"; fi
            if {{copy}} >/dev/null 2>&1; then echo "ran the exe"; else echo "no exe"; fi
            ls /run/user /mnt 2>/dev/null | tr '\n' ' '
            """;

        var run = await RunAsync(bed, bed.Shell(script));

        run.Result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().Contain(["no unit", "no exe"]);
        File.Exists(Path.Combine(bed.Main, "via-the-bus")).Should().BeFalse("a unit started from inside would have written the main checkout with no limits");
    }

    [Fact]
    public async Task ThePrompt_ReachesTheCoder_ThroughTheUnit_ByteForByte()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var prompt = "Fix the thing.\nSecond line — with a dash.\n";

        var run = await RunAsync(bed, bed.Fake("echo-stdin") with { StdIn = prompt });

        run.Result.ExitCode.Should().Be(0, run.Result.StdErr);
        run.Result.StdOut.Should().Be(prompt);
    }

    [Fact]
    public async Task TheCodersTimeout_StopsItsUnit_AndNoProcessOfItStays()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();

        var outcome = await bed.Turns().RunAsync(bed.Job, bed.Fake("sleep", "7314") with { Timeout = TimeSpan.FromSeconds(3) }, "c1");

        var failed = outcome.Should().BeOfType<CoderTurnOutcome.Failed>(outcome.ToString()).Subject;
        failed.Sentence.Should().Contain("ran out of time");
        failed.Run.Result.TimedOut.Should().BeTrue();
        failed.Run.Gone.Should().BeTrue("killing the client does not stop the unit; coai stopped it by name");
        (await bed.Inner.ActiveUnitsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task TheRecord_NamesTheBoundary_TheHostNetworkWithItsReason_AndEveryPropertyTheUnitRanWith()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();

        var outcome = await bed.Turns().RunAsync(bed.Job, bed.Fake("emit", "hello"), "c1");

        var done = outcome.Should().BeOfType<CoderTurnOutcome.Finished>(outcome.ToString()).Subject;
        var record = done.Run.Record;
        record.Network.Should().Be("host");
        record.NetworkReason.Should().Contain("vendor");
        record.Properties.Should().Contain(["ProtectSystem=strict", "ProtectHome=tmpfs", "TemporaryFileSystem=/run/user", "TemporaryFileSystem=/mnt", "InaccessiblePaths=-/run/WSL", $"BindPaths={bed.Worktree}", $"BindReadOnlyPaths={bed.Main}",
            $"BindReadOnlyPaths={bed.CoderRoot}", $"BindReadOnlyPaths={bed.Worktree}/.git", $"BindReadOnlyPaths={bed.SignIn}:{bed.Job.CoderSignInFile}", "NoNewPrivileges=yes", "PrivateUsers=yes", "PrivateNetwork=no", "MemorySwapMax=0", $"MemoryMax={bed.Job.MemoryMegabytes}M"]);
        record.Properties.Should().NotContain([$"BindPaths={bed.Main}", $"BindPaths={bed.CoderRoot}"], "the main checkout and the coder root are never bound writable");
        record.Preflight.Select(row => row.Item).Should().Equal(["user-manager", .. BoundaryPreflight.CoderItems]);
        record.Preflight.Should().OnlyContain(row => row.Held);
    }

    /// <summary>
    /// A crossed variable's VALUE is on no command line while the unit starts. The first shape of the inner command was <c>exec /usr/bin/env -i "NAME=$NAME" …</c>: the shell spelled the value into <c>env</c>'s argv, and <c>/proc/&lt;pid&gt;/cmdline</c> is world-readable —
    /// measured with a dummy value, it showed in 228 of 300 tries. The window is milliseconds long, so the test makes many launches of a short program under ONE admission while a scanner reads the command line of every process, all the time. The needle is a dummy built
    /// here, never a credential.
    /// </summary>
    [Fact]
    public async Task ACrossedVariablesValue_IsOnNoCommandLineOfAnyProcess_WhileTheUnitsStart()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var needle = "zz-dummy-not-a-secret-" + Guid.NewGuid().ToString("N");
        var crossing = new Dictionary<string, string?> { ["ANTHROPIC_API_KEY"] = needle };
        var boundary = bed.Boundary();
        var admission = await boundary.AdmitAsync(bed.Job, FakeCliInvocations.Exe);
        var admitted = admission.Should().BeOfType<BoundaryAdmission.Admitted>(admission.ToString()).Subject;
        using var done = new CancellationTokenSource();
        var scans = 0;
        var seen = new List<int>();
        var scanner = Task.Run(async () =>
        {
            while (!done.IsCancellationRequested)
            {
                foreach (var pid in Directory.EnumerateDirectories("/proc").Select(Path.GetFileName).Where(name => name is not null && name.All(char.IsAsciiDigit)).Select(name => int.Parse(name!, System.Globalization.CultureInfo.InvariantCulture)))
                {
                    if (CommandLineOf(pid).Contains(needle, StringComparison.Ordinal))
                    {
                        seen.Add(pid);
                    }
                }

                scans++;
                await Task.Yield();
            }
        }, TestContext.Current.CancellationToken);

        for (var launch = 1; launch <= 40; launch++)
        {
            var run = await boundary.RunAsync(admitted, bed.Job, bed.Fake("emit", "done") with { Environment = crossing, Passthrough = new HashSet<string>() }, $"c{launch}", TestContext.Current.CancellationToken);
            run.Result.ExitCode.Should().Be(0, run.Result.StdErr);
        }

        await done.CancelAsync();
        await scanner;

        scans.Should().BeGreaterThan(10, "the scanner looked at /proc many times while the units started");
        seen.Should().BeEmpty("no process, at any moment, had the value of the crossed variable on its command line");
        var control = await bed.Turns().RunAsync(bed.Job, bed.Request("/usr/bin/env") with { Environment = crossing, Passthrough = new HashSet<string>() }, "k1");
        control.Should().BeOfType<CoderTurnOutcome.Finished>(control.ToString()).Which.Run.Result.StdOut.Should().Contain($"ANTHROPIC_API_KEY={needle}", "CONTROL: the value DID reach the program — by the environment, not by an argv");
    }

    private static string CommandLineOf(int pid)
    {
        try
        {
            using var stream = new FileStream($"/proc/{pid}/cmdline", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static Task<BoundaryRun> RunAsync(CoderBed bed, ProcessRequest request) => bed.RunLaunchAsync(request);
}
