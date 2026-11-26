using System.Text;
using System.Text.Json.Nodes;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s5 t1, d1 and d2 (V6; the owner's decision of 2026-11-26, which replaces id CX-P1 of the judgement): what the installed codex must say before it is given the coder's write launch — <c>exec --help</c> and <c>exec resume --help</c> each DECLARE every flag of the argv, as an option and never
/// as a word in prose. The RELEASE is a record, never a condition: a release one patch above the measured one, one patch below it and one the CLI does not name are each launched, and no sentence of the product asks for a new measure or a new judgement.
/// </summary>
/// <remarks>
/// The fixtures are the REAL help texts of codex-cli 0.164.0 on the measuring machine (<c>fixtures/codex/exec-help-0.164.0-wsl.txt</c>, <c>exec-resume-help-0.164.0-wsl.txt</c>, captured 2026-11-26 with a scratch home — a release that is NOT the measured 0.171.0, which is the point); the broken variants are that
/// text with one option taken out, so each refusal is tested against a help that is otherwise acceptable.
/// </remarks>
public sealed class ACodexWriteCapabilityTests
{
    private const string Measured = "0.171.0";

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "codex", name));

    private static string ExecHelp() => Fixture("exec-help-0.164.0-wsl.txt");

    private static string ResumeHelp() => Fixture("exec-resume-help-0.164.0-wsl.txt");

    /// <summary>The help without the option block that starts at <paramref name="flag"/>: its line and the wrapped lines under it.</summary>
    private static string WithoutOption(string help, string flag)
    {
        var kept = new List<string>();
        var inside = false;
        foreach (var line in help.Split('\n'))
        {
            inside = (line.TrimStart().StartsWith(flag, StringComparison.Ordinal) || line.TrimStart().StartsWith("-", StringComparison.Ordinal) && line.Contains($", {flag}", StringComparison.Ordinal)) && line.StartsWith("  ", StringComparison.Ordinal)
                || (inside && line.StartsWith("          ", StringComparison.Ordinal));
            if (!inside)
            {
                kept.Add(line);
            }
        }

        return string.Join('\n', kept);
    }

    private static CodexWriteCapability.Refused Refusal(string version, string exec, string resume) =>
        CodexWriteCapability.Judge(version, exec, resume).Should().BeOfType<CodexWriteCapability.Refused>().Subject;

    [Fact]
    public void ARealHelpOfAReleaseThatIsNotTheMeasuredOne_DeclaresEveryFlag_AndIsReady()
    {
        CodexWriteCapability.Judge("codex-cli 0.164.0\n", ExecHelp(), ResumeHelp()).Should().Be(new CodexWriteCapability.Ready("0.164.0"));
    }

    [Theory]
    [InlineData("codex-cli 0.171.0", "0.171.0")]
    [InlineData("codex-cli 0.171.1", "0.171.1")]
    [InlineData("codex-cli 0.170.9", "0.170.9")]
    [InlineData("codex-cli 0.110.0", "0.110.0")]
    [InlineData("codex-cli 1.0.0", "1.0.0")]
    [InlineData("codex-cli 0.172.0-alpha.3", "0.172.0")]
    public void AnOlderOneNewerAndTheMeasuredRelease_AreEachReady_AndNamedAsPrinted(string printed, string named)
    {
        CodexWriteCapability.Judge(printed + "\n", ExecHelp(), ResumeHelp()).Should().Be(new CodexWriteCapability.Ready(named));
    }

    [Theory]
    [InlineData("")]
    [InlineData("codex")]
    [InlineData("a banner with no release\ncodex-cli 0.171.0")]
    public void AVersionLineThatNamesNoRelease_IsReady_RecordedAsUnknown_NeverGuessed(string printed)
    {
        CodexWriteCapability.Judge(printed, ExecHelp(), ResumeHelp()).Should().Be(new CodexWriteCapability.Ready(CodexWriteCapability.UnknownRelease));
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--model")]
    [InlineData("--config")]
    [InlineData("--disable")]
    [InlineData("--output-schema")]
    [InlineData("--output-last-message")]
    public void AFlagTheExecHelpDoesNotDeclare_IsARefusal_ThatNamesTheFlagAndTheHelp_NeverTheRelease(string flag)
    {
        var broken = WithoutOption(ExecHelp(), flag);
        broken.Should().NotBe(ExecHelp(), "the fixture variant must really lack the option");

        var refused = Refusal("codex-cli 0.171.0\n", broken, ResumeHelp());

        refused.Said.Should().Contain(flag).And.Contain("codex exec --help").And.NotContain("exec resume --help");
        refused.Cure.Should().NotContainEquivalentOf("update").And.NotContainEquivalentOf("newer").And.NotContainEquivalentOf("measure");
        refused.CouldNotStart.Should().BeFalse();
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--model")]
    [InlineData("--config")]
    [InlineData("--disable")]
    [InlineData("--output-schema")]
    [InlineData("--output-last-message")]
    public void AFlagTheResumeHelpDoesNotDeclare_IsARefusal_ThatNamesTheFlagAndTheResumeHelp(string flag)
    {
        var broken = WithoutOption(ResumeHelp(), flag);
        broken.Should().NotBe(ResumeHelp());

        var refused = Refusal("codex-cli 0.171.0\n", ExecHelp(), broken);

        refused.Said.Should().Contain(flag).And.Contain("codex exec resume --help");
    }

    [Fact]
    public void AFlagOnlyNamedInProse_IsNotDeclared()
    {
        // `--output-schema` stays in the text of another option's description, but not as an option of its own.
        var prose = WithoutOption(ExecHelp(), "--output-schema") + "\n          mentions --output-schema in a sentence\n";

        Refusal("codex-cli 0.171.0\n", prose, ResumeHelp()).Said.Should().Contain("--output-schema");
    }

    [Fact]
    public void TheRefusalNamesEveryMissingFlagOnBothHelps_InOneSentence()
    {
        var refused = Refusal("codex-cli 0.171.0\n", WithoutOption(ExecHelp(), "--json"), WithoutOption(ResumeHelp(), "--disable"));

        refused.Said.Should().Contain("--json").And.Contain("--disable").And.Contain("codex exec --help").And.Contain("codex exec resume --help");
    }

    [Fact]
    public async Task TheProbe_AsksTheVersionAndBothHelps_ThroughTheOneLauncher_AndNothingElse()
    {
        var launcher = new WatchedLauncher(new ProcessLauncher(), request => request.Arguments switch
        {
            ["--version"] => new ProcessResult(0, "codex-cli 0.164.0\n", string.Empty, false),
            ["exec", "--help"] => new ProcessResult(0, ExecHelp(), string.Empty, false),
            ["exec", "resume", "--help"] => new ProcessResult(0, ResumeHelp(), string.Empty, false),
            _ => throw new InvalidOperationException("a capability check started a turn"),
        });

        var answer = await CodexWriteCapability.ProbeAsync(launcher, "codex", Path.GetFullPath("anywhere"), TestContext.Current.CancellationToken);

        answer.Should().Be(new CodexWriteCapability.Ready("0.164.0"));
        launcher.Requests.Select(request => string.Join(' ', request.Arguments)).Should().Equal("--version", "exec --help", "exec resume --help");
        launcher.Requests.Should().OnlyContain(request => request.Timeout <= TimeSpan.FromSeconds(10), "a hung CLI is stopped by the launcher, not waited for");
    }

    [Fact]
    public async Task TheProbe_IsAskedOnEveryLaunch_NeverCached()
    {
        var launcher = new WatchedLauncher(new ProcessLauncher(), request => request.Arguments switch
        {
            ["--version"] => new ProcessResult(0, "codex-cli 0.164.0\n", string.Empty, false),
            ["exec", "--help"] => new ProcessResult(0, ExecHelp(), string.Empty, false),
            _ => new ProcessResult(0, ResumeHelp(), string.Empty, false),
        });

        await CodexWriteCapability.ProbeAsync(launcher, "codex", Path.GetFullPath("anywhere"), TestContext.Current.CancellationToken);
        await CodexWriteCapability.ProbeAsync(launcher, "codex", Path.GetFullPath("anywhere"), TestContext.Current.CancellationToken);

        launcher.Requests.Should().HaveCount(6, "each launch asks the CLI again: a help that changed between two launches is read as it is");
    }

    [Fact]
    public async Task AVersionThatTimedOut_IsRecordedAsUnknown_AndTheHelpsDecide()
    {
        var launcher = new WatchedLauncher(new ProcessLauncher(), request => request.Arguments switch
        {
            ["--version"] => new ProcessResult(-1, string.Empty, string.Empty, true),
            ["exec", "--help"] => new ProcessResult(0, ExecHelp(), string.Empty, false),
            _ => new ProcessResult(0, ResumeHelp(), string.Empty, false),
        });

        var answer = await CodexWriteCapability.ProbeAsync(launcher, "codex", Path.GetFullPath("anywhere"), TestContext.Current.CancellationToken);

        answer.Should().Be(new CodexWriteCapability.Ready(CodexWriteCapability.UnknownRelease), "a release that did not answer is a record, never a condition");
    }

    [Fact]
    public async Task AHelpThatDoesNotAnswer_IsARefusal_ThatSaysSo()
    {
        var launcher = new WatchedLauncher(new ProcessLauncher(), request => request.Arguments switch
        {
            ["--version"] => new ProcessResult(0, "codex-cli 0.164.0\n", string.Empty, false),
            ["exec", "--help"] => new ProcessResult(-1, string.Empty, string.Empty, true),
            _ => new ProcessResult(0, ResumeHelp(), string.Empty, false),
        });

        var answer = await CodexWriteCapability.ProbeAsync(launcher, "codex", Path.GetFullPath("anywhere"), TestContext.Current.CancellationToken);

        var refused = answer.Should().BeOfType<CodexWriteCapability.Refused>().Subject;
        refused.Said.Should().Contain("codex exec --help did not answer").And.Contain("will not launch the coder without them");
        refused.CouldNotStart.Should().BeFalse();
    }

    [Fact]
    public async Task ACliThatCannotBeStarted_IsARefusal_ThatSaysSo()
    {
        var answer = await CodexWriteCapability.ProbeAsync(new ProcessLauncher(), "/nonexistent/coai-no-such-codex", Path.GetFullPath("."), TestContext.Current.CancellationToken);

        var refused = answer.Should().BeOfType<CodexWriteCapability.Refused>().Subject;
        refused.CouldNotStart.Should().BeTrue();
        refused.Said.Should().Contain("could not be started");
    }

    [Fact]
    public void NoSentenceOfTheCapability_AsksForANewMeasureOrANewJudgement_OrNamesTheMeasuredRelease()
    {
        var refusals = new[]
        {
            Refusal(Measured, WithoutOption(ExecHelp(), "--json"), ResumeHelp()),
            Refusal(Measured, ExecHelp(), WithoutOption(ResumeHelp(), "--output-schema")),
            Refusal("codex-cli 9.9.9", WithoutOption(ExecHelp(), "--disable"), WithoutOption(ResumeHelp(), "--config")),
        };

        foreach (var refused in refusals)
        {
            var text = refused.Said + " " + refused.Cure;
            text.Should().NotContainEquivalentOf("new measure").And.NotContainEquivalentOf("judgement").And.NotContainEquivalentOf("measured on").And.NotContain("0.171.0");
        }
    }

    // ---------- d3: the margin CX-R1, with an injected clock ----------

    private static string Jwt(DateTime expires) =>
        $"{B64("{\"alg\":\"none\"}")}.{B64($"{{\"exp\":{new DateTimeOffset(expires).ToUnixTimeSeconds()}}}")}.{B64("signature-made-up")}";

    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void CXR1_ACopyOneSecondUnderTheMargin_IsRefused_AndOneAtTheMarginIsStarted_AndNoTokenIsInTheSentence()
    {
        var recipe = (CoderSignInRecipe.CopyForJob)CodexCoderVendor.Instance.SignIn;
        var now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
        var runtimeMax = TimeSpan.FromMinutes(30);
        var need = runtimeMax + TimeSpan.FromMinutes(60);
        string Gate(DateTime expires)
        {
            var root = new JsonObject { ["tokens"] = new JsonObject { ["access_token"] = Jwt(expires) } };

            return SignInCopy.Gate(recipe, SignInCopy.ExpiryOf(recipe, root), now, runtimeMax, "codex");
        }

        Gate(now + need).Should().BeEmpty("exp = now + the unit's life + 60 minutes is enough");
        var refused = Gate(now + need - TimeSpan.FromSeconds(1));
        refused.Should().Contain("codex").And.Contain("start codex once yourself");
        refused.Should().NotContain("signature-made-up").And.NotContain("eyJ");
        recipe.Margin.Should().Be(TimeSpan.FromMinutes(60));
        recipe.RefreshArgv.Should().BeEmpty("codex is never refreshed by coai: its refresh token is in no job");
        recipe.Placeholders.Should().ContainKey("tokens/refresh_token").WhoseValue.Should().Be(SignInCopy.Placeholder);
    }

    [Fact]
    public void CXR1_AnExpiryThatCannotBeRead_IsARefusal_NotAGuess()
    {
        var recipe = (CoderSignInRecipe.CopyForJob)CodexCoderVendor.Instance.SignIn;
        var root = new JsonObject { ["tokens"] = new JsonObject { ["access_token"] = "not-a-jwt-made-up" } };

        SignInCopy.Gate(recipe, SignInCopy.ExpiryOf(recipe, root), DateTime.UtcNow, TimeSpan.FromMinutes(30), "codex").Should().Contain("could not be read").And.NotContain("not-a-jwt-made-up");
    }

    [Fact]
    public async Task TheCodexConsultant_PreparesACoderLaunch_ThroughTheProbe_AndRecordsTheRelease_OrRefusesWithTheFlag()
    {
        var consultant = new CodexConsultant(new CodexRuntime());
        var worktree = Path.Combine(Path.GetFullPath("coder-root"), CoaiMcp.Runners.Worktrees.CoderWorktree.FolderOf("job-a"));
        var launch = new ConsultantLaunch(Path.GetFullPath("main-checkout"), "q", string.Empty, Path.GetFullPath("answers"), new ReviewerSettings("codex") { Model = "terra" })
        {
            Confinement = new LaunchConfinement.Coder("job-a", worktree) { Vendor = "codex" },
        };
        ProcessResult Answer(string help) => new(0, help, string.Empty, false);
        var fine = new WatchedLauncher(new ProcessLauncher(), request => request.Arguments switch
        {
            ["--version"] => Answer("codex-cli 0.164.0\n"),
            ["exec", "--help"] => Answer(ExecHelp()),
            _ => Answer(ResumeHelp()),
        });
        var lacking = new WatchedLauncher(new ProcessLauncher(), request => request.Arguments switch
        {
            ["--version"] => Answer("codex-cli 0.164.0\n"),
            ["exec", "--help"] => Answer(WithoutOption(ExecHelp(), "--output-schema")),
            _ => Answer(ResumeHelp()),
        });

        var ready = await consultant.PrepareAsync(launch, fine, TestContext.Current.CancellationToken);
        var refused = await consultant.PrepareAsync(launch, lacking, TestContext.Current.CancellationToken);

        ready.Should().BeOfType<ConsultantPreparation.Ready>().Which.CliVersion.Should().Be("0.164.0");
        refused.Should().BeOfType<ConsultantPreparation.Refused>().Which.Failure.Should().BeOfType<ConsultFailure.VendorRefused>().Which.Said.Should().Contain("--output-schema");
    }
}
