using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a plan round over a feature folder approves (epoch 3 E3 s3 t4). This part: the stored shape of the source — origin <c>folder</c>, the scope path, the commit, the approval id and the id of the pack text — and what
/// <see cref="PlanSource.IsWellFormed"/> accepts of it. The binding of a coder job to it is in <see cref="APlanFolderBindingTests"/>.
/// </summary>
public sealed class APlanFolderRoundIsTheApprovedThingTests
{
    private static readonly string Commit = new('a', 40);

    private static readonly string Approval = new('b', 64);

    private static PlanSource Folder(string? origin = null, string? commit = null, string path = "v2/epoch-3/e3-roles-complete", string? blob = null, long bytes = 91_234, string textId = "0123456789abcdef") =>
        new(origin ?? "folder", commit ?? Commit, path, blob ?? Approval, bytes, textId);

    [Fact]
    public void D1_AFolderSource_IsWellFormed_WithItsScopeItsCommitItsApprovalIdAndItsTextId()
    {
        Folder().IsWellFormed().Should().BeTrue();
        Folder(commit: string.Empty).IsWellFormed().Should().BeTrue("the engine fills the commit; an unresolved one is empty, as for the other origins");
        Folder(blob: new string('c', 40)).IsWellFormed().Should().BeTrue("an approval id is 7 to 64 hex characters, like a blob id");
    }

    [Theory]
    [InlineData("", "path")]
    [InlineData("/v2/epoch-3", "path")]
    [InlineData("v2/../x", "path")]
    [InlineData("C:/v2", "path")]
    public void D1_AFolderSourceWithoutARepositoryRelativeScopePath_IsNotWellFormed(string path, string because)
    {
        Folder(path: path).IsWellFormed().Should().BeFalse(because);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not hex at all")]
    [InlineData("abc")]
    public void D1_AFolderSourceWithoutAnApprovalId_IsNotWellFormed(string blob)
    {
        Folder(blob: blob).IsWellFormed().Should().BeFalse();
    }

    [Fact]
    public void D1_NoOtherNewShapeIsAccepted_AnUnknownOriginANegativeSizeNoTextIdAndABadCommitStayRefused()
    {
        Folder(origin: "Folder").IsWellFormed().Should().BeFalse("the origin is compared exactly");
        Folder(origin: "folders").IsWellFormed().Should().BeFalse();
        Folder(origin: "tree").IsWellFormed().Should().BeFalse();
        Folder(bytes: -1).IsWellFormed().Should().BeFalse();
        Folder(textId: string.Empty).IsWellFormed().Should().BeFalse();
        Folder(commit: "zzzzzzz").IsWellFormed().Should().BeFalse();
        new PlanSource("text", Commit, "v2/epoch-3", Approval, 5, "id").IsWellFormed().Should().BeFalse("a pasted source has no path and no blob, and an approval id does not make it a folder");
        new PlanSource("commit", Commit, "v2/epoch-3", Approval, 5, "id").IsWellFormed().Should().BeTrue("a committed source is as it was: a path and a hex blob");
    }

    [Fact]
    public void D1_AFolderSource_IsStoredAsCamelCaseJson_AndReadBackEqual()
    {
        var source = Folder();

        var json = JsonSerializer.Serialize(source, ServerJsonContext.Default.PlanSource);
        var back = JsonSerializer.Deserialize(json, ServerJsonContext.Default.PlanSource);

        json.Should().Contain("\"origin\": \"folder\"").And.Contain($"\"blob\": \"{Approval}\"").And.Contain("\"path\": \"v2/epoch-3/e3-roles-complete\"");
        back.Should().Be(source);
        back!.IsWellFormed().Should().BeTrue();
    }

    [Fact]
    public void D1_ARoundThatReviewedAFolder_IsNamedByTheFolderItReviewed()
    {
        var stage = new StageRun(RoundMachine.BeginPlanRound, NeedsWorktree: false, Stage: Stage.PlanReview, ReadsCheckout: false, (_, _, _, _) => Task.FromResult<RoundWork>(null!)) { PlanSource = Folder() };

        PlanSources.SubjectOf(stage, "the pack text").Should().Be("e3-roles-complete");
        PlanSources.Stamp(stage, "the pack text", new string('d', 40)).Should().Be(Folder() with { Commit = new string('d', 40) }, "the source of the round is the folder's, and only its commit is the engine's");
    }
}
