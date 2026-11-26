using System.Text.Json;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s5 t3 (V9, V10; SPEC C2 item 3; C3 item 3): the preflight of a codex coder job — with no model, inside the exact launch unit, ONE ROW FOR EVERY ID of class <c>pre</c> of the table: the unit (U1 to U9), the vendor sandbox tried with <c>codex sandbox -P coai</c> (CX-V1 to CX-V6), the tool
/// list with the job's own model (CX-T1), the config read back (CX-C1), the stand-in project and <c>doctor</c> (CX-C3, CX-C4) and the refresh override and the sign-in copy (CX-R2, CX-R3, CX-R4). A wrong answer of ANY row refuses the job before any launch and names the row.
/// </summary>
/// <remarks>
/// <para><b>FakeCli is the codex.</b> Its <c>sandbox</c> verb runs the command under a REAL <c>bwrap</c> with what the profile <c>coai</c> shows, so a row that asks whether the sandbox can read the job folder is answered by a kernel; its <c>debug prompt-input</c> and <c>doctor</c> verbs answer as the measured CLI does for the argv they are given.
/// Each vendor row goes red when the fake LEAKS the one thing the row asks about, each unit row when the property is taken out of the unit's argument list (<c>CoderBoundary.Degrade</c>, the test-only door).</para>
/// <para>Real <c>systemd-run --user</c> units on temp folders only; skipped, with the reason, where there is no user manager. In the serial <c>fakecli-env</c> collection: the fake is steered by the file beside its binary.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ACodexCoderPreflightTests
{
    private static void NeedAUserManager() => Assert.SkipUnless(SystemdUser.Works(out var why), why);

    private static BoundaryAdmission.Refused Refused(BoundaryAdmission admission) =>
        admission.Should().BeOfType<BoundaryAdmission.Refused>($"the preflight must see it: {admission}").Subject;

    // ---------- d4: the ids of the table and the rows ----------

    [Fact]
    public void ThePreIdsOfTheCodexRow_AndTheIdsOfTheRows_AreTheSameSet_AnIdWithNoRowAndARowWithNoIdBothFail()
    {
        var table = CoderBoundaryTable.Shipped;
        var pre = table.PropertiesOf("codex").Where(property => property.Class == "pre" || property.Also.Contains("pre")).Select(property => property.Id).ToHashSet(StringComparer.Ordinal);
        var checkRows = new Dictionary<string, string> { [BoundaryPreflight.CoderFolderHidden] = "K1", [BoundaryPreflight.OtherJobFolderHidden] = "K2" };

        BoundaryPreflight.Items.Should().Contain(checkRows.Keys, "K1 and K2 are asked by the check preflight, which a coder job also runs before its checks");
        var answered = CodexPreflight.RowIds.Values.Concat(checkRows.Values).ToHashSet(StringComparer.Ordinal);

        pre.Except(answered).Should().BeEmpty("every pre id of the table has a row");
        answered.Except(pre).Should().BeEmpty("every row answers an id of the table that is class pre");
        pre.Should().Contain(["U1", "U9", "K1", "K2", "CX-V1", "CX-V6", "CX-T1", "CX-C1", "CX-C3", "CX-C4", "CX-R2", "CX-R3", "CX-R4"]);
        CodexPreflight.RowIds.Keys.Should().BeEquivalentTo([.. CodexPreflight.Rows.Select(row => row.Item), CodexPreflight.SubmodulesRow], "the rows and the map are one list");
    }

    [Fact]
    public async Task OnAHealthyMachine_EveryRowHolds_TheRowsAreTheItemsOfTheList_AndNoRowCarriesATokenOrAFileOfTheJob()
    {
        NeedAUserManager();
        await using var bed = await CodexBed.CreateAsync();

        var admission = await bed.AdmitAsync();

        var admitted = admission.Should().BeOfType<BoundaryAdmission.Admitted>(admission.ToString()).Subject;
        var record = admitted.Records.Values.Single();
        record.Preflight.Select(row => row.Item).Should().Equal(["user-manager", .. CodexPreflight.Items(bed.Job)]);
        record.Preflight.Should().OnlyContain(row => row.Held, string.Join("; ", record.Preflight.Where(row => !row.Held).Select(row => $"{row.Item}: {row.Detail}")));
        record.Preflight.Should().OnlyContain(row => row.Detail.Length > 0);
        var everything = JsonSerializer.Serialize(record.Preflight) + string.Join("\n", record.Properties);
        everything.Should().NotContain(CodexBed.AccessToken).And.NotContain(CodexBed.IdToken);
        File.ReadAllText(bed.AuthFile).Should().Contain(CodexBed.AccessToken, "a positive control: the copy the rows must not print is there");
        Directory.GetFileSystemEntries(bed.Worktree).Select(Path.GetFileName).Should().NotContain(name => name!.StartsWith(".coai-", StringComparison.Ordinal), "the preflight left no marker or probe in the worktree");
        File.Exists(Path.Combine(bed.Worktree, ".coai-preflight-probe")).Should().BeFalse();
        File.Exists(Path.Combine(bed.Job.CoderVendorFolder("codex"), ".coai-preflight-probe")).Should().BeFalse("a write of the vendor sandbox into the job folder never reached the disk");
        File.Exists(Path.Combine(bed.Job.CoderRoot, ".coai-preflight-probe")).Should().BeFalse("nor beside the worktree");
        (await bed.Coder.MainBytesAsync()).Keys.Should().NotContain(name => name.Contains("coai-preflight", StringComparison.Ordinal), "nothing of the preflight is in the main checkout");
    }

    [Fact]
    public async Task ThePreflight_RunsInTheExactUnitOfTheLaunch_TheSamePropertyList_ButTheirOwnDeadline()
    {
        NeedAUserManager();
        await using var bed = await CodexBed.CreateAsync();
        var request = new ProcessRequest(FakeCliInvocations.Exe, bed.Arguments, bed.Worktree) { Timeout = TimeSpan.FromSeconds(60), InheritsEnvironment = false, Passthrough = ProcessEnvironment.Minimal };
        var launches = bed.Coder.Launcher.Requests.Count;
        bed.Steer(("FAKECLI_EXIT", "0"));

        var outcome = await bed.Turns().RunAsync(bed.Job, request, "c1", bed.Vendor, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<CoderTurnOutcome.Finished>(outcome.ToString());
        var started = bed.Coder.Launcher.Requests.Skip(launches).Where(item => item.Executable == "systemd-run").ToList();
        var pre = started.Single(item => item.Arguments.Contains($"coai-coder-{bed.Job.JobId}-pre"));
        var launch = started.Single(item => item.Arguments.Contains($"coai-coder-{bed.Job.JobId}-c1"));
        string[] deadlines = ["RuntimeMaxSec=", "TimeoutStopSec="];
        IEnumerable<string> Shared(ProcessRequest item) => UnitArguments.PropertiesOf(item.Arguments).Where(property => !property.StartsWith(deadlines[0], StringComparison.Ordinal));

        Shared(pre).Should().Equal(Shared(launch), "the preflight asks the OS inside the very unit the coder will run in, not inside one that looks like it");
        UnitArguments.PropertiesOf(pre.Arguments).Should().Contain(property => property.StartsWith("RuntimeMaxSec=", StringComparison.Ordinal));
        pre.Arguments.Should().Contain(argument => argument.StartsWith("CODEX_HOME=", StringComparison.Ordinal), "the same environment allow-list is set inside it");
    }

    // ---------- d4: each row goes red when its property is dropped ----------

    private sealed record Break(Func<IReadOnlyList<string>, IReadOnlyList<string>>? Degrade = null, BoundaryJob? Job = null, bool Skip = false, string Why = "");

    private static Break BreakOf(string item, CodexBed bed)
    {
        var job = bed.Job;
        string[] Drop(IReadOnlyList<string> arguments, string what) => UnitArguments.Drop(arguments, what);
        Break Unit(Func<IReadOnlyList<string>, IReadOnlyList<string>> degrade) => new(degrade);
        Break Fake(string name, string value)
        {
            bed.Steer((name, value));

            return new Break();
        }

        switch (item)
        {
            case "worktree-write":
                return Unit(arguments => Drop(arguments, $"BindPaths={bed.Worktree}"));
            case "main-hidden":
                return Unit(arguments => UnitArguments.Replace(arguments, $"TemporaryFileSystem={job.Tree.MainCheckout}", $"BindReadOnlyPaths={job.Tree.MainCheckout}"));
            case "coder-root-hidden":
                Directory.CreateDirectory(Path.Combine(job.CoderRoot, "coai-coder-someone-elses-job"));

                return Unit(arguments => UnitArguments.Replace(arguments, $"TemporaryFileSystem={job.CoderRoot}", $"BindReadOnlyPaths={job.CoderRoot}"));
            case "git-readonly":
                return Unit(arguments => Drop(arguments, $"BindReadOnlyPaths={bed.Worktree}/.git"));
            case "no-user-manager-inside":
                return new Break(arguments => Drop(Drop(Drop(arguments, "-i"), "TemporaryFileSystem=/run/user"), "ProtectHome=tmpfs"), Skip: Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is null, Why: "no XDG_RUNTIME_DIR in this process, so no bus path to hand a degraded unit");
            case "windows-exe":
                return new Break(arguments => Drop(arguments, "InaccessiblePaths=-/run/WSL"), Skip: bed.Coder.Host.WindowsProgram.Length == 0, Why: "this host has no WSL interop, so there is no Windows program to try");
            case "no-sockets-no-interop":
                return new Break(arguments => Drop(arguments, "TemporaryFileSystem=/mnt"), Skip: Directory.GetFileSystemEntries("/mnt").Length == 0, Why: "/mnt is empty on this host");
            case "home-is-the-jobs":
                return Unit(arguments => UnitArguments.Replace(arguments, $"HOME={job.CoderHomeOf("codex")}", $"HOME={bed.Coder.RealHome}"));
            case "tmp-is-the-jobs":
                return Unit(arguments => Drop(arguments, $"BindPaths={job.CoderVarTempFolder}:{BoundaryUnit.UnitVarTemp}"));
            case "sign-in-not-in-unit":
                var signIn = Path.Combine(bed.Coder.Host.ResolvedHome, ".claude", ".credentials.json");

                return Unit(arguments => UnitArguments.Replace(arguments, $"InaccessiblePaths=-{signIn}", $"BindReadOnlyPaths={signIn}"));
            case "keys-folder-hidden":
                var keys = Path.Combine(bed.Worktree, "keys-under-test");
                Directory.CreateDirectory(Path.Combine(keys, "claude"));
                File.WriteAllText(Path.Combine(keys, "claude", "k-000000000000.key"), "made-up\n");

                return new Break(arguments => Drop(arguments, $"InaccessiblePaths=-{BoundaryJob.RealOrSpelled(keys)}"), job with { KeysFolder = keys });
            case "environment-exact":
                return Unit(arguments => Drop(arguments, "-i"));
            case "coder-other-job-hidden":
                return Unit(arguments => UnitArguments.Replace(arguments, $"TemporaryFileSystem={job.JobCover}:ro", $"BindPaths={job.JobCover}"));
            case "program-unwritable":
                return Unit(arguments => UnitArguments.Replace(arguments, $"BindReadOnlyPaths={Path.GetDirectoryName(FakeCliInvocations.Exe)}", $"BindPaths={Path.GetDirectoryName(FakeCliInvocations.Exe)}"));
            case "vendor-sandbox-starts":
                return Fake("FAKECLI_CODEX_SANDBOX", "fail");
            case "vendor-sandbox-writes":
                return Fake("FAKECLI_CODEX_SANDBOX", "job");
            case "vendor-sandbox-reads":
                return Fake("FAKECLI_CODEX_SANDBOX", "reads");
            case "vendor-sandbox-network":
                return Fake("FAKECLI_CODEX_SANDBOX", "net");
            case "vendor-sandbox-processes":
                return Fake("FAKECLI_CODEX_SANDBOX", "pid");
            case "vendor-sandbox-protected-names":
                return Fake("FAKECLI_CODEX_SANDBOX", "names");
            case "tool-list":
                return Fake("FAKECLI_CODEX_TOOLS", "agents");
            case "config-read-back":
                File.WriteAllText(Path.Combine(bed.CodexHome, "config.toml"), "default_permissions = \"danger\"\n");

                return new Break();
            case "no-project-tools":
                return Fake("FAKECLI_CODEX_TOOLS", "project-mcp");
            case "permission-profile":
                return Fake("FAKECLI_CODEX_DOCTOR", "no-profile");
            case "refresh-override":
                return Unit(arguments => Drop(arguments, $"CODEX_REFRESH_TOKEN_URL_OVERRIDE={CodexCoderVendor.RefreshOverrideUrl}"));
            case "sign-in-is-the-copy":
                CodexBed.SetMode(bed.AuthFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);

                return new Break();
            case "copy-has-the-placeholder":
                CodexBed.WriteCopy(bed.AuthFile, "a-made-up-refresh-token-that-is-not-the-placeholder");

                return new Break();
            default:
                throw new ArgumentOutOfRangeException(nameof(item), item, "a row of the list with no way to break it here: add one");
        }
    }

    public static TheoryData<string> Items => [.. CodexPreflight.Rows.Select(row => row.Item)];

    [Theory]
    [MemberData(nameof(Items))]
    public async Task OneRowGoingWrong_RefusesTheJob_NamingTheRow_BeforeTheCoderIsLaunched(string item)
    {
        NeedAUserManager();
        await using var bed = await CodexBed.CreateAsync();
        var broken = BreakOf(item, bed);
        Assert.SkipWhen(broken.Skip, broken.Why);
        var launches = bed.Coder.Launcher.Requests.Count;

        var admission = await bed.AdmitAsync(bed.Boundary(broken.Degrade), broken.Job);

        var refused = Refused(admission);
        refused.Record.Preflight.Single(row => row.Item == item).Held.Should().BeFalse($"{item} is a row that must say no when its property is taken away");
        var first = refused.Record.Preflight.First(row => !row.Held);
        refused.Sentence.Should().Contain(first.Item).And.Contain("refused before the coder was launched");
        bed.Coder.Launcher.Requests.Skip(launches).Select(request => request.Executable).Should().OnlyContain(executable => executable == "systemd-run" || executable == "systemctl", "only the preflight unit and its control ran: no coder");
    }

    [Fact]
    public async Task ARowThatSaysNo_StopsTheTurnBeforeAnyLaunch_NamingTheRow_AndStartsNoUnitOfTheCoder()
    {
        NeedAUserManager();
        await using var bed = await CodexBed.CreateAsync(("FAKECLI_CODEX_SANDBOX", "net"));
        var request = new ProcessRequest(FakeCliInvocations.Exe, bed.Arguments, bed.Worktree) { Timeout = TimeSpan.FromSeconds(60), InheritsEnvironment = false, Passthrough = ProcessEnvironment.Minimal };
        var launches = bed.Coder.Launcher.Requests.Count;

        var outcome = await bed.Turns().RunAsync(bed.Job, request, "c1", bed.Vendor, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<CoderTurnOutcome.Refused>(outcome.ToString()).Which.Sentence.Should().Contain("vendor-sandbox-network").And.Contain("refused before the coder was launched");
        bed.Coder.Launcher.Requests.Skip(launches).Should().NotContain(started => started.Arguments.Contains($"coai-coder-{bed.Job.JobId}-c1"), "the unit of the coder was never started");
    }

    [Fact]
    public async Task ASubmoduleMount_GetsItsRow_AndTheRowGoesRedWhenTheMountIsWritable()
    {
        NeedAUserManager();
        await using var bed = await CodexBed.CreateAsync();
        var mount = bed.Coder.Inner.Mount("vendor/sub", ("file.txt", "pinned\n"));
        var held = await bed.AdmitAsync();

        held.Should().BeOfType<BoundaryAdmission.Admitted>(held.ToString()).Which.Records.Values.Single().Preflight.Select(row => row.Item).Should().Contain(CodexPreflight.SubmodulesRow);

        var admission = await bed.AdmitAsync(bed.Boundary(arguments => UnitArguments.Replace(arguments, $"BindReadOnlyPaths={mount.Source}:{mount.Target}", $"BindPaths={mount.Source}:{mount.Target}")));

        Refused(admission).Record.Preflight.Single(row => row.Item == CodexPreflight.SubmodulesRow).Held.Should().BeFalse();
    }

    // ---------- no row carries what a program printed ----------

    [Fact]
    public async Task AProgramThatPrintsHostileText_CannotWriteIntoARow_AndNoRowPrintsAToken()
    {
        NeedAUserManager();
        await using var bed = await CodexBed.CreateAsync(("FAKECLI_CODEX_TOOLS", "hostile"), ("FAKECLI_CODEX_DOCTOR", "hostile"));

        var admission = await bed.AdmitAsync();

        var refused = Refused(admission);
        refused.Record.Preflight.Single(row => row.Item == "tool-list").Held.Should().BeFalse("a tool nobody measured is on the list");
        var said = JsonSerializer.Serialize(refused.Record.Preflight) + refused.Sentence;
        said.Should().NotContain("HOSTILE", "a row never carries the output of a program it ran").And.NotContain(CodexBed.AccessToken).And.NotContain(CodexBed.IdToken);
    }

    [Fact]
    public async Task TheToolListIsAskedForTheJobsOwnModel_AndTheFlagsOfTheLaunch()
    {
        NeedAUserManager();
        await using var bed = await CodexBed.CreateAsync(("FAKECLI_CODEX_EXPECT_MODEL", CodexBed.Model));
        (await bed.AdmitAsync()).Should().BeOfType<BoundaryAdmission.Admitted>("the fake answers the model it is asked for: the job's own");

        bed.Steer(("FAKECLI_CODEX_EXPECT_MODEL", "some-other-model"));
        var refused = Refused(await bed.AdmitAsync());

        refused.Record.Preflight.Single(row => row.Item == "tool-list").Held.Should().BeFalse("a probe that was not asked for the job's model gives no list");
    }

    [Fact]
    public async Task TheDoctorExitCodeIsIgnored_ButAMissingFieldIsNot()
    {
        NeedAUserManager();
        await using var bed = await CodexBed.CreateAsync(("FAKECLI_CODEX_DOCTOR", "exit1"));
        (await bed.AdmitAsync()).Should().BeOfType<BoundaryAdmission.Admitted>("a report with exit code 1 is still a report: its network checks can fail");

        foreach (var broken in new[] { "garbage", "config-error", "network-open", "filesystem-open", "approval-open", "mcp-servers", "other-profile" })
        {
            bed.Steer(("FAKECLI_CODEX_DOCTOR", broken));
            var refused = Refused(await bed.AdmitAsync());
            refused.Record.Preflight.Where(row => !row.Held).Select(row => row.Item).Should().Contain(broken == "mcp-servers" ? ["no-project-tools"] : ["permission-profile"], $"the report '{broken}' is wrong in a way a row must see");
        }
    }

    // ---------- C3 item 3: a copy vendor has rows of its own in place of claude's sign-in rows ----------

    [Fact]
    public async Task ACopyVendor_IsNotAskedClaudesTwoSignInRows_ButItsOwn()
    {
        NeedAUserManager();
        await using var codex = await CodexBed.CreateAsync();

        var admitted = (await codex.AdmitAsync()).Should().BeOfType<BoundaryAdmission.Admitted>().Subject.Records.Values.Single().Preflight.Select(row => row.Item).ToList();

        admitted.Should().NotContain(["sign-in-readonly"], "the sign-in of codex is a copy the CLI may rewrite: it is not bound read-only");
        admitted.Should().Contain(["sign-in-is-the-copy", "copy-has-the-placeholder", BoundaryPreflight.SignInAbsent, BoundaryPreflight.KeysHidden], "the copy's own rows, and the proof that claude's real sign-in and the key pool are not in the unit");
        BoundaryPreflight.CoderItems.Should().Contain("sign-in-readonly", "claude is still asked it");
    }

    // ---------- d5, V10: no user manager, Windows, macOS ----------

    [Theory]
    [InlineData(BoundaryPlatform.Windows, "Windows")]
    [InlineData(BoundaryPlatform.MacOs, "macOS")]
    [InlineData(BoundaryPlatform.Other, "operating system")]
    public async Task ACodexCoderOnAPlatformWithNoBoundary_IsRefusedBeforeAnyProcessStarts(BoundaryPlatform platform, string word)
    {
        await using var bed = await CodexBed.CreateAsync();
        var launches = bed.Coder.Launcher.Requests.Count;
        var request = new ProcessRequest(FakeCliInvocations.Exe, bed.Arguments, bed.Worktree) { Timeout = TimeSpan.FromSeconds(60), InheritsEnvironment = false, Passthrough = ProcessEnvironment.Minimal };

        var outcome = await bed.Turns(bed.Boundary(host: bed.Coder.Host with { Platform = platform })).RunAsync(bed.Job, request, "c1", bed.Vendor, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<CoderTurnOutcome.Refused>(outcome.ToString()).Which.Sentence.Should().Contain(word).And.Contain("refused");
        bed.Coder.Launcher.Requests.Skip(launches).Select(started => started.Executable).Should().NotContain(["systemd-run", "systemctl"], "no process of the unit's side was started");
    }

    [Fact]
    public async Task ACodexCoderOnAMachineWithNoUserManager_IsRefused_NamingTheUserManager_BeforeTheCoderStarts()
    {
        await using var bed = await CodexBed.CreateAsync();
        var request = new ProcessRequest(FakeCliInvocations.Exe, bed.Arguments, bed.Worktree) { Timeout = TimeSpan.FromSeconds(60), InheritsEnvironment = false, Passthrough = ProcessEnvironment.Minimal };

        var outcome = await bed.Turns(bed.Boundary(host: bed.Coder.Host with { SystemdRun = "coai-no-such-systemd-run" })).RunAsync(bed.Job, request, "c1", bed.Vendor, TestContext.Current.CancellationToken);

        var refused = outcome.Should().BeOfType<CoderTurnOutcome.Refused>(outcome.ToString()).Subject;
        refused.Sentence.Should().Contain("user-manager").And.Contain("refused before the coder was launched");
    }
}
