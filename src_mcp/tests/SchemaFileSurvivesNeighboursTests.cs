using Xunit;
using FluentAssertions;
using CoaiMcp.Core.Findings;
using CoaiMcp.Server;

namespace CoaiMcp.Tests;

/// <summary>
/// The schema file is shared by every server on this machine, and writing it must not kill a round.
/// </summary>
/// <remarks>
/// <para>Found by the seven-lane matrix of 2026-09-05, on the first run: <c>the round failed: The
/// process cannot access the file 'finding-schema.json' because it is being used by another
/// process</c>. Every round rewrites that file before it launches its reviewers, the data directory
/// is shared by every window, and on Windows two writers is an exception rather than a queue. A whole
/// round died for a file whose content is a compile-time constant and was already correct on disk.</para>
/// <para>So: written only when it is missing or different, and a lost race is not an error — the
/// neighbour is writing the same bytes. The one thing that must never happen is a round dying for it.</para>
/// </remarks>
public sealed class SchemaFileSurvivesNeighboursTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-schema-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Path_ => System.IO.Path.Combine(_dir, "finding-schema.json");

    [Fact]
    public void ItWritesTheSchemaWhenThereIsNone()
    {
        SchemaFile.Ensure(_dir).Should().Be(Path_);

        File.ReadAllText(Path_).Should().Be(FindingSchema.Json);
    }

    [Fact]
    public void ANeighbourHoldingTheFileOpenDoesNotKillTheRound()
    {
        // The exact shape of the failure: the file is already there and correct, and somebody else
        // has it open for writing. On Windows that is an IOException to anyone who tries; the round
        // must not care, because the bytes it wanted are already on disk.
        File.WriteAllText(Path_, FindingSchema.Json);
        using var held = new FileStream(Path_, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var act = () => SchemaFile.Ensure(_dir);

        act.Should().NotThrow();
        SchemaFile.Ensure(_dir).Should().Be(Path_);
    }

    [Fact]
    public void ItDoesNotRewriteAFileThatAlreadyMatches()
    {
        SchemaFile.Ensure(_dir);
        var written = File.GetLastWriteTimeUtc(Path_);

        SchemaFile.Ensure(_dir);

        File.GetLastWriteTimeUtc(Path_).Should().Be(written, "the content is a constant; rewriting it is a race for nothing");
    }

    [Fact]
    public void AStaleOrTruncatedFileIsReplaced()
    {
        File.WriteAllText(Path_, "{ this is not the schema");

        SchemaFile.Ensure(_dir);

        File.ReadAllText(Path_).Should().Be(FindingSchema.Json);
    }

    [Fact]
    public void AndWhenItCannotBeWrittenAtAll_TheRoundStillGetsAPath()
    {
        // Failing OPEN, like the caller memory does: a reviewer that cannot read the schema file is a
        // reviewer that answers unshaped JSON, which the round already handles. A round that never
        // launches because of a locked constant is strictly worse.
        File.WriteAllText(Path_, "stale");
        using var held = new FileStream(Path_, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        SchemaFile.Ensure(_dir).Should().Be(Path_);
    }
}
