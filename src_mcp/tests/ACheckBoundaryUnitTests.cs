using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s2 t4, DoD d1 and d2: a check step runs inside a NAMED <c>systemd-run --user</c> unit, and what that unit can and cannot do is read off the real unit — a write that lands, a write the OS refuses — never off the property list.
/// The environment is read from INSIDE the unit, with a positive control (the same unit without <c>env -i</c> DOES see the user manager's names), so a green here cannot be a green over an empty list.
/// </summary>
/// <remarks>Real <c>systemd-run --user</c> units on temp folders only. Skipped, with the reason, where there is no user manager.</remarks>
public sealed class ACheckBoundaryUnitTests
{
    private static void NeedAUserManager() => Assert.SkipUnless(SystemdUser.Works(out var why), why);

    [Fact]
    public async Task AStep_RunsInANamedUnit_OfTheJobsOwnName()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();

        var run = await bed.ShellAsync("cat /proc/self/cgroup", part: "s7");

        run.Result.ExitCode.Should().Be(0, run.Result.StdErr);
        run.Unit.Should().Be($"coai-check-{bed.JobId}-s7");
        run.Result.StdOut.Should().Contain($"{run.Unit}.service", "the step's processes live in the unit's own cgroup, under the name coai gave it");
        run.Gone.Should().BeTrue();
    }

    [Fact]
    public async Task TheUnit_WritesWhereItMay_AndIsRefusedWhereItMayNot_ReadOffTheRealFileSystem()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var mainBefore = await CoderTestBed.SnapshotOfAsync(bed.Main);
        var outside = Path.Combine(bed.Job.JobRoot, "..", "outside-canary.txt");
        await File.WriteAllTextAsync(outside, "outside\n");
        var script = $$"""
            w() { if { true > "$1"; } 2>/dev/null; then echo "wrote $2"; else echo "refused $2"; fi; }
            w {{bed.Worktree}}/from-unit.txt worktree
            w {{bed.Main}}/from-unit.txt main
            w {{bed.Main}}/.git/from-unit.txt main-git
            w {{bed.Worktree}}/.git worktree-git-pointer-append
            w /tmp/from-unit.txt tmp
            w /var/tmp/from-unit.txt vartmp
            w "$HOME/from-unit.txt" home
            w "$NUGET_PACKAGES/from-unit.txt" nuget-job-packages
            w "$npm_config_cache/from-unit.txt" npm-cache
            w "$DOTNET_CLI_HOME/from-unit.txt" dotnet-home
            w "$NUGET_FALLBACK_PACKAGES/from-unit.txt" nuget-shared-packages
            if [ -r "$NUGET_FALLBACK_PACKAGES/seed.txt" ]; then echo "read shared-packages-seed"; else echo "unreadable shared-packages-seed"; fi
            if [ -e {{outside}} ]; then echo "sees outside-canary"; else echo "hidden outside-canary"; fi
            """;

        var run = await bed.ShellAsync(script);

        run.Result.ExitCode.Should().Be(0, run.Result.StdErr);
        var said = run.Result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        said.Should().Contain(["wrote worktree", "refused main", "refused main-git", "refused worktree-git-pointer-append", "wrote tmp", "wrote vartmp", "wrote home", "wrote nuget-job-packages", "wrote npm-cache", "wrote dotnet-home"]);
        said.Should().Contain(["refused nuget-shared-packages", "read shared-packages-seed", "hidden outside-canary"], "the shared package folder is a read-only fallback and nothing else of the host's temp folder is there");
        File.Exists(Path.Combine(bed.Worktree, "from-unit.txt")).Should().BeTrue("the worktree is the one writable source folder");
        File.Exists(Path.Combine(bed.Job.TempFolder, "from-unit.txt")).Should().BeTrue("the temp folder of the unit is the job's own folder");
        File.Exists(Path.Combine(bed.Job.VarTempFolder, "from-unit.txt")).Should().BeTrue("the var temp folder of the unit is the job's own folder");
        File.Exists(Path.Combine(bed.Job.HomeFolder, "from-unit.txt")).Should().BeTrue("HOME is the job's");
        File.Exists(Path.Combine(bed.Job.NuGetFolder, "from-unit.txt")).Should().BeTrue("NUGET_PACKAGES is the job's writable folder");
        File.Exists(Path.Combine(bed.Job.NpmFolder, "from-unit.txt")).Should().BeTrue();
        File.Exists(Path.Combine(bed.SharedPackages, "from-unit.txt")).Should().BeFalse("the shared package folder was read-only");
        (await CoderTestBed.SnapshotOfAsync(bed.Main)).Should().BeEquivalentTo(mainBefore, "not one byte of the main checkout changed");
        File.Exists(Path.Combine(bed.Main, ".git", "from-unit.txt")).Should().BeFalse();
        File.ReadAllText(bed.Tree.GitEntry).Should().Be(bed.Tree.ExpectedPointer, "the .git pointer of the worktree was not touched");
    }

    [Fact]
    public async Task TheUnit_HidesTheHostsHome_RunsInAUserNamespace_WithNoNewPrivileges_AndAMemoryCeilingThatKills()
    {
        NeedAUserManager();
        Assert.SkipWhen(Directory.GetFileSystemEntries("/home").Length == 0 && Directory.GetFileSystemEntries("/root").Length == 0, "this host has nothing in /home or /root, so hiding them cannot be seen");
        await using var bed = await BoundaryBed.CreateAsync();
        const string script = """
            echo "home=$(ls -A /home /root /run/user /mnt 2>/dev/null | tr -d '\n')"
            echo "uid_map=$(tr -s ' ' < /proc/self/uid_map)"
            echo "$(grep NoNewPrivs /proc/self/status | tr -d '\t')"
            cg=$(cut -d: -f3 /proc/self/cgroup)
            echo "memory.max=$(cat /sys/fs/cgroup$cg/memory.max)"
            echo "memory.swap.max=$(cat /sys/fs/cgroup$cg/memory.swap.max)"
            """;

        var run = await bed.ShellAsync(script);

        var said = run.Result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        said.Should().Contain("home=/home:/mnt:/root:/run/user:", "ProtectHome=tmpfs and the empty /run/user and /mnt: the four folders are there and not one entry of the host's is in them");
        said.Should().Contain("uid_map= 1000 1000 1", "a user namespace with the one user in it, not the host's full range");
        said.Should().Contain("NoNewPrivs:1");
        said.Should().Contain($"memory.max={bed.Job.MemoryMegabytes * 1024L * 1024L}", "the cgroup of the unit has the ceiling");
        said.Should().Contain("memory.swap.max=0", "without it the swap absorbs the excess and nothing is killed (RESULTS_spikes_2026-11-23 §2)");
    }

    [Fact]
    public async Task AHostNetworkStep_CanReadTheResolverFile_APrivateOneNeedsNone_AndNothingElseUnderMntIsThere()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        Assert.SkipWhen(bed.Host.ResolverFile.Length == 0, "/etc/resolv.conf of this host does not link into /mnt, so nothing is hidden from a host-network step");
        const string script = "if [ -s /etc/resolv.conf ]; then echo resolver-readable; else echo resolver-missing; fi; find /mnt -mindepth 1 -maxdepth 2 | tr '\\n' ' '";

        var onHost = await bed.ShellAsync(script, "dns", CheckNetwork.Host);
        var alone = await bed.ShellAsync(script, "dns2", CheckNetwork.Private);

        onHost.Result.StdOut.Should().StartWith("resolver-readable", "without it a host-network step has no DNS: NuGet could not reach api.nuget.org when /mnt was hidden");
        onHost.Result.StdOut.Should().Contain(bed.Host.ResolverFile).And.NotContain("/mnt/c", "the Windows drives stay hidden");
        alone.Result.StdOut.Should().StartWith("resolver-missing").And.NotContain("/mnt/wsl", "a private-network step has no use for it and gets nothing under /mnt");
    }

    [Fact]
    public async Task ExitCode_AndBothStreams_PassThroughTheUnit()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();

        var run = await bed.ShellAsync("echo to-stdout; echo to-stderr >&2; exit 7");

        run.Result.ExitCode.Should().Be(7, "the unit's exit code is the step's exit code");
        run.Result.StdOut.Should().Be("to-stdout\n");
        run.Result.StdErr.Should().Be("to-stderr\n");
        run.Result.TimedOut.Should().BeFalse();
        run.Gone.Should().BeTrue();
    }

    [Fact]
    public async Task TheEnvironment_InsideTheUnit_IsTheAllowListAlone_NoVendorKey_NoCoaiName_NoSocket()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var mark = Guid.NewGuid().ToString("N")[..10];
        var canaries = new[] { $"COAI_T4_CANARY_{mark}", $"T4_CANARY_{mark}_API_KEY", $"T4_CANARY_{mark}_TOKEN" };
        await SetManagerEnvironmentAsync(bed, canaries);
        try
        {
            var inside = await EnvironmentNamesAsync(bed, bed.Boundary());
            var withoutEnvI = await EnvironmentNamesWithoutEnvIAsync(bed);

            withoutEnvI.Should().Contain(canaries, "POSITIVE CONTROL: the unit gets the user manager's environment, so without env -i the canaries are in it");
            inside.Should().NotContain(canaries);
            inside.Should().NotContain(name => name.StartsWith("COAI_", StringComparison.Ordinal) || name.Contains("API_KEY", StringComparison.Ordinal) || name.Contains("TOKEN", StringComparison.Ordinal));
            inside.Should().NotContain(["SSH_AUTH_SOCK", "DBUS_SESSION_BUS_ADDRESS", "GPG_AGENT_INFO", "XDG_RUNTIME_DIR"]);
            inside.Should().BeEquivalentTo(["PATH", "HOME", "DOTNET_CLI_HOME", "NUGET_PACKAGES", "NUGET_FALLBACK_PACKAGES", "npm_config_cache", "TMPDIR", "LANG", "TERM", "GIT_TERMINAL_PROMPT", "NO_COLOR"], "exactly the allow-list");
        }
        finally
        {
            await UnsetManagerEnvironmentAsync(bed, canaries);
        }
    }

    [Fact]
    public async Task TheUnit_SeesTheJobsHome_NeverTheRealOne_AndASafePath()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();

        var run = await bed.ShellAsync("echo \"HOME=$HOME\"; echo \"PATH=$PATH\"; echo \"TMPDIR=$TMPDIR\"");

        var said = run.Result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        said.Should().Contain($"HOME={bed.Job.HomeFolder}");
        said.Should().Contain($"TMPDIR={BoundaryUnit.UnitTemp}");
        var path = said.Single(line => line.StartsWith("PATH=", StringComparison.Ordinal))["PATH=".Length..];
        path.Split(':').Should().NotContain(entry => entry.StartsWith("/mnt", StringComparison.Ordinal) || entry.StartsWith(bed.Host.RealHome, StringComparison.Ordinal), "no Windows folder and no folder of the real home is on the unit's PATH");
    }

    private static async Task<IReadOnlyList<string>> EnvironmentNamesAsync(BoundaryBed bed, CheckBoundary boundary)
    {
        var run = await bed.CommandAsync(["/usr/bin/env"], new Dictionary<string, string?> { ["NO_COLOR"] = "1" }, "env", boundary: boundary);
        run.Result.ExitCode.Should().Be(0, run.Result.StdErr);

        return [.. run.Result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('=')[0])];
    }

    /// <summary>The positive control: the SAME unit with <c>-i</c> taken from <c>env</c>, started straight through the launcher — the preflight would (rightly) refuse it, so it is not admitted — prints what the user manager hands every unit.</summary>
    private static async Task<IReadOnlyList<string>> EnvironmentNamesWithoutEnvIAsync(BoundaryBed bed)
    {
        var arguments = BoundaryUnit.Arguments(bed.Host, bed.Job, $"coai-check-{bed.JobId}-ctl", CheckNetwork.Private, TimeSpan.FromSeconds(60), new Dictionary<string, string?>(), ["/usr/bin/env"], all => [.. all.Where(argument => argument != "-i")]);
        var run = await bed.Launcher.RunAsync(new ProcessRequest("systemd-run", arguments, bed.Worktree) { Timeout = TimeSpan.FromSeconds(60) });
        run.ExitCode.Should().Be(0, run.StdErr);

        return [.. run.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('=')[0])];
    }

    private static async Task SetManagerEnvironmentAsync(BoundaryBed bed, string[] names)
    {
        var set = await bed.Launcher.RunAsync(new ProcessRequest("systemctl", ["--user", "set-environment", .. names.Select(name => $"{name}=canary")], bed.Worktree));
        set.ExitCode.Should().Be(0, set.StdErr);
    }

    private static async Task UnsetManagerEnvironmentAsync(BoundaryBed bed, string[] names) =>
        await bed.Launcher.RunAsync(new ProcessRequest("systemctl", ["--user", "unset-environment", .. names], bed.Worktree));
}
