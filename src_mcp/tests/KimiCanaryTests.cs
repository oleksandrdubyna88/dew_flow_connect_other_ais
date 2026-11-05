using System.ComponentModel;
using System.Text.Json;
using CoaiMcp.Acp;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The canary (E4.2 decision 12 and S2.45 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): a token in the person's sign-in, and an
/// <c>api_key</c> in their config, never appear in anything coai WRITES — every sentence it composes (refusals, a stopped disposal, a lost
/// target, the version warning), the lock file names, the rendered <c>config.toml</c>, the shim's report and its stderr — through every ending.
/// </summary>
/// <remarks>
/// <para><b>The bound, said plainly.</b> What a vendor's own process prints (the agent's stderr, the error text it puts in <c>reason</c>) is
/// VENDOR text relayed by coai and outside this pin: coai cannot keep a vendor's process from printing its own file. The fake agent here
/// writes neither canary. What the pin covers is every string COAI composes; and the control arm proves the detector sees a served canary
/// when one is served — a collector that caught nothing proves nothing, so each one has its own liveness check.</para>
/// <para>E4.2's runners classes log nothing (no <c>ILogger</c> or Serilog under <c>src_mcp/runners/Kimi</c>, <c>Files</c> or <c>Acp</c>): they
/// answer strings and call <c>warn</c>/<c>note</c> callbacks, which are collected here. The Serilog surface is the server's and E4.4's.</para>
/// </remarks>
public sealed class KimiCanaryTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly List<(string Where, string Text)> _seen = [];

    public void Dispose() => _person.Dispose();

    private void See(string where, string text) => _seen.Add((where, text));

    private IEnumerable<string> From(string where) => _seen.Where(entry => entry.Where == where).Select(entry => entry.Text);

    private string Locks => Path.Combine(_person.Folder, "locks");

    private static ProcessRequest Request(KimiPlaces places, string model = KimiModels.Default) =>
        KimiEnvironment.Built(
            new ProcessRequest(places.Executable, ["--ask-acp", "--profile", "kimi", "--model", model, "--home", places.OwnedRoot, "--persons-bin", places.PersonsBin], places.Home),
            places);

    private sealed class FakeKimi(string version = "2.1.1", int exit = 0, Exception? throws = null) : IProcessLauncher
    {
        public Action<ProcessRequest> WhileTheVersionIsAsked { get; set; } = _ => { };

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            WhileTheVersionIsAsked(request);

            return throws is not null
                ? throw throws
                : Task.FromResult(new ProcessResult(exit, exit == 0 ? $"kimi {version}\r\n" : string.Empty, exit == 0 ? string.Empty : "boom\r\n", TimedOut: false));
        }
    }

    /// <summary>One launch through the control; its refusal, warning and version are what the control ANSWERED.</summary>
    private async Task<LaunchTurn> LaunchAsync(KimiPlaces places, FakeKimi launcher, string model = KimiModels.Default, HostKind host = HostKind.Windows, bool keep = false)
    {
        var turn = await KimiLaunch.TakeAsync(host, Request(places, model), places, launcher, TestContext.Current.CancellationToken);
        See("answers", $"{turn.Refusal} | {turn.Warning} | {turn.CliVersion}");
        if (!keep)
        {
            turn.Dispose();
        }

        return turn;
    }

    private async Task LaunchesAsync()
    {
        var places = _person.Places("kh-happy");
        await LaunchAsync(places, new FakeKimi());
        await LaunchAsync(_person.Places("kh-warned"), new FakeKimi("3.0.0"));
        await LaunchAsync(_person.Places("kh-linux"), new FakeKimi(), host: HostKind.Linux);
        await LaunchAsync(_person.Places("kh-model"), new FakeKimi(), model: "moonshot-ai/kimi-k2.6");
        await LaunchAsync(_person.Places("kh-version"), new FakeKimi(exit: 1));
        await LaunchAsync(_person.Places("kh-missing"), new FakeKimi(throws: new Win32Exception("not found")));
        var shape = _person.Places("kh-shape");
        await LaunchAsync(shape, new FakeKimi { WhileTheVersionIsAsked = _ => File.WriteAllText(Path.Combine(shape.Home, "mcp.json"), "{}") });
        var request = Request(_person.Places("kh-inherited")) with { InheritsEnvironment = true };
        See("answers", (await KimiLaunch.TakeAsync(HostKind.Windows, request, _person.Places("kh-inherited"), new FakeKimi(), TestContext.Current.CancellationToken)).Refusal);
    }

    /// <summary>The person's config with a MALFORMED row — an unterminated string on the <c>api_key</c> line, and a model block whose capabilities line is broken.</summary>
    private static string BrokenConfig() =>
        KimiFixture.OwnerConfig().Replace($"api_key = \"{KimiFixture.ApiKeyCanary}\"", $"api_key = \"{KimiFixture.ApiKeyCanary}")
            + "\n[models.\"kimi-code/k3\"]\nprovider = \"managed:kimi-code\"\nmodel = \"k3\"\nmax_context_size = 262144\ncapabilities = [ \"a\", \n";

    private async Task MalformedConfigsAsync()
    {
        var broken = BrokenConfig();
        var slot = KimiPersonsConfig.Read(broken, "your config.toml");
        var block = KimiModelBlocks.For("kimi-code/k3", broken, ((BlocksLoad.Loaded)KimiModelBlocks.Builtin).Blocks);
        See("answers", Describe(slot));
        See("answers", Describe(block));
        slot.Should().BeOfType<SlotRead.Found>("the malformed api_key line is in a table nothing reads, so no parse error exists to quote it");
        block.Should().BeOfType<BlockRead.Refused>().Which.Reason.Should().Contain("capabilities", "the refusal names the KEY").And.Contain("fix the block", "and the cure");
        File.WriteAllText(_person.PersonsConfig, broken);
        var launched = await LaunchAsync(_person.Places("kh-malformed"), new FakeKimi(), model: "kimi-code/k3");
        launched.Refusal.Should().Contain("capabilities").And.Contain("fix the block").And.NotContain("\"a\",", "never the line");
        File.WriteAllText(_person.PersonsConfig, KimiFixture.OwnerConfig());
    }

    private static string Describe(object read) => read.ToString() ?? string.Empty;

    /// <summary>A stopped disposal, a lost target, a busy gate and an unusable one.</summary>
    private async Task EndingsAsync()
    {
        var lostFile = Path.Combine(_person.Credentials, "kimi-code.json");
        var stopped = _person.Places("kh-stopped");
        KimiHome.Made(HostKind.Windows, stopped, Expected()).Should().BeEmpty();
        KimiHome.AfterUnlink = _ => Lose(lostFile);
        try
        {
            See("answers", KimiHome.Dispose(stopped.OwnedRoot, line => See("warn", line)).ToString());
        }
        finally
        {
            KimiHome.AfterUnlink = null;
            Restore(lostFile);
        }

        var plain = _person.Places("kh-plain");
        KimiHome.Made(HostKind.Windows, plain, Expected()).Should().BeEmpty();
        GitScratch.AfterUnlink = _ => Lose(lostFile);
        try
        {
            GitScratch.DeleteEvenIfReadOnly(plain.OwnedRoot);
        }
        catch (LinkTargetLostException e)
        {
            See("exceptions", e.Message);
        }
        finally
        {
            GitScratch.AfterUnlink = null;
            Restore(lostFile);
        }

        await BusyAndUnusableAsync();
    }

    private async Task BusyAndUnusableAsync()
    {
        Directory.CreateDirectory(Locks);
        using (var holder = ((Hold.Taken)HeldFile.Take(KimiCredentialsLock.PathFor(_person.Credentials))).Stream)
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            try
            {
                await KimiLaunch.TakeAsync(HostKind.Windows, Request(_person.Places("kh-busy")), _person.Places("kh-busy"), new FakeKimi(), cancel.Token);
            }
            catch (OperationCanceledException e)
            {
                See("exceptions", e.Message);
            }
        }

        var unusable = Path.Combine(_person.Folder, "locks-as-a-file");
        File.WriteAllText(unusable, "x");
        using var other = KimiCredentialsLock.RootFor(unusable);
        await LaunchAsync(_person.Places("kh-unusable"), new FakeKimi());
    }

    private void Lose(string file)
    {
        File.SetAttributes(file, FileAttributes.Normal);
        File.Delete(file);
    }

    private void Restore(string file)
    {
        File.WriteAllText(file, """{"refresh_token": "not-a-real-one-either"}""");
        File.SetAttributes(file, FileAttributes.ReadOnly);
    }

    private byte[] Expected() => _person.ExpectedConfig();

    /// <summary>One sweep beat over an orphaned Kimi conversation whose person's folder loses a name during the unlink: the loss is a warning.</summary>
    private async Task SweepBeatAsync()
    {
        var homes = Path.Combine(_person.Folder, "homes");
        var data = Path.Combine(_person.Folder, "data");
        var folder = ConsultantHomes.ConversationFolder(homes, ConsultantHomes.Kimi, ConsultationStore.NewId());
        var places = new KimiPlaces(folder, _person.PersonsHome, _person.Bin, [], string.Empty, "kimi");
        KimiHome.Made(HostKind.Windows, places, Expected()).Should().BeEmpty();
        var store = ConversationFolder.StoreOf(data);
        ConversationFolder.Begin(folder, store, DateTime.UtcNow - TimeSpan.FromHours(2), TimeSpan.FromMinutes(10));
        ConversationFolder.End(folder);
        var old = DateTime.UtcNow - TimeSpan.FromHours(2);
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder).Where(entry => !OwnedPaths.IsLinkIfThere(entry)))
        {
            Directory.SetLastWriteTimeUtc(entry, old);
        }

        Directory.SetLastWriteTimeUtc(folder, old);
        var lostFile = Path.Combine(_person.Credentials, "kimi-code.json");
        KimiHome.AfterUnlink = _ => Lose(lostFile);
        try
        {
            ConsultantHomeSweep.Sweep(new HomeSweepInputs(homes, store, _ => null, DateTime.UtcNow, TimeSpan.FromMinutes(15)), line => See("warn", line));
        }
        finally
        {
            KimiHome.AfterUnlink = null;
            Restore(lostFile);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// One shim turn over a fake agent that asks for the sign-in's file by its real path AND through the owned home's junction, and says back
    /// whatever it is given — the REAL <c>coai-mcp --ask-acp --profile kimi</c> in a child process, so the tree hold is ACTUALLY taken (E4.3, carry (a)
    /// of the plan: E4.2's arm ran in-process with flags the shim did not read yet, and a test host never holds an inheritable handle). The
    /// agent probes both lock files from inside the tree and says so; the child's stdout and stderr are the report and the notes.
    /// </summary>
    private async Task<JsonElement> ShimTurnAsync(KimiPlaces places, params string[] reads)
    {
        using var agent = new FakeAcpAgent([.. reads, FakeAcpAgent.Chunk("done")], FakeAcpAgent.Models("kimi-code/k3"));
        var tree = KimiCredentialsLock.TreePathFor(_person.Credentials);
        var home = Path.Combine(places.OwnedRoot, KimiCredentialsLock.HomeHoldName);
        agent.WithFields(agent.EnvField(tree, home));
        var request = new ProcessRequest(
            ServerBinary.Path,
            [
                "--ask-acp", "--profile", "kimi", "--executable", FakeAcpAgent.FakeCliExe, "--model", "kimi-code/k3", "--cwd", agent.Cwd,
                "--deadline-seconds", "60", "--home", places.OwnedRoot, "--persons-bin", places.PersonsBin, "--locks", Locks,
            ],
            agent.Cwd)
        {
            StdIn = "say hello",
            Timeout = TimeSpan.FromMinutes(2),
        };

        var result = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0, result.StdErr);
        See("report", result.StdOut);
        See("note", result.StdErr);
        File.ReadAllLines(agent.EnvFile).Where(line => line.StartsWith("PROBE ", StringComparison.Ordinal)).Select(line => line[^4..]).Should()
            .Equal(["Held", "Held"], "the arm ran UNDER the tree hold: the agent saw the sign-in's file and the home's both held");

        return JsonDocument.Parse(result.StdOut).RootElement.Clone();
    }

    [Fact]
    public async Task NothingOfTheSignIn_EverAppearsInWhatCoaiWrites()
    {
        TheOwnersKimi.SkipUnlessWindowsAndFree("Kimi runs on Windows hosts only (D18)");
        using var rooted = KimiCredentialsLock.RootFor(Locks);

        await LaunchesAsync();
        await MalformedConfigsAsync();
        await EndingsAsync();
        await SweepBeatAsync();
        await RoundRecordsAsync();

        // The shim: the agent asks for the credentials by their real path and through the owned junction, and echoes the replies.
        var places = _person.Places("kh-happy");
        var real = Path.Combine(_person.Credentials, "kimi-code-env-x.json");
        var through = Path.Combine(places.Link, "kimi-code-env-x.json");
        var report = await ShimTurnAsync(places, FakeAcpAgent.ReadEchoed(real), FakeAcpAgent.ReadEchoed(through));
        using var refused = new FakeAcpAgent([]);
        await AskAcpMode.RunAsync(["--ask-acp", "--profile", "fake", "--model", "m-1"], new StringReader("x"), line => See("note", line), new StringWriter(), [refused.Profile()]);

        // What coai wrote: the lock file names (folder listing), the per-home hold's name, the rendered config.
        See("files", string.Join("\n", Directory.EnumerateFileSystemEntries(Locks).Select(Path.GetFileName)));
        See("files", KimiCredentialsLock.HomeHoldName);
        See("config", File.ReadAllText(places.Config));

        // Liveness: a collector that caught nothing proves nothing.
        From("answers").Should().Contain(text => text.Contains("not on the Kimi Code plan"), "the answers collector caught the refusal rows");
        From("answers").Should().Contain(text => text.Contains("is not a version coai has measured"), "and the version warning");
        From("answers").Should().Contain(text => text.Contains("Stopped") && text.Contains("kimi-code.json"), "and the stopped disposal");
        From("exceptions").Should().Contain(text => text.Contains("kimi-code.json") && text.Contains(_person.Credentials), "the lost-target exception was caught, naming the seam's file");
        From("exceptions").Should().OnlyContain(text => text.Length > 0);
        From("warn").Should().Contain(text => text.Contains("kimi-code.json") && text.Contains("kept this beat"), "the sweep's loss sentence was caught");
        From("config").Single().Should().Contain(KimiFixture.SlotKey, "the config collector holds the rendered config");
        From("files").First().Should().MatchRegex("\\.lock", "the lock file names were listed");
        From("files").First().Should().Contain(".tree.lock", "the shim's own hold file was made under the --locks folder and listed");
        From("note").Should().Contain(text => text.Contains("--cwd"), "the shim's note collector caught its own sentence");
        AssertTheRoundsRecordsWereCaught();
        report.GetProperty("reads").GetArrayLength().Should().Be(2);
        report.GetProperty("reads").EnumerateArray().Should().OnlyContain(read => !read.GetProperty("served").GetBoolean() && read.GetProperty("reason").GetString()!.Length > 0,
            "both reads of the sign-in are refused with a type-named reason, never bytes");

        _seen.Where(entry => entry.Text.Contains(KimiFixture.RefreshCanary, StringComparison.Ordinal)).Should().BeEmpty("the refresh-token canary appears in nothing coai writes");
        _seen.Where(entry => entry.Text.Contains(KimiFixture.ApiKeyCanary, StringComparison.Ordinal)).Should().BeEmpty("the api-key canary appears in nothing coai writes");
        _seen.Where(entry => entry.Text.Contains("CANARY-", StringComparison.Ordinal)).Should().BeEmpty();
    }

    // ---------- E4.4: S2.45 re-run through the round's records and the reviewer line ----------

    private KimiRuntime Runtime() => new("kimi", HostKind.Windows, new KimiSeams(_person.PlacesAt, () => ("coai-mcp.exe", [])));

    /// <summary>One reviewer launch through the real adapter and executor; what it ENDED as is collected the four ways the round writes it down.</summary>
    private async Task<ReviewerOutcome> RecordAsync(ScriptedKimi launcher, string model = KimiModels.Default)
    {
        var settings = new ReviewerSettings("kimi") { Model = model, OwnedScratch = Path.Combine(_person.Folder, "round", Guid.NewGuid().ToString("N")[..8]), Timeout = TimeSpan.FromMinutes(5) };
        var worktree = Directory.CreateDirectory(Path.Combine(_person.Folder, "worktree")).FullName;
        var invocation = Runtime().Build(RoleCatalog.ArchitectureRole, "review this", worktree, Path.Combine(_person.Folder, "s.json"), _person.Folder, settings);

        var outcome = await new ReviewerExecutor(launcher).RunAsync(invocation, ct: TestContext.Current.CancellationToken);
        See("line", ReviewerSummaryFactory.Describe(outcome));
        See("ledger", ReviewerSummaryFactory.DescribeWhole(outcome));
        See("warning", outcome.Warning);
        See("record", outcome.ToString());

        return outcome;
    }

    /// <summary>The reviewer line, the ledger text, the warning and the outcome record — through every ending a Kimi reviewer has.</summary>
    private async Task RoundRecordsAsync()
    {
        await RecordAsync(new ScriptedKimi());
        await RecordAsync(new ScriptedKimi(version: "3.0.0"));
        await RecordAsync(new ScriptedKimi(), model: "moonshot-ai/kimi-k2.6");
        await RecordAsync(new ScriptedKimi(_ => KimiShimScripts.Ended(75, AcpEnd.AgentFailed, "insufficient balance", failure: "balance_spent", stderr: "balance_spent: insufficient balance — the Kimi allowance is spent (D14)")));
        await RecordAsync(new ScriptedKimi(_ => KimiShimScripts.Ended(77, AcpEnd.AgentFailed, "Authentication required", failure: "not_signed_in", stderr: "not_signed_in: Authentication required — run `kimi login`")));
        await RecordAsync(new ScriptedKimi(_ => KimiShimScripts.Ended(69, AcpEnd.NotStarted, "another Kimi started on this sign-in is still running", stderr: "another Kimi started on this sign-in is still running")));
        await RecordAsync(new ScriptedKimi(_ => KimiShimScripts.Ended(65, AcpEnd.ModelNotOffered, "the agent's session offers a for 'model'; 'b' is not one of them", stderr: "the agent's session offers a for 'model'; 'b' is not one of them")));
        File.WriteAllText(_person.PersonsConfig, BrokenConfig());
        try
        {
            await RecordAsync(new ScriptedKimi(), model: "kimi-code/k3");
        }
        finally
        {
            File.WriteAllText(_person.PersonsConfig, KimiFixture.OwnerConfig());
        }
    }

    private void AssertTheRoundsRecordsWereCaught()
    {
        // Liveness, one per collector: a collector that caught nothing proves nothing.
        From("line").Should().Contain(text => text.Contains("not on the Kimi Code plan"), "the reviewer line caught the model refusal");
        From("line").Should().Contain(text => text.Contains("still running"), "and the busy sign-in");
        From("line").Should().Contain(text => text.Contains("kimi login"), "and the diagnosed sign-in refusal");
        From("line").Should().Contain(text => text.Contains("does not offer this model"), "and the model the session does not offer");
        From("line").Should().Contain(text => text.Contains("capabilities") && text.Contains("fix the block"), "and the malformed block's refusal, naming the KEY and the cure");
        From("ledger").Should().Contain(text => text.Contains("rate limited"), "the ledger text caught the spent allowance");
        From("warning").Should().Contain(text => text.Contains("is not a version coai has measured"), "the warning collector caught the unmeasured version");
        From("record").Should().Contain(text => text.Contains("NotStarted") && text.Contains("not on the Kimi Code plan"), "the outcome records were caught");
    }

    [Fact]
    public async Task TheDetector_SeesVendorTextRelayedIntoTheRoundsRecords()
    {
        TheOwnersKimi.SkipUnlessWindowsAndFree("Kimi runs on Windows hosts only (D18)");
        using var rooted = KimiCredentialsLock.RootFor(Locks);

        await RecordAsync(new ScriptedKimi(_ => new ProcessResult(70, string.Empty, $"agent printed its own file: {KimiFixture.RefreshCanary}", TimedOut: false)));

        From("line").Should().Contain(text => text.Contains(KimiFixture.RefreshCanary),
            "what a vendor's process prints is relayed by the reviewer line, outside the pin (decision 12) — and the detector sees it, so the pin above is not blind");
    }

    [Fact]
    public async Task TheDetector_SeesAServedCanary_WhenOneIsServed()
    {
        TheOwnersKimi.SkipUnlessWindowsAndFree("Kimi runs on Windows hosts only (D18)");
        using var served = new FakeAcpAgent([]);
        var notes = served.Inside("notes.md");
        File.WriteAllText(notes, $"keep this: {KimiFixture.RefreshCanary}");
        File.WriteAllText(served.Script, $$"""{"log":{{FakeAcpAgent.Quote(served.Log)}},"steps":[{{FakeAcpAgent.ReadEchoed(notes)}},{{FakeAcpAgent.Chunk("done")}}]}""");
        var output = new StringWriter();

        var code = await AskAcpMode.RunAsync(
            ["--ask-acp", "--profile", "fake", "--model", "m-1", "--cwd", served.Cwd, "--deadline-seconds", "30"],
            new StringReader("say hello"), _ => { }, output, [served.Profile()], TestContext.Current.CancellationToken);

        code.Should().Be(0);
        var report = JsonDocument.Parse(output.ToString()).RootElement;
        report.GetProperty("reads")[0].GetProperty("served").GetBoolean().Should().BeTrue("a file INSIDE the snapshot root is served");
        report.GetProperty("answer").GetString().Should().Contain(KimiFixture.RefreshCanary, "the detector sees a served canary when one is served");
    }
}
