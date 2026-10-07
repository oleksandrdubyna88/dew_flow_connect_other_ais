using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The confinement planner is the ONE place a question row's sandbox flags are decided, and every
/// adapter composes them rather than choosing its own (PLAN_question_consultant.md, D4; S1 acceptance 2).
/// </summary>
/// <remarks>
/// <para>Every fragment below is a fact from the probe run of 2026-10-01 (F1–F7 of the plan, cited by
/// row in <c>shared/runtime-capabilities.json</c>): claude's <c>--tools</c> allow-list and
/// <c>--restricted</c>, codex's top-level <c>--search</c>, agy's <c>--mode plan</c>. The goldens are
/// asserted as WHOLE argv lists, so an adapter that adds a sandbox flag of its own — or drops one the
/// planner gave it — fails here rather than reading past its roots with nothing in the output to say so.</para>
/// <para><c>--disallowedTools</c> appears in no plan and no planned launch: the shipped deny lists are
/// offered the user environment's whole tool set and leaked the canary 6 of 6 (F5).</para>
/// </remarks>
public sealed class ConfinementPlannerTests
{
    private const string RootA = "D:/projects/alpha";
    private const string RootB = "D:/projects/beta";

    private static Confinement.Planned Plan(string runtime, CapabilityGrant grant) =>
        ConfinementPlanner.Plan(runtime, grant).Should().BeOfType<Confinement.Planned>().Which;

    // ---------- the planner's fragments, per runtime × capability ----------

    [Fact]
    public void Claude_None_OffersNoToolAtAll_InAScratchCwd()
    {
        var plan = Plan("claude", CapabilityGrant.None);

        plan.Leading.Should().BeEmpty();
        plan.Flags.Should().Equal("--permission-mode", "plan", "--tools", "");
        plan.Cwd.Should().Be(CwdKind.Scratch);
        plan.AddDirs.Should().BeEmpty();
        plan.Flag.Should().Be(AdmissionFlag.None);
    }

    [Fact]
    public void Claude_Disk_IsRestricted_WithTheThreeReadTools_AndEveryRootAdded()
    {
        var plan = Plan("claude", CapabilityGrant.Disk(RootA, RootB));

        plan.Flags.Should().Equal(
            "--permission-mode", "plan", "--restricted", "--tools", "Read,Glob,Grep",
            "--add-dir", RootA, "--add-dir", RootB);
        plan.Cwd.Should().Be(CwdKind.Root, "the first root is the working directory (F4: --restricted confines to the working directories)");
        plan.AddDirs.Should().Equal(RootA, RootB);
        plan.Flags.Should().NotContain("Bash").And.NotContain("PowerShell", "no shell tool is offered to a disk row");
    }

    [Fact]
    public void Claude_Web_OffersTheTwoWebTools_AndNoDirectory()
    {
        var plan = Plan("claude", CapabilityGrant.Web);

        plan.Flags.Should().Equal("--permission-mode", "plan", "--tools", "WebSearch,WebFetch");
        plan.Flags.Should().NotContain("--add-dir", "a web row has no directory to read (A2)");
        plan.Flags.Should().NotContain("--restricted", "nothing file-capable is offered, so there is nothing to restrict");
        plan.Cwd.Should().Be(CwdKind.Scratch);
    }

    [Fact]
    public void Codex_EveryCapability_IsReadOnlyEphemeral_AndFlaggedUnconfined()
    {
        var none = Plan("codex", CapabilityGrant.None);
        none.Leading.Should().BeEmpty();
        none.Flags.Should().Equal("-s", "read-only", "--ephemeral");
        none.Cwd.Should().Be(CwdKind.Scratch);

        var disk = Plan("codex", CapabilityGrant.Disk(RootA));
        disk.Flags.Should().Equal("-s", "read-only", "--ephemeral", "-C", RootA);
        disk.Cwd.Should().Be(CwdKind.Root);

        var web = Plan("codex", CapabilityGrant.Web);
        // The expectation is an ARRAY: `Equal("--search", "because…")` would assert the reason as a
        // second expected item — the trap ConsultantArgvTests already names.
        web.Leading.Should().Equal(["--search"], "F1: `--search` is a TOP-LEVEL flag; `codex exec --search` is a usage error (exit 2)");
        web.Flags.Should().Equal("-s", "read-only", "--ephemeral");
        web.Cwd.Should().Be(CwdKind.Scratch);

        foreach (var plan in (Confinement.Planned[])[none, disk, web])
        {
            plan.Flag.Should().Be(AdmissionFlag.Unconfined, "F2: codex reads anywhere and no flag removes the shell (D13)");
        }
    }

    [Fact]
    public void Antigravity_None_IsPlanMode_AndDisk_AddsTheRoots_BothFlaggedDefaultDeny()
    {
        var none = Plan("antigravity", CapabilityGrant.None);
        none.Flags.Should().Equal("--mode", "plan");
        none.Cwd.Should().Be(CwdKind.Scratch);
        none.Flag.Should().Be(AdmissionFlag.DefaultDeny);

        var disk = Plan("antigravity", CapabilityGrant.Disk(RootA, RootB));
        disk.Flags.Should().Equal("--mode", "plan", "--add-dir", RootA, "--add-dir", RootB);
        disk.Cwd.Should().Be(CwdKind.Root);
        disk.Flag.Should().Be(AdmissionFlag.DefaultDeny, "F7: held by the headless default alone");
    }

    [Fact]
    public void LocalAndApi_None_HaveNoFlagToSend_AndAScratchCwd()
    {
        foreach (var runtime in (string[])["local", "api"])
        {
            var plan = Plan(runtime, CapabilityGrant.None);

            plan.Leading.Should().BeEmpty(runtime);
            plan.Flags.Should().BeEmpty($"{runtime}: a completion has no sandbox flag — it has no tools");
            plan.Cwd.Should().Be(CwdKind.Scratch, runtime);
            plan.Flag.Should().Be(AdmissionFlag.None, runtime);
        }
    }

    // ---------- refusals ----------

    [Theory]
    [InlineData("antigravity", Capability.Web, "cannot fetch a page headless")]
    [InlineData("api", Capability.Disk, "cannot do 'disk'")]
    [InlineData("api", Capability.Web, "not been measured")]
    [InlineData("local", Capability.Disk, "cannot do 'disk'")]
    [InlineData("local", Capability.Web, "cannot do 'web'")]
    public void APairTheMatrixRefuses_IsRefusedByThePlannerToo_WithTheMatrixsOwnSentence(string runtime, Capability capability, string says)
    {
        var grant = capability == Capability.Disk ? CapabilityGrant.Disk(RootA) : new CapabilityGrant(capability, []);

        ConfinementPlanner.Plan(runtime, grant).Should().BeOfType<Confinement.Refused>()
            .Which.Reason.Should().Contain(says, "the planner asks the matrix first and never re-decides it");
    }

    [Fact]
    public void AnUnknownRuntime_IsRefused_NamingTheOnesItKnows()
    {
        ConfinementPlanner.Plan("gemini", CapabilityGrant.None).Should().BeOfType<Confinement.Refused>()
            .Which.Reason.Should().Contain("gemini").And.Contain("claude, codex, antigravity, local, api");
    }

    [Fact]
    public void ADiskGrantWithNoRoot_IsRefused_AndARootOnANoneOrWebGrant_IsRefused()
    {
        ConfinementPlanner.Plan("claude", new CapabilityGrant(Capability.Disk, [])).Should().BeOfType<Confinement.Refused>()
            .Which.Reason.Should().Contain("at least one root", "a disk row with nowhere to read would read its cwd — which is nowhere");
        ConfinementPlanner.Plan("claude", new CapabilityGrant(Capability.None, [RootA])).Should().BeOfType<Confinement.Refused>()
            .Which.Reason.Should().Contain("carries no roots", "a root on a none row is a leak path");
        ConfinementPlanner.Plan("claude", new CapabilityGrant(Capability.Web, [RootA])).Should().BeOfType<Confinement.Refused>()
            .Which.Reason.Should().Contain("carries no roots", "a web row is given the question and nothing of this machine (A2)");
    }

    [Theory]
    [InlineData("projects/alpha")]
    [InlineData("./alpha")]
    [InlineData("")]
    [InlineData("  ")]
    public void ARootThatIsNotAnAbsolutePath_IsRefused(string root)
    {
        ConfinementPlanner.Plan("claude", CapabilityGrant.Disk(root)).Should().BeOfType<Confinement.Refused>()
            .Which.Reason.Should().Contain("absolute", "a relative root resolves against whatever the cwd happens to be");
    }

    [Fact]
    public void DisallowedTools_AppearsInNoPlan()
    {
        // F3/F5: the shipped deny lists leaked the canary 6 of 6 through PowerShell, which no deny
        // list names. A question row is confined by an ALLOW list or by `--restricted`, never by naming
        // what it may not do.
        foreach (var runtime in RuntimeCapabilities.Runtimes)
        {
            foreach (var grant in (CapabilityGrant[])[CapabilityGrant.None, CapabilityGrant.Disk(RootA), CapabilityGrant.Web])
            {
                if (ConfinementPlanner.Plan(runtime, grant) is Confinement.Planned plan)
                {
                    plan.Flags.Concat(plan.Leading).Should().NotContain(ConfinementPlanner.NeverSent, $"{runtime} × {grant.Capability}");
                }
            }
        }
    }

    // ---------- the adapters compose the plan and nothing of their own ----------

    private static readonly string Scratch = Path.Combine(Path.GetTempPath(), "coai-scratch-" + Guid.NewGuid().ToString("N"));

    private static readonly string Answers = Path.Combine(Path.GetTempPath(), "coai-answers-" + Guid.NewGuid().ToString("N"));

    private const string Repo = "D:/rsd/some-checkout";

    private static ConsultantLaunch Planned(string runtime, CapabilityGrant grant, string model = "") => new(
        Repo, "the question", string.Empty, Answers, new ReviewerSettings(runtime) { Model = model })
    {
        Confinement = new LaunchConfinement.Planned(Plan(runtime, grant)),
        ScratchDir = Scratch,
    };

    [Fact]
    public void AClaudeQuestionLaunch_IsTheBaseAndThePlan_AndNothingElse()
    {
        var none = new ClaudeConsultant(new ClaudeRuntime()).Build(Planned("claude", CapabilityGrant.None, "sonnet"));
        none.Request.Arguments.Should().Equal(
            "-p", "--output-format", "json", "--permission-mode", "plan", "--tools", "", NoMcpServers.ClaudeFlag, "--model", "sonnet");
        none.Request.WorkingDirectory.Should().Be(Scratch, "a none row stands in an empty directory, never the checkout");
        none.Role.Should().Be(ConsultantRoles.Question);

        var disk = new ClaudeConsultant(new ClaudeRuntime()).Build(Planned("claude", CapabilityGrant.Disk(RootA, RootB)));
        disk.Request.Arguments.Should().Equal(
            "-p", "--output-format", "json", "--permission-mode", "plan", "--restricted", "--tools", "Read,Glob,Grep",
            "--add-dir", RootA, "--add-dir", RootB, NoMcpServers.ClaudeFlag);
        disk.Request.WorkingDirectory.Should().Be(RootA);

        var web = new ClaudeConsultant(new ClaudeRuntime()).Build(Planned("claude", CapabilityGrant.Web));
        web.Request.Arguments.Should().Equal(
            "-p", "--output-format", "json", "--permission-mode", "plan", "--tools", "WebSearch,WebFetch", NoMcpServers.ClaudeFlag);
        web.Request.Arguments.Should().NotContain("--add-dir").And.NotContain(Repo, "a web row sees nothing of the checkout");
        web.Request.WorkingDirectory.Should().Be(Scratch);
    }

    [Fact]
    public void ACodexQuestionLaunch_PutsSearchBeforeExec_AndStaysEphemeral()
    {
        var web = new CodexConsultant(new CodexRuntime()).Build(Planned("codex", CapabilityGrant.Web, "gpt-6-astra"));
        var args = web.Request.Arguments.ToList();
        var output = args[args.IndexOf("-o") + 1];

        args.Should().Equal(
            "--search", "exec", "-s", "read-only", "--ephemeral", "--skip-git-repo-check", "--color", "never",
            // The row's fast mode, Off by default (todo/PLAN_fast_mode.md), before the instructions on stdin.
            "--json", "-o", output, "-m", "gpt-6-astra", "-c", "service_tier=default", "-");
        Path.GetDirectoryName(Path.GetFullPath(output)).Should().Be(Path.GetFullPath(Answers));
        web.Request.WorkingDirectory.Should().Be(Scratch);

        var none = new CodexConsultant(new CodexRuntime()).Build(Planned("codex", CapabilityGrant.None));
        none.Request.Arguments.Should().StartWith(["exec", "-s", "read-only", "--ephemeral", "--skip-git-repo-check"])
            .And.NotContain("--search").And.NotContain("-C");

        var disk = new CodexConsultant(new CodexRuntime()).Build(Planned("codex", CapabilityGrant.Disk(RootA)));
        disk.Request.Arguments.Should().StartWith(["exec", "-s", "read-only", "--ephemeral", "-C", RootA]);
        disk.Request.WorkingDirectory.Should().Be(RootA);
    }

    [Fact]
    public void AnAgyQuestionLaunch_IsTheMeasuredStreamLaunch_WithThePlansFlags()
    {
        var none = new AntigravityConsultant(new AntigravityRuntime()).Build(Planned("antigravity", CapabilityGrant.None, "gemini-3.1-pro-high"));
        none.Request.Arguments.Should().Equal(
            "--print=", "--input-format", "stream-json", "--output-format", "stream-json", "--mode", "plan", "--model", "gemini-3.1-pro-high");
        none.Request.WorkingDirectory.Should().Be(Scratch);
        none.Request.StdIn.Should().Contain("the question").And.StartWith("{", "the prompt rides the stream, serialised");

        var disk = new AntigravityConsultant(new AntigravityRuntime()).Build(Planned("antigravity", CapabilityGrant.Disk(RootA)));
        disk.Request.Arguments.Should().Equal(
            "--print=", "--input-format", "stream-json", "--output-format", "stream-json", "--mode", "plan", "--add-dir", RootA);
        disk.Request.WorkingDirectory.Should().Be(RootA);
    }

    [Fact]
    public void ALocalQuestionLaunch_RunsTheShimInTheScratchDirectory()
    {
        Directory.CreateDirectory(Answers);
        var schema = Path.Combine(Answers, "schema.json");
        File.WriteAllText(schema, "{}");
        var launch = Planned("local", CapabilityGrant.None) with { AnswerSchemaFile = schema };

        var built = new LocalConsultant(new LocalRuntime("local", "http://127.0.0.1:11434/v1"), "local").Build(launch);

        built.Request.WorkingDirectory.Should().Be(Scratch, "a none row stands nowhere near the checkout");
        built.Request.Arguments.Should().Contain("--ask-local");
        built.Role.Should().Be(ConsultantRoles.Question);
    }

    /// <summary>The structural half of acceptance 2: whatever a planned launch sends of the sandbox vocabulary, the plan gave it.</summary>
    [Fact]
    public void NoAdapter_SendsASandboxFlag_ThePlanDidNotGiveIt()
    {
        foreach (var (runtime, consultant) in Adapters())
        {
            foreach (var grant in (CapabilityGrant[])[CapabilityGrant.None, CapabilityGrant.Disk(RootA), CapabilityGrant.Web])
            {
                if (ConfinementPlanner.Plan(runtime, grant) is not Confinement.Planned plan)
                {
                    continue;
                }

                var argv = consultant.Build(Planned(runtime, grant)).Request.Arguments.ToList();
                var ownFlags = argv.Except(plan.Flags.Concat(plan.Leading)).Where(ConfinementPlanner.IsSandboxVocabulary);

                ownFlags.Should().BeEmpty($"{runtime} × {grant.Capability}: the planner is the one place");
            }
        }
    }

    [Fact]
    public void APlannedLaunch_IsOneShot_SoAHandleIsAContractViolation()
    {
        var resumed = Planned("claude", CapabilityGrant.None) with { Handle = "67289235-65f7-40b5-9532-e63515d90f30" };

        var build = () => new ClaudeConsultant(new ClaudeRuntime()).Build(resumed);

        build.Should().Throw<ArgumentException>().WithMessage("*one-shot*");
    }

    [Fact]
    public void AScratchCwd_NeedsAScratchDirectory_OrTheLaunchIsRefused()
    {
        var nowhere = Planned("claude", CapabilityGrant.None) with { ScratchDir = string.Empty };

        var build = () => new ClaudeConsultant(new ClaudeRuntime()).Build(nowhere);

        build.Should().Throw<ArgumentException>().WithMessage("*ScratchDir*",
            "falling back to the checkout would hand a none row the very directory it must not see");
    }

    /// <summary>The regression guard (acceptance 5): today's argv, byte for byte, with the defaulted confinement.</summary>
    /// <remarks>
    /// The claude row moved when PLAN_the_consultant_works_on_every_vendor.md merged second, as that plan's §7 said it
    /// must: the stuck consultant is confined by the planner's own disk allowlist (<see cref="ConfinementPlanner.ClaudeDiskTools"/>)
    /// plus <c>--restricted</c> where the installed CLI declares it — never the deny list. Its launch is PREPARED first
    /// (<c>IConsultantRuntime.PrepareAsync</c> reads the CLI's help), which is what <c>ClaudeCli</c> stands for here;
    /// the CLI without the flag is <c>ClaudeConsultantArgvTests</c>' business.
    /// </remarks>
    [Fact]
    public void TheShippedConsultants_StillBuildTodaysArgv_ByteForByte()
    {
        var launch = new ConsultantLaunch(Repo, "help me", string.Empty, Answers, new ReviewerSettings("v") { Model = "m", ClaudeCli = ClaudeCapability.WithRestricted });
        launch.Confinement.Should().Be(LaunchConfinement.AsShipped, "the default is the stuck consultant as it ships");

        new ClaudeConsultant(new ClaudeRuntime()).Build(launch).Request.Arguments.Should().Equal(
            "-p", "--output-format", "json", "--permission-mode", "plan",
            "--restricted", "--tools", string.Join(',', ConfinementPlanner.ClaudeDiskTools),
            NoMcpServers.ClaudeFlag, "--add-dir", Repo, "--model", "m");

        var codex = new CodexConsultant(new CodexRuntime()).Build(launch).Request.Arguments.ToList();
        codex.Should().Equal(
            "exec", "-s", "read-only", "--skip-git-repo-check", "--color", "never", "-C", Repo, "--json", "-o", codex[codex.IndexOf("-o") + 1], "-m", "m",
            "-c", "service_tier=default", "-");

        new AntigravityConsultant(new AntigravityRuntime()).Build(launch).Request.Arguments.Should().Equal(
            "--print=", "--input-format", "stream-json", "--output-format", "stream-json", "--mode", "plan", "--model", "m", "--add-dir", Repo);

        foreach (var (_, consultant) in Adapters().Where(a => a.Runtime != "local"))
        {
            consultant.Build(launch).Request.WorkingDirectory.Should().Be(Repo, consultant.Vendor);
            consultant.Build(launch).Role.Should().Be(ConsultantRoles.Consult, consultant.Vendor);
        }
    }

    private static IEnumerable<(string Runtime, IConsultantRuntime Consultant)> Adapters() =>
    [
        ("claude", new ClaudeConsultant(new ClaudeRuntime())),
        ("codex", new CodexConsultant(new CodexRuntime())),
        ("antigravity", new AntigravityConsultant(new AntigravityRuntime())),
    ];
}
