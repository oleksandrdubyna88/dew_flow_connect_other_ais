using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s2 t5, DoD d3: on a platform with no effective boundary — no user manager, a preflight row that says no, native Windows, macOS — the coder job is REFUSED before any launch. "Before any launch" is read off the launcher's own record (no request
/// but the preflight unit and its control was ever made, and the coder's unit name is in none) and off the disk (the marker the fake coder would have written is not there).
/// </summary>
/// <remarks>
/// <para><b>Every item is proved to be able to say no.</b> A preflight that always answers "ok" passes a healthy-machine test and nothing else, so each item the coder's preflight asks has a case that DEGRADES the unit — one property taken away — and sees that very item refuse the job
/// (the same seam as the checks', <c>CoderBoundary.Degrade</c>, a test-only door). The network row is the one item that cannot say no for a coder (it needs the host network), and says so.</para>
/// <para>Where a case needs a real unit it is skipped, with the reason, without a user manager; the no-user-manager cases need none.</para>
/// </remarks>
public sealed class ACoderJobWithNoBoundaryIsRefusedTests
{
    private static void NeedAUserManager() => Assert.SkipUnless(SystemdUser.Works(out var why), why);

    private static string Marker(CoderBed bed) => Path.Combine(bed.Worktree, "the-coder-ran.txt");

    /// <summary>The fake coder that would leave a file in the worktree if it were ever started.</summary>
    private static ProcessRequest Coder(CoderBed bed) => bed.Fake("write-files", Marker(bed));

    /// <summary>Asserts the coder was never launched: no request but the preflight and its control, no coder unit, no marker.</summary>
    private static void NeverLaunched(CoderBed bed, int launches, params string[] allowedPrograms)
    {
        bed.Launcher.Requests.Skip(launches).Select(request => request.Executable).Should().OnlyContain(executable => executable == "systemctl" || allowedPrograms.Contains(executable), "nothing but the preflight unit and its control was started");
        bed.Launcher.Requests.Skip(launches).SelectMany(request => request.Arguments).Should().NotContain(argument => argument.Contains($"coai-coder-{bed.JobId}-c1", StringComparison.Ordinal), "the coder's unit was never described, let alone started");
        bed.Launcher.Requests.Skip(launches).Select(request => request.Executable).Should().NotContain(FakeCliInvocations.Exe);
        File.Exists(Marker(bed)).Should().BeFalse("the coder never ran");
    }

    [Theory]
    [InlineData("/bin/false")]
    [InlineData("/nonexistent/coai-systemd-run")]
    public async Task NoUserManager_RefusesTheJob_WithASentence_AndTheCoderIsNeverLaunched(string systemdRun)
    {
        await using var bed = await CoderBed.CreateAsync();
        var host = bed.Host with { SystemdRun = systemdRun };
        var launches = bed.Launcher.Requests.Count;

        var outcome = await bed.Turns(bed.Boundary(host: host)).RunAsync(bed.Job, Coder(bed), "c1");

        var refused = outcome.Should().BeOfType<CoderTurnOutcome.Refused>(outcome.ToString()).Subject;
        refused.Green.Should().BeFalse();
        refused.Sentence.Should().Contain("user-manager").And.Contain("refused before the coder was launched");
        refused.Record.Preflight.Should().ContainSingle().Which.Held.Should().BeFalse();
        NeverLaunched(bed, launches, systemdRun);
    }

    [Theory]
    [InlineData(BoundaryPlatform.Windows, "Windows")]
    [InlineData(BoundaryPlatform.MacOs, "macOS")]
    [InlineData(BoundaryPlatform.Other, "operating system")]
    public async Task NativeWindows_MacOs_AndOtherSystems_RefuseTheJob_WithNoProcessStarted(BoundaryPlatform platform, string word)
    {
        await using var bed = await CoderBed.CreateAsync();
        var launches = bed.Launcher.Requests.Count;

        var outcome = await bed.Turns(bed.Boundary(host: bed.Host with { Platform = platform })).RunAsync(bed.Job, Coder(bed), "c1");

        outcome.Should().BeOfType<CoderTurnOutcome.Refused>().Which.Sentence.Should().Contain(word).And.Contain("refused");
        bed.Launcher.Requests.Skip(launches).Where(request => request.Executable != "git").Should().BeEmpty("a platform with no boundary is refused before a process of the coder's side starts — there is no mode that runs without one");
        Directory.Exists(bed.Job.CoderFolder).Should().BeFalse("not even the coder's folders were made");
        File.Exists(Marker(bed)).Should().BeFalse();
    }

    [Fact]
    public async Task APreflightThatSaysNo_RefusesTheJob_NamingTheRow_AndTheMainCheckoutIsUntouched()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var main = await bed.MainBytesAsync();
        var launches = bed.Launcher.Requests.Count;
        var broken = bed.Boundary(arguments => Replace(arguments, $"BindReadOnlyPaths={bed.Main}", $"BindPaths={bed.Main}"));

        var outcome = await bed.Turns(broken).RunAsync(bed.Job, bed.Fake("write-files", Path.Combine(bed.Main, "would-be-written.txt")), "c1");

        var refused = outcome.Should().BeOfType<CoderTurnOutcome.Refused>(outcome.ToString()).Subject;
        refused.Sentence.Should().Contain("main-readonly").And.Contain("refused before the coder was launched");
        refused.Record.Preflight.Single(row => row.Item == "main-readonly").Held.Should().BeFalse();
        NeverLaunched(bed, launches, "systemd-run");
        File.Exists(Path.Combine(bed.Main, "would-be-written.txt")).Should().BeFalse("the coder was never started, so it wrote nothing — in a unit whose main checkout was writable");
        (await bed.MainBytesAsync()).Should().BeEquivalentTo(main);
    }

    /// <summary>Every item but the network row (it cannot say no) and the two that only a launch WITH a pool key asks for: those are proved able to say no in <see cref="AKeyCoderBoundaryTests"/>.</summary>
    public static TheoryData<string> Items => [.. BoundaryPreflight.CoderItems.Where(item => item is not ("network-state" or BoundaryPreflight.SignInAbsent or BoundaryPreflight.KeysHidden))];

    [Theory]
    [MemberData(nameof(Items))]
    public async Task OneItemGoingWrong_RefusesTheJob_NamingThatItem_BeforeTheCoderIsLaunched(string item)
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        Assert.SkipWhen(item == "windows-exe" && bed.Host.WindowsProgram.Length == 0, "this host has no WSL interop, so there is no Windows program to try");
        Assert.SkipWhen(item == "no-sockets-no-interop" && Directory.GetFileSystemEntries("/mnt").Length == 0, "/mnt is empty on this host");
        Assert.SkipWhen(item == "no-user-manager-inside" && Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is null, "no XDG_RUNTIME_DIR in this process, so no bus path to hand a degraded unit");
        var launches = bed.Launcher.Requests.Count;

        var outcome = await bed.Turns(bed.Boundary(Degrade(item, bed))).RunAsync(bed.Job, Coder(bed), "c1");

        var refused = outcome.Should().BeOfType<CoderTurnOutcome.Refused>($"the preflight must see it: {outcome}").Subject;
        // The stand-in home of this bed lives under /tmp, so a unit with no job /tmp also shows the stand-in home to the home row's read arm (M17); the sentence then names that row first. The row under test must still say no.
        refused.Sentence.Should().Contain(item == BoundaryPreflight.TempIsTheJobs ? BoundaryPreflight.HomeIsTheJobs : item).And.Contain("refused before the coder was launched");
        refused.Record.Preflight.Single(row => row.Item == item).Held.Should().BeFalse($"{item} is the row that said no");
        NeverLaunched(bed, launches, "systemd-run");
    }

    [Fact]
    public async Task TheNetworkRow_IsTheOneItemThatCannotSayNo_ACoderNeedsItsVendor_AndSaysSo()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();

        var admission = await bed.Boundary().AdmitAsync(bed.Job, FakeCliInvocations.Exe);

        var admitted = admission.Should().BeOfType<BoundaryAdmission.Admitted>(admission.ToString()).Subject;
        admitted.Records.Values.Single().Preflight.Single(row => row.Item == "network-state").Detail.Should().Contain("host network");
    }

    private static Func<IReadOnlyList<string>, IReadOnlyList<string>> Degrade(string item, CoderBed bed) => arguments => item switch
    {
        "worktree-write" => Drop(arguments, $"BindPaths={bed.Worktree}"),
        "main-readonly" => Replace(arguments, $"BindReadOnlyPaths={bed.Main}", $"BindPaths={bed.Main}"),
        "coder-root-readonly" => Replace(arguments, $"BindReadOnlyPaths={bed.CoderRoot}", $"BindPaths={bed.CoderRoot}"),
        "git-readonly" => Drop(arguments, $"BindReadOnlyPaths={bed.Worktree}/.git"),
        "no-user-manager-inside" => Drop(Drop(Drop(arguments, "-i"), "TemporaryFileSystem=/run/user"), "ProtectHome=tmpfs"),
        "windows-exe" => Drop(arguments, "InaccessiblePaths=-/run/WSL"),
        "home-is-the-jobs" => Replace(arguments, $"HOME={bed.Job.CoderHomeFolder}", $"HOME={bed.RealHome}"),
        "sign-in-readonly" => Replace(arguments, $"BindReadOnlyPaths={bed.SignIn}:{bed.Job.CoderSignInFile}", $"BindPaths={bed.SignIn}:{bed.Job.CoderSignInFile}"),
        "program-visible" => Drop(arguments, $"BindReadOnlyPaths={Path.GetDirectoryName(FakeCliInvocations.Exe)}"),
        "tmp-is-the-jobs" => Drop(arguments, $"BindPaths={bed.Job.CoderTempFolder}:{BoundaryUnit.UnitTemp}"),
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
    public async Task ALaunchForAnotherWorktree_IsRefusedBeforeAnyProcess()
    {
        await using var bed = await CoderBed.CreateAsync();
        var launches = bed.Launcher.Requests.Count;
        var elsewhere = bed.Fake("write-files", Marker(bed)) with { WorkingDirectory = bed.Main };

        var outcome = await bed.Turns().RunAsync(bed.Job, elsewhere, "c1");

        outcome.Should().BeOfType<CoderTurnOutcome.Refused>().Which.Sentence.Should().Contain("not the worktree of job").And.Contain("was not started");
        bed.Launcher.Requests.Skip(launches).Should().BeEmpty();
    }

    [Fact]
    public async Task AProgramThatLiesInsideTheWorktree_IsRefused_ACoderIsNeverLaunchedWithABinaryItCouldReplace()
    {
        await using var bed = await CoderBed.CreateAsync();
        var program = Path.Combine(bed.Worktree, "my-claude");
        await File.WriteAllTextAsync(program, "#!/bin/sh\necho hi\n");
        var launches = bed.Launcher.Requests.Count;

        var outcome = await bed.Turns().RunAsync(bed.Job, bed.Request(program), "c1");

        outcome.Should().BeOfType<CoderTurnOutcome.Refused>().Which.Sentence.Should().Contain("lies inside the worktree or the job folder");
        bed.Launcher.Requests.Skip(launches).Should().BeEmpty("refused before the preflight, so before any process");
    }

    [Fact]
    public async Task AJobFolderInsideTheMainCheckout_IsRefused_BecauseTheReadOnlyBindWouldHideIt()
    {
        await using var bed = await CoderBed.CreateAsync();
        var job = bed.Job with { JobRoot = Path.Combine(bed.Main, "job") };
        var launches = bed.Launcher.Requests.Count;

        var outcome = await bed.Turns().RunAsync(job, Coder(bed), "c1");

        outcome.Should().BeOfType<CoderTurnOutcome.Refused>().Which.Sentence.Should().Contain("three separate folders");
        Directory.Exists(job.JobRoot).Should().BeFalse();
        bed.Launcher.Requests.Skip(launches).Where(request => request.Executable != "git").Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("c 1")]
    [InlineData("../x")]
    [InlineData("C1")]
    [InlineData("a-name-that-is-far-too-long")]
    public async Task APartNameThatIsNotSafeInAUnitName_IsRefusedWithAnException_NoProcessStarts(string part)
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var boundary = bed.Boundary();
        var admission = (BoundaryAdmission.Admitted)await boundary.AdmitAsync(bed.Job, FakeCliInvocations.Exe);
        var launches = bed.Launcher.Requests.Count;

        var run = async () => await boundary.RunAsync(admission, bed.Job, Coder(bed), part);

        await run.Should().ThrowAsync<ArgumentException>();
        bed.Launcher.Requests.Count.Should().Be(launches);
        File.Exists(Marker(bed)).Should().BeFalse();
    }

    [Fact]
    public async Task ALaunch_CannotStartWithoutThePreflightsAdmission_OrWithAnotherJobs()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var boundary = bed.Boundary();
        var admission = (BoundaryAdmission.Admitted)await boundary.AdmitAsync(bed.Job, FakeCliInvocations.Exe);
        var stranger = admission with { JobId = "someone-else" };
        var uncovered = admission with { Records = new Dictionary<CheckNetwork, CoaiMcp.Core.Toolchain.BoundaryRecord>() };

        var other = async () => await boundary.RunAsync(stranger, bed.Job, Coder(bed), "c1");
        var none = async () => await boundary.RunAsync(uncovered, bed.Job, Coder(bed), "c1");

        await other.Should().ThrowAsync<InvalidOperationException>();
        await none.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(Marker(bed)).Should().BeFalse();
    }
}
