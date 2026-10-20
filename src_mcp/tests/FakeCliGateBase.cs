using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A real <see cref="PanelService"/> over a real git repository with FakeCli answering every reviewer — what the authors tests that go
/// through the engine (E5.S4's two, S5's end to end) share, so each is one scenario and not forty lines of setup (the shape
/// <see cref="ARoundIsMintedAKeyTests"/> wrote first). The repository and the data directory are <c>testsToDelete-…</c> temp folders.
/// </summary>
[Collection("fakecli-env")]
public abstract class FakeCliGateBase : IAsyncLifetime
{
    protected const string Scope = """
        # SCOPE

        The reviewer's own words must survive a failure. A reviewer that falls over is recorded with
        an outcome and nothing else, so the round summary says "unparseable" without the text that
        would not parse, and the same answer replayed by hand goes through cleanly.

        When it is done: the raw answer is kept beside the session, the refusal names the file, and
        a failed reviewer still reports what it consumed. Constraint: no new dependency, and the
        launcher stays the one in v2.Shared.
        """;

    protected const string Clean = """{"findings": []}""";

    private readonly TempDir _temp = TempDir.For("testsToDelete-coai-gate-");
    private readonly ProcessLauncher _launcher = new();

    /// <summary>The checkout under review. A scenario that needs it somewhere particular says where before <see cref="InitializeAsync"/> runs.</summary>
    protected string Repo { get; set; } = string.Empty;

    protected string Data => _temp.At("data");

    protected TempDir Temp => _temp;

    protected ProcessLauncher Launcher => _launcher;

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    /// <summary>Where the repository is made: a folder of the temp directory, unless the scenario names another.</summary>
    protected virtual Task<string> PrepareAsync() => Task.FromResult(_temp.At("repo"));

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Data);
        Repo = await PrepareAsync();
        Directory.CreateDirectory(Repo);
        if (File.Exists(Path.Combine(Repo, ".git")))
        {
            File.Delete(Path.Combine(Repo, ".git"));
        }

        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(Repo, "app.cs"), "v1\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
        await File.WriteAllTextAsync(Path.Combine(Repo, "app.cs"), string.Concat(Enumerable.Repeat("line\n", 100)));
        await Git("add", ".");
        await Git("commit", "-m", "the feature");
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Script(Clean);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "FAKECLI_STDERR"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _temp.Dispose();

        return ValueTask.CompletedTask;
    }

    protected static void Script(string answer)
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
    }

    protected async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], Repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    /// <summary>
    /// The service. <paramref name="environment"/> is the CALLER's environment as the authors step sees it — never the process's — and
    /// <paramref name="transcripts"/> the folder its transcripts are read from, which is always a temp folder, never a developer's.
    /// </summary>
    protected PanelService Service(Func<string, string?> environment, string transcripts, Serilog.ILogger? log = null) =>
        new(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe }],
                Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
                DataDir = Data,
                AgentLogDir = transcripts,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            log ?? Logger.None,
            Noticing.None,
            environment);

    protected static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    /// <summary>The session file of the branch, as the engine saved it.</summary>
    protected PersistedSession Saved() => new SessionStore(Data).Load(Repo, "feature")!;
}
