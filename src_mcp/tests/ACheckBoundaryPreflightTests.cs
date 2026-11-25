using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s2 t4, DoD d3: before each job a preflight with no model runs the exact property set and asks the OS every item — a write to the main checkout fails, a write to its <c>.git</c> fails, <c>systemd-run --user true</c> fails, a Windows
/// <c>.exe</c> in the worktree does not run, HOME is not the real HOME, a worktree write works — and a wrong answer refuses the job with a sentence, before any step. No user manager, native Windows and macOS are refused.
/// </summary>
/// <remarks>
/// <para><b>Every item is proved to be able to say no.</b> A preflight that always answers "ok" passes a healthy-machine test and nothing else, so each item has a case that DEGRADES the unit — one property taken away, or the environment left alone — and
/// sees that very item refuse the job. The degrade seam rewrites the finished <c>systemd-run</c> argument list (<c>CheckBoundary.Degrade</c>, internal, a test-only door).</para>
/// <para>Real units on temp folders only; skipped, with the reason, without a user manager.</para>
/// </remarks>
public sealed class ACheckBoundaryPreflightTests
{
    private static void NeedAUserManager() => Assert.SkipUnless(SystemdUser.Works(out var why), why);

    private static async Task<BoundaryAdmission> AdmitAsync(BoundaryBed bed, Func<IReadOnlyList<string>, IReadOnlyList<string>>? degrade = null, CheckNetwork network = CheckNetwork.Private) =>
        await bed.Boundary(degrade).AdmitAsync(bed.Job, [network]);

    [Fact]
    public async Task OnAHealthyMachine_EveryItemHolds_AndTheJobIsAdmitted_WithTheRowsRecorded()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();

        var admission = await AdmitAsync(bed);

        var admitted = admission.Should().BeOfType<BoundaryAdmission.Admitted>(admission.ToString()).Subject;
        var record = admitted.Records.Should().ContainSingle().Which.Value;
        record.Boundary.Should().Be(BoundaryRecord.UnitBoundary);
        record.Preflight.Select(row => row.Item).Should().Equal(["user-manager", "worktree-write", "main-readonly", "git-readonly", "no-user-manager-inside", "windows-exe", "home-is-the-jobs", "tmp-is-the-jobs", "coder-folder-hidden", "network-state", "no-sockets-no-interop", "clean-environment"]);
        record.Preflight.Should().OnlyContain(row => row.Held, string.Join("; ", record.Preflight.Where(row => !row.Held).Select(row => $"{row.Item}: {row.Detail}")));
        record.Preflight.Should().OnlyContain(row => row.Detail.Length > 0);
        File.Exists(Path.Combine(bed.Worktree, ".coai-preflight.exe")).Should().BeFalse("the Windows program's copy is removed when the preflight ends");
        Directory.GetFileSystemEntries(bed.Worktree).Select(Path.GetFileName).Should().NotContain(name => name!.StartsWith(".coai-", StringComparison.Ordinal), "the preflight left nothing in the worktree");
        (await CoderTestBed.SnapshotOfAsync(bed.Main)).Keys.Should().NotContain(name => name.Contains(".coai-preflight", StringComparison.Ordinal));
    }

    public static TheoryData<string, string> Degrades => new()
    {
        { "worktree-write", "no writable worktree" },
        { "main-readonly", "main checkout writable" },
        { "git-readonly", ".git pointer writable" },
        { "no-user-manager-inside", "bus and env reachable" },
        { "windows-exe", "WSL interop reachable" },
        { "home-is-the-jobs", "HOME is the real one" },
        { "tmp-is-the-jobs", "no per-job temp folder" },
        { "coder-folder-hidden", "the whole job folder bound again, the coder's folder with it" },
        { "network-state", "host network under a private step" },
        { "no-sockets-no-interop", "/mnt visible" },
        { "clean-environment", "no env -i" },
    };

    [Theory]
    [MemberData(nameof(Degrades))]
    public async Task OneItemGoingWrong_RefusesTheJob_NamingThatItem_BeforeAnyStep(string item, string how)
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        SkipWhereTheHostCannotShowIt(item, bed);
        var launches = bed.Launcher.Requests.Count;

        var admission = await AdmitAsync(bed, Degrade(item, bed));

        var refused = admission.Should().BeOfType<BoundaryAdmission.Refused>($"{how}: the preflight must see it: {admission}").Subject;
        refused.Sentence.Should().Contain(item).And.Contain("refused before any check ran");
        refused.Record.Preflight.Single(row => row.Item == item).Held.Should().BeFalse($"{item} is the row that said no");
        bed.Launcher.Requests.Skip(launches).Select(request => request.Executable).Should().OnlyContain(executable => executable == "systemd-run" || executable == "systemctl", "only the preflight unit and its control ran: no step");
    }

    private static void SkipWhereTheHostCannotShowIt(string item, BoundaryBed bed)
    {
        Assert.SkipWhen(item == "windows-exe" && bed.Host.WindowsProgram.Length == 0, "this host has no WSL interop, so there is no Windows program to try");
        Assert.SkipWhen(item == "network-state" && Directory.GetDirectories("/sys/class/net").Select(Path.GetFileName).SequenceEqual(["lo"]), "this host has no network interface but lo, so a host network looks the same as a private one");
        Assert.SkipWhen(item == "no-sockets-no-interop" && Directory.GetFileSystemEntries("/mnt").Length == 0, "/mnt is empty on this host");
        Assert.SkipWhen(item == "no-user-manager-inside" && Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is null, "no XDG_RUNTIME_DIR in this process, so no bus path to hand a degraded unit");
    }

    private static Func<IReadOnlyList<string>, IReadOnlyList<string>> Degrade(string item, BoundaryBed bed) => arguments => item switch
    {
        "worktree-write" => Drop(arguments, $"BindPaths={bed.Worktree}"),
        "main-readonly" => Replace(arguments, $"BindReadOnlyPaths={bed.Main}", $"BindPaths={bed.Main}"),
        "git-readonly" => Drop(arguments, $"BindReadOnlyPaths={bed.Worktree}/.git"),
        "no-user-manager-inside" => Drop(Drop(Drop(arguments, "-i"), "TemporaryFileSystem=/run/user"), "ProtectHome=tmpfs"),
        "windows-exe" => Drop(arguments, "InaccessiblePaths=-/run/WSL"),
        "home-is-the-jobs" => Replace(arguments, $"HOME={bed.Job.HomeFolder}", $"HOME={bed.Host.RealHome}"),
        "tmp-is-the-jobs" => Drop(arguments, $"BindPaths={bed.Job.TempFolder}:{BoundaryUnit.UnitTemp}"),
        "coder-folder-hidden" => Replace(arguments, $"TemporaryFileSystem={bed.Job.JobRoot}:ro", $"BindPaths={bed.Job.JobRoot}"),
        "network-state" => Replace(arguments, "PrivateNetwork=yes", "PrivateNetwork=no"),
        "no-sockets-no-interop" => Drop(arguments, "TemporaryFileSystem=/mnt"),
        _ => Drop(arguments, "-i"),
    };

    /// <summary>The list without that argument — and without the <c>-p</c> before it when it is a property.</summary>
    private static string[] Drop(IReadOnlyList<string> arguments, string what)
    {
        var at = arguments.ToList().IndexOf(what);
        at.Should().BeGreaterThan(-1, $"the degrade must find '{what}' in the real argument list, or it proves nothing");
        var drop = at > 0 && arguments[at - 1] == "-p" ? 2 : 1;

        return [.. arguments.Take(at - (drop - 1)), .. arguments.Skip(at + 1)];
    }

    private static string[] Replace(IReadOnlyList<string> arguments, string what, string with)
    {
        var at = arguments.ToList().IndexOf(what);
        at.Should().BeGreaterThan(-1, $"the degrade must find '{what}' in the real argument list, or it proves nothing");

        return [.. arguments.Take(at), with, .. arguments.Skip(at + 1)];
    }

    [Fact]
    public async Task AWindowsProgram_RunsWhereThereIsNoBoundary_AndDoesNotRunInsideIt()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        Assert.SkipWhen(bed.Host.WindowsProgram.Length == 0, "this host has no WSL interop, so there is no Windows program to try");
        var copy = Path.Combine(bed.Worktree, ".coai-preflight.exe");
        File.Copy(bed.Host.WindowsProgram, copy);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        try
        {
            var outside = await bed.Launcher.RunAsync(new ProcessRequest("systemd-run", ["--user", "--wait", "--pipe", "--collect", "--quiet", "--unit", $"coai-check-{bed.JobId}-ctl", copy], bed.Worktree) { Timeout = TimeSpan.FromSeconds(60) });
            outside.ExitCode.Should().Be(0, "POSITIVE CONTROL: the very program the preflight tries runs in an ordinary user unit — so its failing inside the boundary is the boundary's doing");
            outside.StdOut.Trim().Should().NotBeEmpty();
        }
        finally
        {
            File.Delete(copy);
        }

        var admission = await AdmitAsync(bed);

        var admitted = admission.Should().BeOfType<BoundaryAdmission.Admitted>(admission.ToString()).Subject;
        admitted.Records.Values.Single().Preflight.Single(row => row.Item == "windows-exe").Detail.Should().Contain("did not run").And.NotContain("no Windows interop", "the program really was tried");
    }

    [Fact]
    public async Task ABusyNetworkChoice_HostForOneStep_IsAdmittedWithItsOwnRows()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();

        var admission = await bed.Boundary().AdmitAsync(bed.Job, [CheckNetwork.Private, CheckNetwork.Host, CheckNetwork.Host]);

        var admitted = admission.Should().BeOfType<BoundaryAdmission.Admitted>(admission.ToString()).Subject;
        admitted.Records.Keys.Should().BeEquivalentTo([CheckNetwork.Private, CheckNetwork.Host], "one preflight for each network state in use, not for each step");
        admitted.Records[CheckNetwork.Host].Preflight.Single(row => row.Item == "network-state").Detail.Should().Contain("host network");
    }

    [Fact]
    public async Task NoUserManager_IsRefused_WithASentence_AndNoStepStarts()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();

        foreach (var program in new[] { "/bin/false", "/nonexistent/coai-systemd-run" })
        {
            var host = bed.Host with { SystemdRun = program };
            var launches = bed.Launcher.Requests.Count;

            var admission = await bed.Boundary(host: host).AdmitAsync(bed.Job, [CheckNetwork.Private]);

            var refused = admission.Should().BeOfType<BoundaryAdmission.Refused>().Subject;
            refused.Sentence.Should().Contain("user-manager").And.Contain("refused before any check ran");
            refused.Record.Preflight.Should().ContainSingle().Which.Held.Should().BeFalse();
            bed.Launcher.Requests.Skip(launches).Select(request => request.Executable).Should().OnlyContain(executable => executable == program || executable == "systemctl", "nothing but the preflight unit was started");
        }
    }

    [Theory]
    [InlineData(BoundaryPlatform.Windows, "Windows")]
    [InlineData(BoundaryPlatform.MacOs, "macOS")]
    [InlineData(BoundaryPlatform.Other, "operating system")]
    public async Task NativeWindows_MacOs_AndOtherSystems_AreRefused_WithNoProcessStarted_AndNoCwdOnlyMode(BoundaryPlatform platform, string word)
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var launches = bed.Launcher.Requests.Count;

        var admission = await bed.Boundary(host: bed.Host with { Platform = platform }).AdmitAsync(bed.Job, [CheckNetwork.Private]);

        admission.Should().BeOfType<BoundaryAdmission.Refused>().Which.Sentence.Should().Contain(word).And.Contain("refused");
        bed.Launcher.Requests.Count.Should().Be(launches, "a platform with no boundary is refused before a process starts — there is no mode that runs without one");
        Directory.Exists(bed.JobRoot).Should().BeFalse("not even the job's folders were made");
    }

    [Theory]
    [InlineData("with space")]
    [InlineData("with:colon")]
    [InlineData("with\\backslash")]
    public async Task APathTheUnitCannotBeGiven_IsRefusedBeforeAnyProcess(string odd)
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var launches = bed.Launcher.Requests.Count;
        var job = bed.Job with { JobRoot = Path.Combine(bed.Parent(), odd) };

        var admission = await bed.Boundary().AdmitAsync(job, [CheckNetwork.Private]);

        admission.Should().BeOfType<BoundaryAdmission.Refused>().Which.Sentence.Should().Contain("cannot be given to a unit");
        bed.Launcher.Requests.Count.Should().Be(launches);
    }

    [Fact]
    public async Task AJobFolderInsideTheMainCheckout_IsRefused_BecauseTheReadOnlyBindWouldHideIt()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var job = bed.Job with { JobRoot = Path.Combine(bed.Main, "job") };

        var admission = await bed.Boundary().AdmitAsync(job, [CheckNetwork.Private]);

        admission.Should().BeOfType<BoundaryAdmission.Refused>().Which.Sentence.Should().Contain("three separate folders");
        Directory.Exists(job.JobRoot).Should().BeFalse();
    }

    [Fact]
    public async Task AStep_CannotStartWithoutThePreflightsAdmission_OrWithAnotherJobs()
    {
        NeedAUserManager();
        await using var bed = await BoundaryBed.CreateAsync();
        var admission = (BoundaryAdmission.Admitted)await bed.Boundary().AdmitAsync(bed.Job, [CheckNetwork.Private]);
        var stranger = admission with { JobId = "someone-else" };
        var privateOnly = NetworkChoice.OnHost("a step that was not preflighted");

        var other = async () => await bed.Boundary().RunAsync(stranger, bed.Job, NetworkChoice.None("t"), "s1", ["/bin/true"], new Dictionary<string, string?>(), TimeSpan.FromSeconds(30), 1024);
        var uncovered = async () => await bed.Boundary().RunAsync(admission, bed.Job, privateOnly, "s1", ["/bin/true"], new Dictionary<string, string?>(), TimeSpan.FromSeconds(30), 1024);

        await other.Should().ThrowAsync<InvalidOperationException>();
        await uncovered.Should().ThrowAsync<InvalidOperationException>();
    }
}
