using System.Diagnostics;
using CoaiMcp.Core.Notices;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The restart storm of 2026-10-06 (<c>todo/PLAN_an_idle_server_is_idle_and_starts_at_once.md</c>, D2): a client gives a
/// server about thirty seconds to answer <c>initialize</c>, and this server first read the vault (<c>creds config</c>, a
/// thirty-second timeout of its own) and then built its service with every startup sweep, all before it read a byte of
/// stdin. That took 19–32 s in the logs and 30–62 s on a copy of the data; the client killed it, started another, and seven sessions did it together.
/// </summary>
/// <remarks>
/// The real binary over real stdio, because what is asserted is the ORDER the process does things in. The slow parts are
/// made deterministic rather than hoped for: <c>creds</c> on the server's PATH is the fake CLI sleeping, and so is the
/// CLI the consultants survey probes.
/// </remarks>
public sealed class TheServerAnswersInitializeAtOnceTests : IDisposable
{
    /// <summary>Far longer than the answer may take, so a server that waits for it cannot pass by luck.</summary>
    private const int SlowMs = 20_000;

    private readonly string _data = Directory.CreateTempSubdirectory("coai-init-").FullName;
    private readonly string _bin = Directory.CreateTempSubdirectory("coai-init-bin-").FullName;

    public void Dispose()
    {
        foreach (var dir in (string[])[_data, _bin])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A child may still hold a file for a moment after the kill; the temp directory is not ours to wait on.
            }
        }
    }

    [Fact]
    public async Task Initialize_IsAnswered_WhileTheVaultReadAndTheProbeAreStillRunning()
    {
        var fake = SlowCredsOnPath();
        using var server = StdioServer.Start(_data, "debug", 1800,
            ("PATH", _bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")),
            ("COAI_CREDS_KEY", "test-config-key"),
            ("FAKECLI_HELP_STDOUT", "creds config -  (" + Server.KeyVault.StdinMarker + ")"), // a current creds: the key goes on stdin
            ("COAI_EXE_CODEX", fake),
            ("FAKECLI_MODE", "vendor"),
            ("FAKECLI_SLEEP_MS", SlowMs.ToString()));
        try
        {
            var clock = Stopwatch.StartNew();

            // Ten seconds, against a vault read that takes twenty: only a server that answers BEFORE it can pass.
            await StdioServer.RoundTrip(server,
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},"
                + "\"clientInfo\":{\"name\":\"init-test\",\"version\":\"0\"}}}",
                timeoutSeconds: 10);
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(SlowMs), "initialize must not wait for the vault read");

            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.FlushAsync();

            // And a TOOL still sees the finished start: providers answers once the vault read has ended, and says how it went.
            var providers = await StdioServer.Call(server, 2, "providers", "{}", timeoutSeconds: 90);
            // The fake prints no JSON body, so the read the tool waited for ends as the vault's own "not valid JSON" sentence.
            providers.ToString().Should().Contain("config entry", "the vault read finished before the tool answered, and its outcome is reported");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    /// <summary>
    /// Own review of the branch (2026-10-07): a client that leaves while the vault is still being read cancels the start —
    /// but the launcher answers a cancelled child as a TIMED-OUT one, so the start went on, logged
    /// <c>starting: … creds config timed out</c> (false) and ran every startup sweep in a process that was ending. A start
    /// whose stop came during the vault read now ends there.
    /// </summary>
    [Fact]
    public async Task AClientThatLeavesDuringTheVaultRead_EndsTheStart_WithoutAFalseVaultLine()
    {
        var fake = SlowCredsOnPath();
        using var server = StdioServer.Start(_data, "debug", 1800,
            ("PATH", _bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")),
            ("COAI_CREDS_KEY", "test-config-key"),
            ("FAKECLI_HELP_STDOUT", "creds config -  (" + Server.KeyVault.StdinMarker + ")"), // a current creds: the key goes on stdin
            ("COAI_EXE_CODEX", fake),
            ("FAKECLI_MODE", "vendor"),
            ("FAKECLI_SLEEP_MS", SlowMs.ToString()));
        try
        {
            await StdioServer.InitializeAsync(server);

            server.StandardInput.Close(); // the client goes away while `creds` still sleeps
            await server.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));

            var logged = string.Concat(Directory.EnumerateFiles(Path.Combine(_data, "logs"), "*.log", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
            logged.Should().NotContain("starting:", "the vault read was cut short by the ending, not timed out, and nothing was started");
        }
        finally
        {
            if (!server.HasExited)
            {
                await StdioServer.StopAsync(server);
            }
        }
    }

    /// <summary>
    /// The failing start, through the real binary (a gap the plan recorded, closed with the cadence consultant's fixture,
    /// 2026-10-07): a session file whose state is null makes the startup sweep throw. The server must end as the crash
    /// it is — exit 70 and the crash written down — as it did when the start ran before serving. Whether `initialize`
    /// is answered first is a race with a start that fails in milliseconds, so it is not asserted.
    /// </summary>
    [Fact]
    public async Task AStartThatFails_EndsAsARecordedCrash_NeverAsAClosedConnection()
    {
        Directory.CreateDirectory(Path.Combine(_data, "sessions"));
        File.WriteAllText(Path.Combine(_data, "sessions", "session-bad.json"), """{"state":null,"rounds":[]}""");
        using var server = StdioServer.Start(_data, "debug", 1800);
        try
        {
            try
            {
                await server.StandardInput.WriteLineAsync(
                    """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"init-test","version":"0"}}}""");
                await server.StandardInput.FlushAsync();
            }
            catch (IOException)
            {
                // The server may already have ended: the start fails in milliseconds.
            }

            await server.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));

            server.ExitCode.Should().Be(HostCrash.ExitCode, "a start that failed is a crash, never a client that went away");
            File.ReadAllText(Path.Combine(_data, ServerNotices.Name)).Should().Contain("ServerStartFailed",
                "the crash is written down where the panel reads it");
        }
        finally
        {
            if (!server.HasExited)
            {
                await StdioServer.StopAsync(server);
            }
        }
    }

    /// <summary>
    /// The fake CLI copied in as <c>creds</c>, next to its own assembly, in a directory of the test's that goes FIRST on the
    /// server's PATH — the vault probes <c>creds --help</c> and reads <c>creds config -</c> off PATH. Returns the fake's own path for the probe.
    /// </summary>
    private string SlowCredsOnPath()
    {
        var exe = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        foreach (var file in FakeCliFiles())
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, file), Path.Combine(_bin, file));
        }

        var creds = Path.Combine(_bin, "creds" + exe);
        File.Copy(Path.Combine(_bin, "FakeCli" + exe), creds);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(creds, File.GetUnixFileMode(Path.Combine(_bin, "FakeCli")));
        }

        return Path.Combine(_bin, "FakeCli" + exe);
    }

    /// <summary>
    /// The fake's own files and every assembly its <c>deps.json</c> names — read from that file rather than listed here,
    /// so a reference the fake gains later is copied too (a hand list went stale the first time it ran: the fake loads
    /// <c>CoaiMcp.Runners</c>).
    /// </summary>
    private static IEnumerable<string> FakeCliFiles()
    {
        var deps = Path.Combine(AppContext.BaseDirectory, "FakeCli.deps.json");
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(deps));
        var assemblies = document.RootElement.GetProperty("targets").EnumerateObject()
            .SelectMany(target => target.Value.EnumerateObject())
            .Where(library => library.Value.TryGetProperty("runtime", out _))
            .SelectMany(library => library.Value.GetProperty("runtime").EnumerateObject().Select(file => Path.GetFileName(file.Name)))
            .ToList(); // read now: the document is disposed when this method returns

        return Directory.EnumerateFiles(AppContext.BaseDirectory, "FakeCli*").Select(Path.GetFileName).OfType<string>()
            .Concat(assemblies)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(file => File.Exists(Path.Combine(AppContext.BaseDirectory, file)));
    }
}
