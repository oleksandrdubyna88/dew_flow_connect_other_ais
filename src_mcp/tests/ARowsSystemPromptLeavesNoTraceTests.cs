using System.Text;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The canary: a row's system prompt leaves no trace anywhere but the reviewer's own input — not in the server's
/// stderr, not in a log line or its properties, not in any file of the data directory (the rounds database, the usage
/// ledger, the session records) and not in the reply the calling AI reads (todo/PLAN_one_model_catalog.md, epic 2,
/// story 2 as revised: "only length and hash are recorded").
/// </summary>
/// <remarks>
/// <para>The leak this was written to find is the reviewer itself: a CLI echoes its input — <c>codex exec</c> prints
/// the prompt on stderr — and a failing one quotes it in its error. The round keeps a reviewer's stderr tail as the
/// reason it failed, so the fake CLI here does exactly that, through every ending a launch can have.</para>
/// <para>That the prompt DOES reach the reviewer is <see cref="ARowsSystemPromptReachesItsReviewerTests"/>'s
/// half.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ARowsSystemPromptLeavesNoTraceTests : IAsyncLifetime
{
    private const string Canary = "CANARY-SYSPROMPT-91c2 keep my house rules private";

    private const string Scope = """
        # SCOPE

        The retry policy is documented and applied in one place. Done when the call retries once on a transient failure.
        """;

    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private string _data = string.Empty;

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-canary-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-canary-data-").FullName;
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v1\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "FAKECLI_STDERR", "FAKECLI_SLEEP_MS"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }
        foreach (var dir in (string[])[_repo, _data])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    public static TheoryData<string, string, string, string> Endings => new()
    {
        // ending, exit code, stderr the CLI prints, sleep before answering
        { "answered, echoing its input", "0", $"user\n{Canary}\n", "0" },
        { "failed, quoting its input", "3", $"error: could not use the instruction '{Canary}'", "0" },
        { "timed out", "0", $"thinking about {Canary}", "20000" },
    };

    [Theory]
    [MemberData(nameof(Endings))]
    public async Task TheRowsPromptIsInNoRecord_HoweverTheReviewerEnded(string ending, string exit, string stderr, string sleepMs)
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", """{"findings": []}""");
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", """{"findings": []}""");
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", exit);
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", stderr);
        Environment.SetEnvironmentVariable("FAKECLI_SLEEP_MS", sleepMs);
        var logged = new ListSink();
        var serverStderr = new StringWriter();
        var was = Console.Error;
        string reply;
        try
        {
            Console.SetError(serverStderr);
            var service = Service(new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(logged).CreateLogger());
            await service.OpenAsync(_repo, "feature");
            reply = await service.ReviewPlanAsync(_repo, "feature", Scope);
        }
        finally
        {
            Console.SetError(was);
        }

        reply.Should().NotContain("CANARY-SYSPROMPT", $"the calling AI reads this reply ({ending})");
        serverStderr.ToString().Should().NotContain("CANARY-SYSPROMPT", $"the server's stderr ({ending})");
        Logged(logged).Should().NotContain("CANARY-SYSPROMPT", $"a log line or its properties ({ending})");
        FilesHolding("CANARY-SYSPROMPT").Should().BeEmpty($"no file of the data directory may hold it ({ending})");
    }

    private PanelService Service(ILogger logger) => new(
        new PanelSettings
        {
            Providers = [new("codex") { ExecutablePath = FakeCliExe, Runtime = "codex", SystemPrompt = Canary }],
            DataDir = _data,
            ReviewerTimeout = TimeSpan.FromSeconds(3),
        },
        VaultKeys.None("no vault in tests"), default, _launcher, logger, Noticing.None);

    /// <summary>Every rendered line and every property value, so a structured field cannot hide what a line does not show.</summary>
    private static string Logged(ListSink sink) =>
        string.Join('\n', sink.Lines.Concat(sink.Events.SelectMany(e => e.Properties.Values.Select(v => v.ToString()))));

    /// <summary>The files under the data directory whose bytes hold the text — the database included, read raw.</summary>
    private List<string> FilesHolding(string text)
    {
        var needle = Encoding.UTF8.GetBytes(text);

        return [.. Directory.EnumerateFiles(_data, "*", SearchOption.AllDirectories)
            .Where(file => Holds(ReadShared(file), needle))
            .Select(file => Path.GetRelativePath(_data, file))];
    }

    private static byte[] ReadShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        return copy.ToArray();
    }

    private static bool Holds(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }
}
