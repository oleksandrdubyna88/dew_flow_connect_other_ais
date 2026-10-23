using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The Team server's claude reviewer starts in the CLI's own safe mode too — <c>--safe-mode</c>, so a prompt-injected job cannot be steered by a
/// <c>CLAUDE.md</c> or hook that reached the slot's configuration — when the CLI on THIS box declares the flag, asked fresh for every job
/// (todo/PLAN_codex_agy_and_claude_reviewers_get_what_kimi_got.md, E5.S2; risk consultation 958e430d). The local twin is
/// <c>ARealReviewFromNormalSettingsCarriesSafeModeTests</c>.
/// </summary>
/// <remarks>
/// <para>The probe is a launch like the review: it goes through the same <c>Shaped</c> request — <see cref="Confinement"/>, the slot's own
/// <c>HOME</c>, the job's folder as <c>TMPDIR</c>/<c>TMP</c>/<c>TEMP</c> — and the job's cancellation, never this server's whole environment.</para>
/// <para>A claude that does not declare the flag, or whose help does not answer, is run WITHOUT it and never refused; the server has no per-launch warning
/// channel, so POST_DEPLOY.md names the one-line check that the box's claude declares it.</para>
/// </remarks>
public sealed class TheTeamClaudeReviewerRunsInSafeModeTests
{
    private const string Envelope = """{"type":"result","subtype":"success","is_error":false,"result":"{\"findings\":[]}"}""";

    private const string HelpDeclaring =
        "Usage: claude [options]\n\nOptions:\n  -p, --print                           Print response and exit\n  --safe-mode                           Start with all customizations\n                                        disabled.\n";

    private const string HelpWithout =
        "Usage: claude [options]\n\nOptions:\n  -p, --print                           Print response and exit\n";

    /// <summary>A claude that prints <see cref="Help"/> for a bare <c>--help</c> and the envelope for anything else, writing down every request and its token.</summary>
    private sealed class Claude(string help, int helpExit = 0) : IProcessLauncher
    {
        public string Help { get; set; } = help;

        public List<ProcessRequest> Probes { get; } = [];

        public List<ProcessRequest> Reviews { get; } = [];

        public List<CancellationToken> ProbeTokens { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            if (request.Arguments is ["--help"])
            {
                Probes.Add(request);
                ProbeTokens.Add(ct);

                return Task.FromResult(new ProcessResult(helpExit, Help, string.Empty, TimedOut: false));
            }

            Reviews.Add(request);

            return Task.FromResult(new ProcessResult(0, Envelope, string.Empty, TimedOut: false));
        }
    }

    private static Task<ReviewAttempt> Run(IProcessLauncher launcher, string runtime = "claude", IReadOnlyDictionary<string, string?>? slot = null, CancellationToken ct = default)
    {
        var account = new AccountSlot(runtime, "a", Path.GetTempPath(), DateTimeOffset.UtcNow, null, false, string.Empty, 0);
        var job = new JobRecord(
            JobId.New(), "dev@example.com", runtime, runtime == "claude" ? "haiku" : "gpt-6", "PlanCritique", "review this",
            JobStatus.Running, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), TimeSpan.FromSeconds(60));

        return new ReviewLauncher(launcher).RunAsync(
            new VendorConfig(runtime, runtime, [job.Model], ["a"]), account, job, slot ?? new Dictionary<string, string?>(), ct);
    }

    [Fact]
    public async Task ARealTeamReview_FromNormalSettings_CarriesSafeMode()
    {
        var claude = new Claude(HelpDeclaring);

        var attempt = await Run(claude);

        attempt.Should().BeOfType<ReviewAttempt.Answered>();
        claude.Probes.Should().ContainSingle("the job asks the box's claude for its help before it builds the launch");
        claude.Reviews.Should().ContainSingle().Which.Arguments.Should().Contain("--safe-mode")
            .And.Contain("--disallowedTools", "the confined job keeps every other flag it had");
    }

    [Fact]
    public async Task AClaudeWithoutTheFlag_StillRuns_WithoutIt()
    {
        var claude = new Claude(HelpWithout);

        var attempt = await Run(claude);

        attempt.Should().BeOfType<ReviewAttempt.Answered>("a missing flag is never a refusal");
        claude.Reviews.Should().ContainSingle().Which.Arguments.Should().NotContain("--safe-mode");
    }

    [Fact]
    public async Task AHelpThatDoesNotAnswer_IsUnknown_RunsWithoutTheFlag_AndNeverRetriesWithAnother()
    {
        var claude = new Claude(string.Empty, helpExit: 1);

        var attempt = await Run(claude);

        attempt.Should().BeOfType<ReviewAttempt.Answered>();
        claude.Probes.Should().HaveCount(2, "asked once more, then given up on — the capability probe's own budget");
        claude.Reviews.Should().ContainSingle("one launch: an unknown capability is never a second launch, with or without the flag")
            .Which.Arguments.Should().NotContain("--safe-mode");
    }

    [Fact]
    public async Task ASwappedCliBetweenJobs_IsReadAgain()
    {
        var claude = new Claude(HelpDeclaring);
        await Run(claude);

        claude.Help = HelpWithout;
        await Run(claude);

        claude.Probes.Should().HaveCount(2, "no cache: every job asks");
        claude.Reviews.Select(review => review.Arguments.Contains("--safe-mode")).Should().Equal([true, false]);
    }

    [Fact]
    public async Task TheTeamProbeRunsUnderTheJobsConfinement()
    {
        var slot = SlotEnvironment.For("claude", Path.Combine(Path.GetTempPath(), "testsToDelete-slot-a"), token: "not-a-real-token");
        using var cancellation = new CancellationTokenSource();
        var claude = new Claude(HelpDeclaring);

        await Run(claude, slot: slot, ct: cancellation.Token);

        var probe = claude.Probes.Should().ContainSingle().Subject;
        var review = claude.Reviews.Should().ContainSingle().Subject;
        probe.InheritsEnvironment.Should().BeFalse("the probe is confined like the review: it never starts from this server's whole environment");
        foreach (var (name, value) in slot)
        {
            probe.Environment.Should().ContainKey(name, "the slot's own HOME and configuration, so the help is the one that account's CLI prints").WhoseValue.Should().Be(value);
        }

        probe.WorkingDirectory.Should().Be(review.WorkingDirectory, "the job's own folder, never the server's");
        new[] { "TMPDIR", "TMP", "TEMP" }.Should().OnlyContain(name => probe.Environment[name] == probe.WorkingDirectory, "the job's folder is its temporary one, the probe's too");
        probe.Timeout.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(10));
        claude.ProbeTokens.Should().ContainSingle().Which.Should().Be(cancellation.Token, "the job's cancellation reaches the probe");
    }

    [Fact]
    public async Task ACodexJob_IsNeverAskedForClaudesHelp()
    {
        var claude = new Claude(HelpDeclaring);

        await Run(claude, runtime: "codex");

        claude.Probes.Should().BeEmpty("only a claude job spends a spawn on the help");
    }
}
