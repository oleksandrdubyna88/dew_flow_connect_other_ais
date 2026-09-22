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
/// </remarks>
public sealed class OnlyThreeFieldsLeaveTests
{
    /// <summary>The wire type has exactly these, by name, and nothing else.</summary>
    /// <remarks>
    /// Named rather than counted: a test asserting "three properties" passes when somebody swaps one
    /// of them for `SymbolName`, which is precisely the mistake being guarded against.
    /// </remarks>
    [Fact]
    public void TheWireCarriesLanguageAndTwoSkeletons_AndNothingElse() =>
        typeof(UploadedPair).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(one => one.Name)
            .Where(name => name != "EqualityContract")
            .Should().BeEquivalentTo(["Language", "SkeletonBefore", "SkeletonAfter"]);

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
        var stored = new StoredPair(
            FindingId: 4242,
            SymbolName: "ChargeAcmeCustomer",
            Language: "CSharp",
            SkeletonBefore: "method_1() { }",
            SkeletonAfter: "method_1() { lock (var_1) { } }",
            Keep: Keep.Kept,
            Severity: "Major",
            Category: "Reliability",
            Title: "a race in the acme payment path");

        var wire = System.Text.Json.JsonSerializer.Serialize(
            new UploadRequest([Wire(stored)]), Server.ServerJsonContext.Default.UploadRequest);

        wire.Should().NotContain("4242").And.NotContain("ChargeAcmeCustomer");
        wire.Should().NotContain("Major").And.NotContain("Reliability");
        wire.Should().NotContain("acme", "the finding's own words never cross");
        wire.Should().Contain("method_1").And.Contain("CSharp");
    }

    /// <summary>
    /// The wire AS IT IS TODAY, captured to a file so that a later widening can be measured.
    /// </summary>
    /// <remarks>
    /// <para><b>This test exists to be committed BEFORE the field it guards against.</b> A comment
    /// is about to become the wire's fourth property, and the promise that makes that safe to deploy
    /// in any order is that a pair nobody commented on still serialises to exactly these bytes — so
    /// every server already running keeps receiving what it received yesterday until somebody types
    /// something. A fixture written at the same time as the widening cannot prove that: it would
    /// have been produced by the new code and would agree with it whatever the new code did.</para>
    /// <para>So the bytes are captured here, against the THREE-field type, and this assertion is
    /// green in this commit. The next commit adds the field and renames this class; the assertion
    /// travels with it unchanged, and from then on it compares the new serialiser against a file
    /// the new serialiser never wrote.</para>
    /// <para>Line endings are normalised on both sides — a checkout under <c>autocrlf</c> and a CI
    /// runner disagree about them, and that is not a change to the wire.</para>
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
