using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// S2 of <c>research/PLAN_a_reviewer_is_not_sent_more_than_it_can_read.md</c> (D3, D4, D5), at the roster: a reviewer whose first
/// prompt is larger than its row's <c>maxPromptKib</c> is not built, a dealt lens goes only to a row whose cap fits it, and the
/// roster says which row was left out, with the size, the cap and the cure.
/// </summary>
/// <remarks>
/// <para>The size is counted by the SAME helper that composes the prompt the reviewer is sent
/// (<c>RosterBuilder.FirstPrompt</c>), so the number a cap is compared with is the number <c>ReviewerWork.PromptBytes</c> says. Every
/// size below is read back from an uncapped row of the same round — never typed — so a change to the reviewer prompt moves the
/// tests with it instead of turning them green over an empty comparison.</para>
/// </remarks>
public sealed class ARowIsOnlyGivenAPromptItCanReadTests : IDisposable
{
    private const string Requirements = "Requirements";
    private const string Brief = "Brief";

    /// <summary>The three lenses of the plan role the deal walks — every one has a prompt file, so none is skipped for want of text.</summary>
    private static readonly string[] Lenses = ["brief-a", "brief-b", "brief-c"];

    private readonly TempDir _data = TempDir.For("coai-prompt-cap-roster-");

    public void Dispose() => _data.Dispose();

    /// <summary>Two rows on one runtime, so one prompt is composed for both — only the cap tells them apart.</summary>
    private static ProviderSettings Row(string id, int capKib = 0) => new(id) { Enabled = true, Runtime = "codex", PromptCap = CoaiMcp.Core.Catalog.PromptCap.OfKib(capKib) };

    private PanelService ServiceOf(RoleCatalog catalog, params ProviderSettings[] rows)
    {
        Directory.CreateDirectory(Path.Combine(_data, "prompts"));
        foreach (var lens in (string[])["req-a", .. Lenses])
        {
            File.WriteAllText(Path.Combine(_data, "prompts", $"{lens}.md"), "Whether the requirement is met.");
        }

        return new PanelService(
            new PanelSettings
            {
                DataDir = _data,
                CodeWorkspace = "none",
                Rounds = new PanelConfig(Roles: null, StagePolicy.Human) { Catalog = catalog },
                Providers = rows,
            },
            VaultKeys.None("no vault"), default, new Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None, Noticing.None);
    }

    /// <summary>One result role with one prompt (the code stage's roster) and one plan role with three lenses (the plan stage's deal).</summary>
    private static RoleCatalog Catalog() =>
        RoleComposition.Compose([
            new RoleEntry(Requirements, Name: "Requirements we wrote", Stage: RoleStages.Result,
                Prompts: [new PromptEntry("req-a", "General", "Whether the requirement is met.")]),
            new RoleEntry(Brief, Name: "The brief", Stage: RoleStages.Plan,
                Prompts: [.. Lenses.Select(id => new PromptEntry(id, id, "What the brief says."))]),
        ]);

    private static string Scratch() => Directory.CreateTempSubdirectory("coai-prompt-cap-scratch-").FullName;

    /// <summary>A plan round's roster for one role, over a context of the given size — the one <c>BuildWork</c> every plan test here shares.</summary>
    private static RoundWork PlanRoster(PanelService service, int contextBytes) =>
        service.Roster.BuildWork(
            [RoleCatalog.ArchitectureRole], string.Empty, new string('x', contextBytes), round: 1, stage: Stage.PlanReview,
            readsCheckout: false, codexTiers: CodexTiers.None);

    private static RoundWork CodeRoster(PanelService service, int contextBytes, bool deal = false) =>
        service.Roster.BuildWork(
            [Requirements], Scratch(), new string('x', contextBytes), round: 1, stage: Stage.CodeReview, readsCheckout: true,
            codexTiers: CodexTiers.None, deal: deal);

    /// <summary>The plan stage with its lenses dealt: three lenses of one role across the rows that can read them.</summary>
    private static RoundWork DealtPlanRoster(PanelService service, int contextBytes, int seed) =>
        service.Roster.BuildWork(
            [Brief], string.Empty, new string('x', contextBytes), round: 1, stage: Stage.PlanReview, readsCheckout: false,
            codexTiers: CodexTiers.None, seed: seed, planPrompts: Lenses, deal: true);

    /// <summary>What an UNCAPPED row's first prompt measures for a context of this size — read back from the work, never guessed.</summary>
    private int BytesOf(int contextBytes)
    {
        var bytes = PlanRoster(ServiceOf(Catalog(), Row("uncapped")), contextBytes).Reviewers.Single().PromptBytes;
        bytes.Should().HaveValue("the work records the size of the first prompt it was built with");

        return bytes.GetValueOrDefault();
    }

    private int ContextForExactKib()
    {
        const int start = 5000;
        var padding = (1024 - BytesOf(start) % 1024) % 1024;
        var context = start + padding;
        BytesOf(context).Should().Be(BytesOf(start) + padding, "the fixture: the prompt grows exactly with the context, one byte per byte");
        (BytesOf(context) % 1024).Should().Be(0, "the fixture: a prompt of exactly N KiB, so the cap can sit exactly on it");

        return context;
    }

    [Fact]
    public void TheBinaryListsTheField_NowThatTheCapIsHonoured()
    {
        FeaturesMode.Listed.Should().Contain("maxPromptKib");
    }

    [Fact]
    public void ARowCappedUnderItsPrompt_IsNotBuilt_AndTheRoundSaysTheSizeTheCapAndTheCure()
    {
        var service = ServiceOf(Catalog(), Row("alpha", capKib: 1), Row("beta"));
        var bytes = BytesOf(5000);
        bytes.Should().BeGreaterThan(1024, "the fixture: this prompt really is over the 1 KiB cap");

        var work = PlanRoster(service, 5000);

        work.Reviewers.Should().ContainSingle().Which.Invocation.Provider.Should().Be("beta", "the capped row is never launched");
        var left = work.Excluded.Should().ContainSingle().Subject;
        left.Provider.Should().Be("alpha");
        left.Role.Should().Be(RoleCatalog.ArchitectureRole);
        left.Reason.Should().Contain($"its prompt is {bytes:N0} bytes", "the size, as the round audit counts it")
            .And.Contain("this row reads at most 1,024 bytes (maxPromptKib 1)", "the cap, in bytes - said as bytes - and as the operator typed it")
            .And.Contain("send the plan in parts (one epic per round), or raise the row's cap", "the cure");
    }

    [Fact]
    public void APromptExactlyAtTheCap_IsBuilt_AndOneByteOverIsNot()
    {
        var context = ContextForExactKib();
        var kib = BytesOf(context) / 1024;

        var at = PlanRoster(ServiceOf(Catalog(), Row("capped", kib)), context);
        var over = PlanRoster(ServiceOf(Catalog(), Row("capped", kib)), context + 1);

        at.Reviewers.Should().ContainSingle("the cap is inclusive: a prompt of exactly maxPromptKib × 1024 bytes is read");
        at.Excluded.Should().BeEmpty();
        over.Reviewers.Should().BeEmpty("one byte over the cap is over it");
        over.Excluded.Should().ContainSingle();
    }

    [Fact]
    public void ACapRoomyEnoughForThePrompt_ChangesNothingAboutTheWork()
    {
        var plain = PlanRoster(ServiceOf(Catalog(), Row("alpha"), Row("beta")), 5000).Reviewers;
        var roomy = PlanRoster(ServiceOf(Catalog(), Row("alpha", capKib: 4096), Row("beta", capKib: 4096)), 5000).Reviewers;

        roomy.Select(w => (w.Invocation.Provider, w.Invocation.Role, w.Prompt, w.PromptBytes))
            .Should().Equal(plain.Select(w => (w.Invocation.Provider, w.Invocation.Role, w.Prompt, w.PromptBytes)));
    }

    [Fact]
    public void ACodeRoundLeavesAnOverCapRowOutToo()
    {
        var work = CodeRoster(ServiceOf(Catalog(), Row("alpha", capKib: 1), Row("beta")), 5000);

        work.Reviewers.Select(w => w.Invocation.Provider).Distinct().Should().Equal("beta");
        work.Excluded.Should().ContainSingle().Which.Reason.Should().Contain("send a smaller change, or raise the row's cap");
    }

    /// <summary>
    /// A document round's cure is the document stage's own: a too-large document is "reviewed in parts" (<c>DocumentRules</c>),
    /// so the sentence says that, not the generic "send less".
    /// </summary>
    [Fact]
    public void ADocumentRoundSaysToSendTheDocumentInParts()
    {
        var work = ServiceOf(Catalog(), Row("alpha", capKib: 1), Row("beta")).Roster.BuildWork(
            [RoleCatalog.ArchitectureRole], string.Empty, new string('x', 5000), round: 1, stage: Stage.DocumentReview,
            readsCheckout: false, codexTiers: CodexTiers.None);

        work.Excluded.Should().ContainSingle().Which.Reason.Should()
            .Contain("send the document in parts, or raise the row's cap");
    }

    /// <summary>
    /// Dealing first and excluding in the leaf is the defect this code already fixed once: it cost a custom role its whole round
    /// (<c>TheRoundSaysWhatItCouldNotAskTests</c>). A lens handed to a row that cannot read it is a lens nobody asked, so the
    /// deal is made among the rows that CAN — walked over ten seeds, because which row gets which lens is the seed's business.
    /// </summary>
    [Fact]
    public void EveryLensIsAskedOfTheRowThatFits_OnEverySeed_AndTheCappedRowIsNeverLaunched()
    {
        var service = ServiceOf(Catalog(), Row("alpha"), Row("beta", capKib: 1));

        foreach (var seed in Enumerable.Range(0, 10))
        {
            var work = DealtPlanRoster(service, 5000, seed);

            work.Reviewers.Select(w => w.Prompt).Order().Should().Equal(Lenses, $"seed {seed}: all three lenses are asked");
            work.Reviewers.Should().OnlyContain(w => w.Invocation.Provider == "alpha", $"seed {seed}: only the row that can read them");
            work.Excluded.Should().BeEmpty($"seed {seed}: the capped row was never dealt anything, so there is nothing to say it left out");
        }
    }

    /// <summary>
    /// The carrier test measures a prompt BEFORE the leaf asks whether the prompt has any text — and a prompt with no text is
    /// not a size, it is a sentence the leaf says (<c>TheRoundSaysWhatItCouldNotAskTests</c>). Composing it for the measure threw
    /// out of the whole roster the moment one row had a cap and one dealt lens had never been written.
    /// </summary>
    [Fact]
    public void ADealtLensWithNoPromptText_IsSaidAsNotAsked_WhenARowHasACap_AndTheRoundIsStillBuilt()
    {
        const string unwritten = "brief-unwritten";
        var catalog = RoleComposition.Compose([
            new RoleEntry(Brief, Name: "The brief", Stage: RoleStages.Plan,
                Prompts: [.. ((string[])[.. Lenses, unwritten]).Select(id => new PromptEntry(id, id, "What the brief says."))]),
        ]);
        var service = ServiceOf(catalog, Row("alpha"), Row("beta", capKib: 1));

        var work = service.Roster.BuildWork(
            [Brief], string.Empty, new string('x', 5000), round: 1, stage: Stage.PlanReview, readsCheckout: false,
            codexTiers: CodexTiers.None, seed: 0, planPrompts: [.. Lenses, unwritten], deal: true);

        work.NotAsked.Should().ContainSingle().Which.Reason.Should().Contain(unwritten).And.Contain("no text");
        work.Reviewers.Select(w => w.Prompt).Order().Should().Equal(Lenses, "every lens that has text is still asked");
        work.Reviewers.Should().OnlyContain(w => w.Invocation.Provider == "alpha", "only the row whose cap fits");
    }

    [Fact]
    public void WhenEveryRowIsCappedUnderThePrompt_NoLensSilentlyDisappears_EachRowAndRoleIsNamed()
    {
        var service = ServiceOf(Catalog(), Row("alpha", capKib: 1), Row("beta", capKib: 1));

        var work = CodeRoster(service, 5000, deal: true);

        work.Reviewers.Should().BeEmpty();
        work.Excluded.Select(e => (e.Provider, e.Role)).Should().BeEquivalentTo([("alpha", Requirements), ("beta", Requirements)]);
        work.Excluded.Should().OnlyContain(e => e.Reason.Contains("maxPromptKib 1") && e.Reason.Contains("raise the row's cap"));
        var refusal = service.NoReviewerRefusal(Stage.CodeReview, work);
        refusal.Should().Contain("could not go to alpha").And.Contain("could not go to beta").And.Contain("maxPromptKib 1");
    }
}
