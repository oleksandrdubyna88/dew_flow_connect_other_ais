using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a Kimi CONSULTANT launch is (E5.4 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.4.1–S5.4.6): the reviewer's own shim request,
/// composed from <see cref="KimiRuntime"/> and never a second builder (decision 1) — standing in the conversation's snapshot on the conversation's
/// home (decision 2), continued with <c>--session</c> by the report's session id (decision 3), dropped only on the shim's END word (decision 5).
/// </summary>
/// <remarks>
/// Pure: <c>Build</c> writes nothing, so the fake person (<see cref="KimiFixture"/>) is only the place the paths are resolved from, and every
/// platform runs these.
/// </remarks>
public sealed class KimiConsultantTests : IDisposable
{
    private const string Session = "0d6a5f2e-1111-2222-3333-444455556666";

    private readonly KimiFixture _person = new();

    public void Dispose() => _person.Dispose();

    private string Repo => Path.Combine(_person.Folder, "checkout");

    private string Answers => Path.Combine(_person.Folder, "data", "coai-answers");

    /// <summary>The conversation's folder under the homes root: <c>coai-kimi\&lt;id&gt;</c>.</summary>
    private string Folder => ConsultantHomes.ConversationFolder(Path.Combine(_person.Folder, "homes"), ConsultantHomes.Kimi, new string('a', 32));

    private string Tree => Path.Combine(Folder, "tree");

    private KimiConsultant Consultant() =>
        new(new KimiRuntime("my-kimi", HostKind.Windows, new KimiSeams(_person.PlacesAt, () => ("coai-mcp", []))), "my-kimi");

    private ConsultantLaunch Turn(string handle = "", string model = "") =>
        new(Repo, "why is the count wrong\nsee the diff", handle, Answers,
            new ReviewerSettings("my-kimi") { Model = model, Timeout = TimeSpan.FromMinutes(5), OwnedScratch = Folder })
        {
            Workdir = Tree,
        };

    private static string After(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    // ---------- S5.4.1 ----------

    [Fact]
    public void AKimiRow_IsAConsultant_AndAnAnsweringRuntime()
    {
        var row = new VendorIdentity("my-kimi", "kimi", string.Empty);

        var consultant = ConsultantResolution.For(row);

        consultant.Should().NotBeNull("features.consultant names kimi (decision 9), and OnACli resolves it (decision 10)");
        consultant.Should().BeOfType<KimiConsultant>().Which.Vendor.Should().Be("my-kimi");
        ConsultantResolution.Consulting.Should().Contain("kimi");
        QuestionResolution.Answering.Should().Contain("kimi", "Answering follows Consulting by itself (decision 10)");
    }

    // ---------- S5.4.2 ----------

    [Fact]
    public void TheFirstTurn_IsTheShimRequest_InTheSnapshot_OnTheConversationsHome()
    {
        var invocation = Consultant().Build(Turn());
        var request = invocation.Request;

        request.WorkingDirectory.Should().Be(Tree, "D6: the conversation's one stable snapshot — never the checkout, never the answers folder");
        request.Arguments.Should().ContainInConsecutiveOrder("--ask-acp", "--profile", "kimi", "--model", KimiModels.Default, "--cwd", Tree);
        var home = After(request.Arguments, AcpWords.HomeFlag);
        Path.GetDirectoryName(home).Should().Be(Folder, "the home is a CHILD of the conversation folder (E5.3 decision a)");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        request.Arguments.Should().NotContain("--session", "a first turn continues nothing");
        request.StdIn.Should().Be("why is the count wrong\nsee the diff", "the prompt rides stdin, never the argv");
        request.Arguments.Should().NotContain(argument => argument.Contains('\n') || argument.Contains('\r'));
        request.Arguments.Should().NotContain(argument => argument.Contains(Repo, StringComparison.OrdinalIgnoreCase), "never the checkout (D6)");
        KimiEnvironment.Refusal(request, _person.PlacesAt(home)).Should().BeEmpty("the one launch control admits exactly what the adapter builds");
        invocation.Role.Should().Be(ConsultantRoles.Consult);
        invocation.Adapter.Should().BeOfType<KimiRuntime>("the executor asks KimiLaunch through the reviewer adapter (decision 1, E5.3 decision e)");
        invocation.Model.Should().Be(KimiModels.Default, "a row with no model runs on the default, and the invocation says so (E4 decision 75)");
    }

    // ---------- S5.4.3 ----------

    [Fact]
    public void ALaterTurn_LoadsItsSession()
    {
        var first = Consultant().Build(Turn()).Request;
        var later = Consultant().Build(Turn(handle: Session)).Request;

        later.Arguments.Should().ContainInConsecutiveOrder("--session", Session);
        later.Arguments.TakeLast(2).Should().Equal(["--session", Session], "the pair ends the argv");
        later.WorkingDirectory.Should().Be(first.WorkingDirectory, "M-K5: a loaded session resolves paths against the folder it was made in");
        After(later.Arguments, AcpWords.HomeFlag).Should().Be(After(first.Arguments, AcpWords.HomeFlag), "the session lives in the conversation's one home");
        FluentActions.Invoking(() => Consultant().Build(Turn(handle: "a b"))).Should().Throw<ArgumentException>("a malformed handle is a contract violation");
    }

    // ---------- S5.4.4 ----------

    [Theory]
    [InlineData("Workdir")]
    [InlineData("OwnedScratch")]
    public void NoSnapshot_OrNoHomeFolder_IsRefusedByName(string missing)
    {
        var launch = missing == "Workdir"
            ? Turn() with { Workdir = string.Empty }
            : Turn() with { Settings = Turn().Settings with { OwnedScratch = string.Empty } };

        FluentActions.Invoking(() => Consultant().Build(launch)).Should().Throw<ArgumentException>()
            .WithMessage($"*{missing}*", missing == "Workdir" ? "never the checkout (D6)" : "never TEMP or the answers folder for a conversation's home (D19)");
    }

    // ---------- S5.4.5 ----------

    [Theory]
    [InlineData(Session, Session)]
    [InlineData("a b", "")]
    [InlineData(null, "")]
    public void TheHandle_IsTheReportsSessionId(string? session, string expected)
    {
        var stdout = session is null ? "not json" : KimiShimScripts.Report(answer: "advice", session: session);

        var handle = Consultant().ReadHandle(new ProcessResult(0, stdout, string.Empty, TimedOut: false));

        handle.Should().Be(expected, "the handle is the report's sessionId when it is a well-formed handle, else nothing");
    }

    // ---------- S5.4.6 ----------

    [Theory]
    [InlineData(AcpEnd.SessionLost, 70, true)]
    [InlineData(AcpEnd.AgentFailed, 70, false)]
    public void ADroppedConversation_IsTheReportsSessionLost(AcpEnd end, int exit, bool dropped)
    {
        var result = new ProcessResult(exit, KimiShimScripts.Report(end, string.Empty, "Unknown sessionId: <id>", tokens: false, session: Session), string.Empty, TimedOut: false);

        Consultant().DroppedTheConversation(result).Should().Be(dropped, "the shim's END word decides, never the reason's text (decision 5)");
    }

    [Fact]
    public void AReasonThatSaysSessionLost_IsNotADroppedConversation()
    {
        var result = new ProcessResult(70, KimiShimScripts.Report(AcpEnd.AgentFailed, string.Empty, "the agent said session_lost", tokens: false), string.Empty, TimedOut: false);

        Consultant().DroppedTheConversation(result).Should().BeFalse("the reason is the agent's text, and text is never read for a verdict");
    }

    // ---------- decisions 4, 6, 7 ----------

    [Fact]
    public void TheVendorRemembers_TheUsageIsCumulative_AndItAsksForASnapshotAndAHomeFolder()
    {
        IConsultantRuntime consultant = Consultant();

        consultant.Memory.Should().BeOfType<ConsultantMemory.VendorRemembers>("a loaded session remembered (M-K5)");
        consultant.UsageIsCumulative.Should().BeTrue("M-K5b: /usage is the session's running total");
        consultant.NeedsASanitisedSnapshot.Should().BeTrue("D6");
        consultant.HomesFolder.Should().Be(ConsultantHomes.Kimi, "one owned home per conversation under coai-kimi (J3)");
        consultant.Toolbox.Should().Contain("Read").And.Contain("Grep").And.Contain("Glob").And.Contain("refused");
        consultant.Toolbox.Should().NotContain("bounded", "Grep and Glob read the disk directly (M-K6): never claimed bounded");
    }

    // ---------- E5.6: a question row ----------

    private const string RootA = "D:/projects/alpha";

    private const string RootB = "D:/projects/beta";

    /// <summary>The row's one-shot folder (coai-vh\&lt;8&gt; in the product): where its home is made.</summary>
    private string RowFolder => Path.Combine(_person.Folder, "homes", "coai-vh", "0123abcd");

    /// <summary>The row's EMPTY folder a none row stands in (QuestionRowLaunch's `cwd`, beside the home).</summary>
    private string RowScratch => Path.Combine(_person.Folder, "coai-question-0123abcd", "cwd");

    /// <summary>The copy of the roots a disk row stands in.</summary>
    private string RowTree => Path.Combine(_person.Folder, "coai-question-0123abcd", "tree");

    private static Confinement.Planned KimiPlan(CapabilityGrant grant, params string[] flags) =>
        grant.Capability == Capability.Disk
            ? new("kimi", grant, [], flags, CwdKind.Snapshot, [], AdmissionFlag.Unconfined) { SnapshotSources = grant.Roots }
            : new("kimi", grant, [], flags, CwdKind.Scratch, [], AdmissionFlag.Unconfined);

    private ConsultantLaunch Question(Confinement.Planned plan) =>
        new(Repo, "Which helper retries an outbound send?", string.Empty, Answers,
            new ReviewerSettings("my-kimi") { Timeout = TimeSpan.FromMinutes(5), OwnedScratch = RowFolder })
        {
            Confinement = new LaunchConfinement.Planned(plan),
            ScratchDir = RowScratch,
            Workdir = RowTree,
        };

    /// <summary>S5.6.5 (E5.6 decision 3): a none row in its empty folder, a disk row in the copy of its roots — the home in the row's folder, one shot, the minimal child.</summary>
    [Fact]
    public void AKimiQuestionLaunch_IsTheShimRequest_InItsFolderOrItsCopy_OnTheRowsHome()
    {
        var none = Consultant().Build(Question(KimiPlan(CapabilityGrant.None)));
        none.Request.WorkingDirectory.Should().Be(RowScratch, "a none row stands in an empty folder of its own");
        none.Request.Arguments.Should().ContainInConsecutiveOrder("--cwd", RowScratch);

        var disk = Consultant().Build(Question(KimiPlan(CapabilityGrant.Disk(RootA, RootB))));
        disk.Request.WorkingDirectory.Should().Be(RowTree, "a disk row reads the copy of its roots (D6)");
        disk.Request.Arguments.Should().ContainInConsecutiveOrder("--cwd", RowTree);

        foreach (var invocation in new[] { none, disk })
        {
            var request = invocation.Request;
            var home = After(request.Arguments, AcpWords.HomeFlag);
            Path.GetDirectoryName(home).Should().Be(RowFolder, "the row's home lives in the row's own one-shot folder, gone with it");
            Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
            request.Arguments.Should().NotContain(AcpWords.SessionFlag, "a question row is one shot");
            request.Arguments.Should().NotContain(argument => argument.Contains(RootA, StringComparison.OrdinalIgnoreCase)
                || argument.Contains(RootB, StringComparison.OrdinalIgnoreCase), "the roots are read through their copy, never named");
            request.InheritsEnvironment.Should().BeFalse();
            request.Passthrough.Should().BeEquivalentTo(ProcessEnvironment.Minimal, "the question row's road (ForQuestion)");
            KimiEnvironment.Refusal(request, _person.PlacesAt(home)).Should().BeEmpty("the one launch control admits it");
            invocation.Role.Should().Be(ConsultantRoles.Question);
        }
    }

    /// <summary>S5.6.6 (E5.6 decision 3): a plan with a flag was not made for kimi — refused by name, never spliced.</summary>
    [Theory]
    [InlineData(Capability.None)]
    [InlineData(Capability.Disk)]
    public void APlanWithFlags_IsRefusedForKimi(Capability capability)
    {
        var grant = capability == Capability.Disk ? CapabilityGrant.Disk(RootA) : CapabilityGrant.None;

        var build = () => Consultant().Build(Question(KimiPlan(grant, "--yolo")));

        build.Should().Throw<ArgumentException>().WithMessage("*shim*");
    }
}
