using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The pull-request image job RUNS the image and uses the server — and the half of that which can run without a docker
/// daemon runs here.
/// </summary>
/// <remarks>
/// <para><c>src_server/smoke-image.sh</c> starts the built image with a throwaway data directory and the contract
/// runner's Local sign-in configuration (<c>http/run-contracts.mjs</c>: a per-run random HMAC key, domain
/// <c>example.com</c>, admin <c>boss@example.com</c>, the same seeded <c>vendors.json</c> and <c>usage.jsonl</c>), waits
/// for <c>/api/health</c>, mints a token, reads <c>/api/usage</c> and checks the seeded launch came back, then runs each
/// CLI's <c>--version</c> inside the running container. The container itself needs docker, which this machine's suite
/// does not have; what is checked here is everything around it: the key, the token, the seed and the body check — against
/// the REAL server in-process — and that the key never reaches an argument list, that a failed start shows the
/// container's log, and that the container is stopped (a <c>docker</c> stub on PATH records what it was asked).</para>
/// </remarks>
[Collection(ServerCollection.Name)]
public sealed class TheImageSmokeUsesTheServerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-smoke-" + Guid.NewGuid().ToString("N"));

    public TheImageSmokeUsesTheServerTests() => Directory.CreateDirectory(_dir);

    private static string Script => ShellScripts.Posix(Path.Combine(ShellScripts.RepoRoot(), "src_server", "smoke-image.sh"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp directory a stray handle still holds is the OS's to clean.
        }
    }

    [Fact]
    public async Task TheTokenItMints_IsAcceptedByTheRealServer_AndTheSeededLedgerIsWhatItChecks()
    {
        var (code, output) = Bash($"source '{Script}'; new_key");
        code.Should().Be(0, output);
        var key = output.Trim();

        using var server = new TeamServer(new Dictionary<string, string?> { ["Auth__Local__SigningKey"] = key });
        (code, output) = Bash($"source '{Script}'; seed '{ShellScripts.Posix(server.DataDir)}' && mint '{key}' dev@example.com");
        code.Should().Be(0, output);
        var token = output.Trim();

        using var client = server.WithToken(token);
        using var response = await client.GetAsync("/api/usage?window=year", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        ((int)response.StatusCode).Should().Be(200, "the server must accept the token the smoke mints: {0}", body);
        CheckUsage(body).Code.Should().Be(0, "the body the real server answers over the seeded ledger passes the smoke's check");
        CheckUsage("{\"scope\":\"me\",\"vendors\":[]}").Code.Should().NotBe(0,
            "an answer without the seeded launch must fail the check, or it checks nothing");
    }

    [Fact]
    public void EachRunHasItsOwnKey_LongEnoughForTheServer()
    {
        var (code, output) = Bash($"source '{Script}'; new_key; echo; new_key");

        code.Should().Be(0, output);
        var keys = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        keys.Should().HaveCount(2);
        keys[0].Should().NotBe(keys[1], "a key written down is a credential; this one exists for one run");
        keys.Should().OnlyContain(one => Encoding.UTF8.GetByteCount(one) >= 32, "the server refuses a key under 32 bytes");
    }

    [Fact]
    public void WithoutAnImage_ItRefuses_AndDockerIsNeverAsked()
    {
        var (code, output, calls) = RunWithStubDocker(string.Empty);

        code.Should().NotBe(0);
        output.Should().Contain("usage");
        calls.Should().BeEmpty("nothing may start before the arguments are known");
    }

    [Fact]
    public void AStartThatNeverAnswers_ShowsTheContainersLog_StopsIt_AndNeverPutsTheKeyOnAnArgumentList()
    {
        var (code, output, calls) = RunWithStubDocker("coai-server:smoke");

        code.Should().NotBe(0, "a server that never answered /api/health must fail the job");
        output.Should().Contain("the stub server said boom", "the container's own log is what says why");
        calls.Should().Contain(line => line.StartsWith("run ", StringComparison.Ordinal) && line.Contains("--rm", StringComparison.Ordinal),
            "docker's own --rm removes the container — never a host-wide kill");
        calls.Should().Contain(line => line.StartsWith("stop ", StringComparison.Ordinal) && line.EndsWith(" stub-container-id", StringComparison.Ordinal),
            "the container it started — that id, nothing else — is stopped on the way out, failure included");
        calls.Should().Contain(line => line.Contains("-e Auth__Local__SigningKey ", StringComparison.Ordinal),
            "the key goes in by NAME, from the environment");

        var key = File.ReadAllText(Path.Combine(_dir, "docker-saw-key")).Trim();
        Encoding.UTF8.GetByteCount(key).Should().BeGreaterThanOrEqualTo(32, "docker run received the key through its environment");
        calls.Should().NotContain(line => line.Contains(key, StringComparison.Ordinal), "an argument list is readable by every process");
        output.Should().NotContain(key, "the key is never printed");
    }

    private (int Code, string Output) CheckUsage(string body)
    {
        var file = Path.Combine(_dir, "usage-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, body);

        return Bash($"source '{Script}'; check_usage \"$(cat '{ShellScripts.Posix(file)}')\"");
    }

    /// <summary>
    /// Runs the whole script with a <c>docker</c> stub first on PATH: <c>run</c> answers an id, <c>logs</c> says one line,
    /// everything is recorded. Nothing listens on the port, so the health wait runs out its (short) bound.
    /// </summary>
    private (int Code, string Output, string[] Calls) RunWithStubDocker(string image)
    {
        var bin = Directory.CreateDirectory(Path.Combine(_dir, "bin")).FullName;
        var calls = Path.Combine(_dir, "docker-calls");
        var sawKey = Path.Combine(_dir, "docker-saw-key");
        File.WriteAllText(calls, string.Empty);
        Stub(Path.Combine(bin, "docker"),
            "#!/bin/sh\n"
            + $"echo \"$*\" >> '{ShellScripts.Posix(calls)}'\n"
            + "case \"$1\" in\n"
            + $"  run) printf '%s' \"${{Auth__Local__SigningKey:-}}\" > '{ShellScripts.Posix(sawKey)}'; echo stub-container-id ;;\n"
            + "  logs) echo 'the stub server said boom' ;;\n"
            + "esac\n");
        var (code, output) = Bash(
            $"export PATH='{ShellScripts.PathEntry(bin)}':\"$PATH\"; exec bash '{Script}' {(image.Length == 0 ? string.Empty : $"'{image}'")}",
            // GITHUB_ACTIONS off: on a runner the script prints `::add-mask::<key>` — the runner's own masking
            // instruction, which never reaches the log — and this case asserts the key is never printed otherwise.
            new Dictionary<string, string> { ["SMOKE_PORT"] = "1", ["SMOKE_WAIT_SECONDS"] = "2", ["GITHUB_ACTIONS"] = "false" });

        return (code, output, File.ReadAllLines(calls));
    }

    private static void Stub(string path, string text)
    {
        File.WriteAllText(path, text.Replace("\r\n", "\n", StringComparison.Ordinal));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>bash -c, stdout and stderr together; skipped where there is no bash.</summary>
    private static (int Code, string Output) Bash(string command, IDictionary<string, string>? env = null)
    {
        var bash = ShellScripts.Bash();
        Assert.SkipWhen(bash.Length == 0, "no bash on this machine — the script's behaviour runs on CI's Linux and macOS legs");
        var start = new ProcessStartInfo(bash)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(command);
        foreach (var (name, value) in env ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit(TimeSpan.FromSeconds(60)).Should().BeTrue("the script must not hang");

        return (process.ExitCode, stdout + stderr.Result);
    }
}
