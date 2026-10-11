using System.Text.Json;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A mode the extension launches only to READ never makes the data directory (research/PLAN_paths_per_side.md E5.7, D3).
/// </summary>
/// <remarks>
/// <para><b>What the gate's risk consultation on story 5.7 found, and a run confirmed.</b> The extension refuses to create
/// a NAMED data directory that is missing — the notice says it is missing, and a folder made in a bare <c>/mnt/z</c> mount
/// point would hide that. But its readers start this binary: <c>--bugs-json</c> and <c>--pairs-json</c> opened the rounds
/// database through <c>RoundsDb.Open</c>, whose first line is <c>Directory.CreateDirectory</c>; <c>--real-method</c>,
/// <c>--file-at</c> and <c>--tree-at</c> did the same through their shared pair read; and <c>--providers</c> built the
/// consultation services, which provisioned <c>schemas/</c> under the data directory in a field initialiser. Measured
/// 2026-10-10 with <c>COAI_DATA_DIR</c> naming a missing child of a temp folder: those six left the folder behind, holding
/// <c>coai.db</c> or <c>schemas/</c>. A read that makes the folder it reports on is the write the extension refuses.</para>
/// <para><b>The list is the inventory</b> of every mode the extension launches as a reader. The extension's
/// <c>everyServerModeTheExtensionLaunches.test.ts</c> reads it from this file, so a mode the extension starts reading
/// is red there until it is listed here — and then it is held to the same promise.</para>
/// </remarks>
[Collection("console-out")]
public sealed class AReaderModeNeverMakesTheDataDirectoryTests : IDisposable
{
    private readonly string _parent = Directory.CreateTempSubdirectory("coai-reader-").FullName;
    private readonly string? _dirWas = Environment.GetEnvironmentVariable("COAI_DATA_DIR");
    private readonly string? _sideWas = Environment.GetEnvironmentVariable("COAI_DATA_SIDE");

    private string Missing => Path.Combine(_parent, "named-but-missing");

    // READER MODES — every mode the extension launches only to read (`everyServerModeTheExtensionLaunches.test.ts` parses
    // this block). Each row is the mode and the arguments that reach its read with a database that is not there.
    public static TheoryData<string[]> ReaderModes => new()
    {
        new[] { "--log" },
        new[] { "--log", "--paged", "--since", "2026-01-01T00:00:00Z" },
        new[] { "--findings", "--session", "s1", "--stage", "CodeReview", "--number", "1" },
        new[] { "--bugs-json" },
        new[] { "--pairs-json", "--limit", "5" },
        new[] { "--real-method", "--id", "1" },
        new[] { "--file-at", "--id", "1" },
        new[] { "--tree-at", "--id", "1" },
        new[] { "--trees" },
        new[] { "--round-counts" },
        new[] { "--features" },
        new[] { "--version" },
        // Launched on a person's act, but it reads stdin and answers stdout only (research/PLAN_names_and_a_missing_root.md 3.3).
        new[] { "--check-security" },
    };
    // END READER MODES

    public AReaderModeNeverMakesTheDataDirectoryTests()
    {
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", Missing);
        Environment.SetEnvironmentVariable("COAI_DATA_SIDE", null);
    }

    [Theory]
    [MemberData(nameof(ReaderModes))]
    public async Task AReaderModeLeavesAMissingNamedDataDirectoryAbsent(string[] args)
    {
        // A mode that reads stdin (`--check-security`) reads an empty one here, never this test runner's own.
        var inWas = Console.In;
        Console.SetIn(new StringReader(string.Empty));
        int code;
        try
        {
            (_, code) = await Stdout.OfAsync(() => Program.RunAsync(args));
        }
        finally
        {
            Console.SetIn(inWas);
        }

        Directory.Exists(Missing).Should().BeFalse(
            $"`coai-mcp {string.Join(' ', args)}` exited {code} and left the missing data directory behind, holding: {Inside()}");
    }

    [Fact]
    public async Task FindingsManyLeavesAMissingNamedDataDirectoryAbsent()
    {
        var keys = Path.Combine(_parent, "keys.json");
        await File.WriteAllTextAsync(keys, """[{"sessionId":"s1","stage":"CodeReview","number":1}]""");

        await Stdout.OfAsync(() => Program.RunAsync(["--findings-many", "--keys-file", keys]));

        Directory.Exists(Missing).Should().BeFalse($"--findings-many left the missing data directory behind, holding: {Inside()}");
    }

    [Fact]
    public async Task CadenceLeavesAMissingNamedDataDirectoryAbsent()
    {
        await Stdout.OfAsync(() => Program.RunAsync(["--cadence", "--repo", _parent, "--branch", "main", "--plan", "x"]));

        Directory.Exists(Missing).Should().BeFalse($"--cadence left the missing data directory behind, holding: {Inside()}");
    }

    /// <summary>
    /// <c>--providers</c> through its injected seam: the real launcher would ask this machine's vault and its CLIs.
    /// </summary>
    [Fact]
    public async Task ProvidersLeavesAMissingNamedDataDirectoryAbsent_AndStillAnswers()
    {
        var output = new StringWriter();
        Program.Classify(["--providers"]).Should().Be(Program.Startup.Providers, "the seam below is what this mode runs");

        var code = await Program.ProvidersJsonCoreAsync(
            name => name == "COAI_DATA_DIR" ? Missing : null, new NothingAnswers(), output, _ => { });

        Directory.Exists(Missing).Should().BeFalse($"--providers left the missing data directory behind, holding: {Inside()}");
        code.Should().Be(0, "the providers' health does not depend on the data directory being there");
        output.ToString().Should().Contain("\"providers\"");
    }

    /// <summary>A reader still answers what it answered before, without the folder: an empty corpus, an empty list.</summary>
    [Fact]
    public async Task TheReadersStillAnswerEmpty_NotAnError()
    {
        var (bugs, bugsCode) = await Stdout.OfAsync(() => Program.RunAsync(["--bugs-json"]));
        var (pairs, pairsCode) = await Stdout.OfAsync(() => Program.RunAsync(["--pairs-json"]));
        var (fileAt, fileAtCode) = await Stdout.OfAsync(() => Program.RunAsync(["--file-at", "--id", "7"]));

        (bugsCode, pairsCode, fileAtCode).Should().Be((0, 0, 0), "a machine that has never run a round is asked a fair question");
        JsonDocument.Parse(bugs).RootElement.GetProperty("candidates").GetArrayLength().Should().Be(0);
        JsonDocument.Parse(pairs).RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
        JsonDocument.Parse(fileAt).RootElement.GetProperty("reason").GetString().Should().Be("pair_not_found");
    }

    /// <summary>A database that IS there is still migrated and read by the reader that migrates — the fix reads, never skips.</summary>
    [Fact]
    public async Task AnExistingDatabaseIsStillOpenedAndRead()
    {
        Directory.CreateDirectory(Missing);
        using (Store.RoundsDb.Open(Missing, Serilog.Core.Logger.None))
        {
        }

        var (pairs, code) = await Stdout.OfAsync(() => Program.RunAsync(["--pairs-json"]));

        code.Should().Be(0);
        JsonDocument.Parse(pairs).RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    /// <summary>An attribute read that is refused — the database may be there; the disk would not say.</summary>
    private static FileAttributes Denied(string path) => throw new UnauthorizedAccessException($"Access to the path '{path}' is denied.");

    /// <summary>
    /// The empty answer is for a database CONFIRMED absent only (the E5 code round): <c>File.Exists</c> answers false on an
    /// access or I/O error as well, so a reader reported an empty corpus — "no material" — for a database it merely could
    /// not read. "Cannot tell" keeps the I/O failure every one of these modes already has: 74.
    /// </summary>
    [Fact]
    public async Task ADatabaseTheDiskWillNotDescribeIsAnIoFailure_NotAnEmptyAnswer()
    {
        var (bugs, bugsCode) = Stdout.Of(() => Program.BugsJson(["--bugs-json"], Denied));
        var (pairs, pairsCode) = Stdout.Of(() => Program.PairsJson(["--pairs-json"], Denied));
        var (fileAt, fileAtCode) = await Stdout.OfAsync(() => Program.FileAtAsync(["--file-at", "--id", "7"], Denied));

        (bugsCode, pairsCode, fileAtCode).Should().Be((74, 74, 74),
            $"a database the disk would not describe was answered as one that is not there: --bugs-json said {bugs.Trim()}, --pairs-json {pairs.Trim()}, --file-at {fileAt.Trim()}");
        Directory.Exists(Missing).Should().BeFalse("an unanswered question about the database is no licence to make the folder");
    }

    /// <summary>The presence itself: there, confirmed absent, cannot tell — the one mapping, asked for a FILE.</summary>
    [Fact]
    public void TheDatabasesPresenceIsThreeValued()
    {
        Store.RoundsDb.PresenceOf(Missing, File.GetAttributes).Should().Be(Server.RootPresence.Absent);
        Store.RoundsDb.PresenceOf(Missing, Denied).Should().Be(Server.RootPresence.Unknown);
        Store.RoundsDb.PresenceOf(Missing, _ => throw new IOException("busy")).Should().Be(Server.RootPresence.Unknown);
        Store.RoundsDb.PresenceOf(Missing, _ => FileAttributes.Normal).Should().Be(Server.RootPresence.Present);
        Store.RoundsDb.PresenceOf(Missing, _ => FileAttributes.Directory).Should().Be(Server.RootPresence.Absent, "a folder named coai.db is no database");
    }

    private string Inside()
    {
        try
        {
            var found = Directory.EnumerateFileSystemEntries(Missing, "*", SearchOption.AllDirectories)
                .Select(entry => Path.GetRelativePath(Missing, entry));

            return string.Join(", ", found) is { Length: > 0 } listed ? listed : "(nothing)";
        }
        catch (DirectoryNotFoundException)
        {
            return "(not there)";
        }
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _dirWas);
        Environment.SetEnvironmentVariable("COAI_DATA_SIDE", _sideWas);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_parent, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp folder that outlives one run is litter, not a failed test.
        }
    }

    /// <summary>Every launch fails: no vault, no CLI — the providers answer is about configuration, not the data folder.</summary>
    private sealed class NothingAnswers : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(127, string.Empty, "not here", false));
    }
}
