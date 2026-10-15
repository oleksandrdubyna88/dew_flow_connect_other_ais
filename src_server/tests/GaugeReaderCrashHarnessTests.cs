using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The shipping proof of story 6.1a (<c>todo/PLAN_every_round_is_counted.md</c>, the epic-6 risk consultation): the
/// REAL server binary, run as a child, with a fake <c>codex app-server</c> on its PATH that survives the parent that
/// started it, over disposable fake credentials — killed mid-read, and what the containment does about it.
/// </summary>
/// <remarks>
/// <para>What it proves: an already-running contender and a newly started server launch nothing on the account until
/// the reader's tree — the reader AND its grandchild — is gone; a crash between the marker and the registration, and a
/// partial record, keep the slot unavailable across another restart; a process under a reused number is untouched.
/// What it does NOT prove, recorded here as the plan asked: that a vendor-side rotation survives an interruption
/// before local persistence — nothing here talks to a vendor — and a kill that fails for real, which no test machine
/// can stage safely (<see cref="ReaderBarrierTests.AFailedTermination_KeepsTheSlotUnavailable_AcrossARestart"/> stages
/// it through the reconcile's seam).</para>
/// <para>The reviewer the job launches is the same fake <c>codex</c>, which does not know <c>exec</c> and exits 64: the
/// job fails at the vendor, which is a finished job, and the capture runs after it — the harness needs no answer from a
/// reviewer. The fake app-server is steered by a file in the server's temp directory, because a reader is launched
/// confined and no variable of this test's reaches it.</para>
/// <para>Linux and Windows only: the tree discipline lists descendants there; on macOS the capture refuses to spawn, and
/// so there is no crash to stage.</para>
/// </remarks>
public sealed class GaugeReaderCrashHarnessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coai-crash-" + Guid.NewGuid().ToString("N"));

    private readonly List<ServerChild> _servers = [];

    private readonly List<Process> _helpers = [];

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private string DataDir => Path.Combine(_root, "data");

    private string Temp => Path.Combine(_root, "tmp");

    private string PidFile => Path.Combine(_root, "reader.pids");

    private string StartedFile => Path.Combine(_root, "reader.started");

    public GaugeReaderCrashHarnessTests()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(Temp);
        File.WriteAllText(Path.Combine(DataDir, "vendors.json"), """[{ "id": "codex", "runtime": "codex", "models": ["m"], "slots": ["a"] }]""");
        var registry = new SlotRegistry(DataDir, new JsonFileStore());
        registry.MarkSignedIn(registry.Read("codex", "a"));
        GaugeFixtures.WriteAuth(registry.Read("codex", "a").Directory, DateTimeOffset.UtcNow.AddDays(9));
        File.WriteAllText(
            Path.Combine(Temp, "fakecli-appserver.json"),
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["pidFile"] = PidFile,
                ["startedFile"] = StartedFile,
                ["holdMs"] = "120000",
                ["afterEofMs"] = "-1",
                ["grandchild"] = "1",
            }));
    }

    public void Dispose()
    {
        foreach (var server in _servers)
        {
            server.Dispose();
        }

        foreach (var helper in _helpers)
        {
            EndQuietly(helper);
        }

        foreach (var pid in ReaderPids())
        {
            EndQuietly(pid);
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp directory a dying child still holds is the OS's to clean.
        }
    }

    [Fact]
    public async Task ACrashMidRead_AnAlreadyRunningContender_ReconcilesTheTreeBeforeItLaunchesAnything()
    {
        Assert.SkipUnless(ProcessTree.Supported, "the capture refuses to spawn where descendants cannot be listed");
        var a = await StartServerAsync();
        var contender = await StartServerAsync();
        await SubmitAsync(a);
        var reader = await ReaderStartedAsync(a);

        var marker = State().Reader;
        marker.Should().NotBeNull("the marker was written before the spawn");
        marker!.Phase.Should().Be(ReaderPhases.Running);
        marker.Tree.Select(member => member.Pid).Should().Contain(reader.Root.Pid, "the child's identity was recorded as it started");

        a.Crash();
        ProcessTree.IsAlive(reader.Root).Should().BeTrue("the reader survived the server that started it");
        ProcessTree.IsAlive(reader.Grandchild).Should().BeTrue();

        var job = await SubmitAsync(contender);
        var ended = await TerminalAsync(contender, job);

        ended.Should().NotBe("queued", "the contender took the account once the tree was gone, and ran the job");
        ProcessTree.IsAlive(reader.Root).Should().BeFalse("the contender's acquisition killed the reader before the lease was usable");
        ProcessTree.IsAlive(reader.Grandchild).Should().BeFalse("and its grandchild");
        // The contender's own capture after its job may have a reader of its own in flight by now; the crashed server's
        // is named by no marker any more.
        (State().Reader?.Tree ?? []).Select(member => member.Pid).Should().NotContain(reader.Root.Pid, "the old marker was cleared on confirmation");
        State().Blocked.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task ACrashMidRead_ANewlyStartedServer_ReconcilesTheTreeAtBoot()
    {
        Assert.SkipUnless(ProcessTree.Supported, "the capture refuses to spawn where descendants cannot be listed");
        var a = await StartServerAsync();
        await SubmitAsync(a);
        var reader = await ReaderStartedAsync(a);
        a.Crash();
        ProcessTree.IsAlive(reader.Root).Should().BeTrue();

        var fresh = await StartServerAsync();

        await UntilAsync(() => !ProcessTree.IsAlive(reader.Root) && !ProcessTree.IsAlive(reader.Grandchild),
            "the startup sweep reconciles the slot before the pump claims anything", fresh);
        State().Reader.Should().BeNull();
        var job = await SubmitAsync(fresh);
        (await TerminalAsync(fresh, job)).Should().NotBe("queued", "the account is usable again");
    }

    [Theory]
    [InlineData("spawning")]
    [InlineData("partial")]
    public async Task ACrashBeforeTheRegistration_KeepsTheSlotUnavailable_AcrossAnotherRestart(string shape)
    {
        Plant(shape == "spawning"
            ? new ReaderMarkerDto(ReaderPhases.Spawning, 424242, DateTime.UtcNow, DateTimeOffset.UtcNow)
            : new ReaderMarkerDto(ReaderPhases.Running, 424242, DateTime.UtcNow, DateTimeOffset.UtcNow, [new ProcessIdentity(0, default)]));

        var first = await StartServerAsync();
        var job = await SubmitAsync(first);
        await Task.Delay(TimeSpan.FromSeconds(3));
        (await StatusAsync(first, job)).Should().Be("queued", "a reader nobody can name may still hold the account: nothing launches");
        State().Blocked.Should().Be(BlockedReasons.ReaderIdentityMissing);
        (await CatalogAsync(first)).Vendors.Single().Slots.Ready.Should().Be(0);
        first.Dispose();

        var second = await StartServerAsync();
        var again = await SubmitAsync(second);
        await Task.Delay(TimeSpan.FromSeconds(3));
        (await StatusAsync(second, again)).Should().Be("queued", "another restart proves nothing: availability is the price of an ambiguous crash");
        State().Blocked.Should().Be(BlockedReasons.ReaderIdentityMissing);
        File.Exists(PidFile).Should().BeFalse("no reader was spawned by either server");
    }

    [Fact]
    public async Task AMarkerNamingAReusedNumber_LeavesThatProcessAlone_AndTheSlotIsUsable()
    {
        var bystander = StartHelper();
        var identity = new ProcessIdentity(bystander.Id, bystander.StartTime.ToUniversalTime().AddHours(-1));
        Plant(new ReaderMarkerDto(ReaderPhases.Running, 424242, DateTime.UtcNow, DateTimeOffset.UtcNow, [identity]));

        var server = await StartServerAsync();
        var job = await SubmitAsync(server);

        (await TerminalAsync(server, job)).Should().NotBe("queued", "the marker named nothing of ours, so the account is usable");
        bystander.HasExited.Should().BeFalse("a start time that does not match is somebody else's process under a reused number");
        (State().Reader?.Tree ?? []).Select(member => member.Pid).Should().NotContain(bystander.Id, "the planted marker was cleared; any marker now is the server's own reader");
    }

    /// <summary>The real server binary as a child, over this harness's data and temp directories, with the fake codex on its PATH.</summary>
    private async Task<ServerChild> StartServerAsync()
    {
        var server = new ServerChild(DataDir, Temp, FakeCodexDirectory());
        _servers.Add(server);
        await server.StartAsync();

        return server;
    }

    /// <summary>
    /// Where a <c>codex</c> that is the fake CLI lives. Windows: a copy of the apphost named <c>codex.exe</c> beside the
    /// test binaries, where its own managed assembly and dependencies already are. Elsewhere: a script that names the
    /// runtime (a confined launch is handed no <c>DOTNET_ROOT</c>) and execs the fake in place.
    /// </summary>
    private string FakeCodexDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            // Copied once per build output: a copy a still-dying reader holds open cannot be overwritten, and a copy from the
            // same output is the same bytes.
            var copy = Path.Combine(AppContext.BaseDirectory, "codex.exe");
            if (!File.Exists(copy))
            {
                File.Copy(Path.Combine(AppContext.BaseDirectory, "FakeCli.exe"), copy);
            }

            return AppContext.BaseDirectory;
        }

        var directory = Path.Combine(_root, "bin");
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "codex");
        var runtime = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", ".."));
        File.WriteAllText(script, $"#!/bin/sh\nexport DOTNET_ROOT=\"{runtime}\"\nexec \"{Path.Combine(AppContext.BaseDirectory, "FakeCli")}\" \"$@\"\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        return directory;
    }

    private async Task<string> SubmitAsync(ServerChild server)
    {
        var response = await server.Client.PostAsJsonAsync("/api/reviews", new { vendor = "codex", model = "m", prompt = "review this", role = "Architecture", timeoutSeconds = 30 });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var accepted = JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), ServerJsonContext.Default.ReviewAcceptedDto)!;

        return accepted.Id;
    }

    private static async Task<string> StatusAsync(ServerChild server, string job)
    {
        var status = JsonSerializer.Deserialize(await server.Client.GetStringAsync($"/api/reviews/{job}"), ServerJsonContext.Default.ReviewStatusDto)!;

        return status.Status;
    }

    private static async Task<CatalogDto> CatalogAsync(ServerChild server) =>
        JsonSerializer.Deserialize(await server.Client.GetStringAsync("/api/catalog"), ServerJsonContext.Default.CatalogDto)!;

    /// <summary>Polls the job until it is done or failed; answers its last status.</summary>
    private static async Task<string> TerminalAsync(ServerChild server, string job)
    {
        var status = "queued";
        await UntilAsync(async () => (status = await StatusAsync(server, job)) is "done" or "failed", $"job {job} to end", server);

        return status;
    }

    /// <summary>Waits for the fake reader to say it is up, and answers the identities it wrote: its own and its grandchild's.</summary>
    private async Task<(ProcessIdentity Root, ProcessIdentity Grandchild)> ReaderStartedAsync(ServerChild server)
    {
        await UntilAsync(() => File.Exists(StartedFile) && ReaderPids().Count == 2, "the reader to start", server);
        var pids = ReaderPids();

        return (Identity(pids[0]), Identity(pids[1]));
    }

    private IReadOnlyList<int> ReaderPids()
    {
        try
        {
            return File.Exists(PidFile) ? [.. File.ReadAllLines(PidFile).Select(int.Parse)] : [];
        }
        catch (Exception e) when (e is IOException or FormatException)
        {
            return [];
        }
    }

    private static ProcessIdentity Identity(int pid)
    {
        using var process = Process.GetProcessById(pid);

        return new ProcessIdentity(pid, process.StartTime.ToUniversalTime());
    }

    private Process StartHelper()
    {
        var process = Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli"))
        {
            ArgumentList = { "sleep", "600000" },
            UseShellExecute = false,
        })!;
        _helpers.Add(process);

        return process;
    }

    private void Plant(ReaderMarkerDto marker)
    {
        var path = Path.Combine(new SlotRegistry(DataDir, new JsonFileStore()).Read("codex", "a").Directory, "state.json");
        var store = new JsonFileStore();
        store.Write(path, store.Read(path, ServerJsonContext.Default.SlotStateDto)! with { Reader = marker }, ServerJsonContext.Default.SlotStateDto);
    }

    private SlotStateDto State() =>
        new JsonFileStore().Read(
            Path.Combine(new SlotRegistry(DataDir, new JsonFileStore()).Read("codex", "a").Directory, "state.json"),
            ServerJsonContext.Default.SlotStateDto)!;

    private static Task UntilAsync(Func<bool> condition, string what, ServerChild server) =>
        UntilAsync(() => Task.FromResult(condition()), what, server);

    /// <summary>A bounded wait that names what it waited for, and what the server said, when it runs out.</summary>
    private static async Task UntilAsync(Func<Task<bool>> condition, string what, ServerChild server)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"waited {Patience.TotalSeconds:0} s for {what}. The server said:\n{server.Said}");
    }

    private static void EndQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }

        process.Dispose();
    }

    private static void EndQuietly(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            // Already gone, or never ours.
        }
    }

    /// <summary>One real <c>coai-server</c> as a child process: its port, its client, what it said, and the two ways it ends.</summary>
    private sealed class ServerChild(string dataDir, string temp, string fakeCodexDirectory) : IDisposable
    {
        private readonly StringBuilder _said = new();

        private Process? _process;

        public HttpClient Client { get; private set; } = null!;

        public string Said => _said.ToString();

        public async Task StartAsync()
        {
            var port = FreePort();
            var exe = Path.Combine(AppContext.BaseDirectory, "coai-server" + (OperatingSystem.IsWindows() ? ".exe" : ""));
            File.Exists(exe).Should().BeTrue($"the harness needs the server binary beside it, at {exe}");
            var start = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add("--urls");
            start.ArgumentList.Add($"http://127.0.0.1:{port}");
            foreach (var (name, value) in Settings(dataDir, temp, fakeCodexDirectory))
            {
                start.Environment[name] = value;
            }

            _process = Process.Start(start)!;
            _ = DrainAsync(_process.StandardOutput);
            _ = DrainAsync(_process.StandardError);
            Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(30) };
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Tokens.For($"dev@{TeamServer.Domain}", TeamServer.LocalSigningKey));
            await HealthyAsync();
        }

        /// <summary>The server alone, not its tree: what a crash leaves behind is exactly the children it started.</summary>
        public void Crash()
        {
            _process!.Kill(entireProcessTree: false);
            _process.WaitForExit();
        }

        public void Dispose()
        {
            if (_process is { } process)
            {
                EndQuietly(process);
            }

            Client?.Dispose();
        }

        private static IEnumerable<(string, string)> Settings(string dataDir, string temp, string fakeCodexDirectory) =>
        [
            ("Coai__DataDir", dataDir), ("Coai__AllowedDomains", TeamServer.Domain), ("Coai__AllowAnyDomain", "false"),
            ("Coai__RequireForwardedHttps", "false"), ("Coai__Admins", $"boss@{TeamServer.Domain}"), ("Coai__SessionTtlDays", "7"),
            ("Auth__Local__SigningKey", TeamServer.LocalSigningKey), ("Auth__Microsoft__Tenant", ""), ("Auth__Microsoft__Audiences", ""),
            ("Auth__Google__Enabled", "false"),
            // The fake codex is the only thing on the PATH a confined launch is handed, and the reader's temp directory
            // holds its steering file. The server's own temp (job directories) goes there too.
            ("PATH", fakeCodexDirectory), ("TMPDIR", temp), ("TMP", temp), ("TEMP", temp),
        ];

        private async Task HealthyAsync()
        {
            var deadline = DateTime.UtcNow + Patience;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    if ((await Client.GetAsync("/api/health")).IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // Not listening yet.
                }

                _process!.HasExited.Should().BeFalse($"the server died before it was healthy. It said:\n{Said}");
                await Task.Delay(100);
            }

            throw new TimeoutException($"the server was not healthy within {Patience.TotalSeconds:0} s. It said:\n{Said}");
        }

        private async Task DrainAsync(StreamReader stream)
        {
            while (await stream.ReadLineAsync() is { } line)
            {
                lock (_said)
                {
                    _said.AppendLine(line);
                }
            }
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            return port;
        }
    }
}
