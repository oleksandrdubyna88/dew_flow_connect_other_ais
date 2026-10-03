using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--consultants</c> and <c>--check-consultant</c> run as the PROCESSES the panel runs: the exit codes, the one
/// document on stdout, the file written for the other side, a HOME with no git identity, two checks at once, and a
/// check killed mid-turn.
/// </summary>
/// <remarks>
/// <para>Epic 4 of PLAN_the_consultant_works_on_every_vendor.md. The panel reaches these two modes through a spawn and
/// nothing else, so only a real process can show what it reads: 0 / 65 / 74 and never 64, exactly one JSON document,
/// and — for the lock — what two PROCESSES do, which two objects in one process cannot prove
/// (<c>EngineLease</c>'s own cross-process test is the precedent).</para>
/// <para>Every child gets its own data directory, its own temp directory (so its scratch is observable) and the fake
/// CLI's steering in ITS environment — never this process's, which the in-process suites mutate — with every inherited
/// <c>FAKECLI_*</c>, <c>COAI_CREDS_KEY</c> and consultant setting removed first.</para>
/// </remarks>
public sealed class ConsultantModesCliScenarioTests : IDisposable
{
    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    private readonly string _data = Directory.CreateTempSubdirectory("coai-modes-data-").FullName;
    private readonly string _temp = Directory.CreateTempSubdirectory("coai-modes-temp-").FullName;
    private readonly string _record = Directory.CreateTempSubdirectory("coai-modes-argv-").FullName;

    public void Dispose()
    {
        foreach (var dir in (string[])[_data, _temp, _record])
        {
            try
            {
                Runners.Files.GitScratch.DeleteEvenIfReadOnly(dir);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover temp directory is not a failing test.
            }
        }
    }

    /// <summary>
    /// The caller map every child reads: three definitions on the fake CLI — codex-, claude- and antigravity-shaped —
    /// and one legacy reference to a reviewer row nobody has, which resolves to nothing.
    /// </summary>
    /// <remarks>
    /// All four kinds are pinned: the shipped map resolves every kind to a REAL CLI by runtime name, and a test that
    /// left one to it would probe whatever claude or codex this machine has installed.
    /// </remarks>
    private static string Consultants() =>
        JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>>
        {
            ["claude"] = Definition("codex"),
            ["codex"] = Definition("claude"),
            ["gemini"] = Definition("antigravity"),
            ["other"] = new() { ["vendor"] = "nobody" },
        });

    private static Dictionary<string, string> Definition(string runtime) =>
        new() { ["vendor"] = runtime, ["runtime"] = runtime, ["executablePath"] = FakeCliExe };

    private ProcessStartInfo Info(IReadOnlyDictionary<string, string> steering, params string[] args)
    {
        var info = new ProcessStartInfo(ServerBinary.Path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        foreach (var name in info.Environment.Keys.Where(Inherited).ToList())
        {
            info.Environment.Remove(name);
        }

        info.Environment["COAI_DATA_DIR"] = _data;
        info.Environment["COAI_CONSULTANTS"] = Consultants();
        info.Environment["COAI_CONSULT_ENABLED"] = "true";
        info.Environment["COAI_REVIEWER_TIMEOUT_MINUTES"] = "2";
        info.Environment["FAKECLI_MODE"] = "vendor";
        info.Environment["FAKECLI_RECORD_DIR"] = _record;
        info.Environment["FAKECLI_STDOUT"] = """{"type":"thread.started","thread_id":"0198f2c1-check"}""" + "\n";
        info.Environment["FAKECLI_OUTFILE_TEXT"] = "marker: {{cwd-file:CHECK.md}}\ncanary: CANNOT";
        // The scratch lands HERE, where the test can see whether it was deleted.
        foreach (var name in (string[])["TEMP", "TMP", "TMPDIR"])
        {
            info.Environment[name] = _temp;
        }

        foreach (var (name, value) in steering)
        {
            info.Environment[name] = value;
        }

        return info;
    }

    private static bool Inherited(string name) =>
        name.StartsWith("FAKECLI_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("COAI_", StringComparison.OrdinalIgnoreCase);

    private (int Code, string Out, string Err) Run(IReadOnlyDictionary<string, string> steering, params string[] args)
    {
        using var process = Process.Start(Info(steering, args))!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(180_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
            throw new TimeoutException($"{args[0]} did not answer and exit within three minutes");
        }

        Task.WaitAll([stdout, stderr], 10_000).Should().BeTrue("the pipes close when the child does");

        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    private (int Code, string Out, string Err) Run(params string[] args) => Run(new Dictionary<string, string>(), args);

    private static JsonElement Row(string answer, string kind) =>
        JsonDocument.Parse(answer).RootElement.GetProperty("consultants").EnumerateArray()
            .Single(row => row.GetProperty("callerKind").GetString() == kind).Clone();

    /// <summary>The consultant TURNS the fake CLI recorded — a codex turn starts `exec`; a `--version` probe is not one.</summary>
    private int TurnsLaunched => Directory.EnumerateFiles(_record, "*.argv").Count(file => File.ReadAllText(file).StartsWith("exec\0", StringComparison.Ordinal));

    private string StatePath => Path.Combine(_data, "consultations", "health", "claude.check.json");

    // ---------- --consultants ----------

    [Fact]
    public void TheConsultants_AreTheFourCallerKinds_ADefinitionProbedThroughItsCli_AndTheFileWritten()
    {
        var (code, answer, err) = Run(new Dictionary<string, string> { ["FAKECLI_STDOUT"] = "fakecli 9.9.9" }, "--consultants");

        code.Should().Be(0, err);
        var root = JsonDocument.Parse(answer).RootElement;
        root.GetProperty("consultants").EnumerateArray().Select(row => row.GetProperty("callerKind").GetString())
            .Should().Equal("claude", "codex", "gemini", "other");
        root.GetProperty("utc").GetString().Should().EndWith("Z");
        var claude = Row(answer, "claude");
        claude.GetProperty("available").GetBoolean().Should().BeTrue(claude.ToString());
        claude.GetProperty("runtime").GetString().Should().Be("codex");
        claude.GetProperty("cli").GetProperty("probed").GetBoolean().Should().BeTrue();
        claude.GetProperty("cli").GetProperty("found").GetBoolean().Should().BeTrue();
        claude.GetProperty("cli").GetProperty("version").GetString().Should().Be("fakecli 9.9.9", "the RESOLVED definition's CLI was asked, not a reviewer row");
        claude.GetProperty("cli").GetProperty("authSource").GetString().Should().Be("own auth");
        claude.GetProperty("limitation").GetProperty("standing").GetString().Should().NotBeEmpty();
        answer.Should().NotContainEquivalentOf("signed in", "a version probe is not a sign-in check");
        // A claude whose --help came back without --restricted: its row says so, and the limitation follows.
        var onClaude = Row(answer, "codex");
        onClaude.GetProperty("capability").GetString().Should().Be("no-restricted");
        onClaude.GetProperty("limitation").GetProperty("standing").GetString().Should().Be("unconfined");
        Row(answer, "gemini").GetProperty("agy").GetProperty("settingsPath").GetString().Should().Contain("antigravity-cli");
        // A legacy reference to a reviewer nobody has: data, with the sentence consult would refuse with.
        var nobody = Row(answer, "other");
        nobody.GetProperty("available").GetBoolean().Should().BeFalse();
        nobody.GetProperty("reason").GetString().Should().Contain("nobody");
        nobody.GetProperty("cli").GetProperty("probed").GetBoolean().Should().BeFalse("nothing is run for a consultant consult would refuse");

        var written = File.ReadAllText(Path.Combine(_data, "consultations", "health", "consultants.json"));
        JsonDocument.Parse(written).RootElement.GetProperty("utc").GetString().Should().Be(root.GetProperty("utc").GetString(), "the file IS the answer, with its time");
    }

    [Fact]
    public void AnUnreadableDataDirectory_Is74_AndAnArgument_Is65_NeverTheCodeThatMeansTooOld()
    {
        var file = Path.Combine(_temp, "not-a-directory");
        File.WriteAllText(file, "a file where the data directory should be");

        var unreadable = Run(new Dictionary<string, string> { ["COAI_DATA_DIR"] = file }, "--consultants");
        var extra = Run("--consultants", "--everything");

        unreadable.Code.Should().Be(74, unreadable.Err);
        unreadable.Out.Should().BeEmpty();
        extra.Code.Should().Be(65, "the mode is known and takes no argument — 64 would read as an old binary");
    }

    /// <summary>
    /// The stdio server writes the same answer once in the background after it starts — for the other side, which
    /// cannot run this side's binary — and says nothing on stdout, which carries the protocol.
    /// </summary>
    [Fact]
    public async Task TheServer_WritesTheConsultantsInTheBackground_AndNothingReachesTheProtocol()
    {
        var info = Info(new Dictionary<string, string> { ["FAKECLI_STDOUT"] = "fakecli 9.9.9" });
        info.RedirectStandardInput = true;
        var written = Path.Combine(_data, "consultations", "health", "consultants.json");
        using var server = Process.Start(info)!;
        var stdout = server.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        _ = server.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            await Until(() => File.Exists(written), "the server never wrote consultants.json");
            Row(File.ReadAllText(written), "claude").GetProperty("cli").GetProperty("version").GetString().Should().Be("fakecli 9.9.9");
        }
        finally
        {
            // Closing stdin is how a client ends its server.
            server.StandardInput.Close();
            if (!server.WaitForExit(30_000))
            {
                server.Kill(entireProcessTree: true);
            }
        }

        (await stdout).Should().BeEmpty("not one byte reaches the protocol stream without a request");
    }

    /// <summary>
    /// A local consultant is probed WITH its model, as <c>--providers</c> probes a reviewer — without it every local or
    /// api consultant read "no model — name one" whatever its definition said (the code round of epic 4).
    /// </summary>
    [Fact]
    public void ALocalConsultantWithAModel_IsReportedAsItsDefinitionSays()
    {
        var consultants = JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>>
        {
            ["claude"] = Definition("codex"),
            ["codex"] = Definition("codex"),
            ["gemini"] = Definition("codex"),
            ["other"] = new() { ["vendor"] = "local", ["runtime"] = "local", ["model"] = "qwen3-coder", ["baseUrl"] = "http://127.0.0.1:9/v1" },
        });

        var (code, answer, err) = Run(new Dictionary<string, string> { ["COAI_CONSULTANTS"] = consultants }, "--consultants");

        code.Should().Be(0, err);
        var local = Row(answer, "other");
        local.GetProperty("model").GetString().Should().Be("qwen3-coder");
        local.GetProperty("cli").GetProperty("authSource").GetString().Should().Be("own auth", local.ToString());
        local.GetProperty("cli").GetProperty("note").GetString().Should().NotContain("no model");
    }

    /// <summary>
    /// A survey cut short by the server's own shutdown is not written: its probes answered "cancelled", which the file
    /// would have shown the other side as a CLI that could not be found (the code round of epic 4).
    /// </summary>
    [Fact]
    public async Task ASurveyCutShortByShutdown_IsNotWrittenForTheOtherSide()
    {
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();
        var settings = new CoaiMcp.Server.PanelSettings
        {
            Providers = [],
            DataDir = _data,
            Consultants = new Dictionary<string, CoaiMcp.Server.ConsultantChoice>
            {
                ["claude"] = new("local", "qwen3-coder", Runtime: "local", BaseUrl: "http://127.0.0.1:9/v1"),
            },
        };

        await CoaiMcp.Server.ConsultantsReadMode.WriteInBackground(
            settings, new Runners.Processes.ProcessLauncher(),
            Serilog.Core.Logger.None, CoaiMcp.Server.Noticing.None, stopped.Token);

        File.Exists(Path.Combine(_data, "consultations", "health", "consultants.json")).Should().BeFalse(
            "a shutdown's half-answer must not replace what the other side last saw");
    }

    // ---------- --check-consultant ----------

    [Fact]
    public void ACheck_WithAnEmptyHomeAndNoGitIdentity_StillCommits_Answers_AndDeletesItsScratch()
    {
        var home = Directory.CreateDirectory(Path.Combine(_temp, "empty-home")).FullName;

        var (code, said, err) = Run(
            new Dictionary<string, string>
            {
                ["HOME"] = home,
                ["USERPROFILE"] = home,
                ["XDG_CONFIG_HOME"] = home,
                ["GIT_CONFIG_NOSYSTEM"] = "1",
                ["GIT_CONFIG_GLOBAL"] = Path.Combine(home, "no-such-gitconfig"),
            },
            "--check-consultant", "--caller", "claude");

        code.Should().Be(0, err);
        var result = JsonDocument.Parse(said).RootElement;
        result.GetProperty("state").GetString().Should().Be("answered", said + err);
        result.GetProperty("markerRead").GetBoolean().Should().BeTrue("the scratch repository was committed and read");
        result.GetProperty("canary").GetString().Should().Be("not-attempted");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch is deleted at the end");
        Directory.EnumerateFiles(Path.Combine(_data, "consultations"), "*.json").Should().BeEmpty("no consultation record");
    }

    [Fact]
    public void ACheckWithoutACaller_Is65()
    {
        Run("--check-consultant").Code.Should().Be(65, "64 means 'this binary has never heard of --check-consultant'");
        TurnsLaunched.Should().Be(0);
    }

    [Fact]
    public async Task TwoChecksAtOnce_LaunchExactlyOnce_AndTheOtherIsAlreadyChecking()
    {
        var slow = new Dictionary<string, string> { ["FAKECLI_SLEEP_MS"] = "4000" };

        var first = Task.Run(() => Run(slow, "--check-consultant", "--caller", "claude"), TestContext.Current.CancellationToken);
        var second = Task.Run(() => Run(slow, "--check-consultant", "--caller", "claude"), TestContext.Current.CancellationToken);
        var both = await Task.WhenAll(first, second);

        both.Select(run => run.Code).Should().AllBeEquivalentTo(0);
        both.Select(run => JsonDocument.Parse(run.Out).RootElement.GetProperty("state").GetString())
            .Should().BeEquivalentTo(["answered", "already-checking"], string.Join("\n---\n", both.Select(run => run.Out + run.Err)));
        TurnsLaunched.Should().Be(1, "the second check's exclusive open failed, so it never paid for a launch");
    }

    [Fact]
    public async Task AHolderKilledMidCheck_IsAbandonedToTheNextReader_AndANewCheckRuns()
    {
        using (var holder = Process.Start(Info(new Dictionary<string, string> { ["FAKECLI_SLEEP_MS"] = "120000" }, "--check-consultant", "--caller", "claude"))!)
        {
            _ = holder.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            _ = holder.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await Until(() => TurnsLaunched == 1, "the holder never launched its turn");
            ReadState().GetProperty("state").GetString().Should().Be("checking");

            holder.Kill(entireProcessTree: true);
            holder.WaitForExit(10_000).Should().BeTrue();
        }

        var (_, consultants, _) = Run("--consultants");
        Row(consultants, "claude").GetProperty("check").GetProperty("state").GetString()
            .Should().Be("abandoned", "its lock went with its process, whatever the file still says");

        var (code, said, err) = Run("--check-consultant", "--caller", "claude");
        code.Should().Be(0, err);
        JsonDocument.Parse(said).RootElement.GetProperty("state").GetString().Should().Be("answered", "nobody holds the lock, so a new check runs");
        TurnsLaunched.Should().Be(2);
    }

    private JsonElement ReadState() =>
        JsonDocument.Parse(File.ReadAllText(StatePath)).RootElement.Clone();

    private static async Task Until(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(because);
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }
}
