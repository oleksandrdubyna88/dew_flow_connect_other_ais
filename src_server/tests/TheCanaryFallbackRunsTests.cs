using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The deploy canary's fallback RUNS (story 3.5 of <c>todo/PLAN_every_round_is_counted.md</c>, the epic-3 code round):
/// <c>deploy/systemd-release.sh</c> sends <c>"kind":"canary"</c>, and a server older than the kind refuses it with a 400
/// before any vendor starts — so the script asks once more as a plain review rather than rolling back a release that is
/// fine. This drives the REAL <c>canary</c> function, sourced from the script, against a stub <c>curl</c> on PATH that
/// answers as an old server, a new one, or one refusing for another reason.
/// </summary>
/// <remarks>
/// <para>The code round's finding: the loop was checked only by a stubbed run outside the suite, so a later edit could
/// break the old-server retry — and roll back every deploy to a server that predates the kind — with every suite green.</para>
/// <para>Needs bash and a Python 3 (the script parses the answers with <c>python3</c>); where either is missing this
/// reports itself skipped — CI's Linux and macOS legs have both.</para>
/// </remarks>
public sealed class TheCanaryFallbackRunsTests : IDisposable
{
    /// <summary>What a server that predates the kind answers for it (<c>JobKinds.Refusal</c>'s sentence, from 0.10.0).</summary>
    private const string OldServerRefusal = """{"error":"'canary' is not a kind of job. Allowed: review, chat, consult."}""";

    /// <summary>
    /// A stub <c>curl</c>: a POST is logged (its <c>-d</c> body, one line) and answered with the body, a newline and the
    /// status — the shape the script's <c>-w $'\n%{http_code}'</c> gives it; a GET is the poll, answered <c>done</c>.
    /// </summary>
    private const string StubCurl = """
        #!/usr/bin/env bash
        post=0; body=""
        while [ $# -gt 0 ]; do
            case "$1" in
                -X) [ "$2" = POST ] && post=1; shift ;;
                -d) body=$2; shift ;;
            esac
            shift
        done
        if [ "$post" = 1 ]; then
            printf '%s\n' "$body" >>"$CANARY_STUB_LOG"
            case "$CANARY_STUB_SERVER:$body" in
                old:*'"kind":"canary"'*) printf '%s\n%s' "$CANARY_STUB_OLD_ANSWER" 400; exit 0 ;;
                refusing:*) printf '%s\n%s' '{"error":"timeoutSeconds is out of range"}' 400; exit 0 ;;
            esac
            printf '%s\n%s' '{"id":"j1"}' 202
            exit 0
        fi
        printf '%s' '{"status":"done","answer":"{\"findings\":[]}"}'
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-canary-run-" + Guid.NewGuid().ToString("N"));

    public TheCanaryFallbackRunsTests() => Directory.CreateDirectory(_dir);

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
    public void AnOldServer_RefusesTheKind_AndTheCanaryAsksAgainAsAPlainReview()
    {
        var (code, stderr, posts) = Canary("old");

        code.Should().Be(0, stderr);
        posts.Should().HaveCount(2, "the refusal of the kind is asked once more, without it");
        posts[0].Should().Contain("\"kind\":\"canary\"");
        posts[1].Should().NotContain("\"kind\"", "the second ask is the plain review an old server knows");
        stderr.Should().Contain("does not know the canary kind yet");
    }

    [Fact]
    public void ANewServer_TakesTheKind_AndIsAskedOnce()
    {
        var (code, stderr, posts) = Canary("new");

        code.Should().Be(0, stderr);
        posts.Should().ContainSingle().Which.Should().Contain("\"kind\":\"canary\"");
    }

    [Fact]
    public void AnyOtherRefusal_IsNotRetried_AndFailsTheCanary()
    {
        var (code, stderr, posts) = Canary("refusing");

        code.Should().NotBe(0, "a refusal that is not about the kind is the release's problem, and must roll it back");
        posts.Should().ContainSingle("only the unknown-kind refusal is worth asking again");
        stderr.Should().Contain("HTTP 400");
    }

    /// <summary>Sources the real script and runs its <c>canary</c> once, with the stub curl and a python3 first on PATH.</summary>
    private (int Code, string StdErr, IReadOnlyList<string> Posts) Canary(string server)
    {
        var bash = ShellScripts.Bash();
        var python = ShellScripts.Python();
        Assert.SkipWhen(bash.Length == 0 || python.Length == 0,
            "no bash or no Python 3 on this machine — the canary's run is CI's Linux and macOS legs'");
        Path.IsPathRooted(python).Should().BeTrue("the python3 shim below is FIRST on PATH, so a bare name would exec itself for ever");
        var bin = Directory.CreateDirectory(Path.Combine(_dir, "bin")).FullName;
        Script(Path.Combine(bin, "curl"), StubCurl);
        Script(Path.Combine(bin, "python3"), $"#!/usr/bin/env bash\nexec '{ShellScripts.Posix(python)}' \"$@\"\n");
        var log = Path.Combine(_dir, "posts.log");

        var start = new ProcessStartInfo(bash)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(
            $"export PATH='{ShellScripts.PathEntry(bin)}':\"$PATH\"; "
            + $"source '{ShellScripts.Posix(Path.Combine(ShellScripts.RepoRoot(), "deploy", "systemd-release.sh"))}'; "
            + "canary codex gpt-test 30");
        start.Environment["COAI_TOKEN"] = "test-token";
        start.Environment["COAI_URL"] = "https://coai.example.test";
        start.Environment["CANARY_STUB_SERVER"] = server;
        start.Environment["CANARY_STUB_LOG"] = ShellScripts.Posix(log);
        start.Environment["CANARY_STUB_OLD_ANSWER"] = OldServerRefusal;
        using var process = Process.Start(start)!;
        // Both streams read without blocking, and the wait BOUNDED: a child that never exits must fail this test in a
        // minute, never hold a CI leg to its job limit (PR #761: a self-execing python3 shim did exactly that).
        var stderr = process.StandardError.ReadToEndAsync();
        _ = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("the canary did not finish within 60 s against a stub that answers at once");
        }

        return (process.ExitCode, stderr.Result, File.Exists(log) ? File.ReadAllLines(log) : []);
    }

    private static void Script(string path, string text)
    {
        File.WriteAllText(path, text.Replace("\r\n", "\n", StringComparison.Ordinal));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
