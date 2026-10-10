using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>COAI_CREDS_KEY</c> is read from the same LAYERED configuration as every other setting: the
/// settings file the panel writes, with the client's environment outranking it key by key.
/// </summary>
/// <remarks>
/// <para>Measured 2026-10-01 on the operator's machine: the key was saved in the panel, the file held
/// it, the server was reconnected — and <c>providers</c> answered <c>vaultRead: false</c>, "no
/// COAI_CREDS_KEY configured". The extension writes the key into the settings file and nowhere else
/// (<c>envBlock</c>); no spawn of its carries it in the environment. Every vault read — serve,
/// <c>--providers</c>, <c>--probe-api</c> — took it from the raw environment, three lines after
/// building the layered configuration everything else was read from. So every vendor that needs a
/// vault key was badged "cannot review" and dropped from every round, with its key in the vault.</para>
/// <para>The serving path cannot be driven here without letting a real binary resolve <c>creds</c>
/// from PATH and APPDATA — which would read the person's real vault. Its guard is therefore
/// structural, and the shape it forbids is the exact line that shipped.</para>
/// </remarks>
public sealed partial class TheVaultKeyIsReadWhereThePanelWritesItTests : IDisposable
{
    private const string VendorKey = "sk-or-0123456789abcdefghijklmnopqrstuv";

    private readonly string _dir = Directory.CreateTempSubdirectory("coai-vault-key-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp directory that outlives one run is litter, not a failed test.
        }
    }

    [Fact]
    public async Task Providers_reads_the_vault_with_the_key_the_settings_file_holds()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), """{"COAI_CREDS_KEY": "the-key-the-panel-saved"}""");
        var vault = new VaultAnswers("{\"openrouter\":\"" + VendorKey + "\"}");
        var output = new StringWriter();

        var code = await Program.ProvidersJsonCoreAsync(
            name => name == "COAI_DATA_DIR" ? _dir : null, vault, output, _ => { });

        code.Should().Be(0);
        var answer = JsonDocument.Parse(output.ToString()).RootElement;
        answer.GetProperty("vaultRead").GetBoolean().Should().BeTrue(answer.GetProperty("vaultNote").GetString());
        answer.GetProperty("vaultKeyNames").EnumerateArray().Select(n => n.GetString()).Should().Equal("openrouter");
        // Hoisted: the predicate is an expression tree, and one may not hold a collection expression.
        // The key travels on stdin, never as an argument (PLAN_creds_config_key_on_stdin.md).
        string[] theReadAsks = ["config", "-"];
        vault.Requests.Should().Contain(r => r.Arguments.SequenceEqual(theReadAsks) && r.StdIn == "the-key-the-panel-saved\n");
        output.ToString().Should().NotContain(VendorKey, "only the key NAMES leave the process");
    }

    [Fact]
    public async Task Providers_still_lets_the_environment_outrank_the_settings_file()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), """{"COAI_CREDS_KEY": "the-key-the-panel-saved"}""");
        var vault = new VaultAnswers("{\"openrouter\":\"" + VendorKey + "\"}");

        await Program.ProvidersJsonCoreAsync(
            name => name switch
            {
                "COAI_DATA_DIR" => _dir,
                "COAI_CREDS_KEY" => "the-client-env-key",
                _ => null,
            },
            vault,
            new StringWriter(),
            _ => { });

        var read = vault.Requests.Where(IsTheVaultRead).Should().ContainSingle().Subject;
        read.Arguments.Should().Equal("config", "-");
        read.StdIn.Should().Be("the-client-env-key\n");
    }

    /// <summary>A delegate lookup of the key variable — the shape every vault read shipped with.</summary>
    [GeneratedRegex(@"\(\s*(?:Server\.)?KeyVault\.KeyVariable\s*\)")]
    private static partial Regex KeyLookedUpByHand();

    /// <summary>A CALL of the sanctioned read (the leading dot leaves its declaration out), capturing what it was handed.</summary>
    [GeneratedRegex(@"\.ReadFromConfigurationAsync\(\s*(\w+)")]
    private static partial Regex SanctionedRead();

    [Fact]
    public void No_production_file_looks_the_key_up_outside_the_vault()
    {
        var offenders = Lookups()
            .Where(lookup => !IsTheVault(lookup.File))
            .Select(lookup => $"{lookup.File}: {lookup.Text}")
            .ToList();

        offenders.Should().BeEmpty(
            "the key comes from the layered configuration through KeyVault.ReadFromConfigurationAsync — "
            + "a raw lookup skips the settings file the panel writes it into");
    }

    /// <summary>The companion the prohibition needs: over the same tree, the pattern still finds the ONE sanctioned lookup.</summary>
    [Fact]
    public void The_scan_still_finds_the_one_sanctioned_lookup_inside_the_vault()
    {
        Lookups().Where(lookup => IsTheVault(lookup.File)).Should().ContainSingle(
            "KeyVault.ReadFromConfigurationAsync looks the key up qualified, so a pattern that stops matching it "
            + "would also stop matching every offender, and the prohibition above would pass over nothing");
    }

    private static List<(string File, string Text)> Lookups() =>
        [.. ProductionSources().SelectMany(file => KeyLookedUpByHand().Matches(File.ReadAllText(file)).Select(m => (Relative(file), m.Value)))];

    private static bool IsTheVault(string relativeFile) =>
        relativeFile.EndsWith(Path.Combine("Server", "KeyVault.cs"), StringComparison.Ordinal);

    [Fact]
    public void Every_vault_read_is_handed_the_layered_configuration()
    {
        var reads = ProductionSources()
            .SelectMany(file => SanctionedRead().Matches(File.ReadAllText(file)).Select(m => (File: Relative(file), Argument: m.Groups[1].Value)))
            .ToList();

        // The companion a scan needs: it must still FIND the reads, or it passes over nothing. Serve,
        // `--providers` and `--probe-api` are the three that read the vault today.
        reads.Should().HaveCountGreaterThanOrEqualTo(3, "the scan stopped finding the vault reads");
        reads.Should().OnlyContain(
            read => read.Argument == "configuration",
            "the environment alone is the defect this guards; the layered configuration is what carries the panel's key");
    }

    /// <summary>The refuted approach, kept in the suite: the prohibition must match the lines that shipped.</summary>
    [Fact]
    public void The_lines_that_shipped_the_defect_are_what_the_scan_refuses()
    {
        KeyLookedUpByHand().IsMatch("KeyVault.ForThisMachine(launcher, Environment.GetEnvironmentVariable).ReadAsync(Environment.GetEnvironmentVariable(KeyVault.KeyVariable));")
            .Should().BeTrue("serve, as it shipped");
        KeyLookedUpByHand().IsMatch(".ReadAsync(Environment.GetEnvironmentVariable(Server.KeyVault.KeyVariable));")
            .Should().BeTrue("--providers, as it shipped");
        KeyLookedUpByHand().IsMatch("var keys = await KeyVault.ForThisMachine(launcher, env).ReadAsync(env(KeyVault.KeyVariable));")
            .Should().BeTrue("--probe-api, as it shipped");
        SanctionedRead().Match("KeyVault.ForThisMachine(launcher, env).ReadFromConfigurationAsync(env)").Groups[1].Value
            .Should().Be("env", "a read handed the raw environment is caught by the argument check");
    }

    private static List<string> ProductionSources()
    {
        var root = Path.Combine(RepoRoot(), "src_mcp", "src");
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        files.Should().Contain(file => file.EndsWith("Program.cs", StringComparison.Ordinal), "the scan must reach the production sources");

        return files;
    }

    private static string Relative(string file) => Path.GetRelativePath(RepoRoot(), file);

    /// <summary>Whether a launch is the vault's own read — <c>creds config -</c> — rather than a CLI probe.</summary>
    private static bool IsTheVaultRead(ProcessRequest request) =>
        request.Arguments.Count > 0 && request.Arguments[0] == "config";

    /// <summary>Walks up to the repository root, which the test binary sits four folders under.</summary>
    private static string RepoRoot()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !Directory.Exists(Path.Combine(here.FullName, ".agents")))
        {
            here = here.Parent;
        }

        return here?.FullName ?? AppContext.BaseDirectory;
    }

    /// <summary>
    /// The vault, answering <c>creds config</c> with a body and every other launch — the vendor CLIs'
    /// version probes — with a version line; and recording what it was asked.
    /// </summary>
    /// <remarks>
    /// Answering every launch with the vault body made each CLI "report" the key as its version, which
    /// is the fixture talking, not the product: a stand-in must not be more permissive than what it
    /// replaces.
    /// </remarks>
    private sealed class VaultAnswers(string stdout) : IProcessLauncher
    {
        private readonly List<ProcessRequest> _requests = [];

        public IReadOnlyList<ProcessRequest> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            lock (_requests)
            {
                _requests.Add(request);
            }

            // The vault's own --help probe is answered as a current creds CLI would: naming the marker.
            var body = IsTheVaultRead(request)
                ? stdout
                : request.Arguments is ["--help"] ? "creds config -  (" + Server.KeyVault.StdinMarker + ")" : "fake-cli 1.0.0";

            return Task.FromResult(new ProcessResult(0, body, string.Empty, false));
        }
    }
}
