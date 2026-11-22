using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoaiMcp.Install;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Tests;

/// <summary>
/// A scratch machine for the installer's scenarios: a temp folder that is HOME, USERPROFILE, CLAUDE_CONFIG_DIR and CODEX_HOME at once (the four a vendor child is started with),
/// a PATH folder holding wrapper files named <c>claude</c> and <c>codex</c> that start the suite's FakeCli as the vendor's MCP verbs, and a <c>coai-mcp</c> file to register.
/// </summary>
/// <remarks>
/// <para><b>It cannot reach the real home.</b> The installer reads its environment from the INJECTED function, and a vendor child's four variables come from the same place
/// (<see cref="InstallHost.VendorEnvironment"/>), so nothing here depends on the test process's own HOME. <see cref="Paths"/> lists every file the installer could name, and
/// <c>TheTestsNeverReachTheRealHomeTests</c> fails if one is outside this folder or under the real home.</para>
/// <para>The subject is the REAL installer over REAL files, and the vendors are the only doubles — the collaborators a scenario cannot have (a signed-in Claude Code on a CI runner).</para>
/// </remarks>
internal sealed class InstallWorld : IDisposable
{
    private static long _tick;

    private readonly TempDir _root = TempDir.For("coai-install-");
    private readonly Dictionary<Client, string> _steering = [];
    private readonly bool _claude;
    private readonly bool _codex;

    private InstallWorld(bool claude, bool codex, bool claudeConfigDir, bool codexHome)
    {
        _claude = claude;
        _codex = codex;
        Home = _root.At("home");
        Bin = _root.At("bin");
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(Bin);
        ClaudeConfigDir = claudeConfigDir ? _root.At("claude-dir") : string.Empty;
        CodexHome = codexHome ? _root.At("codex-dir") : string.Empty;
        Log = _root.At("calls.log");
        Binary = MakeBinary(_root.At("opt", "coai"));
        WriteWrappers();
    }

    /// <summary>A world with claude and codex installed, the defaults of the common scenario.</summary>
    internal static InstallWorld Create(bool claude = true, bool codex = true, bool claudeConfigDir = false, bool codexHome = false) =>
        new(claude, codex, claudeConfigDir, codexHome);

    internal string Home { get; }

    internal string Bin { get; }

    internal string ClaudeConfigDir { get; }

    internal string CodexHome { get; }

    internal string Log { get; }

    /// <summary>The <c>coai-mcp</c> the installer registers (<c>--binary</c>).</summary>
    internal string Binary { get; }

    /// <summary>The folder the world lives in — and the only one it may touch.</summary>
    internal string Root => _root.Path;

    internal string ClaudeJson => Path.Combine(ClaudeConfigDir.Length > 0 ? ClaudeConfigDir : Home, ".claude.json");

    internal string CodexToml => Path.Combine(CodexHome.Length > 0 ? CodexHome : Path.Combine(Home, ".codex"), "config.toml");

    internal string StateDir => Path.Combine(Home, ".local", "state", "coai");

    internal string BackupsDir => Path.Combine(StateDir, "install-backups");

    /// <summary>The environment the installer is given: the four variables, a PATH of the wrappers' folder alone, and nothing of the real machine's.</summary>
    internal IReadOnlyDictionary<string, string?> Env => new Dictionary<string, string?>
    {
        ["HOME"] = Home,
        ["USERPROFILE"] = Home,
        ["CLAUDE_CONFIG_DIR"] = ClaudeConfigDir.Length > 0 ? ClaudeConfigDir : null,
        ["CODEX_HOME"] = CodexHome.Length > 0 ? CodexHome : null,
        ["PATH"] = Bin,
        ["LOCALAPPDATA"] = Path.Combine(Home, "AppData", "Local"),
    };

    /// <summary>Every file the installer could name in this world — what the guard test checks against the real home.</summary>
    internal IReadOnlyList<string> Paths
    {
        get
        {
            var host = InstallHost.From(name => Env.GetValueOrDefault(name), OperatingSystem.IsWindows());

            return [host.ClaudeJson, host.ClaudeSettings, host.CodexToml, host.AgyJson, host.StateDir, host.BackupsDir, host.LockFile, host.LocalBin];
        }
    }

    private string MakeBinary(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, OperatingSystem.IsWindows() ? "coai-mcp.exe" : "coai-mcp");
        File.WriteAllText(path, "#!/bin/sh\necho 'coai-mcp 9.9.9'\n");
        Executable(path);

        return path;
    }

    /// <summary>A second <c>coai-mcp</c> in another folder, named for the file the installer recognises as ours.</summary>
    internal string AnotherBinary(params string[] folder)
    {
        var path = Path.Combine(MakeBinaryFolder(folder), OperatingSystem.IsWindows() ? "coai-mcp.exe" : "coai-mcp");
        File.WriteAllText(path, "#!/bin/sh\necho 'coai-mcp 1.0.0'\n");
        Executable(path);

        return path;
    }

    private string MakeBinaryFolder(string[] folder)
    {
        var path = Path.Combine([_root.Path, .. folder]);
        Directory.CreateDirectory(path);

        return path;
    }

    private static void Executable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
    }

    /// <summary>Steers one vendor stand-in: <c>fail: "add-json:1"</c> fails its first <c>add-json</c>; <c>hang</c> sleeps past the installer's timeout. Rewrites the wrapper.</summary>
    internal void Steer(Client cli, string fail = "", string hang = "")
    {
        _steering[cli] = $"FAKECLI_MCP_FAIL='{fail}' FAKECLI_MCP_HANG='{hang}'";
        WriteWrappers();
    }

    private void WriteWrappers()
    {
        foreach (var (installed, cli) in new[] { (_claude, Client.Claude), (_codex, Client.Codex) })
        {
            if (!installed)
            {
                continue;
            }

            var steering = _steering.GetValueOrDefault(cli, string.Empty);
            if (OperatingSystem.IsWindows())
            {
                var set = string.Concat(steering.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(one => $"set {one.Replace("'", string.Empty)}\r\n"));
                File.WriteAllText(Path.Combine(Bin, cli.Word() + ".cmd"), $"@echo off\r\nset FAKECLI_MCP_LOG={Log}\r\n{set}\"{FakeCliInvocations.Exe}\" vendor-mcp {cli.Word()} %*\r\n");
            }
            else
            {
                var path = Path.Combine(Bin, cli.Word());
                File.WriteAllText(path, $"#!/bin/sh\nFAKECLI_MCP_LOG='{Log}' {steering} exec '{FakeCliInvocations.Exe}' vendor-mcp {cli.Word()} \"$@\"\n");
                Executable(path);
            }
        }
    }

    internal SetupDependencies Deps(TimeSpan? vendorTimeout = null, InstallFaults? faults = null, string? self = null) => new(
        name => Env.GetValueOrDefault(name),
        OperatingSystem.IsWindows(),
        "linux",
        self ?? Binary,
        "9.9.9",
        new ProcessLauncher(),
        new TickingClock(),
        4242,
        vendorTimeout ?? TimeSpan.FromSeconds(30),
        faults ?? InstallFaults.None);

    /// <summary>Runs <c>--setup-clients</c> with <paramref name="args"/> against this world; the object printed, the exit code and what went to stderr.</summary>
    internal Task<(int Code, JsonElement Out, string Err)> RunAsync(params string[] args) => RunAsync(Deps(), "setup-clients", args);

    internal async Task<(int Code, JsonElement Out, string Err)> RunAsync(SetupDependencies deps, string mode, params string[] args)
    {
        var output = new StringWriter();
        var errors = new List<string>();
        var code = await SetupClientsMode.RunAsync(mode, args, deps, output, errors.Add);
        var said = output.ToString();

        return (code, said.Length == 0 ? default : JsonDocument.Parse(said).RootElement.Clone(), string.Join('\n', errors));
    }

    /// <summary>The vendor stand-ins' calls, one line each — empty when nothing was run.</summary>
    internal IReadOnlyList<string> Calls => File.Exists(Log) ? File.ReadAllLines(Log) : [];

    internal string ClaudeJsonText => File.Exists(ClaudeJson) ? File.ReadAllText(ClaudeJson) : string.Empty;

    internal string CodexTomlText => File.Exists(CodexToml) ? File.ReadAllText(CodexToml) : string.Empty;

    internal IReadOnlyList<string> BackupFolders => Directory.Exists(BackupsDir) ? [.. Directory.GetDirectories(BackupsDir).Order(StringComparer.Ordinal)] : [];

    /// <summary>One hash over every file under the home folder — names and bytes — so "nothing changed" is a comparison, not a hope.</summary>
    internal string HomeHash()
    {
        var all = Directory.GetFiles(Home, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal);
        var text = string.Concat(all.Select(file => $"{Path.GetRelativePath(Home, file)}={(Path.GetFileName(file) == "install.lock" ? "lock" : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))))};"));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>Writes a file of the world, making its folder.</summary>
    internal void Put(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose() => _root.Dispose();

    /// <summary>A clock that moves a millisecond per read, so two backups of one test never share a name.</summary>
    private sealed class TickingClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero).AddMilliseconds(Interlocked.Increment(ref _tick));
    }
}
