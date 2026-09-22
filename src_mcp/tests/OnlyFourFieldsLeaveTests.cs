using System.Reflection;
using CoaiMcp.Collecting;
using CoaiMcp.Core.Collecting;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What crosses the machine boundary, pinned so that widening it is a red build.
/// </summary>
/// <remarks>
/// <para><b>This is the only test in the repository whose failure means a privacy regression rather
/// than a bug.</b> Five stories keep a corpus anonymous and they are worth exactly what this one
/// upload sends.</para>
/// <para><b>Why a test and not a comment.</b> <see cref="StoredPair"/> and <see cref="UploadedPair"/>
/// describe the same artefact at two different distances — one a row in somebody's database, the
/// other a thing on the wire — and the obvious maintenance move is to notice the duplication and use
/// one for both. A story-5 code round flagged exactly that, because doing it would send the finding
/// id, the symbol, the severity, the category and the title in one edit that reads like a
/// simplification.</para>
/// <para><b>WIDENED ONCE, on 2026-09-21, from three fields to four</b>, and the file and class were
/// renamed with it because a test called "three" asserting four is the drift it exists to refuse.
/// The operator decided that *a comment a person wrote is public; it goes everywhere, including to
/// the server, for storage and later processing.* That is what makes the fourth field different in
/// kind from the five this still refuses: those are DERIVED from somebody's repository and are
/// anonymised before they leave, while a comment is typed by a person into a box that says, beside
/// it, that it leaves the machine unanonymised. The guard is unchanged in what it guards — it has
/// gained one field a person chose to publish, and it went RED on the widening before it was
/// changed, which is the only reason anybody can trust it still works.</para>
/// <para>And the widening is <b>invisible to every deployed server until somebody types</b>:
/// <see cref="APairWithoutACommentIsByteIdenticalToTheWireBeforeComments"/> compares against bytes
/// captured from the build that predates the field.</para>
/// </remarks>
public sealed class OnlyFourFieldsLeaveTests
{
    /// <summary>The wire type has exactly these, by name, and nothing else.</summary>
    /// <remarks>
    /// Named rather than counted: a test asserting "four properties" passes when somebody swaps one
    /// of them for `SymbolName`, which is precisely the mistake being guarded against.
    /// </remarks>
    [Fact]
    public void TheWireCarriesLanguageTwoSkeletonsAndAComment_AndNothingElse() =>
        typeof(UploadedPair).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(one => one.Name)
            .Where(name => name != "EqualityContract")
            .Should().BeEquivalentTo(["Language", "SkeletonBefore", "SkeletonAfter", "Comment"]);

    /// <summary>
    /// And the thing it is mapped FROM carries five more that must never leave.
    /// </summary>
    /// <remarks>
    /// Asserted so this test keeps meaning something. If `StoredPair` ever loses these, the mapping
    /// is no longer narrowing anything and somebody should think again about whether two types are
    /// still earning their keep — rather than discovering it by sending an id.
    /// </remarks>
    [Fact]
    public void TheStoredRowStillCarriesWhatMustNotLeave() =>
        typeof(StoredPair).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(one => one.Name)
            .Should().Contain(["FindingId", "SymbolName", "Severity", "Category", "Title"]);

    /// <summary>The mapping takes the three and looks at nothing else.</summary>
    /// <remarks>
    /// The types could both be right while the mapping reached around them — this drives the real
    /// conversion and asserts on what comes out.
    /// </remarks>
    [Fact]
    public void TheMappingSendsTheSkeletonsAndTheLanguage()
    {
        var wire = System.Text.Json.JsonSerializer.Serialize(
            new UploadRequest([Wire(Fixture)]), Server.ServerJsonContext.Default.UploadRequest);

        wire.Should().NotContain("4242").And.NotContain("ChargeAcmeCustomer");
        wire.Should().NotContain("Major").And.NotContain("Reliability");
        wire.Should().NotContain("acme", "the finding's own words never cross");
        wire.Should().Contain("method_1").And.Contain("CSharp");
    }

    /// <summary>
    /// A pair nobody commented on sends the bytes it sent before the field existed.
    /// </summary>
    /// <remarks>
    /// <para>The fixture was captured from the build that predates comments and committed BEFORE the
    /// type changed — the same discipline the server's frozen step-1 schema has, and for the same
    /// reason: a promise about what a deployed thing receives cannot be checked against the code
    /// that changed it. <b>That is checkable, not asserted:</b> in pull request #453 the fixture and
    /// THIS assertion land in their own commit, word for word, in the still-three-field
    /// <c>OnlyThreeFieldsLeaveTests</c> — green there, before the field existed. A reviewer who
    /// doubts the fixture's provenance can check that commit out of the pull request and run it;
    /// a fixture generated by the widened serialiser could not have passed there.</para>
    /// <para><b>The pull request, not `HEAD~1`</b>: `main` takes squash merges, so the two commits
    /// arrive as one and a reference to "the previous commit" would point at somebody else's work
    /// within a day. GitHub keeps the branch's commits on the pull request, which is where the
    /// evidence actually lives.</para>
    /// <para>This is what makes the widening safe to deploy in any order. A null comment is OMITTED
    /// by <c>WhenWritingNull</c>, so every existing server keeps receiving exactly what it received
    /// yesterday until somebody types something. A default of <c>""</c> instead of null would
    /// serialise an empty property and quietly change that, which is the failure this test exists to
    /// catch.</para>
    /// </remarks>
    [Fact]
    public void APairWithoutACommentIsByteIdenticalToTheWireBeforeComments()
    {
        var before = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "upload-request-before-comments.json"));

        var wire = System.Text.Json.JsonSerializer.Serialize(
            new UploadRequest([Wire(Fixture)]), Server.ServerJsonContext.Default.UploadRequest);

        wire.ReplaceLineEndings("\n").Should().Be(before.ReplaceLineEndings("\n"));
    }

    /// <summary>Two pairs differing only in what a person typed are ONE pair.</summary>
    /// <remarks>
    /// The constraint the whole design rests on. If a comment entered <see cref="PairId.Of"/>, every
    /// entry already in quarantine and in the corpus would change identity, the client's
    /// acknowledgement matching would break against all of them, and two people who found the same
    /// defect would stop deduplicating.
    /// </remarks>
    [Fact]
    public void TheCommentIsNotPartOfTheIdentity()
    {
        var bare = new UploadedPair("CSharp", "method_1() { }", "method_1() { lock { } }");
        var said = bare with { Comment = "this one bit us in production" };

        PairId.Of(said).Should().Be(PairId.Of(bare));
    }

    /// <summary>One pair, the same in every test here, so a failure is about the mapping.</summary>
    private static StoredPair Fixture => new(
        FindingId: 4242,
        SymbolName: "ChargeAcmeCustomer",
        Language: "CSharp",
        SkeletonBefore: "method_1() { }",
        SkeletonAfter: "method_1() { lock (var_1) { } }",
        Keep: Keep.Kept,
        Severity: "Major",
        Category: "Reliability",
        Title: "a race in the acme payment path");

    /// <summary>The same mapping the run uses, reached through the run's own type.</summary>
    /// <remarks>
    /// Reflection rather than a copy of the three lines: a copy would pass while the real mapping
    /// widened, which is the failure this whole file exists to prevent.
    /// </remarks>
    private static UploadedPair Wire(StoredPair pair) =>
        (UploadedPair)typeof(UploadRun)
            .GetMethod("Wire", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [pair])!;
}
