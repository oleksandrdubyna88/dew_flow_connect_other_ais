using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The vault's config key reaches <c>creds</c> on STDIN — never in its command line, which every
/// user inside WSL and every process of the same user on Windows can read
/// (research/PLAN_creds_config_key_on_stdin.md).
/// </summary>
/// <remarks>
/// <para>Driven against the real fake CLI, which records each launch's argv and stdin
/// (<c>FAKECLI_RECORD_DIR</c>) and answers a bare <c>--help</c> without recording it. So a record
/// is exactly a launch that could have carried the key.</para>
/// <para>Every key here is a fake value; none was ever minted by a vault.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class TheVaultKeyTravelsOnStdinTests : IDisposable
{
    private const string FakeKey = "cfgk_FAKEFAKEFAKEFAKEFAKEFAKEFAKE";

    private const string NewHelp = "creds — …\n  creds config -   …\n  (config-key-stdin)\n";

    private const string OldHelp = "creds — …\n  creds config <key>                 print one config file (for an app at startup)\n";

    private readonly string _records = Path.Combine(Path.GetTempPath(), "coai-vault-stdin-" + Guid.NewGuid().ToString("N"));

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public TheVaultKeyTravelsOnStdinTests()
    {
        FakeCliSteering.Reset();
        Directory.CreateDirectory(_records);
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", """{"grok": "sk-fake"}""");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", _records);
        Environment.SetEnvironmentVariable("FAKECLI_HELP_STDOUT", NewHelp);
    }

    public void Dispose()
    {
        FakeCliSteering.Reset();
        Directory.Delete(_records, recursive: true);
    }

    /// <summary>Each recorded launch: its argv, then its stdin as the last field.</summary>
    private List<string[]> Launches() =>
        [.. Directory.EnumerateFiles(_records, "*.argv").Select(path => LaunchRecords.Read(path).Split('\0'))];

    private static Task<VaultKeys> Read(KeyVault vault) =>
        vault.ReadFromConfigurationAsync(VaultConfiguration.Holding(FakeKey), TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_current_CLI_is_started_with_a_dash_and_given_the_key_on_stdin()
    {
        var keys = await Read(new KeyVault(new ProcessLauncher(), FakeCliExe));

        keys.Available.Should().BeTrue();
        keys.Keys["grok"].Should().Be("sk-fake");
        var launch = Launches().Should().ContainSingle("the probe is not a read, and there is exactly one read").Subject;
        launch[..^1].Should().Equal("config", "-");
        launch[^1].Should().Be(FakeKey + "\n");
    }

    [Fact]
    public async Task No_argument_of_any_launch_carries_the_key()
    {
        await Read(new KeyVault(new ProcessLauncher(), FakeCliExe));

        Launches().Should().NotBeEmpty("an empty record would make the next line pass for nothing");
        Launches().SelectMany(launch => launch[..^1]).Should().NotContain(argument => argument.Contains("FAKE"));
    }

    [Fact]
    public async Task An_old_CLI_is_refused_and_never_given_the_key_in_any_form()
    {
        // The one fallback this must never take: an old creds reads the key only from argv.
        Environment.SetEnvironmentVariable("FAKECLI_HELP_STDOUT", OldHelp);

        var keys = await Read(new KeyVault(new ProcessLauncher(), FakeCliExe));

        keys.Available.Should().BeFalse();
        keys.Unavailability.Should().Contain("update the creds CLI").And.Contain(KeyVault.StdinMarker);
        Launches().Should().BeEmpty("nothing but the --help probe was started");
    }

    [Fact]
    public async Task A_help_that_exits_non_zero_is_its_own_sentence_and_nothing_is_read()
    {
        Environment.SetEnvironmentVariable("FAKECLI_HELP_EXIT", "3");

        var keys = await Read(new KeyVault(new ProcessLauncher(), FakeCliExe));

        keys.Available.Should().BeFalse();
        keys.Unavailability.Should().Contain("--help exited 3");
        Launches().Should().BeEmpty();
    }

    [Fact]
    public async Task A_help_that_hangs_is_abandoned_at_the_bound_and_nothing_is_read()
    {
        Environment.SetEnvironmentVariable("FAKECLI_HELP_SLEEP_MS", "20000");

        var keys = await Read(new KeyVault(new ProcessLauncher(), FakeCliExe, probeTimeout: TimeSpan.FromMilliseconds(800)));

        keys.Available.Should().BeFalse();
        keys.Unavailability.Should().Contain("did not answer --help");
        Launches().Should().BeEmpty();
    }

    [Fact]
    public async Task A_CLI_that_cannot_be_started_still_moves_on_to_the_next_place()
    {
        var keys = await Read(new KeyVault(new ProcessLauncher(), "creds-binary-that-is-not-on-path", [FakeCliExe]));

        keys.Available.Should().BeTrue();
        Launches().Should().ContainSingle().Which[..^1].Should().Equal("config", "-");
    }

    [Fact]
    public async Task An_old_CLI_that_started_is_the_answer_and_the_next_place_is_never_tried()
    {
        // "Started and refused" is an answer, exactly as a refused read always was: a second copy
        // is not asked. The fallback here would fail loudly if it ran — it does not exist.
        Environment.SetEnvironmentVariable("FAKECLI_HELP_STDOUT", OldHelp);

        var keys = await Read(new KeyVault(new ProcessLauncher(), FakeCliExe, ["a-fallback-that-must-not-run"]));

        keys.Unavailability.Should().Contain("update the creds CLI");
        keys.Unavailability.Should().NotContain("not installed");
    }

    [Fact]
    public async Task No_sentence_on_any_path_carries_the_key()
    {
        // Positive control first: the sweep must see a planted copy, or an empty result proves nothing.
        Carries(["a sentence with " + FakeKey], FakeKey).Should().BeTrue();

        var sentences = new List<string>();
        foreach (var (help, exit, sleep, stdout, cliExit, expected) in new (string, string?, string?, string, string, string)[]
        {
            (OldHelp, null, null, "{}", "0", "update the creds CLI"),
            (NewHelp, "3", null, "{}", "0", "--help exited 3"),
            (NewHelp, null, "20000", "{}", "0", "did not answer --help"),
            (NewHelp, null, null, "{}", "92", "refused (exit 92)"),
            (NewHelp, null, null, "not json", "0", "not valid JSON"),
        })
        {
            Environment.SetEnvironmentVariable("FAKECLI_HELP_STDOUT", help);
            Environment.SetEnvironmentVariable("FAKECLI_HELP_EXIT", exit);
            Environment.SetEnvironmentVariable("FAKECLI_HELP_SLEEP_MS", sleep);
            Environment.SetEnvironmentVariable("FAKECLI_STDOUT", stdout);
            Environment.SetEnvironmentVariable("FAKECLI_EXIT", cliExit);
            // Only the hang gets the short bound: a cold start on a loaded machine must not turn the
            // other four into "did not answer" and skip the sentences they exist to reach.
            var bound = sleep is null ? KeyVault.DefaultProbeTimeout : TimeSpan.FromMilliseconds(800);
            var keys = await Read(new KeyVault(new ProcessLauncher(), FakeCliExe, probeTimeout: bound));
            keys.Unavailability.Should().Contain(expected);
            sentences.Add(keys.Unavailability);
        }

        sentences.Should().OnlyContain(sentence => sentence.Length > 0, "every one of these paths is a refusal");
        Carries(sentences, FakeKey).Should().BeFalse();
    }

    private static bool Carries(IEnumerable<string> lines, string key) =>
        lines.Any(line => line.Contains(key, StringComparison.Ordinal) || line.Contains(key["cfgk_".Length..], StringComparison.Ordinal));
}

/// <summary>
/// The vault against a launcher that records every request and answers by script — for what the real
/// fake CLI cannot stage: a cancelled probe, a binary that vanishes between probe and read, and the
/// exact shape of each request.
/// </summary>
public sealed class TheVaultsRequestsAreShapedForTheKeyTests
{
    private const string FakeKey = "cfgk_FAKEFAKEFAKEFAKEFAKEFAKEFAKE";

    private sealed class Scripted(Func<ProcessRequest, ProcessResult> answer) : IProcessLauncher
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private static ProcessResult Help(string text) => new(0, text, string.Empty, false);

    private static Task<VaultKeys> Read(KeyVault vault) =>
        vault.ReadFromConfigurationAsync(VaultConfiguration.Holding(FakeKey), TestContext.Current.CancellationToken);

    [Fact]
    public void The_marker_is_the_value_the_creds_CLI_prints()
    {
        // dew_flow_creds_for_devs pins the same value on its side (CommandLine.ConfigStdinMarker). A
        // change on either side alone makes every CLI read as too old, so the value is pinned, not the name.
        KeyVault.StdinMarker.Should().Be("config-key-stdin");
    }

    [Fact]
    public async Task The_probe_and_the_read_are_shaped_as_the_plan_says()
    {
        var launcher = new Scripted(r => r.Arguments is ["--help"] ? Help("(config-key-stdin)") : Help("{}"));

        await Read(new KeyVault(launcher, "creds"));

        launcher.Requests.Should().HaveCount(2);
        var probe = launcher.Requests[0];
        probe.Arguments.Should().Equal("--help");
        probe.StdIn.Should().BeEmpty("the probe is never given anything, least of all the key");
        probe.Timeout.Should().Be(KeyVault.DefaultProbeTimeout).And.Be(TimeSpan.FromSeconds(10));
        probe.MaxOutputChars.Should().Be(64 * 1024);
        var read = launcher.Requests[1];
        read.Arguments.Should().Equal("config", "-");
        read.StdIn.Should().Be(FakeKey + "\n");
        read.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task A_marker_only_on_stderr_is_not_a_marker()
    {
        var launcher = new Scripted(r => new ProcessResult(0, "usage", "config-key-stdin", false));

        var keys = await Read(new KeyVault(launcher, "creds"));

        keys.Unavailability.Should().Contain("update the creds CLI");
        launcher.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task A_cancelled_probe_says_cancelled_rather_than_calling_the_CLI_hung()
    {
        var launcher = new Scripted(r => new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true, Cancelled: true));

        var keys = await Read(new KeyVault(launcher, "creds"));

        keys.Unavailability.Should().Contain("cancelled").And.NotContain("hung");
        launcher.Requests.Should().ContainSingle("nothing is read after a cancelled probe");
    }

    [Fact]
    public async Task A_CLI_that_vanishes_between_probe_and_read_is_its_own_answer()
    {
        // The probe started it, so it is the answer: the next place is NOT asked, and the sentence says
        // what happened instead of "not installed".
        var launcher = new Scripted(r => r.Arguments is ["--help"]
            ? Help("(config-key-stdin)")
            : throw new System.ComponentModel.Win32Exception(2, "gone"));

        var keys = await Read(new KeyVault(launcher, "creds", ["a-fallback-that-must-not-run"]));

        keys.Unavailability.Should().Contain("could not be started for the read").And.NotContain("not installed");
        launcher.Requests.Select(r => r.Executable).Should().OnlyContain(e => e == "creds");
    }
}

/// <summary>
/// Clears every <c>FAKECLI_*</c> variable this process holds. The vault's probe made several more of them
/// matter to the vault tests, so a class that sets only its own and inherits the rest from whichever test
/// ran before can see a "--help exited 3" it never asked for (own review).
/// </summary>
internal static class FakeCliSteering
{
    public static void Reset()
    {
        foreach (var name in Environment.GetEnvironmentVariables().Keys.Cast<string>()
                     .Where(name => name.StartsWith("FAKECLI_", StringComparison.Ordinal))
                     .ToList())
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
