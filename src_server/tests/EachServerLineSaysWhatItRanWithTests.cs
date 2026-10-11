using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// Each ledger line says what its launch RAN WITH — story 3.4 of <c>todo/PLAN_every_round_is_counted.md</c>, the Team
/// server's half: the CLI and its version, the effort and the tier as the launch actually sent them with their sources,
/// and the job and the account slot — by the slot's NAME, never an email.
/// </summary>
/// <remarks>
/// <para><b>What was wrong.</b> A server line said who, which vendor and model, and what it cost — not which CLI release
/// ran it, not whether the request's effort was applied (<c>ClientOptions</c> drops every codex effort), not what tier
/// the launch was told, and not which account carried it. A server measured against a local run could not say whether
/// the two ran with the same options.</para>
/// <para>The seam rule: the REAL <see cref="JobRunner"/> and <see cref="ReviewLauncher"/>, and a fake
/// <see cref="IProcessLauncher"/> that keeps every request it is handed — argv and environment — so each assertion
/// compares the line with what was received, not with what the job was configured to ask for.</para>
/// </remarks>
[Collection(ServerCollection.Name)]
public sealed class EachServerLineSaysWhatItRanWithTests : IDisposable
{
    private const string Release = "codex-cli 0.160.0";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-server-ran-with-" + Guid.NewGuid().ToString("N"));

    public EachServerLineSaysWhatItRanWithTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp directory a vendor helper still holds is the OS's to clean, not this test's to fail on.
        }
    }

    private SlotRegistry Slots() => new(_dir, new JsonFileStore());

    /// <summary>One vendor on one signed-in slot, whose own codex configuration says <paramref name="config"/>.</summary>
    private void Vendor(string id, string runtime, string model, string config = "")
    {
        File.WriteAllText(Path.Combine(_dir, "vendors.json"),
            $$"""[{ "id": "{{id}}", "runtime": "{{runtime}}", "models": ["{{model}}"], "slots": ["a"] }]""");
        var registry = Slots();
        registry.MarkSignedIn(registry.Read(id, "a"));
        if (config.Length > 0)
        {
            var codexHome = Directory.CreateDirectory(Path.Combine(registry.DirectoryFor(id, "a"), ".codex")).FullName;
            File.WriteAllText(Path.Combine(codexHome, "config.toml"), config);
        }
    }

    private async Task<JsonElement> RunOnce(Received launcher, JobRecord job, string vendor)
    {
        var jobs = new JobStore();
        var runner = new JobRunner(
            jobs, new VendorCatalogHost(_dir), Slots(),
            new ReviewLauncher(launcher, versionOf: runtime => runtime == "codex" ? Release + "\n" : string.Empty),
            new UsageLedger(_dir));
        jobs.Submit(job);

        (await runner.PumpAsync(vendor, CancellationToken.None)).Should().BeTrue("the slot is signed in and free");

        return JsonDocument.Parse(File.ReadAllLines(Path.Combine(_dir, "usage.jsonl")).Single()).RootElement.Clone();
    }

    private static JobRecord Job(string vendor, string model, string effort = "", string effortFrom = "") =>
        new(JobId.New(), "dev@example.com", vendor, model, "Architecture", "review this", JobStatus.Queued,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), TimeSpan.FromSeconds(60),
            Effort: effort, EffortFrom: effortFrom);

    private static string Text(JsonElement line, string name) =>
        line.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : "<absent>";

    [Fact]
    public async Task TheRecordedLaunch_IsWhatWasSent()
    {
        Vendor("codex", "codex", "gpt-5.6-luna", "model_reasoning_effort = \"low\"\nservice_tier = \"flex\"\n");
        var launcher = new Received();
        var job = Job("codex", "gpt-5.6-luna");

        var line = await RunOnce(launcher, job, "codex");

        var sent = launcher.Launch;
        (Text(line, "tier"), Text(line, "tierFrom")).Should().Be((Setting(sent.Arguments, "service_tier"), OptionSources.Server),
            "a server launch is told the standard tier by the server itself — the request carries no tier — and the argv says so");
        Setting(sent.Arguments, "service_tier").Should().Be("default", "the argv the CLI received, not the slot's configured 'flex'");
        Setting(sent.Arguments, "model_reasoning_effort").Should().BeEmpty("the request carried no effort, and nothing added one");
        var slotConfig = CodexConfigFile.TextIn(name => sent.Environment.GetValueOrDefault(name));
        (Text(line, "effort"), Text(line, "effortFrom")).Should().Be(
            (CodexConfigFile.RootValue(slotConfig, "model_reasoning_effort"), OptionSources.CliConfig),
            "nothing was sent, and the config.toml the launch's own CODEX_HOME points at — the SLOT's — names the effort");
        Text(line, "effort").Should().Be("low");
        Text(line, "cli").Should().Be("codex");
        Text(line, "cliVersion").Should().Be(Release, "the catalog's probe already asked the CLI; the line names its first line");
        launcher.VersionsAsked.Should().Be(0, "no --version is spawned for a ledger line");
        Text(line, "job").Should().Be(job.Id);
        Text(line, "slot").Should().Be("a", "the slot's name from vendors.json");
    }

    [Fact]
    public async Task TheSlot_IsItsName_AndNoFieldButTheCallersCarriesAnEmail()
    {
        Vendor("codex", "codex", "gpt-5.6-luna");

        var line = await RunOnce(new Received(), Job("codex", "gpt-5.6-luna"), "codex");

        Text(line, "slot").Should().Be("a");
        line.EnumerateObject()
            .Where(field => field.Name != "email" && field.Value.ValueKind == JsonValueKind.String)
            .Should().OnlyContain(field => !field.Value.GetString()!.Contains('@'),
                "an account is named by its slot; the only address on a line is the caller's own");
    }

    [Fact]
    public async Task ADroppedRemoteEffort_IsRecordedAsDropped()
    {
        var job = await Accepted("codex", "gpt-5.6-luna", "high");
        job.Effort.Should().BeEmpty("codex takes no effort a server can apply");
        job.EffortFrom.Should().Be(OptionSources.Dropped, "the job must carry what became of the request's effort to its line");
        // The slot's configuration names an effort. Two facts, two fields (the consultation of 2026-10-11): the request's
        // effort was DROPPED, and what ran is the slot's configured 'low', labelled as the configuration's.
        Vendor("codex", "codex", "gpt-5.6-luna", "model_reasoning_effort = \"low\"\n");
        var launcher = new Received();

        var line = await RunOnce(launcher, job, "codex");

        Text(line, "effortNotSent").Should().Be(OptionSources.Dropped);
        (Text(line, "effort"), Text(line, "effortFrom")).Should().Be(("low", OptionSources.CliConfig),
            "the person's setting did not run; the slot's configured effort did");
        Setting(launcher.Launch.Arguments, "model_reasoning_effort").Should().BeEmpty();
    }

    [Fact]
    public async Task AClampedEffort_IsRecordedAsTheServers_AndATakenOneAsTheRows()
    {
        var clamped = await Accepted("claude", "opus", "max", new Dictionary<string, string?> { ["Coai__MaxEffort"] = "medium" });
        var taken = await Accepted("claude", "opus", "high");
        (clamped.EffortFrom, taken.EffortFrom).Should().Be((OptionSources.Server, OptionSources.Row));
        Vendor("claude", "claude", "opus");
        var launcher = new Received();

        var line = await RunOnce(launcher, clamped, "claude");

        (Text(line, "effort"), Text(line, "effortFrom")).Should().Be((After(launcher.Launch.Arguments, "--effort"), OptionSources.Server),
            "the CLI was told the cap, and the cap was the server's choice, not the person's");
        Text(line, "effort").Should().Be("medium");
        (Text(line, "tier"), Text(line, "tierFrom")).Should().Be((string.Empty, string.Empty),
            "a claude launch on this server is handed no fast-mode file, and claude's own settings are not read");
    }

    [Fact]
    public void AnOldLedgerLine_StillReads_WhatItRanWith()
    {
        File.WriteAllText(
            Path.Combine(_dir, "usage.jsonl"),
            """{"utc":"2026-10-10T10:00:00.0000000Z","provider":"codex","model":"m","role":"Architecture","stage":"TeamServer","seconds":8.4,"tokensIn":14508,"tokensOut":5,"costUsd":null,"outcome":"ok","email":"dev@example.com","kind":"review","turn":"1760090400-abc","attempt":"first"}""" + "\n"
            + """{"utc":"2026-10-10T10:00:01.0000000Z","provider":"codex","model":"m","role":"Architecture","stage":"TeamServer","seconds":1.0,"tokensIn":3,"tokensOut":1,"costUsd":null,"outcome":"ok","email":"dev@example.com","kind":"review","turn":"1760090400-def","attempt":"first","cli":"codex","cliVersion":"codex-cli 0.160.0","effort":"low","effortFrom":"cli-config","tier":"default","tierFrom":"server","job":"1760090400-def","slot":"a"}""" + "\n");
        var at = new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);

        var scan = new UsageReader(_dir).Read(new UsageRange(at.AddMinutes(-1), at.AddMinutes(1)));

        scan.Unreadable.Should().Be(0, "a line older than the fields is history, never damage");
        scan.Lines.Should().HaveCount(2);
        (scan.Lines[0].RanWith, scan.Lines[0].Job, scan.Lines[0].Slot).Should().Be((LaunchOptions.None, string.Empty, string.Empty),
            "an old line says nothing of what it ran with, which is the truth about it");
        scan.Lines[1].RanWith.Should().Be(new LaunchOptions("codex", "codex-cli 0.160.0", "low", "cli-config", "default", "server"),
            "the reader dropped the fields the line holds");
        (scan.Lines[1].Job, scan.Lines[1].Slot).Should().Be(("1760090400-def", "a"));
    }

    /// <summary>A request accepted through the real <c>POST /api/reviews</c>, as the job the store holds for it.</summary>
    private static async Task<JobRecord> Accepted(string vendor, string model, string effort, IDictionary<string, string?>? settings = null)
    {
        using var server = new TeamServer(settings);
        File.WriteAllText(Path.Combine(server.DataDir, "vendors.json"),
            $$"""[{ "id": "{{vendor}}", "runtime": "{{vendor}}", "models": ["{{model}}"], "slots": ["b"] }]""");
        var request = new ReviewRequestDto(vendor, model, "review this", "Architecture", 60) { Effort = effort };
        var response = await server.ClientFor($"dev@{TeamServer.Domain}").PostAsJsonAsync("/api/reviews", request);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var accepted = (await response.Content.ReadFromJsonAsync<ReviewAcceptedDto>())!;

        return server.Services.GetRequiredService<JobStore>().Find(accepted.Id, $"dev@{TeamServer.Domain}")!;
    }

    /// <summary>The value of a codex <c>-c key=value</c> override on the argv, or empty.</summary>
    private static string Setting(IReadOnlyList<string> argv, string key) =>
        argv.Zip(argv.Skip(1)).Where(pair => pair.First == "-c" && pair.Second.StartsWith(key + "=", StringComparison.Ordinal))
            .Select(pair => pair.Second[(key.Length + 1)..]).FirstOrDefault() ?? string.Empty;

    private static string After(IReadOnlyList<string> argv, string flag) =>
        argv.Zip(argv.Skip(1)).Where(pair => pair.First == flag).Select(pair => pair.Second).FirstOrDefault() ?? string.Empty;

    /// <summary>
    /// A vendor that answers cleanly — codex in its <c>-o</c> file, claude in its envelope — and keeps every request it was
    /// handed, environment and all.
    /// </summary>
    private sealed class Received : IProcessLauncher
    {
        private const string ClaudeEnvelope = """{"type":"result","subtype":"success","is_error":false,"result":"{\"findings\":[]}"}""";

        private const string CodexStream = """{"type":"turn.completed","usage":{"input_tokens":120,"cached_input_tokens":0,"output_tokens":9}}""";

        private readonly ConcurrentQueue<ProcessRequest> _requests = new();

        /// <summary>The one review launch — never a <c>--version</c>.</summary>
        public ProcessRequest Launch => _requests.Single(r => r.Arguments is not ["--version"]);

        public int VersionsAsked => _requests.Count(r => r.Arguments is ["--version"]);

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            _requests.Enqueue(request);
            var answerFile = After(request.Arguments, "-o");
            if (answerFile.Length > 0)
            {
                File.WriteAllText(answerFile, """{"findings":[]}""");
            }

            return Task.FromResult(new ProcessResult(0, answerFile.Length > 0 ? CodexStream : ClaudeEnvelope, string.Empty, TimedOut: false));
        }
    }
}
