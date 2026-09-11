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
        changes[0].Pathspecs.Should().Equal("src/file.cs");
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
            ["src/old.cs", "src/new.cs"],
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
    public void APathWithATabOrANewlineSurvivesBecauseTheSeparatorIsNeither()
    {
        // Legal names on Linux, and the reason the field separator has to be NUL rather than
        // anything a filesystem allows. Neither can be produced on Windows, so this is the only
        // place either is exercised.
        var changes = NumstatReader.Read("1\t0\tsrc/od\td.cs\0" + "1\t0\tsrc/two\nlines.cs\0").ToList();

        changes.Select(c => c.Path).Should().Equal(
            ["src/od", "src/two\nlines.cs"],
            "a tab inside a name is indistinguishable from a column separator, and that is a fact "
            + "about tabs rather than something a parser can recover from");
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
    public void ATruncatedRenameIsDroppedRatherThanGuessedAt()
    {
        // It cannot happen against a git that exited zero, which the assembler has already checked.
        // What it must not do is invent a name from half a record.
        var changes = NumstatReader.Read("2\t1\tsrc/kept.cs\0" + "1\t0\t\0src/only-one-name.cs\0").ToList();

        changes.Should().ContainSingle().Which.Path.Should().Be("src/kept.cs");
    }
}
