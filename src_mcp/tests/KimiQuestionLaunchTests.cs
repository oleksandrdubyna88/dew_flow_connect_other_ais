using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a Kimi QUESTION-row launch is (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.5 and S5.6.6): the shim's request in the row's
/// empty folder or in its copy of the roots, on a home inside the row's own folder, with no <c>--session</c> — and a plan that carries a flag is
/// refused, because Kimi's confinement is the shim's.
/// </summary>
public sealed class KimiQuestionLaunchTests
{
    private const string RootA = "D:/projects/alpha";
    private const string RootB = "D:/projects/beta";

    private static readonly string RowFolder = Path.Combine(Path.GetTempPath(), "coai-question-" + Guid.NewGuid().ToString("N"));
    private static readonly string RowTree = Path.Combine(RowFolder, "tree");
    private static readonly string RowCwd = Path.Combine(RowFolder, "cwd");

    private static Confinement.Planned Plan(string runtime, CapabilityGrant grant) =>
        ConfinementPlanner.Plan(runtime, grant).Should().BeOfType<Confinement.Planned>().Which;

    private static ConsultantLaunch Planned(Confinement.Planned plan) => new(
        "repo", "the question", string.Empty, "answers", new ReviewerSettings("kimi") { OwnedScratch = RowFolder })
    {
        Confinement = new LaunchConfinement.Planned(plan),
        ScratchDir = RowCwd,
        Workdir = RowTree,
    };

    // A fake person's paths, so building reads nothing of this machine: the request names the places, it does not open them.
    private static KimiConsultant Consultant() => new(new KimiRuntime("kimi", HostKind.Windows, new KimiSeams(Places, () => ("coai-mcp", []))));

    private static KimiPlaces Places(string home) => KimiPlaces.ForShim(home, Path.Combine(RowFolder, "persons-bin"));

    private static string After(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    [Fact]
    public void AKimiQuestionLaunch_IsTheShimRequest_InItsFolderOrItsCopy_OnTheRowsHome()
    {
        var none = Consultant().Build(Planned(Plan("kimi", CapabilityGrant.None))).Request;
        var disk = Consultant().Build(Planned(Plan("kimi", CapabilityGrant.Disk(RootA, RootB)))).Request;

        none.WorkingDirectory.Should().Be(RowCwd, "a none row stands in an empty folder of its own");
        disk.WorkingDirectory.Should().Be(RowTree, "a disk row stands in the copy of its roots (D6)");
        foreach (var request in (Runners.Processes.ProcessRequest[])[none, disk])
        {
            var home = After(request.Arguments, AcpWords.HomeFlag);

            Path.GetDirectoryName(home).Should().Be(RowFolder, "the row's home lives in the row's own folder, gone with it");
            KimiEnvironment.Refusal(request, Places(home)).Should().BeEmpty("the one launch control admits exactly what the adapter builds, for a none row as for a disk row");
            request.Arguments.Should().NotContain(AcpWords.SessionFlag, "a question row is one shot");
            request.Arguments.Should().NotContain(argument => argument.Contains(RootA) || argument.Contains(RootB), "the roots are read through their copy, never named");
            request.InheritsEnvironment.Should().BeFalse();
        }
    }

    [Fact]
    public void APlanWithFlags_IsRefusedForKimi()
    {
        var none = Plan("kimi", CapabilityGrant.None);

        foreach (var plan in (Confinement.Planned[])[none with { Flags = ["--yolo"] }, none with { Leading = ["--search"] }])
        {
            var building = () => Consultant().Build(Planned(plan));

            building.Should().Throw<ArgumentException>().WithMessage("*shim*");
        }
    }
}
