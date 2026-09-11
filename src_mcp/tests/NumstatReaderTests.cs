using CoaiMcp.Runners.Context;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The numstat shapes, exhaustively, without a repository.
/// </summary>
/// <remarks>
/// <see cref="ContextAssemblerRenameTests"/> proves the parser against real git, which is what
/// settles whether the SHAPE below is git's. These prove the parser against every shape at once,
/// including the ones a fixture repository is awkward to produce — a path with a newline in it is
/// legal on Linux and cannot be created on Windows at all.
/// <para>Every fixture here was copied from the output of a real <c>git diff --numstat -z</c> on
/// 2026-09-10, with the NUL bytes written as <c>\0</c>. A shape invented from the manual would prove
/// only that the parser reads what its author imagined.</para>
/// </remarks>
public sealed class NumstatReaderTests
{
    [Fact]
    public void AnOrdinaryChangeIsItsOwnPath()
    {
        var changes = NumstatReader.Read("2\t1\tsrc/file.cs\0").ToList();

        changes.Should().ContainSingle();
        changes[0].Should().Be(new NumstatChange("src/file.cs", string.Empty, IsBinary: false));
        changes[0].Path.Should().Be("src/file.cs", "the PATH is the name, unprefixed — it is what the reviewer reads");
        changes[0].Pathspecs.Should().Equal(":(literal)src/file.cs");
    }

    [Fact]
    public void ABinaryChangeIsMarkedByItsMissingCounts()
    {
        var changes = NumstatReader.Read("-\t-\tsrc/logo.png\0").ToList();

        changes.Should().ContainSingle().Which.IsBinary.Should().BeTrue();
    }

    [Fact]
    public void ARenameCarriesBothItsNames()
    {
        // The whole defect, as one record: the path column is EMPTY and the two names are the next
        // two fields. The human-readable form joins them into `src/{old.cs => new.cs}`, which is a
        // sentence rather than a path.
        var changes = NumstatReader.Read("1\t0\t\0src/old.cs\0src/new.cs\0").ToList();

        changes.Should().ContainSingle();
        changes[0].Path.Should().Be("src/new.cs", "the name the reviewer will look for is where it IS");
        changes[0].RenamedFrom.Should().Be("src/old.cs");
        changes[0].Pathspecs.Should().Equal(
            [":(literal)src/old.cs", ":(literal)src/new.cs"],
            "one name alone makes git print a whole-file add or a whole-file delete");
    }

    [Fact]
    public void APathOutsideAsciiIsNotEscapedAtAll()
    {
        // Under the default core.quotePath the human-readable form prints this as
        // "src/\321\204\320\260\320\271\320\273.cs", quotes included. `-z` prints the bytes.
        var changes = NumstatReader.Read("2\t1\tsrc/файл.cs\0").ToList();

        changes.Should().ContainSingle().Which.Path.Should().Be("src/файл.cs");
    }

    [Fact]
    public void APathWithATabOrANewlineSurvivesWholeBecauseTheSeparatorIsNeither()
    {
        // Legal names on Linux, and the reason the field separator has to be NUL rather than
        // anything a filesystem allows. Neither can be created on Windows, so this is the only place
        // either is exercised — and a fixture rather than a repository for that reason.
        //
        // The FIRST version of this test asserted `src/od`, the truncated value, as though it were
        // right: the parser split on every tab, and the test was written from the parser instead of
        // from the guarantee. Six reviewers across two vendors caught it in one round. A path with a
        // tab in it that arrives shortened matches nothing, so the file reaches the reviewer named
        // with an empty diff — this class's own defect, one line below the fix for it, and a way for
        // a contributor to hide a file from review by naming it.
        var changes = NumstatReader.Read("1\t0\tsrc/od\td.cs\0" + "1\t0\tsrc/two\nlines.cs\0").ToList();

        changes.Select(c => c.Path).Should().Equal(
            ["src/od\td.cs", "src/two\nlines.cs"],
            "the path is everything after the SECOND tab — a tab inside a name is part of the name, "
            + "and NUL is what separates one record from the next");
    }

    [Fact]
    public void AWholeDiffIsReadInTheOrderGitListedIt()
    {
        // Copied verbatim from a real run: a binary add, a delete, a binary delete, a text rename
        // and a non-ASCII edit, in git's own order.
        const string measured =
            "-\t-\tsrc/brand.png\0" +
            "0\t1\tsrc/dead.cs\0" +
            "-\t-\tsrc/logo.png\0" +
            "1\t0\t\0src/old.cs\0src/new.cs\0" +
            "2\t1\tsrc/файл.cs\0";

        var changes = NumstatReader.Read(measured).ToList();

        changes.Select(c => c.Path).Should().Equal(
            "src/brand.png", "src/dead.cs", "src/logo.png", "src/new.cs", "src/файл.cs");
        changes.Count(c => c.IsBinary).Should().Be(2);
        changes.Single(c => c.RenamedFrom.Length > 0).Path.Should().Be("src/new.cs",
            "a rename is ONE file that moved, never an add beside a delete");
    }

    [Fact]
    public void NothingChangedReadsAsNothing() =>
        NumstatReader.Read(string.Empty).Should().BeEmpty();

    [Fact]
    public void ATruncatedRenameIsRefusedRatherThanSilentlyShorteningTheDiff()
    {
        // It cannot happen against a git that exited zero, which the assembler has already checked.
        // What it must not do is return the records read so far: that hands the reviewer a diff
        // SHORTER than the change and calls it the change, which is this class's whole subject
        // arriving by a different door. The first version dropped the rest and said nothing.
        var truncated = () =>
            NumstatReader.Read("2\t1\tsrc/kept.cs\0" + "1\t0\t\0src/only-one-name.cs\0").ToList();

        truncated.Should().Throw<ContextException>()
            .WithMessage("*rename record ended without its new name*")
            .WithMessage("*src/only-one-name.cs*", "the reader names what it was in the middle of");
    }
}
