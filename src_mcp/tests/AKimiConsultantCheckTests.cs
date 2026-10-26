using System.Text.Json;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>coai-mcp --check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): one turn as a consultation
/// makes it — in a COPY of the check's scratch repository, on a ONE-SHOT owned home under <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>, disposed junction-first
/// whatever ended the check. The check's own invariant (the scratch repository unchanged, the canary untouched) holds.
/// </summary>
/// <remarks>
/// <para>No new branch in the check: it already gives a copy to a runtime that asks (<c>NeedsASanitisedSnapshot</c>) and a one-shot home to a runtime that
/// declares a <c>HomesFolder</c>, which <see cref="KimiConsultant"/> does (E5.4 decision 6). What makes it safe for Kimi is E5.3 decision g, so these tests
/// prove the PATH. The shim is played (<see cref="PlayedKimi"/>) over a fake person (<see cref="KimiFixture"/>) — no turn is paid for and the real
/// <c>kimi</c> is never started. Every folder is a <c>testsToDelete-</c> folder of the test's own; the fake person lives OUTSIDE the homes root.</para>
/// </remarks>
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _data = TempDir.For("testsToDelete-kimi-check-data-");
    private readonly TempDir _temp = TempDir.For("testsToDelete-kimi-check-temp-");
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-check-homes-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _data.Dispose();
        _temp.Dispose();
        _homes.Dispose();
        _person.Dispose();
    }

    /// <summary>The deleter's own unlink recorder — a Kimi home disposed junction-first never lets the deleter meet its junction.</summary>
    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    private void TheDeleterNeverMetTheJunction() =>
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");

    private void ThePersonsFolderIsWhole(IReadOnlyList<string> before) =>
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");

    private string OneShotRoot => Path.Combine(_homes, VibeHomes.OneShotFolder);

    /// <summary>The settings of a check whose caller is a Kimi row <c>my-kimi</c>, with the homes root given (empty: a check built by hand, rootless).</summary>
    private PanelSettings Settings(string homesRoot) => new()
    {
        Providers = [],
        DataDir = _data,
        ReviewerTimeout = TimeSpan.FromSeconds(60),
        ConsultEnabled = true,
        ConsultantHomesRoot = homesRoot,
        Consultants = new Dictionary<string, ConsultantChoice>
        {
            [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi"),
        },
    };

    /// <summary>The person, the host and the lock root this flow's Kimi adapters see — set by a SYNCHRONOUS call, as the service scenarios do.</summary>
    private IDisposable StandIn() => new Scopes(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

    /// <summary>Several scopes, given back in reverse.</summary>
    private sealed class Scopes(params IDisposable[] scopes) : IDisposable
    {
        public void Dispose()
        {
            foreach (var scope in scopes.Reverse())
            {
                scope.Dispose();
            }
        }
    }

    /// <summary>The check, run through <c>ConsultantCheckMode.AnswerAsync</c> with args <c>--check-consultant --caller claude</c>; the exit code and the JSON it printed.</summary>
    private Task<(int Code, JsonElement Said, string Err)> Check(PanelSettings settings, PlayedKimi played, CancellationToken ct)
    {
        // QWEN-FILL-BEGIN s55-check test
        var (code, stdout, stderr) = await ConsultantCheckMode.AnswerAsync(settings, ["--check-consultant", "--caller", "claude"], played, _temp, _warned.Add, Noticing.None, ct);
        return (code, stdout.Length > 0 ? JsonDocument.Parse(stdout).RootElement.Clone() : default, stderr);
        // QWEN-FILL-END s55-check
    }

    // ---------- S5.5.1 ----------

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        // QWEN-FILL-BEGIN s551-copy test
        using var stood = StandIn();
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = (_, request) =>
            {
                var seen = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md"));
                var marker = seen.Trim().Split(' ')[^1].TrimEnd('.');
                var answer = $"marker: {marker}{Environment.NewLine}canary: CANNOT";
                return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 20000, 30, answer);
            },
        };
        var before = _person.Hash();

        var (code, said, _) = await Check(Settings(_homes), played, TestContext.Current.CancellationToken);

        code.Should().Be(0);
        said.GetProperty("state").GetString().Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue();

        var request = played.Shims.Should().ContainSingle().Subject.Request;
        var cwd = request.Arguments[request.Arguments.ToList().IndexOf("--cwd") + 1];
        cwd.Should().Contain(Path.Combine("own", "tree"));
        request.WorkingDirectory.Should().Be(cwd);

        var home = request.Arguments[request.Arguments.ToList().IndexOf("--home") + 1];
        Path.GetDirectoryName(Path.GetDirectoryName(home)).Should().Be(OneShotRoot);
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");

        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty();
        ThePersonsFolderIsWhole(before);
        // QWEN-FILL-END s551-copy
    }

    // ---------- S5.5.2 ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool theTurnFails)
    {
        _ = theTurnFails; // the body below reads it (it is the arm); drop this line once it does
        // QWEN-FILL-BEGIN s552-gone test
        using var stood = StandIn();
        RecordingTheDeleter();
        var before = _person.Hash();
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = theTurnFails
                ? (_, request) => PlayedKimi.Lost(PlayedKimi.SessionOf(request))
                : (_, request) =>
                {
                    var text = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md")).Trim();
                    var marker = text.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Last().TrimEnd('.');
                    var answer = $"marker: {marker}{Environment.NewLine}canary: CANNOT";
                    return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 20000, 30, answer);
                },
        };

        var (_, said, _) = await Check(Settings(_homes), played, TestContext.Current.CancellationToken);

        (!Directory.Exists(OneShotRoot) || !Directory.EnumerateFileSystemEntries(OneShotRoot).Any()).Should().BeTrue("coai-vh\\<8> is gone");
        TheDeleterNeverMetTheJunction();
        ThePersonsFolderIsWhole(before);
        if (theTurnFails)
        {
            said.GetProperty("state").GetString().Should().NotBe("answered", said.ToString());
        }
        // QWEN-FILL-END s552-gone
    }

    // ---------- S5.5.3 ----------

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        // QWEN-FILL-BEGIN s553-cancel test
        using var stood = StandIn();
        RecordingTheDeleter();
        var before = _person.Hash();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                cts.Cancel();
                await Task.Delay(Timeout.Infinite, ct);
            },
        };

        var said = default(JsonElement);
        try
        {
            (_, said, _) = await Check(Settings(_homes), played, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // AnswerAsync let the cancellation out: there is no record to read.
        }

        if (said.ValueKind != JsonValueKind.Undefined)
        {
            said.GetProperty("state").GetString().Should().Be("failed", said.ToString());
            said.ToString().ToLowerInvariant().Should().Contain("cancel", said.ToString());
        }

        if (Directory.Exists(OneShotRoot))
        {
            Directory.EnumerateFileSystemEntries(OneShotRoot).Should().BeEmpty("the one-shot home must be disposed when the check is cancelled");
        }

        TheDeleterNeverMetTheJunction();
        ThePersonsFolderIsWhole(before);
        // QWEN-FILL-END s553-cancel
    }

    // ---------- S5.5.4 ----------

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        // QWEN-FILL-BEGIN s554-rootless test
        using var stood = StandIn();
        RecordingTheDeleter();
        var before = _person.Hash();

        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = (_, request) =>
            {
                var checkText = File.ReadAllText(Path.Combine(request.WorkingDirectory, "CHECK.md"));
                var marker = checkText
                    .Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Last()
                    .TrimEnd('.');
                var answer = $"marker: {marker}{Environment.NewLine}canary: CANNOT";
                return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 20000, 30, answer);
            },
        };

        await Check(Settings(string.Empty), played, TestContext.Current.CancellationToken);

        var request = played.Shims.Should().ContainSingle().Subject.Request;
        var argv = request.Arguments.ToList(); // argument list of the shim request (name as used in s551-copy)
        var home = argv[argv.IndexOf("--home") + 1];

        Path.GetFileName(Path.GetDirectoryName(home)).Should().Be("own");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty();
        TheDeleterNeverMetTheJunction();
        ThePersonsFolderIsWhole(before);
        // QWEN-FILL-END s554-rootless
    }
}
