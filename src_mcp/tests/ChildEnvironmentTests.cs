using CoaiMcp.Core.Notices;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every question launch's child environment is the measured minimal list and nothing else — no
/// <c>COAI_*</c>, no secret-named variable (PLAN_question_consultant.md, §4 S2c; S1 acceptance 6).
/// </summary>
/// <remarks>
/// <para>Observed on a REAL child (the fake CLI's <c>env-names</c> verb prints every variable it was
/// started with), the way <see cref="ProcessLauncherTests"/> observes the Team server's allowlist — a
/// dictionary asserted in-process would be a test of the dictionary. In the <c>fakecli-env</c>
/// collection for the reason that class gives.</para>
/// <para>The Windows list is the benchmark's, measured on 2026-10-01 (every CLI started and answered
/// on it); the Unix list is NOT the benchmark's — it had no Linux subject — and is the launcher's own
/// measured requirement (HOME, or every Node CLI fails in initialisation) plus the locale and
/// temporary-directory names. Recorded as a deviation in the plan's S1 block.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ChildEnvironmentTests
{
    private static readonly TimeSpan NoBudgetPressure = TimeSpan.FromMinutes(1);

    [Fact]
    public void TheWindowsList_IsTheBenchmarksS2cList_Literally()
    {
        ProcessEnvironment.MinimalFor(isWindows: true).Should().BeEquivalentTo([
            "PATH", "PATHEXT", "SystemRoot", "windir", "ComSpec", "USERPROFILE", "HOMEDRIVE", "HOMEPATH",
            "APPDATA", "LOCALAPPDATA", "TEMP", "TMP", "USERNAME", "ProgramData",
            "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432",
            "PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER", "PROCESSOR_LEVEL", "PROCESSOR_REVISION",
            "OS", "NUMBER_OF_PROCESSORS",
        ]);
    }

    [Fact]
    public void TheUnixList_IsWhatANodeCliNeedsToStart_AndNothingMore()
    {
        ProcessEnvironment.MinimalFor(isWindows: false).Should().BeEquivalentTo([
            "PATH", "HOME", "USER", "LOGNAME", "SHELL", "LANG", "LC_ALL", "LC_CTYPE", "TERM", "TMPDIR", "TMP", "TEMP", "DOTNET_ROOT",
        ]);
    }

    [Fact]
    public void TheUnixList_CarriesDotnetRoot_SoAChildOnARuntimeOutsideTheDefaultPlaceStillStarts()
    {
        // macOS CI, PR #646: the runtime lives under the runner's home, the default probe finds nothing, and a
        // framework-dependent child on the minimal list said "You must install .NET to run this application".
        // Windows finds its runtime through the registry, so its list needs no such name.
        ProcessEnvironment.MinimalFor(isWindows: false).Should().Contain("DOTNET_ROOT");
    }

    [Fact]
    public void NoNameOnEitherList_IsACoaiVariable_OrNamesACredential_OrCarriesAProxy()
    {
        foreach (var isWindows in (bool[])[true, false])
        {
            foreach (var name in ProcessEnvironment.MinimalFor(isWindows))
            {
                name.Should().NotStartWith("COAI_", "nothing of this product's own configuration reaches a question child");
                CredentialWords.NamesACredential(name).Should().BeFalse($"{name} reads as a credential");
                name.ToUpperInvariant().Should().NotContain("PROXY", "a proxy name carries a credential in a URL more often than not; a question row does not get one");
            }
        }

        ProcessEnvironment.Minimal.Should().BeEquivalentTo(ProcessEnvironment.MinimalFor(OperatingSystem.IsWindows()));
    }

    /// <summary>
    /// Names the child's OWN runtime writes into its environment at start, so it reports them whatever it was given:
    /// on macOS CoreFoundation sets <c>__CF_USER_TEXT_ENCODING</c> when it is absent (macOS CI, PR #646). Nothing here
    /// is passed by the launcher, which is what the assertion below is about.
    /// </summary>
    private static readonly string[] SetByTheChildItself = OperatingSystem.IsMacOS() ? ["__CF_USER_TEXT_ENCODING"] : [];

    [Fact]
    public async Task AQuestionChild_SeesTheMinimalList_AndNothingElse()
    {
        using var canary = new Canary("COAI_QCANARY");
        using var secret = new Canary("MY_SECRET_TOKEN");
        var request = ConsultantLaunches.ForQuestion(
            new ProcessRequest(FakeCliInvocations.Exe, ["env-names"], AppContext.BaseDirectory) { Timeout = NoBudgetPressure });

        var result = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0, "a child must still START on the minimal list alone; it said: {0}", result.StdErr);
        var names = result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToList();
        names.Should().NotBeEmpty();
        names.Should().NotContain(canary.Name).And.NotContain(secret.Name);
        var passed = names.Except(SetByTheChildItself).ToList();
        passed.Should().OnlyContain(name => ProcessEnvironment.Minimal.Contains(name),
            "the list and nothing else: {0}", string.Join(", ", passed.Where(n => !ProcessEnvironment.Minimal.Contains(n))));
        names.Should().Contain(name => string.Equals(name, "PATH", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
    }

    [Fact]
    public void TheMinimalListIsAChoicePerRequest_AndTheDefaultIsTheLauncherOwnAllowlist()
    {
        // Widened rather than copied: `ProcessRequest.Passthrough` is what a non-inheriting launch
        // keeps, defaulting to the Team server's allowlist — so every launch written before the
        // question consultant confines exactly as it did.
        new ProcessRequest("x", [], ".").Passthrough.Should().BeSameAs(ProcessEnvironment.Passthrough);
        ProcessEnvironment.Minimal.Should().NotBeEquivalentTo(ProcessEnvironment.Passthrough,
            "the measured list carries no proxy, certificate or locale name the Team server's does");
    }

    [Fact]
    public void EveryPlannedLaunch_AsksForTheMinimalEnvironment_AndEveryShippedOneStillInherits()
    {
        var scratch = Directory.CreateTempSubdirectory("coai-env-scratch-").FullName;
        var answers = Directory.CreateTempSubdirectory("coai-env-answers-").FullName;
        try
        {
            foreach (var (runtime, consultant, grant) in Launchable())
            {
                var plan = (Confinement.Planned)ConfinementPlanner.Plan(runtime, grant);
                var launch = new ConsultantLaunch("D:/repo", "q", string.Empty, answers, new ReviewerSettings(runtime) { ApiKey = "k-0123456789abcdef" }, Path.Combine(answers, "s.json"))
                {
                    Confinement = new LaunchConfinement.Planned(plan),
                    ScratchDir = scratch,
                };
                File.WriteAllText(launch.AnswerSchemaFile, "{}");

                var request = consultant.Build(launch).Request;

                request.InheritsEnvironment.Should().BeFalse($"{runtime} × {grant.Capability}");
                request.Passthrough.Should().BeEquivalentTo(ProcessEnvironment.Minimal, $"{runtime} × {grant.Capability}");
                request.Environment.Keys.Should().BeSubsetOf(["ANTHROPIC_API_KEY", "OPENAI_API_KEY", "ANTIGRAVITY_API_KEY", ApiRuntime.KeyVariable],
                    $"{runtime}: the vendor key is the only variable a launch hands over, joined last");
            }

            var shipped = new ConsultantLaunch("D:/repo", "q", string.Empty, answers, new ReviewerSettings("claude"));
            new ClaudeConsultant(new ClaudeRuntime()).Build(shipped).Request.InheritsEnvironment.Should().BeTrue(
                "the stuck consultant runs the developer's own CLI in the developer's own environment, as before (A12)");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
            Directory.Delete(answers, recursive: true);
        }
    }

    private static IEnumerable<(string Runtime, IAnsweringRuntime Consultant, CapabilityGrant Grant)> Launchable()
    {
        yield return ("claude", new ClaudeConsultant(new ClaudeRuntime()), CapabilityGrant.None);
        yield return ("claude", new ClaudeConsultant(new ClaudeRuntime()), CapabilityGrant.Disk("D:/projects/alpha"));
        yield return ("claude", new ClaudeConsultant(new ClaudeRuntime()), CapabilityGrant.Web);
        yield return ("codex", new CodexConsultant(new CodexRuntime()), CapabilityGrant.None);
        yield return ("codex", new CodexConsultant(new CodexRuntime()), CapabilityGrant.Web);
        yield return ("antigravity", new AntigravityConsultant(new AntigravityRuntime()), CapabilityGrant.None);
        yield return ("local", new LocalConsultant(new LocalRuntime("local", "http://127.0.0.1:11434/v1"), "local"), CapabilityGrant.None);
        yield return ("api", new ApiConsultant(new ApiRuntime("grok", "https://api.x.ai/v1"), "grok", "grok"), CapabilityGrant.None);
    }

    /// <summary>A variable that exists in THIS process for one test and is gone when the test is.</summary>
    private sealed class Canary : IDisposable
    {
        public string Name { get; }

        public Canary(string prefix)
        {
            Name = $"{prefix}_{Guid.NewGuid():N}";
            Environment.SetEnvironmentVariable(Name, "the parent's own configuration");
        }

        public void Dispose() => Environment.SetEnvironmentVariable(Name, null);
    }
}
