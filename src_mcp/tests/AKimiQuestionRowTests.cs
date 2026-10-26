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
/// A Kimi QUESTION row's launch (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.5 and S5.6.6): the planner's plan
/// composed by <see cref="KimiConsultant"/> — a none row in its empty folder, a disk row in the copy of its roots, the home one-shot
/// under the row's own folder — and a plan that carries any flag refused, because a Kimi row's confinement is the shim's (D15).
/// </summary>
/// <remarks>
/// Pure: <c>Build</c> writes nothing. The fake person (<see cref="KimiFixture"/>) is only where the paths are resolved from.
/// </remarks>
public sealed class AKimiQuestionRowTests : IDisposable
{
    private const string RootA = "D:/projects/alpha";

    private const string RootB = "D:/projects/beta";

    private readonly KimiFixture _person = new();

    public void Dispose() => _person.Dispose();

    private string Repo => Path.Combine(_person.Folder, "checkout");

    private string Answers => Path.Combine(_person.Folder, "data", "coai-answers");

    /// <summary>The row's own one-shot folder — the owned scratch the row's home is made under.</summary>
    private string RowFolder => Path.Combine(_person.Folder, "coai-question-row");

    private string Scratch => Path.Combine(RowFolder, "scratch");

    private string RowTree => Path.Combine(RowFolder, "tree");

    private KimiConsultant Consultant() =>
        new(new KimiRuntime("my-kimi", HostKind.Windows, new KimiSeams(_person.PlacesAt, () => ("coai-mcp", []))), "my-kimi");

    private ConsultantLaunch Question(Confinement.Planned plan) =>
        new(Repo, "the question", string.Empty, Answers, new ReviewerSettings("my-kimi") { OwnedScratch = RowFolder })
        {
            Confinement = new LaunchConfinement.Planned(plan),
            ScratchDir = Scratch,
            Workdir = RowTree,
        };

    private static Confinement.Planned Plan(CapabilityGrant grant, params string[] flags) =>
        new("kimi", grant, [], flags, grant.Capability == Capability.Disk ? CwdKind.Snapshot : CwdKind.Scratch, [], AdmissionFlag.Unconfined)
        {
            SnapshotSources = grant.Roots,
        };

    /// <summary>S5.6.5: the none row stands in its empty folder, the disk row in the copy of its roots; both home on the row's folder, one shot, and neither names a root.</summary>
    [Fact]
    public void AKimiQuestionLaunch_IsTheShimRequest_InItsFolderOrItsCopy_OnTheRowsHome()
    {
        var none = Consultant().Build(Question(Plan(CapabilityGrant.None)));
        var disk = Consultant().Build(Question(Plan(CapabilityGrant.Disk(RootA, RootB))));

        none.Request.WorkingDirectory.Should().Be(Scratch, "a none row stands in an empty folder of its own");
        disk.Request.WorkingDirectory.Should().Be(RowTree, "a disk row stands in the copy of its roots, never a root (D6)");

        foreach (var request in (ProcessRequest[])[none.Request, disk.Request])
        {
            request.Arguments.Should().Contain("--ask-acp");
            request.Arguments.Should().NotContain(AcpWords.SessionFlag, "a question row is one shot: no session to load");
            request.Arguments.Should().NotContain(RootA).And.NotContain(RootB, "the roots are read through their copy, never named to Kimi");
            request.InheritsEnvironment.Should().BeFalse();
            request.Arguments.Should().ContainInConsecutiveOrder("--home");
            var home = request.Arguments[request.Arguments.ToList().IndexOf("--home") + 1];
            home.Should().StartWith(RowFolder + Path.DirectorySeparatorChar, "the row's home lives in the row's own folder, gone with it");
        }

        none.Role.Should().Be(ConsultantRoles.Question);
        disk.Role.Should().Be(ConsultantRoles.Question);
    }

    /// <summary>S5.6.6: a plan carrying a flag is refused by name — a Kimi row's confinement is the shim's, not its argv.</summary>
    [Fact]
    public void APlanWithFlags_IsRefusedForKimi()
    {
        var launch = Question(Plan(CapabilityGrant.None, "--yolo"));

        FluentActions.Invoking(() => Consultant().Build(launch))
            .Should().Throw<ArgumentException>().WithMessage("*the shim*");
    }

}
