using System.ComponentModel;
using System.Text.Json;
using CoaiMcp.Acp;
using CoaiMcp.Core.Rounds;
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
        await LaunchAsync(_person.Places("kh-warned"), new FakeKimi("2.2.0"));
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
    private async Task MalformedConfigsAsync()
    {
        var broken = KimiFixture.OwnerConfig().Replace($"api_key = \"{KimiFixture.ApiKeyCanary}\"", $"api_key = \"{KimiFixture.ApiKeyCanary}")
            + "\n[models.\"kimi-code/k3\"]\nprovider = \"managed:kimi-code\"\nmodel = \"k3\"\nmax_context_size = 262144\ncapabilities = [ \"a\", \n";
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

    private byte[] Expected()
    {
        var slot = ((SlotRead.Found)KimiPersonsConfig.ReadFile(_person.PersonsConfig)).Slot;
        var block = ((BlockRead.Found)KimiModelBlocks.For(KimiModels.Default, File.ReadAllText(_person.PersonsConfig), ((BlocksLoad.Loaded)KimiModelBlocks.Builtin).Blocks)).Block;

        return KimiConfig.Bytes(KimiConfig.Render(slot, block));
    }

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

    /// <summary>One shim turn over a fake agent that asks for the sign-in's file by its real path AND through the owned home's junction, and says back whatever it is given.</summary>
    private async Task<JsonElement> ShimTurnAsync(KimiPlaces places, params string[] reads)
    {
        using var agent = new FakeAcpAgent([.. reads, FakeAcpAgent.Chunk("done")]);
        var output = new StringWriter();
        var code = await AskAcpMode.RunAsync(
            ["--ask-acp", "--profile", "fake", "--model", "m-1", "--cwd", agent.Cwd, "--deadline-seconds", "30", "--home", places.OwnedRoot, "--locks", Locks],
            new StringReader("say hello"), line => See("note", line), output, [agent.Profile()], TestContext.Current.CancellationToken);
        code.Should().Be(0);
        See("report", output.ToString());

        return JsonDocument.Parse(output.ToString()).RootElement.Clone();
    }

    [Fact]
    public async Task NothingOfTheSignIn_EverAppearsInWhatCoaiWrites()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Kimi runs on Windows hosts only (D18)");
        using var rooted = KimiCredentialsLock.RootFor(Locks);

        await LaunchesAsync();
        await MalformedConfigsAsync();
        await EndingsAsync();
        await SweepBeatAsync();

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
        From("note").Should().Contain(text => text.Contains("--cwd"), "the shim's note collector caught its own sentence");
        report.GetProperty("reads").GetArrayLength().Should().Be(2);
        report.GetProperty("reads").EnumerateArray().Should().OnlyContain(read => !read.GetProperty("served").GetBoolean() && read.GetProperty("reason").GetString()!.Length > 0,
            "both reads of the sign-in are refused with a type-named reason, never bytes");

        _seen.Where(entry => entry.Text.Contains(KimiFixture.RefreshCanary, StringComparison.Ordinal)).Should().BeEmpty("the refresh-token canary appears in nothing coai writes");
        _seen.Where(entry => entry.Text.Contains(KimiFixture.ApiKeyCanary, StringComparison.Ordinal)).Should().BeEmpty("the api-key canary appears in nothing coai writes");
        _seen.Where(entry => entry.Text.Contains("CANARY-", StringComparison.Ordinal)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheDetector_SeesAServedCanary_WhenOneIsServed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Kimi runs on Windows hosts only (D18)");
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
