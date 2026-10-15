using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Rounds;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What git's <c>ls-tree -l -z</c> said about a path, and what a PLAN requires of that entry — both pure, the records
/// below are the strings git printed (measured, git 2.55).
/// </summary>
public sealed class ATreeEntryIsReadExactlyTests
{
    private const string PlanOid = "707279fd268816a6390d22d1db277a907635e45f";
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private const string Sha12 = "0123456789ab";

    /// <summary>One record as git prints it: <c>mode type oid SP %7s TAB path NUL</c>.</summary>
    private static string Record(string mode, string type, string oid, string size, string path) =>
        $"{mode} {type} {oid} {size,7}\t{path}\0";

    private static TreeListing Listing(string mode, string type, long size, string path = "plan.md") =>
        new([new TreeEntry(mode, type, PlanOid, size, path)], string.Empty);

    private static string RefusedBy(TreeListing listing, string path = "plan.md") =>
        PlanEntry.Pick(listing, path, Sha).Should().BeOfType<EntryVerdict.Refused>().Subject.Sentence;

    [Fact]
    public void ARecord_IsModeTypeOidPaddedSizeTabPath()
    {
        var output =
            Record("100644", "blob", PlanOid, "11", "plan.md") +
            Record("100755", "blob", PlanOid, "263170", "big.md") +
            Record("100644", "blob", PlanOid, "10000000", "huge.md");

        var listing = TreeEntries.Parse(output);

        listing.Entries.Should().HaveCount(3, "one, two and zero extra spaces of padding are all the same grammar");
        listing.Malformed.Should().BeEmpty();
        listing.Entries.Should().Equal(
            new TreeEntry("100644", "blob", PlanOid, 11, "plan.md"),
            new TreeEntry("100755", "blob", PlanOid, 263170, "big.md"),
            new TreeEntry("100644", "blob", PlanOid, 10000000, "huge.md"));
    }

    [Fact]
    public void ADashSize_IsMinusOne()
    {
        var output =
            Record("160000", "commit", "0123456789012345678901234567890123456789", "-", "sub") +
            Record("040000", "tree", "954c0de97d2bcaae55f9b6dc32b876fd9d6f24d0", "-", "todo");

        var listing = TreeEntries.Parse(output);

        listing.Entries.Should().HaveCount(2);
        listing.Entries.Should().OnlyContain(e => e.Size == -1);
        listing.Entries.Select(e => e.Mode).Should().Equal("160000", "040000");
    }

    [Fact]
    public void APathMayHoldATab_TheFirstTabSplits()
    {
        var listing = TreeEntries.Parse(Record("100644", "blob", PlanOid, "11", "docs/a\tb.md"));

        listing.Entries.Should().ContainSingle().Which.Path.Should().Be("docs/a\tb.md");
        listing.Malformed.Should().BeEmpty();
    }

    [Fact]
    public void AMalformedRecord_StopsTheParse_AndKeepsTheOnesBefore()
    {
        var good = Record("100644", "blob", PlanOid, "11", "plan.md");
        var bad = new[]
        {
            $"100644 blob {PlanOid}\tthree-fields.md\0",
            $"100644 blob NOTHEX{new string('a', 35)} {11,7}\tnon-hex.md\0",
            $"100644 blob {PlanOid} {"12x",7}\tbad-size.md\0",
            $"100644 blob {PlanOid} {11,7} no-tab-here\0",
        };

        foreach (var record in bad)
        {
            var listing = TreeEntries.Parse(good + record + good);

            listing.Malformed.Should().NotBeEmpty($"{record.Replace("\0", string.Empty)} is not a record");
            listing.Malformed.Should().Be(record.TrimEnd('\0')[..Math.Min(60, record.Length - 1)]);
            listing.Entries.Should().HaveCount(1, "the record before it is kept and nothing after it is read");
        }
    }

    [Fact]
    public void AnEmptyOutput_IsNoEntries_NotMalformed()
    {
        var listing = TreeEntries.Parse(string.Empty);

        listing.Entries.Should().BeEmpty();
        listing.Malformed.Should().BeEmpty();
    }

    [Fact]
    public void ZeroEntries_IsNotInCommit()
    {
        RefusedBy(TreeEntries.Parse(string.Empty)).Should().Contain("not in commit");
    }

    [Fact]
    public void SeveralEntries_IsOneFileWanted()
    {
        var two = new TreeListing(
            [new TreeEntry("100644", "blob", PlanOid, 3, "d/a.md"), new TreeEntry("100644", "blob", PlanOid, 3, "d/b.md")],
            string.Empty);

        RefusedBy(two, "d/").Should().Contain("names 2 entries");
    }

    [Fact]
    public void AnEntryWhosePathDiffers_IsRefused()
    {
        RefusedBy(Listing("100644", "blob", 5, "todo/PLAN x.md"), "todo/")
            .Should().Contain("git listed 'todo/PLAN x.md' for 'todo/'");
        RefusedBy(Listing("100644", "blob", 5, "Plan.md"))
            .Should().Contain("git listed 'Plan.md' for 'plan.md'", "the path is compared ordinally, not ignoring case");
    }

    [Theory]
    [InlineData("120000", "blob", "symbolic link")]
    [InlineData("160000", "commit", "submodule")]
    [InlineData("040000", "tree", "directory")]
    [InlineData("100664", "blob", "mode 100664")]
    [InlineData("100644", "tree", "not a blob")]
    public void AMode_DecidesBeforeTheType(string mode, string type, string word)
    {
        RefusedBy(Listing(mode, type, 7)).Should().Contain(word);
    }

    [Theory]
    [InlineData("100644")]
    [InlineData("100755")]
    public void ARegularFile_IsAccepted(string mode)
    {
        PlanEntry.Pick(Listing(mode, "blob", 11), "plan.md", Sha)
            .Should().BeOfType<EntryVerdict.Accepted>().Which.Entry.Oid.Should().Be(PlanOid);
    }

    [Fact]
    public void ASizeOfZero_AndOneOverTheCeiling_AreRefusedWithTheNumbers()
    {
        RefusedBy(Listing("100644", "blob", 0)).Should().Contain("empty");
        RefusedBy(Listing("100644", "blob", 262145)).Should().Contain("262145").And.Contain("262144");
        PlanEntry.Pick(Listing("100644", "blob", 262144), "plan.md", Sha).Should().BeOfType<EntryVerdict.Accepted>();
    }

    [Fact]
    public void TheSentencesNameThePathAndTheShortSha()
    {
        var refusals = new[]
        {
            new TreeListing([], "100644 blob garbage"),
            TreeEntries.Parse(string.Empty),
            new TreeListing([new TreeEntry("100644", "blob", PlanOid, 3, "a"), new TreeEntry("100644", "blob", PlanOid, 3, "b")], string.Empty),
            Listing("100644", "blob", 5, "Plan.md"),
            Listing("040000", "tree", -1),
            Listing("120000", "blob", 7),
            Listing("160000", "commit", -1),
            Listing("100664", "blob", 7),
            Listing("100644", "tree", 7),
            Listing("100644", "blob", 0),
            Listing("100644", "blob", 262145),
        };

        foreach (var listing in refusals)
        {
            RefusedBy(listing).Should().Contain("'plan.md'").And.Contain(Sha12).And.NotContain(Sha[..13]);
        }
    }
}
