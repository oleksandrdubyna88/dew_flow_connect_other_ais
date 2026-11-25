using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server.Keys;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s7 t5 — the pieces of the key wiring that need no rig: the adapter's contract (one credential, the variable, never an argument), the resume handle's key, what the role runner may be handed, and how a finished launch is read for its key.
/// The launches themselves are proved end to end in <see cref="AKeyRoleLaunchTests"/> and <see cref="AKeyCoderLaunchTests"/>.
/// </summary>
public sealed class AKeyWiringTests
{
    private static readonly string Token = KeyWorld.Token("wire");

    private static ConsultantLaunch Launch(bool credential, string apiKey = "", string home = "", LaunchConfinement? confinement = null) =>
        new(Path.GetFullPath("repo"), "go", string.Empty, Path.GetFullPath("answers"), new ReviewerSettings("claude") { ClaudeCli = ClaudeCapability.WithRestricted, ApiKey = apiKey })
        {
            Credential = credential ? new LaunchCredential(KeyWorld.IdOf(Token), Token) : null,
            OwnHome = home,
            Confinement = confinement ?? LaunchConfinement.AsShipped,
        };

    // ---------- the adapter ----------

    [Fact]
    public void AReadOnlyLaunchWithAKey_HasTheKeyInItsEnvironmentOnly_AHomeOfItsOwn_AndNoForeignCredential()
    {
        var home = Path.GetFullPath("job-home");

        var request = new ClaudeConsultant(new ClaudeRuntime()).Build(Launch(credential: true, home: home)).Request;

        request.Environment[ClaudeConsultant.KeyVariable].Should().Be(Token);
        request.Environment.Should().ContainKey("ANTHROPIC_API_KEY").WhoseValue.Should().BeNull("the person's own API key is removed, not inherited");
        request.Environment["HOME"].Should().Be(home);
        request.Environment["CLAUDE_CONFIG_DIR"].Should().StartWith(home);
        request.Arguments.Should().NotContain("--bare").And.NotContain(argument => argument.Contains(KeyWorld.Sentinel, StringComparison.Ordinal));
        request.StdIn.Should().NotContain(KeyWorld.Sentinel);
    }

    [Fact]
    public void ALaunchWithAKey_AndAnApiKeyTogether_IsAContractViolation()
    {
        var build = () => new ClaudeConsultant(new ClaudeRuntime()).Build(Launch(credential: true, apiKey: "an-api-key"));

        build.Should().Throw<ArgumentException>().WithMessage("*ONE credential*").Which.Message.Should().NotContain(KeyWorld.Sentinel);
    }

    [Fact]
    public void ACredentialAndAnInvocation_PrintWithNoPartOfTheKey()
    {
        var launch = Launch(credential: true);
        var invocation = new ClaudeConsultant(new ClaudeRuntime()).Build(launch);

        launch.ToString().Should().NotContain(KeyWorld.Sentinel);
        launch.Credential!.ToString().Should().Be($"credential {KeyWorld.IdOf(Token)}");
        new ClaudeKeySecret(KeyWorld.IdOf(Token), Token).ToString().Should().NotContain(KeyWorld.Sentinel);
        invocation.Request.Arguments.Should().NotContain(argument => argument.Contains(KeyWorld.Sentinel, StringComparison.Ordinal));
    }

    [Fact]
    public void ACoderLaunchWithAKey_GetsTheVariable_ButNoHomeOfTheLaunchRoad_ItsUnitHasItsOwn()
    {
        var worktree = Path.Combine(Path.GetFullPath("coder-root"), CoaiMcp.Runners.Worktrees.CoderWorktree.FolderOf("job-wire"));

        var request = new ClaudeConsultant(new ClaudeRuntime()).Build(Launch(credential: true, home: Path.GetFullPath("ignored"), confinement: new LaunchConfinement.Coder("job-wire", worktree))).Request;

        request.Environment.Should().ContainKey(ClaudeConsultant.KeyVariable).And.NotContainKey("HOME");
    }

    // ---------- the resume handle ----------

    [Fact]
    public void AResumeHandle_IsBoundToTheKeyOfItsSession_AndAnotherKeyIsRefused()
    {
        var worktree = Path.Combine(Path.GetFullPath("coder-root"), CoaiMcp.Runners.Worktrees.CoderWorktree.FolderOf("job-wire"));
        var session = "33333333-3333-3333-3333-333333333333";
        var first = new LaunchConfinement.Coder("job-wire", worktree) { KeyId = "k-aaaaaaaaaaaa" };
        var handle = CoderHandle.Bind(first, session);

        handle.KeyId.Should().Be("k-aaaaaaaaaaaa");
        CoderLaunches.WhyNot(first with { Resume = handle }).Should().BeEmpty("the same key resumes its own session");
        CoderLaunches.WhyNot(first with { KeyId = "k-bbbbbbbbbbbb", Resume = handle }).Should().Contain("another claude key");
        CoderLaunches.WhyNot(first with { KeyId = string.Empty, Resume = handle }).Should().Contain("another claude key", "a launch with no key does not resume a session a key started");
    }

    // ---------- what the runner may be handed ----------

    [Theory]
    [InlineData("CLAUDE_CODE_OAUTH_TOKEN", false)]
    [InlineData("ANTHROPIC_API_KEY", false)]
    [InlineData("COAI_CLAUDE_KEY_one", false)]
    [InlineData("COAI_CLAUDE_ACCOUNT_one", false)]
    [InlineData("COAI_ROLE_CLAUDE_PERSON_SIGN_IN", true)]
    [InlineData("COAI_KEYS_SHARED", true)]
    public void TheRoleRunnersEnvironment_NeverHoldsAKeyOrItsSource_AndCarriesTheTwoDeviceSwitches(string name, bool allowed) =>
        RoleRunEnvironment.Allowed(name).Should().Be(allowed);

    // ---------- how a finished launch is read for its key ----------

    private static RoleLaunched Finished(ReviewerOutcome? terminal = null, ProcessResult? process = null, bool exposed = false) =>
        new(new ReviewerLaunch(terminal, "answer", Usage.None, string.Empty, process), null, RoleModelLauncher.RuntimeOf("claude").Runtime, null, string.Empty, KeyId: "k-aaaaaaaaaaaa", KeyExposed: exposed);

    private static KeyLaunchReport Read(RoleLaunched launched, RoleStop? stop) => RoleKeyRoad.ReportOf(launched, stop, new DateTime(2026, 11, 25, 8, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void AGoodLaunch_ClearsTheCooldown_AndAModelThatDisagreedWithTheRole_DoesNotHurtTheKey()
    {
        Read(Finished(), null).End.Should().Be(KeyLaunchEnd.Succeeded);
        Read(Finished(), new RoleStop(RoleStopWords.OutputInvalid, "bad", "output-invalid", false, false)).End.Should().Be(KeyLaunchEnd.Succeeded);
        Read(Finished(), new RoleStop(RoleStopWords.Refusal, "no", "refused", false, false)).End.Should().Be(KeyLaunchEnd.Succeeded);
    }

    [Fact]
    public void ALimit_IsACooldown_AThrottleIs60Seconds_AndACrashOrATimeoutNeverTouchesTheKey()
    {
        Read(Finished(), new RoleStop(RoleStopWords.Limit, "spent", "quota", true, false)).End.Should().Be(KeyLaunchEnd.Limit);
        Read(Finished(), new RoleStop(RoleStopWords.Limit, "busy", "rate-limited", false, true)).End.Should().Be(KeyLaunchEnd.Throttled);
        Read(Finished(), new RoleStop(RoleStopWords.Crash, "fell over", "exit", false, false)).End.Should().Be(KeyLaunchEnd.Transient);
        Read(Finished(), new RoleStop(RoleStopWords.CoderBoundary, "no write", "write-stopped", false, false)).End.Should().Be(KeyLaunchEnd.Transient);
    }

    [Fact]
    public void ALimitThatStatedItsReset_CarriesTheResetToThePool()
    {
        var limited = new ReviewerOutcome.RateLimited("You've hit your limit. Resets in 3h 0m.");

        var report = Read(Finished(limited), new RoleStop(RoleStopWords.Limit, "limit", "quota", true, false));

        report.End.Should().Be(KeyLaunchEnd.Limit);
        report.StatedReset.Should().NotBeNull();
        report.StatedReset!.Value.UtcDateTime.Should().BeAfter(new DateTime(2026, 11, 25, 8, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ANotLoggedInEnvelope_DoesNotKillTheKey_UntilARealRefusedKeyFixtureExists_AndATextFoundInTheOutput_IsAnExposure_BeforeAnythingElse()
    {
        var notLoggedIn = new ProcessResult(1, RoleStopFixtures.Load("claude-no-sign-in").StdOut, string.Empty, TimedOut: false);

        // On a keyed launch that text more probably means the CLI did not get the key, so one good key would die per job; a revoked key probably answers 401, which is not read (second judgement, deviation 2).
        Read(Finished(process: notLoggedIn), new RoleStop(RoleStopWords.Crash, "the sign-in is lost", "exit", true, false)).End.Should().Be(KeyLaunchEnd.Transient);
        Read(Finished(exposed: true), null).End.Should().Be(KeyLaunchEnd.Exposed);
        Read(Finished(process: notLoggedIn, exposed: true), null).End.Should().Be(KeyLaunchEnd.Exposed);
    }

    [Fact]
    public void AResumeOfAClaudeModelWithNoKeyId_IsGone_NeverTheNoKeyOfAnotherVendor()
    {
        using var data = TempDir.For("coai-resume-");
        var road = RoleKeyRoad.For(data.Path, TimeProvider.System, _ => true, _ => null);

        road.Resume(new ModelRef("sonnet", "claude"), string.Empty).Should().BeOfType<RoleKey.Gone>("an empty id would otherwise read as no key at all, which is the person's sign-in");
        road.Resume(new ModelRef("gpt-5.5", "codex"), string.Empty).Should().BeOfType<RoleKey.None>("another vendor has no key, and that is right");
    }

    [Fact]
    public void TheRunnersUnit_AndEveryBoundaryUnit_NeverDumpCore_AUnitThatMayHoldAKeyLeavesNoCoreFile()
    {
        CoaiMcp.Runners.Detach.DetachedLauncher.SystemdRunArguments(new CoaiMcp.Runners.Detach.DetachSpec("coai-mcp", ["--role-run", "x"], TempDir.Root, "coai-role-x", 1000)).Should().Contain("LimitCORE=0");
        CoaiMcp.Runners.Toolchain.BoundaryUnit.Hardening.Should().Contain("LimitCORE=0");
    }

    [Fact]
    public void TheStagedBlobsOfARawDiff_AreTheNewOnesOnly_NotADeletionNorASubmodule()
    {
        var zero = new string('0', 40);
        var raw = $":100644 100644 {new string('a', 40)} {new string('b', 40)} M\0src/a.txt\0:000000 100644 {zero} {new string('c', 40)} A\0src/new.txt\0:100644 000000 {new string('d', 40)} {zero} D\0src/gone.txt\0:160000 160000 {new string('e', 40)} {new string('f', 40)} M\0sub\0";

        CoderCommits.BlobsOf(raw).Should().Equal(new string('b', 40), new string('c', 40));
    }

    // ---------- the structural scan of the launch road ----------

    private static readonly System.Text.RegularExpressions.Regex LoggedKey = new(
        @"\b(log\w*|Log\w*|logger|Console\s*\.\s*\w+|Warning|Information|Error|Debug|Verbose|Fatal|Note)\b[^;]*?(\.Secret\b|\bReveal\s*\(|\bCredential\b|\bClaudeKeySecret\b)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    [Fact]
    public void Scan_NoLogOrConsoleCallOfTheLaunchRoad_TakesTheSecretOrTheCredential_AndTheScanSeesOneWhenThereIs()
    {
        var root = Path.Combine(RepoPaths.Root(), "src_mcp");
        var files = Directory.GetFiles(Path.Combine(root, "src", "Server", "RoleJobs"), "*.cs").Append(Path.Combine(root, "runners", "Consultation", "ClaudeConsultant.cs")).Append(Path.Combine(root, "runners", "Consultation", "LaunchCredential.cs")).ToList();
        files.Should().HaveCountGreaterThan(30, "the scan must see the whole launch road");

        var offenders = files
            .SelectMany(path => LoggedKey.Matches(System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path), @"///[^\n]*|//[^\n]*", string.Empty)).Select(match => $"{Path.GetFileName(path)}: {match.Value.Trim()[..Math.Min(80, match.Value.Trim().Length)]}"))
            .ToList();

        offenders.Should().BeEmpty("a log or console call that takes the secret or the credential prints a key");
        LoggedKey.IsMatch("run.Log.Information(\"launch {Key}\", pooled.Secret);").Should().BeTrue("the scan matches a logged secret");
        LoggedKey.IsMatch("Console.Error.WriteLine(launch.Credential);").Should().BeTrue("and a logged credential");
        LoggedKey.IsMatch("run.Log.Information(\"role job {JobId} switched key {Key}\", run.Request.JobId, _lastKeyId);").Should().BeFalse("the real line names the key by its id");
    }

    // ---------- a key that is gone ----------

    [Fact]
    public async Task ALaunchWithAKeyThatIsGone_IsAStopThatReadsAsALimit_AndStartsNothing()
    {
        using var data = TempDir.For("coai-gone-");
        var launcher = new RoleModelLauncher(_ => null, NoProcessTracking.Instance);

        var launched = await launcher.LaunchAsync(new ModelRef("sonnet", "claude"), "go", data.Path, data.At("out"), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken, string.Empty, new RoleKey.Gone("gone: the key of this session was removed"));

        launched.Launch.Should().BeNull();
        launched.KeyStop.Should().NotBeNull();
        launched.KeyStop!.Word.Should().Be(RoleStopWords.Limit);
        launched.KeyStop.SkipsVendor.Should().BeFalse("the vendor is not given up: the loop goes on with the next key");
        RoleFailover.StopOfLaunch(new ModelRef("sonnet", "claude"), launched).Should().Be(launched.KeyStop);
    }
}
