using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Install;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Scenario 22: the REAL <c>claude</c> and <c>codex</c> on a clean scratch HOME — after one command, <c>claude mcp get coai</c> says Connected and <c>codex mcp get coai --json</c> holds the path.
/// </summary>
/// <remarks>
/// <para>Runs ONLY with <c>COAI_REAL_CLIS=1</c> — and a run without it is reported SKIPPED, never green: "not run" is not a pass. It needs the two CLIs installed on this box, and it
/// never touches the real home: HOME, USERPROFILE, CLAUDE_CONFIG_DIR and CODEX_HOME are one scratch folder, for the installer and for every command this test starts. The registered binary is the
/// one this build made, so <c>claude mcp get</c> starts the real server for its health check.</para>
/// </remarks>
public sealed class TheRealClisAcceptTheEntryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task AfterOneCommand_ClaudeIsConnected_AndCodexHoldsThePath()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("COAI_REAL_CLIS") == "1", "the real CLIs run only with COAI_REAL_CLIS=1 — this is a skip, not a pass");
        using var scratch = TempDir.For("coai-real-clis-");
        var env = new Dictionary<string, string?>
        {
            ["HOME"] = scratch.At("home"),
            ["USERPROFILE"] = scratch.At("home"),
            ["CLAUDE_CONFIG_DIR"] = null,
            ["CODEX_HOME"] = null,
            ["PATH"] = Environment.GetEnvironmentVariable("PATH"),
            ["LOCALAPPDATA"] = scratch.At("home", "AppData", "Local"),
            // Passed on as it is: the installer meets the real condition (a session or not) exactly as a person would, and the vendor child inherits the same variable.
            ["CLAUDE_CODE_CHILD_SESSION"] = Environment.GetEnvironmentVariable("CLAUDE_CODE_CHILD_SESSION"),
        };
        Directory.CreateDirectory(env["HOME"]!);
        var host = InstallHost.From(name => env.GetValueOrDefault(name), OperatingSystem.IsWindows());
        var finder = new ClientFinder(host, ExecutableResolver.IsInstalledFile, new ProcessLauncher());
        var claude = finder.PathOf(Client.Claude);
        var codex = finder.PathOf(Client.Codex);
        Assert.SkipWhen(claude.Length == 0 || codex.Length == 0, $"needs claude and codex installed (claude: '{claude}', codex: '{codex}')");
        var binary = ServerBinary.Path;
        File.Exists(binary).Should().BeTrue($"{binary} is built by the solution");

        var output = new StringWriter();
        var deps = new SetupDependencies(name => env.GetValueOrDefault(name), OperatingSystem.IsWindows(), "linux", binary, "real", new ProcessLauncher(), TimeProvider.System, Environment.ProcessId, TimeSpan.FromSeconds(60), InstallFaults.None);
        var errors = new List<string>();
        var code = await SetupClientsMode.RunAsync("setup-clients", ["--only", "claude,codex", "--binary", binary], deps, output, errors.Add);

        var root = JsonDocument.Parse(output.ToString()).RootElement;
        root.Result("codex").Should().Be("changed", string.Join('\n', errors) + output);
        if (InstallHost.From(name => env.GetValueOrDefault(name), OperatingSystem.IsWindows()).InClaudeSession)
        {
            // Measured 2026-11-22 on claude 2.1.330: inside a Claude Code session (CLAUDE_CODE_CHILD_SESSION set) `claude mcp add-json` REFUSES stdin. The installer meets that BEFORE any command: claude is `refused`,
            // the exit is 78, the vendor is not asked, and the session variable is not stripped. "Connected" is then NOT verified by this run: it needs a terminal outside a session.
            code.Should().Be(78, output.ToString());
            root.Result("claude").Should().Be("refused");
            root.Client("claude").GetProperty("reason").GetString().Should().Contain("inside a Claude Code session the claude CLI refuses this registration");
            var claudeJson = Path.Combine(env["HOME"]!, ".claude.json");
            (File.Exists(claudeJson) ? File.ReadAllText(claudeJson) : string.Empty).Should().NotContain("coai-mcp", "nothing was registered (claude itself writes its first-run file)");
        }
        else
        {
            code.Should().Be(0, string.Join('\n', errors) + output);
            root.Result("claude").Should().Be("changed");
            Run(claude, ["mcp", "get", "coai"], env).Should().Contain("Connected", "claude started the registered server and it answered");
        }

        var codexGet = Run(codex, ["mcp", "get", "coai", "--json"], env);
        codexGet.Should().Contain(binary.Replace("\\", "\\\\"));
    }

    private static string Run(string exe, string[] args, Dictionary<string, string?> env)
    {
        var info = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        foreach (var (name, value) in env)
        {
            info.Environment[name] = value;
        }

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Deadline))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{exe} {string.Join(' ', args)} did not finish");
        }

        return stdout.Result;
    }
}
