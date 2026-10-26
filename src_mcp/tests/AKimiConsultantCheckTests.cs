using System.Text.Json;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): one turn as a consultation makes it — in a
/// COPY of the check's scratch repository, on a ONE-SHOT owned home (<c>&lt;root&gt;\coai-vh\&lt;8&gt;\kh-&lt;10 hex&gt;</c>), disposed junction-first whatever ended
/// the check. The whole mode in process (<see cref="ConsultantCheckMode.AnswerAsync"/>), git for real, the shim played (<see cref="PlayedKimi"/>), the
/// person a fake one (<see cref="KimiFixture"/>) — no turn is paid for and the real <c>kimi</c> is never started.
/// </summary>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly TempDir _data = TempDir.For("testsToDelete-kcheck-data-");
    private readonly TempDir _temp = TempDir.For("testsToDelete-kcheck-temp-");
    private readonly TempDir _homes = TempDir.For("testsToDelete-kcheck-homes-");
    private readonly List<string> _warned = [];

    public void Dispose()
    {
        _data.Dispose();
        _temp.Dispose();
        _homes.Dispose();
    }

    /// <summary>The settings of a check of a Kimi consultant row <c>my-kimi</c> for the caller <c>claude</c>, with <paramref name="homesRoot"/> as the homes root (empty: a hand-built one).</summary>
    private PanelSettings KimiSettings(string homesRoot)
    {
        // QWEN-FILL-BEGIN s55-settings test
        return new PanelSettings
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
        // QWEN-FILL-END s55-settings
    }

    /// <summary>One <c>--check-consultant --caller claude</c> in process: the exit code and the JSON it said.</summary>
    private async Task<(int Code, JsonElement Said)> CheckAsync(PanelSettings settings, PlayedKimi played)
    {
        var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], played, _temp, _warned.Add, Noticing.None, TestContext.Current.CancellationToken);

        return (code, JsonDocument.Parse(stdout).RootElement.Clone());
    }

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        // QWEN-FILL-BEGIN s551-copy test
        using var person = new KimiFixture();
        using var stood = KimiScenarioScopes.StandIn(person);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = (_, request) =>
            {
                var cwd = request.Arguments[request.Arguments.ToList().IndexOf("--cwd") + 1];
                var marker = File.ReadAllText(Path.Combine(cwd, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');
                return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, $"marker: {marker}{Environment.NewLine}canary: CANNOT");
            },
        };
        var before = person.Hash();
        var settings = KimiSettings(_homes.Path);

        var (code, said) = await CheckAsync(settings, played);

        code.Should().Be(0, "the check of my-kimi must pass on a Kimi row that runs in a copy");
        said.GetProperty("state").GetString().Should().Be("answered", said.ToString());
        said.GetProperty("markerRead").GetBoolean().Should().BeTrue("the copy holds the scratch's CHECK.md");

        var shim = played.Shims.Should().ContainSingle().Subject.Request;

        var cwdValue = shim.Arguments[shim.Arguments.ToList().IndexOf("--cwd") + 1];
        cwdValue.Should().Contain(Path.Combine("own", "tree"), "the consultant stands in a copy in the check's own scratch, never the repository");
        shim.WorkingDirectory.Should().Be(cwdValue, "the shim's working directory is the same copy folder");

        var home = shim.Arguments[shim.Arguments.ToList().IndexOf("--home") + 1];
        var oneShotRoot = Path.Combine(_homes.Path, CoaiMcp.Runners.Vibe.VibeHomes.OneShotFolder) + Path.DirectorySeparatorChar;
        home.Should().StartWith(oneShotRoot, "the one-shot home lives under the short homes root");
        Path.GetFileName(Path.GetDirectoryName(home)!).Should().HaveLength(8, "the one-shot folder is named with 8 hex digits");
        Path.GetFileName(home).Should().StartWith("kh-", "the home is a Kimi home folder");
        Path.GetFileName(home).Should().HaveLength(3 + 10, "the Kimi home is kh- plus 10 hex digits");
        home.Should().NotStartWith(_temp.Path, "the home is never under TEMP, which is where the scratch lives");

        Directory.EnumerateDirectories(_temp.Path, "coai-check-*").Should().BeEmpty("the copy goes with the check's scratch");
        person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
        // QWEN-FILL-END s551-copy
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool theTurnFails)
    {
        // QWEN-FILL-BEGIN s552-gone test
        using var person = new KimiFixture();
        using var stood = KimiScenarioScopes.StandIn(person);
        var before = person.Hash();

        Func<int, ProcessRequest, ProcessResult> turn = theTurnFails
            ? (_, _) => new ProcessResult(70, string.Empty, "the played agent failed", TimedOut: false)
            : (_, request) =>
            {
                var cwd = request.Arguments[request.Arguments.ToList().IndexOf("--cwd") + 1];
                var marker = File.ReadAllText(Path.Combine(cwd, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');
                return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, $"marker: {marker}{Environment.NewLine}canary: CANNOT");
            };

        var played = new PlayedKimi(new ProcessLauncher()) { Turn = turn };
        var unlinked = new List<string>();
        GitScratch.AfterUnlink = unlinked.Add;

        try
        {
            var (code, said) = await CheckAsync(KimiSettings(_homes), played);

            var state = said.GetProperty("state").GetString();
            if (theTurnFails)
                state.Should().NotBe("answered", said.ToString());
            else
                state.Should().Be("answered", said.ToString());

            played.Shims.Should().ContainSingle("the shim was launched once, so the home was made");

            var oneShot = Path.Combine(_homes, CoaiMcp.Runners.Vibe.VibeHomes.OneShotFolder);
            if (Directory.Exists(oneShot))
                Directory.EnumerateFileSystemEntries(oneShot).Should().BeEmpty("the home's folder goes when the check ends, whatever ended it");

            unlinked.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
            person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }
        // QWEN-FILL-END s552-gone
    }

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        // QWEN-FILL-BEGIN s553-cancel test
        using var person = new KimiFixture();
        using var stood = KimiScenarioScopes.StandIn(person);
        var before = person.Hash();
        var unlinked = new List<string>();
        GitScratch.AfterUnlink = unlinked.Add;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            During = async (_, ct) =>
            {
                await caller.CancelAsync();
                ct.ThrowIfCancellationRequested();
            },
        };

        try
        {
            var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(
                KimiSettings(_homes), ["--check-consultant", "--caller", "claude"], played, _temp, _warned.Add, Noticing.None, caller.Token);
            var said = JsonDocument.Parse(stdout).RootElement.Clone();

            said.GetProperty("state").GetString().Should().Be("failed", said.ToString());
            said.GetProperty("failureKind").GetString().Should().Be(ConsultFailureKinds.Cancelled, said.ToString());
            played.Shims.Should().ContainSingle("the played shim must have started before the token was cancelled");
            Directory.EnumerateFileSystemEntries(Path.Combine(_homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the one-shot home goes junction-first even when the check is cancelled");
            unlinked.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
            person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
            Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the check's scratch goes even when the check is cancelled");
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }
        // QWEN-FILL-END s553-cancel
    }

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        // QWEN-FILL-BEGIN s554-rootless test
        using var person = new KimiFixture();
        using var stood = KimiScenarioScopes.StandIn(person);
        var before = person.Hash();
        var unlinked = new List<string>();
        var home = string.Empty;
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = (_, request) =>
            {
                home = request.Arguments[request.Arguments.ToList().IndexOf("--home") + 1];
                var cwd = request.Arguments[request.Arguments.ToList().IndexOf("--cwd") + 1];
                var marker = File.ReadAllText(Path.Combine(cwd, "CHECK.md")).Trim().Split(' ')[^1].TrimEnd('.');
                return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, $"marker: {marker}{Environment.NewLine}canary: CANNOT");
            },
        };

        GitScratch.AfterUnlink = unlinked.Add;
        try
        {
            var (_, said) = await CheckAsync(KimiSettings(string.Empty), played);

            said.GetProperty("state").GetString().Should().Be("answered", said.ToString());

            var own = Path.GetDirectoryName(home);
            var scratch = Path.GetDirectoryName(own);
            Path.GetFileName(own).Should().Be("own", "the home sits in the check's own folder");
            Path.GetFileName(home).Should().StartWith("kh-", "the one-shot home is named kh-<10 hex>");
            Path.GetFileName(scratch).Should().StartWith("coai-check-", "the home is inside the check's scratch");
            Path.GetDirectoryName(scratch).Should().Be(_temp.Path, "the scratch is a direct child of the check's temp root");

            Directory.EnumerateDirectories(_temp, "coai-check-*").Should().BeEmpty("the scratch, and the home inside it, are gone");
            Directory.Exists(home).Should().BeFalse("the home is gone after the check");
            unlinked.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
            person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }
        // QWEN-FILL-END s554-rootless
    }
}
