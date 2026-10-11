using System.Collections.Concurrent;
using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Each ledger line says what its launch RAN WITH — story 3.4 of <c>todo/PLAN_every_round_is_counted.md</c>, the local half:
/// which CLI and which release of it, and the effort and tier as actually sent, each with its source.
/// </summary>
/// <remarks>
/// <para><b>What was wrong</b> (the plan's symptom 6). A codex row's effort is kept and never sent
/// (<c>RosterBuilder.EffortFor</c>), so every local codex launch ran at whatever <c>~/.codex/config.toml</c> said — <c>high</c>
/// on the operator's machine — and its tier came from the same file until fast mode shipped. The ledger recorded neither
/// the effort nor the tier nor the CLI, so the plan's measurement had to reconstruct them by hand.</para>
/// <para><b>What must hold.</b> A line's effort and tier are what the launch SENT, labelled <c>row</c>; a value the row set
/// and the path never sends is labelled <c>ignored</c> and is empty; a value nothing set or sent is labelled from the CLI's
/// own configuration — and reading that configuration never adds a flag to the launch. The comparison is with the argv
/// and the environment the launcher actually RECEIVED, never with the row's configured values.</para>
/// <para>Every test here steers <c>CODEX_HOME</c> or the fake CLI through the process environment, so the class runs in the
/// collection that does not run in parallel.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class EachLineSaysWhatItRanWithTests : IAsyncLifetime
{
    /// <summary>A codex configuration that names both an effort and a tier — what a person's own file does.</summary>
    private const string CodexConfig = "model = \"gpt-6-luna\"\nmodel_reasoning_effort = \"xhigh\"\nservice_tier = 'priority' # mine\n\n[profiles.slow]\nmodel_reasoning_effort = \"low\"\n";

    private static readonly string[] Steering =
        ["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_VERSION_STDOUT", "CODEX_HOME"];

    private readonly string? _codexHomeBefore = Environment.GetEnvironmentVariable("CODEX_HOME");
    private readonly TempDir _dir = TempDir.For("coai-ran-with-");
    private readonly ProcessLauncher _git = new();

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    private string Repo => Made("repo");

    private string Data => Made("data");

    /// <summary>A folder under this test's own, made on first use.</summary>
    private string Made(string name) => Directory.CreateDirectory(_dir.At(name)).FullName;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        foreach (var name in Steering)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Environment.SetEnvironmentVariable("CODEX_HOME", _codexHomeBefore);
        _dir.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>A codex home holding <paramref name="config"/> — or none at all — made the process's for this test.</summary>
    private string CodexHome(string name, string config)
    {
        var home = Made(name);
        if (config.Length > 0)
        {
            File.WriteAllText(Path.Combine(home, "config.toml"), config);
        }

        Environment.SetEnvironmentVariable("CODEX_HOME", home);

        return home;
    }

    private PanelService Service(IProcessLauncher launcher, params ProviderSettings[] providers) => new(
        new PanelSettings
        {
            Providers = providers,
            DataDir = Data,
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            RateLimitBackoff = TimeSpan.FromMilliseconds(5),
        },
        VaultKeys.None("no vault in tests"), default, launcher, Logger.None, Noticing.None);

    private IReadOnlyList<ReviewerWork> Work(params ProviderSettings[] providers) =>
        Service(new ProcessLauncher(), providers).Roster.BuildWork(
            [RoleCatalog.ArchitectureRole], string.Empty, "ctx", round: 1, stage: Stage.PlanReview, readsCheckout: false,
            codexTiers: CodexTiers.None).Reviewers;

    private static ProviderSettings Codex(string id = "codex") => new(id) { Enabled = true, Runtime = "codex" };

    /// <summary>The line the ledger writes for <paramref name="invocation"/> — through the real <see cref="UsageLedger"/>.</summary>
    private JsonElement LineFor(ReviewerInvocation invocation)
    {
        var ledger = new UsageLedger(_dir.At("ledger-" + Guid.NewGuid().ToString("N")));
        ledger.Record(invocation, new ReviewerOutcome.Ok(new NormalisedReview([], []), false, new Usage(10, 2, null)), "m", "PlanReview", TimeSpan.FromSeconds(3));

        return JsonDocument.Parse(File.ReadAllLines(ledger.Path).Single()).RootElement.Clone();
    }

    private static string Text(JsonElement line, string name) =>
        line.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : "<absent>";

    // ---------- the codex row's effort ----------

    [Fact]
    public void ACodexRowsEffort_IsRecordedAsIgnored()
    {
        // Whatever this machine's own config.toml says: an ignored effort is not relabelled as the configuration's.
        CodexHome("codex-home-ignored", CodexConfig);
        var invocation = Work(Codex() with { CliEffort = "high" }).Single().Invocation;

        string.Join(' ', invocation.Request.Arguments).Should().NotContain("reasoning_effort", "codex is never told a row's effort");

        var line = LineFor(invocation);
        Text(line, "effortFrom").Should().Be(OptionSources.Ignored,
            "the row asked for 'high' and the launch path never sends a codex effort — the line must say so, not stay silent");
        Text(line, "effort").Should().BeEmpty("what the row asked for did not run, and nothing is claimed about what did");
        Text(line, "cli").Should().Be("codex");
    }

    // ---------- the CLI's own configuration labels, and changes nothing ----------

    [Fact]
    public void AnEmptyValue_IsLabelledFromCliConfig_NeverChanged()
    {
        var row = Codex() with { Fast = Core.Catalog.FastMode.Cli };

        CodexHome("codex-home-empty", string.Empty);
        var bare = Work(row).Single().Invocation;
        CodexHome("codex-home-configured", CodexConfig);
        var configured = Work(row).Single().Invocation;

        WithoutPaths(configured.Request.Arguments).Should().Equal(WithoutPaths(bare.Request.Arguments),
            "reading the CLI's configuration labels the line and never adds a flag to the launch");
        string.Join(' ', configured.Request.Arguments).Should().NotContain("service_tier").And.NotContain("reasoning_effort");

        var line = LineFor(configured);
        (Text(line, "effort"), Text(line, "effortFrom")).Should().Be(("xhigh", OptionSources.CliConfig),
            "nothing was sent, and the codex the launch starts reads xhigh from its config.toml");
        (Text(line, "tier"), Text(line, "tierFrom")).Should().Be(("priority", OptionSources.CliConfig));
        var nothing = LineFor(bare);
        (Text(nothing, "effort"), Text(nothing, "effortFrom"), Text(nothing, "tierFrom")).Should().Be((string.Empty, string.Empty, string.Empty),
            "with no configuration naming either, the line says nothing rather than guess the vendor's default");
    }

    /// <summary>The argv with every path-valued flag's value set aside — the answer file is a fresh temp folder per build.</summary>
    private static IEnumerable<string> WithoutPaths(IReadOnlyList<string> argv) =>
        argv.Select((arg, at) => at > 0 && argv[at - 1] is "-o" or "-C" or "--output-schema" ? "<path>" : arg);

    // ---------- a real round: the line is what the launcher received ----------

    [Fact]
    public async Task TheRecordedLaunch_IsWhatWasSent()
    {
        await GitRepository();
        var codexHome = CodexHome("codex-home-round", CodexConfig);
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", """{"findings":[]}""");
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", """{"type":"result","is_error":false,"result":"{\"findings\":[]}"}""");
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", "codex-cli 0.160.0\n");
        var launcher = new Recording(new ProcessLauncher());
        var service = Service(launcher,
            // Off (the default) with an effort codex is never told; Off is what the tier probe asks about.
            Codex() with { ExecutablePath = FakeCliExe, CliEffort = "high" },
            // "As the CLI is set", and no effort: the CLI's own configuration decides both.
            Codex("codex-2") with { ExecutablePath = FakeCliExe, Fast = Core.Catalog.FastMode.Cli },
            new ProviderSettings("claude")
            {
                Enabled = true,
                Runtime = "claude",
                ExecutablePath = FakeCliExe,
                Model = "claude-opus-5",
                CliEffort = "high",
                Fast = Core.Catalog.FastMode.On,
            });
        await service.OpenAsync(Repo, "feature");

        await service.ReviewPlanAsync(Repo, "feature", "the plan");

        var lines = File.ReadAllLines(Path.Combine(Data, "usage.jsonl")).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList();
        lines.Select(l => Text(l, "provider")).Distinct().Should().BeEquivalentTo(["codex", "codex-2", "claude"]);
        foreach (var line in lines)
        {
            var sent = launcher.LaunchOf(Text(line, "provider"), Text(line, "role"));
            var argv = sent.Arguments;
            switch (Text(line, "provider"))
            {
                case "codex":
                    (Text(line, "tier"), Text(line, "tierFrom")).Should().Be((Setting(argv, "service_tier"), OptionSources.Row),
                        "an Off row is told the standard tier, and the line names what the argv carried");
                    Setting(argv, "service_tier").Should().Be("default");
                    Setting(argv, "model_reasoning_effort").Should().BeEmpty();
                    (Text(line, "effort"), Text(line, "effortFrom")).Should().Be((string.Empty, OptionSources.Ignored));
                    Text(line, "cliVersion").Should().Be(LaunchOptions.FirstLine(VersionAnswer()),
                        "the version is the tier probe's answer, which this launch's codex was asked before it started");
                    launcher.VersionsAsked.Should().Contain(FakeCliExe, "the version comes from a --version that ran anyway");
                    break;
                case "codex-2":
                    Setting(argv, "service_tier").Should().BeEmpty("'As the CLI is set' sends no tier");
                    Setting(argv, "model_reasoning_effort").Should().BeEmpty();
                    var effective = CodexConfigFile.TextIn(name => sent.Environment.GetValueOrDefault(name) ?? Environment.GetEnvironmentVariable(name));
                    (Text(line, "tier"), Text(line, "tierFrom")).Should().Be((CodexConfigFile.RootValue(effective, "service_tier"), OptionSources.CliConfig),
                        "the CLI ran with what the config.toml its environment points at names");
                    (Text(line, "effort"), Text(line, "effortFrom")).Should().Be((CodexConfigFile.RootValue(effective, "model_reasoning_effort"), OptionSources.CliConfig));
                    (Text(line, "effort"), Text(line, "tier")).Should().Be(("xhigh", "priority"), $"the launch's CODEX_HOME was {codexHome}");
                    Text(line, "cliVersion").Should().BeEmpty("no --version was asked for a row whose tier does not depend on the release");
                    break;
                default:
                    (Text(line, "effort"), Text(line, "effortFrom")).Should().Be((After(argv, "--effort"), OptionSources.Row));
                    After(argv, "--effort").Should().Be("high");
                    (Text(line, "tier"), Text(line, "tierFrom")).Should().Be((ClaudeTierIn(argv), OptionSources.Row));
                    ClaudeTierIn(argv).Should().Be("fast", "an On row on an Opus model is handed the fastMode:true file");
                    break;
            }

            Text(line, "cli").Should().Be(Text(line, "provider") == "claude" ? "claude" : "codex");
        }
    }

    private static string VersionAnswer() => Environment.GetEnvironmentVariable("FAKECLI_VERSION_STDOUT") ?? string.Empty;

    /// <summary>The value of a codex <c>-c key=value</c> override on the argv, or empty.</summary>
    private static string Setting(IReadOnlyList<string> argv, string key) =>
        argv.Zip(argv.Skip(1)).Where(pair => pair.First == "-c" && pair.Second.StartsWith(key + "=", StringComparison.Ordinal))
            .Select(pair => pair.Second[(key.Length + 1)..]).FirstOrDefault() ?? string.Empty;

    private static string After(IReadOnlyList<string> argv, string flag) =>
        argv.Zip(argv.Skip(1)).Where(pair => pair.First == flag).Select(pair => pair.Second).FirstOrDefault() ?? string.Empty;

    /// <summary>What the <c>--settings</c> file claude was handed says about its fast mode — in the ledger's words.</summary>
    private static string ClaudeTierIn(IReadOnlyList<string> argv)
    {
        var file = After(argv, "--settings");
        if (file.Length == 0)
        {
            return string.Empty;
        }

        return JsonDocument.Parse(File.ReadAllText(file)).RootElement.GetProperty("fastMode").GetBoolean() ? "fast" : "default";
    }

    private async Task GitRepository()
    {
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(Repo, "Query.cs"), "class Query { int F() => 1; }\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
    }

    private async Task Git(params string[] args)
    {
        var result = await _git.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], Repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    /// <summary>The real launcher, with every request it was handed kept — the argv AND the environment.</summary>
    private sealed class Recording(IProcessLauncher inner) : IProcessLauncher
    {
        private readonly ConcurrentQueue<ProcessRequest> _requests = new();

        public IEnumerable<string> VersionsAsked => _requests.Where(r => r.Arguments is ["--version"]).Select(r => r.Executable);

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            _requests.Enqueue(request);

            return inner.RunAsync(request, ct);
        }

        /// <summary>
        /// The review launch of one row and role: a codex launch by its answer file (<c>&lt;provider&gt;-&lt;role&gt;.json</c>),
        /// claude's by its <c>-p</c> — every claude launch of the round is the one claude row's.
        /// </summary>
        public ProcessRequest LaunchOf(string provider, string role) =>
            _requests.First(r => provider == "claude"
                ? r.Arguments is ["-p", ..]
                : r.Arguments.Contains("exec") && Path.GetFileName(After(r.Arguments, "-o")) == $"{provider}-{role}.json");
    }
}
