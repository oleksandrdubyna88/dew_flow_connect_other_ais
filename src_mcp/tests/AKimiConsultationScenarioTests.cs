using System.Text.Json;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Kimi consultation through the whole <c>consult</c> flow (E5.4 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.4.7–S5.4.10 and
/// S5.4.14–S5.4.20): the real service, the real snapshot (git runs for real), the real launch control over a fake person's sign-in
/// (<see cref="KimiFixture"/>, a real junction on Windows, a symbolic link elsewhere through <see cref="WindowsStandIn"/>) and a real gate file —
/// only the shim is played (<see cref="PlayedKimi"/>), so no turn is paid for and the real <c>kimi</c> is never started.
/// </summary>
/// <remarks>
/// <para>The service builds its Kimi adapter BY NAME, so the fake person and the measured host are stood in per async flow
/// (<see cref="KimiSeams.Using"/>, the <c>VibePathBudget.Using</c> precedent), with the lock root (<c>KimiCredentialsLock.RootFor</c>) beside them.
/// Every folder a test makes is the scenario's data directory, the fixture's <c>testsToDelete-kimi-</c> folder, or a <c>testsToDelete-</c> folder of
/// its own; the person's folder lives outside the homes root the sweeps delete.</para>
/// <para>These are E5.3's service-level arms too (its deviation 16): the re-login refusal with the record left open (S5.4.16), the two-turn canary
/// (S5.4.17), one sign-in's peak (S5.4.18), the gate another process holds (S5.4.19), and the owned root the service hands (S5.4.20).</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiConsultationScenarioTests : ConsultScenarioBase
{
    private static readonly IReadOnlyDictionary<string, ConsultantChoice> OnKimi = new Dictionary<string, ConsultantChoice>
    {
        [CallerIdentity.Claude] = new("my-kimi", Runtime: "kimi"),
    };

    /// <summary>The homes root, inside the scenario's data directory — removed with it, and never the machine's own local data.</summary>
    private string Homes => Directory.CreateDirectory(Path.Combine(_data, "homes")).FullName;

    private string FolderOf(string id) => ConsultantHomes.ConversationFolder(Homes, ConsultantHomes.Kimi, id);

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

    /// <summary>
    /// The person, the host and the lock root this flow's Kimi adapters see — set by a SYNCHRONOUS call, so the values reach the test's own flow
    /// and every service call it awaits.
    /// </summary>
    private static Scopes StandIn(KimiFixture person) => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))),
        CallingAs("CLAUDE_CODE_SESSION_ID"));

    private PanelService Kimi(PlayedKimi played, Serilog.ILogger? log = null) =>
        Service(providers: [], consultants: OnKimi, launcher: played, homes: Homes, log: log);

    private static string IdOf(JsonElement reply) => reply.GetProperty("consultationId").GetString()!;

    private static void Answered(JsonElement reply) =>
        reply.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());

    private JsonElement Record(string id) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(_data, "consultations", id + ".json"))).RootElement;

    /// <summary>The consult lines of the ledger, in order: what each turn was billed.</summary>
    private List<JsonElement> LedgerLines() =>
    [
        .. File.ReadAllLines(Path.Combine(_data, "usage.jsonl")).Select(line => JsonDocument.Parse(line).RootElement)
            .Where(line => line.GetProperty("role").GetString() == ConsultantRoles.Consult),
    ];

    private static (long In, long Out) Tokens(JsonElement element) => (element.GetProperty("tokensIn").GetInt64(), element.GetProperty("tokensOut").GetInt64());

    /// <summary>A three-turn conversation whose shim reports the given CUMULATIVE totals (null: <c>notCaptured</c>).</summary>
    private async Task<(PlayedKimi Played, string Id)> ThreeTurnsAsync(params (long In, long Out)?[] totals)
    {
        var played = new PlayedKimi(_launcher)
        {
            Turn = (n, request) => totals[n] is { } total ? PlayedKimi.Answered(PlayedKimi.SessionOf(request), total.In, total.Out) : PlayedKimi.Uncaptured(PlayedKimi.SessionOf(request)),
        };
        var service = Kimi(played);
        var first = await Consult(service, "why is the count wrong");
        Answered(first);
        var id = IdOf(first);
        foreach (var problem in new[] { "and now?", "and then?" }.Take(totals.Length - 1))
        {
            Answered(await Consult(service, problem, id));
        }

        return (played, id);
    }

    // ---------- S5.4.7 ----------

    [Fact]
    public async Task AThreeTurnConversation_IsBilledEachTurnsShare()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);

        var (played, id) = await ThreeTurnsAsync((20_000, 30), (40_004, 72), (60_010, 101));

        var lines = LedgerLines().Select(Tokens).ToList();
        lines.Should().HaveCount(3);
        lines[1].In.Should().Be(20_004L, "Expected the second turn's billed TokensIn to be its share of the session's running total (M-K5b)");
        lines.Should().Equal([(20_000L, 30L), (20_004L, 42L), (20_006L, 29L)]);
        Tokens(Record(id).GetProperty("billed")).Should().Be((60_010L, 101L), "the record's running total is the session's last total");
        var shims = played.Shims.ToArray();
        shims[0].Request.Arguments.Should().NotContain(AcpWords.SessionFlag, "turn 1 starts the session");
        shims.Skip(1).Should().OnlyContain(launch => launch.Request.Arguments.TakeLast(2).SequenceEqual(new[] { AcpWords.SessionFlag, PlayedKimi.Minted }),
            "every later turn continues turn 1's session by the id its report named");
    }

    // ---------- S5.4.8 ----------

    [Fact]
    public async Task AnUncapturedTurn_IsUnknown_AndTheNextCarriesItsShare()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);

        var (_, id) = await ThreeTurnsAsync((20_000, 30), null, (60_010, 101));

        var lines = LedgerLines();
        Tokens(lines[1]).Should().Be((0L, 0L), "an uncaptured turn is billed unknown, never a guess (decision 4)");
        lines[1].GetProperty("usageNote").GetString().Should().NotBeEmpty("and the line says the usage was not captured");
        Tokens(lines[2]).Should().Be((40_010L, 71L), "the next captured share carries the uncaptured turn's spend: filed late, never twice");
        Tokens(Record(id).GetProperty("billed")).Should().Be((60_010L, 101L), "the conversation's total stays right");
    }

    // ---------- S5.4.14, through the service ----------

    [Fact]
    public async Task ATotalBelowWhatWasBilled_IsAReset_AndTheTurnIsBilledWhatItSaid()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);

        var (_, id) = await ThreeTurnsAsync((20_000, 30), (5_000, 9));

        var lines = LedgerLines();
        Tokens(lines[1]).In.Should().Be(5_000L, "Expected the second turn's billed TokensIn to be the reset report's own (decision 12)");
        Tokens(lines[1]).Should().Be((5_000L, 9L));
        Tokens(Record(id).GetProperty("billed")).Should().Be((25_000L, 39L), "the record's total advances by the share");
    }

    // ---------- S5.4.9 and S5.4.15 ----------

    private async Task<(PlayedKimi Played, PanelService Service, string Id, string Said)> DroppedAsync()
    {
        var played = new PlayedKimi(_launcher)
        {
            Turn = (n, request) => n == 0 ? PlayedKimi.Answered(PlayedKimi.SessionOf(request), 20_000, 30) : PlayedKimi.Lost(PlayedKimi.SessionOf(request)),
        };
        var service = Kimi(played);
        var first = await Consult(service, "why is the count wrong");
        Answered(first);
        var id = IdOf(first);

        return (played, service, id, Refusal(await Consult(service, "and now?", id)));
    }

    [Fact]
    public async Task ADroppedSession_ClosesTheConversationWithItsCure()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);

        var (_, _, id, said) = await DroppedAsync();

        said.Should().Contain($"(failure: {ConsultFailureKinds.ConversationDropped})", "Expected the failure to be ConversationDropped: the report's END word is session_lost");
        said.Should().Contain("no longer holds this conversation").And.Contain("start a new consultation");
        Record(id).GetProperty("failureKind").GetString().Should().Be(ConsultFailureKinds.ConversationDropped);
    }

    [Fact]
    public async Task ADroppedSession_NeverStartsAFreshOne()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        var recorded = new List<string>();
        var before = person.Hash();

        var (played, service, id, said) = await DroppedAsync();

        played.Shims.Should().HaveCount(2, "Expected the played shim to have been launched 2 times: turn 1 and the follow-up — no fresh session after the loss");
        played.Shims.Last().Request.Arguments.Should().Contain(AcpWords.SessionFlag, "the follow-up asked for the session it had, and nothing launched after it");
        var record = Record(id);
        record.GetProperty("status").GetString().Should().NotBe(ConsultationStatuses.Open).And.Be(ConsultationStatuses.Failed, "the conversation is over, not resumable");
        record.GetProperty("handle").GetString().Should().BeEmpty("a handle the vendor no longer holds is never offered again");
        said.Should().Contain("start a new consultation").And.Contain("next sweep", "the cure says where the home goes: close_consult refuses a failed record");
        said.Should().NotContain("pick the conversation up", "it is not resumable");

        GitScratch.AfterUnlink = recorded.Add;
        try
        {
            service.SweepConsultations();
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }

        Directory.Exists(FolderOf(id)).Should().BeFalse("the sweep takes a failed conversation's home (D20)");
        recorded.Should().BeEmpty("KimiHome unlinked the junction first, so the deleter never met it");
        person.Hash().Should().Equal(before, "the person's sign-in folder is whole");
    }

    // ---------- S5.4.10 ----------

    [Fact]
    public async Task TheTurnIsBilledUnderTheModelThatRan()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);

        Answered(await Consult(Kimi(new PlayedKimi(_launcher)), "why is the count wrong"));

        LedgerLines().Single().GetProperty("model").GetString().Should().Be(KimiModels.Default, "a row with no model runs on Kimi's default, and is billed under it (E4 decision 75)");
    }

    // ---------- S5.4.16 ----------

    [Fact]
    public async Task AReLoginBetweenTurns_IsLaunchRefused_TheRecordStaysOpen_AndCloseConsultRemovesTheHomeJunctionFirst()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        var played = new PlayedKimi(_launcher);
        var service = Kimi(played);
        var first = await Consult(service, "why is the count wrong");
        Answered(first);
        var id = IdOf(first);
        File.WriteAllText(person.PersonsConfig, KimiFixture.OwnerConfig().Replace(KimiFixture.SlotKey, "oauth/kimi-code-env-y"));
        var before = person.Hash();

        var said = Refusal(await Consult(service, "and now?", id));

        said.Should().Contain($"(failure: {ConsultFailureKinds.LaunchRefused})").And.Contain("config.toml is not what coai writes").And.Contain("re-login");
        played.Shims.Should().HaveCount(1, "the refused turn started nothing");
        Record(id).GetProperty("status").GetString().Should().Be(ConsultationStatuses.Open,
            "a refused launch is the turn's failure, not the conversation's: nothing was launched");
        var recorded = new List<string>();
        GitScratch.AfterUnlink = recorded.Add;
        try
        {
            Answered(await Close(service, id, ConsultationOutcomes.Abandoned));
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }

        Directory.Exists(FolderOf(id)).Should().BeFalse("close_consult takes the conversation's home with it");
        recorded.Should().BeEmpty("the junction was unlinked by KimiHome, never met by the bare deleter");
        person.Hash().Should().Equal(before, "the person's sign-in folder is whole — names, attributes and bytes");
    }

    // ---------- S5.4.17 ----------

    /// <summary>Every name under the homes root and the data directory — never entering a link — one per line.</summary>
    private string Names() =>
        string.Join("\n", Directory.EnumerateFileSystemEntries(_data, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Select(path => Path.GetRelativePath(_data, path)));

    private string Records() =>
        string.Join("\n", Directory.EnumerateFiles(Path.Combine(_data, "consultations")).Select(File.ReadAllText));

    [Fact]
    public async Task NothingOfTheSignIn_AppearsInATwoTurnConversationsRecords()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        var sink = new ListSink();
        var log = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        var service = Kimi(new PlayedKimi(_launcher), log);
        var seen = new List<(string Where, string Text)>();

        var first = await Consult(service, "why is the count wrong");
        var id = IdOf(first);
        var second = await Consult(service, "and now?", id);
        seen.Add(("names", Names()));
        var closed = await Close(service, id, ConsultationOutcomes.Solved);
        seen.AddRange([("reply", first.GetRawText()), ("reply", second.GetRawText()), ("reply", closed.GetRawText())]);
        seen.Add(("record", Records()));
        seen.Add(("ledger", File.ReadAllText(Path.Combine(_data, "usage.jsonl"))));
        seen.AddRange(sink.Lines.Select(line => ("log", line)));
        seen.Add(("names", Names()));

        // Liveness: a collector that caught nothing proves nothing.
        seen.Should().Contain(entry => entry.Where == "reply" && entry.Text.Contains("separator"), "the replies carry the advice");
        seen.Should().Contain(entry => entry.Where == "record" && entry.Text.Contains("separator") && entry.Text.Contains(PlayedKimi.Minted), "the record carries the advice and the handle");
        seen.Should().Contain(entry => entry.Where == "ledger" && entry.Text.Contains("\"consult\""), "the ledger carries both turns");
        seen.Should().Contain(entry => entry.Where == "log" && entry.Text.Contains(id), "the log names the consultation");
        seen.Should().Contain(entry => entry.Where == "names" && entry.Text.Contains("kh-"), "the folder names were listed while the home stood");
        seen.Where(entry => entry.Text.Contains(KimiFixture.RefreshCanary, StringComparison.Ordinal)).Should().BeEmpty("the refresh-token canary appears in nothing coai writes");
        seen.Where(entry => entry.Text.Contains(KimiFixture.ApiKeyCanary, StringComparison.Ordinal)).Should().BeEmpty("the api-key canary appears in nothing coai writes");
        seen.Where(entry => entry.Text.Contains("CANARY-", StringComparison.Ordinal)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheDetector_SeesACanaryTheVendorRelays()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        var played = new PlayedKimi(_launcher) { Turn = (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 10, 1, $"it printed its own file: {KimiFixture.RefreshCanary}") };

        var reply = await Consult(Kimi(played), "why is the count wrong");

        reply.GetRawText().Should().Contain(KimiFixture.RefreshCanary.Split(' ')[0], "what a vendor's process says is relayed as advice — outside the pin — and the detector sees it, so the pin above is not blind");
        Records().Should().Contain(KimiFixture.RefreshCanary.Split(' ')[0]);
    }

    // ---------- S5.4.18 ----------

    private async Task<string> SecondRepositoryAsync(TempDir dir)
    {
        var repo = Directory.CreateDirectory(dir.At("repo")).FullName;
        foreach (var args in new[] { new[] { "init", "-b", "main" }, ["config", "user.email", "t@example.com"], ["config", "user.name", "t"] })
        {
            (await _launcher.RunAsync(new ProcessRequest("git", args, repo), TestContext.Current.CancellationToken)).ExitCode.Should().Be(0);
        }

        await File.WriteAllTextAsync(Path.Combine(repo, "Other.cs"), "int Other() => 1;\n", TestContext.Current.CancellationToken);
        foreach (var args in new[] { new[] { "add", "." }, ["commit", "-m", "base"] })
        {
            (await _launcher.RunAsync(new ProcessRequest("git", args, repo), TestContext.Current.CancellationToken)).ExitCode.Should().Be(0);
        }

        return repo;
    }

    [Fact]
    public async Task TwoConversationsOnOneSignIn_RunOneShimAtATime_ThroughTheService()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        using var other = TempDir.For("testsToDelete-kimi-repo-");
        var repo2 = await SecondRepositoryAsync(other);
        var gate = KimiCredentialsLock.PathFor(person.Credentials);
        var gateHeld = new System.Collections.Concurrent.ConcurrentBag<bool>();
        var played = new PlayedKimi(_launcher)
        {
            During = async (_, ct) =>
            {
                gateHeld.Add(HeldFile.IsHeld(gate));
                await Task.Delay(300, ct);
            },
        };
        var service = Kimi(played);
        var ct = TestContext.Current.CancellationToken;

        var replies = await Task.WhenAll(service.ConsultAsync(_repo, "why is the count wrong", "[]", string.Empty, ct), service.ConsultAsync(repo2, "why is Other one", "[]", string.Empty, ct));

        replies.Select(reply => JsonDocument.Parse(reply).RootElement.TryGetProperty("error", out var error) ? error.ToString() : string.Empty)
            .Should().OnlyContain(error => error.Length == 0, "both answered");
        played.Peak.Should().Be(1, "Expected the peak of live Kimi launches on one sign-in to be 1 (decision k)");
        var windows = played.Shims.OrderBy(launch => launch.StartUtc).ToList();
        windows.Should().HaveCount(2);
        windows[1].StartUtc.Should().BeOnOrAfter(windows[0].EndUtc, "the second's start is not before the first's end");
        gateHeld.Should().HaveCount(2).And.OnlyContain(held => held, "both used ONE gate path, held for the life of each launch");
    }

    // ---------- S5.4.19 ----------

    [Fact]
    public async Task AGateAnotherProcessHolds_DelaysTheShim_UntilItsRelease()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        Directory.CreateDirectory(Path.Combine(person.Folder, "locks"));
        // Another process's gate: a plain non-inheriting take in this host stands in for it.
        var elsewhere = ((Hold.Taken)HeldFile.Take(KimiCredentialsLock.PathFor(person.Credentials))).Stream;
        var released = DateTime.MaxValue;
        var releasing = Task.Run(async () =>
        {
            await Task.Delay(1000, TestContext.Current.CancellationToken);
            // Stamped BEFORE the release: a launch that starts in the instant after the dispose must still read as after it.
            released = DateTime.UtcNow;
            elsewhere.Dispose();
        }, TestContext.Current.CancellationToken);
        var played = new PlayedKimi(_launcher);

        var reply = await Consult(Kimi(played), "why is the count wrong");
        await releasing;

        Answered(reply);
        var started = played.Shims.Single().StartUtc;
        started.Should().BeOnOrAfter(released, "Expected the shim to start after the gate was released, not while the gate was held");
        (started - released).Should().BeLessThan(TimeSpan.FromSeconds(2), "the gate is waited for in one-second slices");
    }

    // ---------- S5.4.20 ----------

    [Fact]
    public async Task TheConsultant_HandsTheService_ItsPlaceFolderAsTheOwnedScratch()
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        var played = new PlayedKimi(_launcher);

        var reply = await Consult(Kimi(played), "why is the count wrong");

        Answered(reply);
        var request = played.Shims.Single().Request;
        var home = request.Arguments[request.Arguments.ToList().IndexOf(AcpWords.HomeFlag) + 1];
        Path.GetDirectoryName(home).Should().Be(FolderOf(IdOf(reply)), "Expected the shim's --home to be a child of coai-kimi\\<id>: the place's folder is the OwnedScratch");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$", "never the folder itself, whose turn the place holds (E5.3 decision a)");
        request.WorkingDirectory.Should().Be(Path.Combine(FolderOf(IdOf(reply)), "tree"), "the conversation's one snapshot (D6)");
    }
}
