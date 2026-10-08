using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The fake CLI's launch record (<c>FAKECLI_RECORD_DIR</c>) is either whole or not there: a test that lists and reads
/// the records WHILE a launch runs must never meet a record the child is still writing.
/// </summary>
/// <remarks>
/// <para>The defect, as it cost a release on 2026-10-07: tag <c>mcp-v0.44.1</c> failed its <c>coai-mcp (win-x64)</c>
/// job twice on <see cref="QuestionRowOnAgyScenarioTests"/>'s cut-short test, with <c>IOException: The process cannot
/// access the file '…\&lt;guid&gt;.argv' because it is being used by another process</c>. That test polls the records
/// until the follow-up has launched. The child wrote its record with <c>File.WriteAllText</c> — write access, other
/// READERS allowed — and the test read it with <c>File.ReadAllText</c>, which allows other readers only. On Windows a
/// share mode is enforced, so a read that lands while the child still holds its handle throws. Reproduced on a 24-core
/// machine: 3 of 30 runs under CPU load, 0 of 5 without.</para>
/// <para>The gate (<c>FAKECLI_RECORD_GATE</c>) holds the child inside that window, so the race is a fixed state here
/// rather than a timing: the child has written its record and has not closed it.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ALaunchRecordIsWholeOrAbsentTests : IDisposable
{
    private const string Prompt = "the prompt\nover two lines";

    private static readonly string[] Steering = ["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_RECORD_DIR", "FAKECLI_RECORD_GATE"];

    private readonly ProcessLauncher _launcher = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-record-").FullName;
    private readonly string _record;
    private readonly string _gate;

    public ALaunchRecordIsWholeOrAbsentTests()
    {
        _record = Directory.CreateDirectory(Path.Combine(_dir, "record")).FullName;
        _gate = Path.Combine(_dir, "release");
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", """{"findings": []}""");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", _record);
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_GATE", _gate);
    }

    public void Dispose()
    {
        foreach (var name in Steering)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>What the scenario tests do with the records: list them, read each one, split it into its argv.</summary>
    private IReadOnlyList<string[]> Records() =>
        [.. Directory.EnumerateFiles(_record, "*.argv").Select(path => LaunchRecords.Read(path).Split('\0'))];

    [Fact]
    public async Task ARecordStillBeingWritten_IsNotYetVisible_AndReadingTheRecordsNeverMeetsItsHandle()
    {
        var launching = _launcher.RunAsync(
            new ProcessRequest(FakeCliInvocations.Exe, ["exec", "-"], _dir) { StdIn = Prompt, Timeout = TimeSpan.FromSeconds(60) },
            TestContext.Current.CancellationToken);
        try
        {
            await UntilAsync(() => File.Exists(_gate + ".held"), "the child never reached its record");

            // The child has written its record and still holds it open — the moment a polling test can land in.
            var reading = () => Records();

            reading.Should().NotThrow("a reader must never meet the writer's handle: on Windows a share mode is enforced")
                .Subject.Should().BeEmpty("a launch whose record is still being written has not been recorded yet");
        }
        finally
        {
            // Not the test's token: a cancelled test must still let the child go, or the launch is never awaited.
            await File.WriteAllTextAsync(_gate, "go", CancellationToken.None);
            await launching;
        }

        // The positive half, with the same child: once it lets go, the record is there and it is whole.
        Records().Should().ContainSingle("one launch, one record")
            .Which.Should().Equal(["exec", "-", Prompt], "the record is the argv with stdin last, nothing cut short");
    }

    [Fact]
    public async Task AWholeRecordSomethingElseStillHolds_IsReadAnyway()
    {
        // Renaming the record into place is not the end of other handles on it: the rename's own handle, or a scanner
        // that opened the new file, can still hold it with write or delete access while letting others in. After the
        // recorder was made whole-or-absent, 1 of 40 runs under load still failed on a record that was ALREADY renamed.
        // A reader asking for "other readers only" is refused by such a handle; the record reader must not ask that.
        var path = Path.Combine(_record, $"{Environment.ProcessId}-{Guid.NewGuid():N}.argv");
        await File.WriteAllTextAsync(path, "exec\0-\0" + Prompt, TestContext.Current.CancellationToken);
        await using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        var reading = () => LaunchRecords.Read(path);

        reading.Should().NotThrow("another handle on a whole record must not stop a test reading it")
            .Subject.Split('\0').Should().Equal(["exec", "-", Prompt]);
    }

    private static readonly string[] SanctionedReaders = [nameof(ALaunchRecordIsWholeOrAbsentTests) + ".cs", nameof(LaunchRecords) + ".cs"];

    /// <summary>Every statement in this suite that names a <c>.argv</c> record and reads a file — by statement, not by line.</summary>
    /// <remarks>Statements, because most of these reads are formatted over two lines. It cannot follow a record path
    /// through a variable into a later statement; the two reads written that way were routed by hand.</remarks>
    private static IReadOnlyList<(string File, string Statement)> RecordReads() =>
        [.. Directory.EnumerateFiles(Path.Combine(ProductionSources.RepositoryRoot(), "src_mcp", "tests"), "*.cs")
            // This file and the reader itself name both on purpose; nothing else may.
            .Where(path => !SanctionedReaders.Contains(Path.GetFileName(path)))
            .SelectMany(path => File.ReadAllText(path).Split(';').Select(statement => (Path.GetFileName(path), statement)))
            .Where(read => read.statement.Contains(".argv", StringComparison.Ordinal) && read.statement.Contains("Read", StringComparison.Ordinal))];

    [Fact]
    public void EveryRecordReadInTheSuite_GoesThroughTheOneReader()
    {
        RecordReads().Where(read => read.Statement.Contains("File.Read", StringComparison.Ordinal))
            .Select(read => read.File)
            .Should().BeEmpty("a record read with File.ReadAllText is refused by any other handle on the record (LaunchRecords)");
    }

    [Fact]
    public void TheRecordReadScan_StillFindsTheReadsItGuards()
    {
        RecordReads().Where(read => read.Statement.Contains("LaunchRecords.Read", StringComparison.Ordinal))
            .Select(read => read.File)
            .Should().Contain(nameof(QuestionRowOnAgyScenarioTests) + ".cs", "a scan that matches nothing passes forever");
    }

    private static async Task UntilAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, because);
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}
