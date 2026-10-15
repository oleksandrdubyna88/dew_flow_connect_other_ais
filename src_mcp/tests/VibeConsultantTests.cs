using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a Vibe CONSULTANT launch is (S3 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>): it stands in the conversation's one
/// snapshot of the live tree and never in the checkout (D6), on the conversation's own home in the built environment (D17),
/// resumes by the session id Vibe reports (D7), and reads its usage per turn (M-V5) — each pinned to its cell of
/// research/RESULTS_vibe_confinement.md.
/// </summary>
public sealed class VibeConsultantTests
{
    private const string Repo = @"D:\rsd\some-checkout";
    private const string Folder = @"C:\Users\p\AppData\Local\coai-vibe\0123456789abcdef0123456789abcdef";
    private static readonly string Tree = Path.Combine(Folder, "tree");

    private static VibeConsultant Consultant() => new(new VibeRuntime("vibe", HostKind.Windows));

    private static ConsultantLaunch Turn(string handle = "", string model = "glm-5-3") =>
        new(Repo, "why is the count wrong\nsee the diff", handle, @"C:\data\consultations\answers",
            new ReviewerSettings("vibe") { Model = model, Timeout = TimeSpan.FromMinutes(5), OwnedScratch = Folder })
        {
            Workdir = Tree,
        };

    private static string After(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    [Fact]
    public void AConsultTurn_StandsInTheConversationsSnapshot_NeverInTheCheckout()
    {
        var invocation = Consultant().Build(Turn());
        var request = invocation.Request;

        request.WorkingDirectory.Should().Be(Tree, "D6: the conversation's one stable copy of the live tree");
        After(request.Arguments, "--workdir").Should().Be(Tree);
        request.Arguments.Should().NotContain(argument => argument.Contains(Repo, StringComparison.OrdinalIgnoreCase),
            "M-V11: Vibe reads only its working folder, and the checkout carries the branch's own .vibe/ (D6)");
        request.Arguments.Should().ContainInConsecutiveOrder("--agent", "plan").And.ContainInConsecutiveOrder("--enabled-tools", "read_file");
        request.Arguments.Should().NotContain("--trust").And.NotContain("--add-dir").And.NotContain("--resume", "a first turn resumes nothing");
        request.StdIn.Should().Be("why is the count wrong\nsee the diff", "the prompt rides stdin (D7, §13)");
        invocation.Role.Should().Be(ConsultantRoles.Consult);
        invocation.Adapter.Should().BeOfType<VibeRuntime>("the answer and the usage are read by the reviewer adapter's one copy");
    }

    [Fact]
    public void AConsultTurn_RunsOnTheConversationsOwnHome_InTheBuiltEnvironment()
    {
        var request = Consultant().Build(Turn()).Request;

        VibeLaunch.EnvironmentRefusal(request).Should().BeEmpty("the one launch control admits exactly what it builds (D17)");
        request.InheritsEnvironment.Should().BeFalse("§22 A: an inherited VIBE_* configures Vibe whatever its home holds");
        request.Environment[VibeLaunch.CliVariable].Should().Be("python", "§3: the measured CLI");
        request.Environment[VibeLaunch.ModelVariable].Should().Be("glm-5-3", "D10: the model rides the explicit environment");
        request.Environment[VibeLaunch.HomeVariable].Should().StartWith(Folder + Path.DirectorySeparatorChar,
            "D17: the conversation's home, inside its own folder under coai-vibe");
        Path.GetDirectoryName(After(request.Arguments, "--output-dir")).Should().Be(Folder,
            "D7: the per-attempt export lives in the conversation's own folder, swept with it (D20)");
    }

    [Fact]
    public void AResumedTurn_CarriesTheSessionId_OnTheSameSnapshotAndTheSameHome()
    {
        var first = Consultant().Build(Turn()).Request;
        var resumed = Consultant().Build(Turn(handle: "4f1c2d3e-aaaa-bbbb-cccc-0123456789ab")).Request;

        resumed.Arguments.Should().ContainInConsecutiveOrder("--resume", "4f1c2d3e-aaaa-bbbb-cccc-0123456789ab");
        resumed.WorkingDirectory.Should().Be(first.WorkingDirectory,
            "§22 B/E: a resumed session is bound to the folder it was created in — a new path is outside its boundary");
        resumed.Environment[VibeLaunch.HomeVariable].Should().Be(first.Environment[VibeLaunch.HomeVariable],
            "the session --resume needs lives in the home");
    }

    [Fact]
    public void TheHandle_IsTheSessionIdTheOutputCarries_AndNothingElse()
    {
        var consultant = Consultant();
        var answered = JsonSerializer.Serialize(new object[]
        {
            new Dictionary<string, object> { ["role"] = "user", ["content"] = "q", ["sessionId"] = "a1b2c3d4-0000-1111-2222-333344445555" },
            new Dictionary<string, object> { ["role"] = "assistant", ["content"] = "OK", ["sessionId"] = "a1b2c3d4-0000-1111-2222-333344445555" },
        });

        consultant.ReadHandle(new ProcessResult(0, answered, string.Empty, TimedOut: false))
            .Should().Be("a1b2c3d4-0000-1111-2222-333344445555", "first probe: every element of --output json carries sessionId");
        consultant.ReadHandle(new ProcessResult(0, """[{"role":"assistant","content":"x","sessionId":"bad id; rm -rf"}]""", string.Empty, TimedOut: false))
            .Should().BeEmpty("a handle is a file name and an argv value — a malformed one is no handle");
        consultant.ReadHandle(new ProcessResult(1, "Error: not json", string.Empty, TimedOut: false)).Should().BeEmpty();
    }

    [Fact]
    public void TheVendorRemembers_TheUsageIsPerTurn_AndItAsksForASnapshotAndAHomeFolder()
    {
        IConsultantRuntime consultant = Consultant();

        consultant.Memory.Should().BeOfType<ConsultantMemory.VendorRemembers>("--resume answered from the earlier turn (M-V5)");
        consultant.UsageIsCumulative.Should().BeFalse("M-V5, 3 of 3: a resumed export's usage is the turn's own");
        consultant.NeedsASanitisedSnapshot.Should().BeTrue("D6");
        consultant.HomesFolder.Should().Be(ConsultantHomes.Vibe, "D17: one home per conversation under coai-vibe");
        consultant.Toolbox.Should().Contain("read_file").And.Contain("working folder");
    }

    [Fact]
    public void WithoutASnapshotOrAFolderOfItsOwn_TheLaunchIsAContractViolation_NeverTheCheckout()
    {
        var noSnapshot = () => Consultant().Build(Turn() with { Workdir = string.Empty });
        var noFolder = () => Consultant().Build(Turn() with { Settings = Turn().Settings with { OwnedScratch = string.Empty } });

        noSnapshot.Should().Throw<ArgumentException>().WithMessage("*snapshot*",
            "falling back to the checkout would hand Vibe the branch's own .vibe/ and AGENTS.md (D6)");
        noFolder.Should().Throw<ArgumentException>().WithMessage("*home*", "D17: never the person's own ~/.vibe");
    }

    [Fact]
    public void AModelOffTheCuratedList_IsStillBuilt_AndRefusedByTheOneLaunchControl()
    {
        // Build is pure; the refusal is VibeLaunch's, the same on every path (D10) — so the consultant adds no check of its own.
        var request = Consultant().Build(Turn(model: "mistral-large-typo")).Request;

        VibeLaunch.ModelRefusal(VibeLaunch.Value(request, VibeLaunch.ModelVariable)).Should().Contain("mistral-large-typo");
        Consultant().Build(Turn(model: string.Empty)).Request.Environment[VibeLaunch.ModelVariable]
            .Should().Be(VibeModels.Default, "a row that names no model runs on Vibe's own default");
    }

    [Fact]
    public void AVibeRow_ConsultsAndAnswersQuestions_OnTheVibeConsultant()
    {
        var vibe = new VendorIdentity("mistral", "vibe", string.Empty);

        ConsultantResolution.Consulting.Should().Contain("vibe", "features.consultant names it (S3)");
        ConsultantResolution.For(vibe).Should().BeOfType<VibeConsultant>().Which.Vendor.Should().Be("mistral");
        QuestionResolution.Answering.Should().Contain("vibe");
        QuestionResolution.For(vibe).Should().BeOfType<VibeConsultant>();
        RuntimeCapabilities.Runtimes.Should().Contain("vibe", "the question consultant can launch it");
    }
}
