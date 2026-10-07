using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The claude consultant's argv, byte for byte — with <c>--restricted</c> when the installed CLI knows it,
/// without it when it does not, fresh, resumed and with a model.
/// </summary>
/// <remarks>
/// <para><b>These assert what is SENT, not what it does.</b> A flag in an argv is a request to the CLI; whether
/// the CLI honours it is a fact about the CLI, and a unit test cannot see it (testing.md, <i>Asserting that you
/// SENT a protective option is not testing that it protects</i>). The evidence of EFFECT is the live probe,
/// <c>scripts/probe-claude-consultant-confinement.mjs</c>, and its record,
/// <c>research/RESULTS_claude_consultant_confinement.md</c>: the supported argv below — the same flags, with
/// stream-json in place of json so the tool calls are visible — was confined 9 of 9 on Windows claude 2.1.258,
/// fresh and resumed, with the sign-in intact. The unsupported argv held 9 of 9 on WSL claude 2.1.197, but the
/// same shape LEAKED an outside read on Windows 2.1.258 on 2026-10-01, so it is not called confinement anywhere —
/// the limitation row for a claude without <c>--restricted</c> says so.</para>
/// <para>Why an allowlist and not the deny-list it replaced: the deny-list never named PowerShell, and on
/// Windows it leaked a canary outside the repository 6 of 6 while offering the user's whole tool set (2026-10-01,
/// <c>RESULTS_question_consultant_capabilities.md</c> on branch <c>feat/question-consultant</c>).</para>
/// <para>These goldens OWN the argv: an equality over the whole list is what says there is no deny-list, no shell
/// and ONE <c>Read,Glob,Grep</c> argument (<c>--tools &lt;tools...&gt;</c> is variadic; the comma-joined form is the
/// one the probe measured). The tests that used to assert pieces of it — <c>ConsultantsTests.ClaudeIsReadOnly_…</c>,
/// <c>ConsultStoryTwoGateTests.AConsultantMayREADTheTree_…</c> — were folded in here (epic 3's code round).</para>
/// </remarks>
public sealed class ClaudeConsultantArgvTests
{
    private const string Repo = "D:/rsd/some-checkout";
    private const string Handle = "67289235-65f7-40b5-9532-e63515d90f30";
    private const string Model = "claude-opus-5";


    private static IReadOnlyList<string> Argv(ClaudeCapability cli, string handle = "", string model = "") =>
        new ClaudeConsultant(new ClaudeRuntime())
            .Build(new ConsultantLaunch(Repo, "help me", handle, "D:/answers", new ReviewerSettings("claude") { Model = model, ClaudeCli = cli }))
            .Request.Arguments;

    private static readonly string[] Supported =
        ["-p", "--output-format", "json", "--permission-mode", "plan", "--restricted", "--tools", "Read,Glob,Grep", "--strict-mcp-config", "--add-dir", Repo];

    private static readonly string[] Unsupported =
        ["-p", "--output-format", "json", "--permission-mode", "plan", "--tools", "Read,Glob,Grep", "--strict-mcp-config", "--add-dir", Repo];

    [Fact]
    public void Supported_Fresh() =>
        Argv(ClaudeCapability.WithRestricted).Should().Equal(Supported);

    [Fact]
    public void Supported_Resumed() =>
        Argv(ClaudeCapability.WithRestricted, handle: Handle).Should().Equal([.. Supported, "--resume", Handle]);

    [Fact]
    public void Supported_WithAModel() =>
        Argv(ClaudeCapability.WithRestricted, model: Model).Should().Equal([.. Supported, "--model", Model]);

    [Fact]
    public void Supported_ResumedWithAModel() =>
        Argv(ClaudeCapability.WithRestricted, handle: Handle, model: Model).Should().Equal([.. Supported, "--resume", Handle, "--model", Model]);

    [Fact]
    public void Unsupported_Fresh() =>
        Argv(ClaudeCapability.NoRestricted).Should().Equal(Unsupported);

    [Fact]
    public void Unsupported_Resumed() =>
        Argv(ClaudeCapability.NoRestricted, handle: Handle).Should().Equal([.. Unsupported, "--resume", Handle]);

    [Fact]
    public void Unsupported_WithAModel() =>
        Argv(ClaudeCapability.NoRestricted, model: Model).Should().Equal([.. Unsupported, "--model", Model]);

    [Fact]
    public void Unsupported_ResumedWithAModel() =>
        Argv(ClaudeCapability.NoRestricted, handle: Handle, model: Model).Should().Equal([.. Unsupported, "--resume", Handle, "--model", Model]);

    [Fact]
    public void ALaunchNobodyPrepared_IsRefused_NeverBuiltUnconfined()
    {
        // ReviewerSettings.ClaudeCli defaults to Unprobed. A new call site that builds a claude consultant without
        // PrepareAsync must fail in its first test — not ship an argv without --restricted for a claude that has it.
        var unprepared = () => Argv(ClaudeCapability.Unprobed);

        unprepared.Should().Throw<ArgumentException>().WithMessage("*PrepareAsync*");
    }

    [Fact]
    public void ALaunchWhoseCapabilityIsUnknown_IsRefused_TooAsAContractViolation()
    {
        // PrepareAsync refuses the TURN on Unknown; reaching Build with it means a caller skipped that refusal.
        var unknown = () => Argv(ClaudeCapability.Unknown("claude --help exited 1"));

        unknown.Should().Throw<ArgumentException>();
    }
}
