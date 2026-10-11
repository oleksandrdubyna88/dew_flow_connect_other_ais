using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A mode a person starts never creates a NAMED data folder that is confirmed absent (research/PLAN_names_and_a_missing_root.md,
/// story 3.1). The extension refuses to create it (research/PLAN_paths_per_side.md, D3) — the notice says it is missing, and a
/// folder made in a bare <c>/mnt/z</c> mount point would hide that — but coai-mcp made it in every mode that writes:
/// <c>--check-consultant</c> / <c>--check-model</c> through <c>ConsultCheckLock</c> (<c>CreateDirectory(&lt;data&gt;/consultations/health)</c>,
/// recursive, so the root came with it), and <c>--collect-bugs</c>, <c>--pairs-keep</c>, <c>--pairs-decide</c>,
/// <c>--upload-pairs</c>, <c>--requeue-refused</c> through <c>RoundsDb.Open</c>. Each now stops at once: one stderr sentence
/// naming the folder, exit 74, nothing written.
/// </summary>
/// <remarks>
/// <para><b>Only a CONFIRMED absence refuses.</b> A root the disk would not describe — access denied, busy, an I/O error, a
/// sleeping share — answers <see cref="RootPresence.Unknown"/> and is never refused; the default location (no
/// <c>COAI_DATA_DIR</c>) is made lazily as always; and so is <c>&lt;root&gt;/&lt;side&gt;</c> inside a root that is there.</para>
/// <para><b>The list is the inventory</b> the extension's <c>everyServerModeTheExtensionLaunches.test.ts</c> reads: a mode it
/// classifies as a person's writer must be run here.</para>
/// </remarks>
[Collection("console-out")]
public sealed class AWriterModeRefusesAMissingNamedRootTests : IDisposable
{
    private readonly string _parent = Directory.CreateTempSubdirectory("coai-writer-").FullName;
    private readonly string? _dirWas = Environment.GetEnvironmentVariable("COAI_DATA_DIR");
    private readonly string? _sideWas = Environment.GetEnvironmentVariable("COAI_DATA_SIDE");
    private readonly string? _keyWas = Environment.GetEnvironmentVariable("COAI_BUGS_KEY");

    private string Missing => Path.Combine(_parent, "named-but-missing");

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public AWriterModeRefusesAMissingNamedRootTests()
    {
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", Missing);
        Environment.SetEnvironmentVariable("COAI_DATA_SIDE", null);
        Environment.SetEnvironmentVariable("COAI_BUGS_KEY", "test-key");
        File.WriteAllText(Path.Combine(_parent, "none.json"), """{"items":[]}""");
    }

    // WRITER MODES — every mode a person starts that opens the rounds database (`everyServerModeTheExtensionLaunches.test.ts`
    // reads this block). Each row reaches the open with arguments the mode accepts.
    public static TheoryData<string[]> RoundsDbWriters => new()
    {
        new[] { "--collect-bugs" },
        new[] { "--pairs-keep", "--in", "{none}" },
        new[] { "--pairs-decide", "--in", "{none}" },
        new[] { "--upload-pairs", "--server", "http://127.0.0.1:9" },
        new[] { "--requeue-refused" },
    };
    // END WRITER MODES

    [Theory]
    [MemberData(nameof(RoundsDbWriters))]
    public async Task ARoundsDatabaseWriter_StopsAt74_AndLeavesTheMissingNamedRootAbsent(string[] args)
    {
        var argv = args.Select(arg => arg == "{none}" ? Path.Combine(_parent, "none.json") : arg).ToArray();

        var (err, code) = await Stderr(() => Program.RunAsync(argv));

        Directory.Exists(Missing).Should().BeFalse($"`coai-mcp {string.Join(' ', args)}` exited {code} and made the missing data folder, holding: {Inside()}");
        code.Should().Be(74, $"a folder that is not there is a data directory this mode cannot use; it said: {err}");
        err.Should().Contain(Missing, "the sentence names the folder a person has to restore");
    }

    [Fact]
    public async Task CheckModel_StopsAt74_AndLeavesTheMissingNamedRootAbsent()
    {
        Program.Classify(["--check-model"]).Should().Be(Program.Startup.CheckModel, "the entry below is what this mode runs");
        Program.WriterModes.Should().Contain(Program.Startup.CheckModel, "and the dispatch refuses it before it starts");

        var (code, _, err) = await ConsultantCheckMode.AnswerModelAsync(
            Settings(), RowJson, new FailsEveryCall(), _parent, _ => { }, Noticing.None, TestContext.Current.CancellationToken, VaultKeys.None("t"));

        Directory.Exists(Missing).Should().BeFalse($"--check-model exited {code} and made the missing data folder, holding: {Inside()}");
        code.Should().Be(74, err);
        err.Should().Contain(Missing);
    }

    [Fact]
    public async Task CheckConsultant_StopsAt74_AndLeavesTheMissingNamedRootAbsent()
    {
        // A consultant that PASSES preflight: an unavailable one writes nothing anyway, and this test would prove nothing.
        Program.Classify(["--check-consultant"]).Should().Be(Program.Startup.CheckConsultant, "the entry below is what this mode runs");
        Program.WriterModes.Should().Contain(Program.Startup.CheckConsultant, "and the dispatch refuses it before it starts");

        var (code, _, err) = await ConsultantCheckMode.AnswerAsync(
            Settings(), ["--check-consultant", "--caller", "claude"], new FailsEveryCall(), _parent, _ => { }, Noticing.None, TestContext.Current.CancellationToken);

        Directory.Exists(Missing).Should().BeFalse($"--check-consultant exited {code} and made the missing data folder, holding: {Inside()}");
        code.Should().Be(74, err);
        err.Should().Contain(Missing);
    }

    // ---------- the helper: three answers, and only a confirmed absence refuses ----------

    /// <summary>An attribute read the disk refuses — the root may be there; it would not say.</summary>
    private static FileAttributes Denied(string path) => throw new UnauthorizedAccessException($"Access to the path '{path}' is denied.");

    private Func<string, string?> Named(string root, string side = "") =>
        name => name switch { "COAI_DATA_DIR" => root, "COAI_DATA_SIDE" => side.Length > 0 ? side : null, _ => null };

    [Fact]
    public void AConfirmedAbsentNamedRoot_IsRefused_NamingTheFolder()
    {
        var said = PanelSettings.NamedRootMissing(Named(Missing), File.GetAttributes);

        said.Should().Be($"The data folder {Missing} (COAI_DATA_DIR) was not found on this machine just now — nothing is written there until it exists.");
    }

    [Fact]
    public void ARootTheDiskWillNotDescribe_IsNeverRefused()
    {
        // Access denied, busy, an I/O error, a timeout: Unknown — a sleeping share answers this, and is never "missing".
        PanelSettings.NamedRootMissing(Named(Missing), Denied).Should().BeEmpty("cannot tell is not absent");
        PanelSettings.NamedRootMissing(Named(Missing), _ => throw new IOException("busy")).Should().BeEmpty("cannot tell is not absent");
        Program.MissingRootRefusal(Program.Startup.CheckModel, Named(Missing), Denied).Should().BeEmpty();
    }

    [Fact]
    public void TheDefaultLocation_IsNeverRefused()
    {
        // A disk that answers "not there" for EVERY path, so the default location is absent too: still never refused.
        static FileAttributes NothingIsThere(string path) => throw new DirectoryNotFoundException(path);

        PanelSettings.NamedRootMissing(_ => null, NothingIsThere).Should().BeEmpty("no COAI_DATA_DIR is the default location, made lazily");
        PanelSettings.NamedRootMissing(name => name == "COAI_DATA_DIR" ? "   " : null, NothingIsThere).Should().BeEmpty("whitespace names no folder");
    }

    [Fact]
    public void TheCheckIsOnTheRoot_ASideFolderInsideAnExistingRootIsStillMade()
    {
        // The root is there; its side folder is not. That is the lazily-made case, and the writer goes ahead and makes it.
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _parent);
        Environment.SetEnvironmentVariable("COAI_DATA_SIDE", "wsl");

        PanelSettings.NamedRootMissing(Environment.GetEnvironmentVariable, File.GetAttributes).Should().BeEmpty();
        var (_, code) = Stdout.Of(Program.RequeueRefused);

        code.Should().Be(0);
        File.Exists(Path.Combine(_parent, "wsl", "coai.db")).Should().BeTrue("the side folder inside an existing root is made when first written");
    }

    [Fact]
    public void AModeThatDoesNotWrite_IsNotRefusedHere()
    {
        Program.MissingRootRefusal(Program.Startup.Log, Named(Missing), File.GetAttributes).Should().BeEmpty("a reader answers on its own terms");
        Program.MissingRootRefusal(Program.Startup.CheckSecurity, Named(Missing), File.GetAttributes).Should().BeEmpty("--check-security never touches the data directory");
    }

    private static string RowJson =>
        "{\"row\":{\"id\":\"codex-2\",\"runtime\":\"codex\",\"executablePath\":" + System.Text.Json.JsonSerializer.Serialize(FakeCliExe) + "}}";

    /// <summary>The settings these modes build from this environment, with a consultant that passes preflight.</summary>
    private static PanelSettings Settings() => PanelSettings.FromEnvironment(Environment.GetEnvironmentVariable) with
    {
        Providers = [],
        ConsultEnabled = true,
        Consultants = new Dictionary<string, ConsultantChoice>
        {
            [CallerIdentity.Claude] = new("codex", Runtime: "codex", ExecutablePath: FakeCliExe),
        },
    };

    private static async Task<(string Err, int Code)> Stderr(Func<Task<int>> mode)
    {
        var stderr = new StringWriter();
        var was = Console.Error;
        try
        {
            Console.SetError(stderr);
            var (_, code) = await Stdout.OfAsync(mode);

            return (stderr.ToString(), code);
        }
        finally
        {
            Console.SetError(was);
        }
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
        Environment.SetEnvironmentVariable("COAI_BUGS_KEY", _keyWas);
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

    /// <summary>Every launch fails: nothing is paid for, and whatever the mode writes before a launch is what is measured.</summary>
    private sealed class FailsEveryCall : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(127, string.Empty, "not here", false));
    }
}
