using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The authors snapshot on the round (E5.S1 of <c>todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E5.md</c>): nullable, text-free,
/// carrying every text-free member of E4's <see cref="RoundAuthors"/>, surviving the session file, stripped from the <c>open</c> and
/// <c>status</c> answers, and with ONE rule for a stored state that the extension reads from the same vectors file.
/// </summary>
public sealed class RoundAuthorsSnapshotTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("testsToDelete-coai-authors-snapshot-");

    public void Dispose() => _dir.Dispose();

    private static ActivityTotals Totals(int agents) => new(agents, agents * 2, agents * 3, new TokenTerms(agents * 10, agents * 20, agents * 30, agents * 40));

    private static ImmutableArray<AuthorRow> ThreeRows() =>
    [
        RoundAuthorsFixtures.Row("main", association: Association.SameSession, model: "claude-opus-5-5"),
        RoundAuthorsFixtures.Row("a1", association: Association.WroteHere, model: "claude-sonnet-5-5"),
        RoundAuthorsFixtures.Row("a2", window: WindowKind.BeforeOpen, association: Association.ChildOf, model: "claude-sonnet-5-5"),
    ];

    private static ImmutableArray<WindowAuthors> TwoWindows() =>
    [
        new(WindowKind.BeforeOpen, ObservationKinds.Partial, [AttributionReasons.PeerRunning], Totals(1), ActivityTotals.Unknown, Totals(2), 3),
        new(WindowKind.Round, ObservationKinds.Observed, [], ActivityTotals.Zero, Totals(4), ActivityTotals.Zero, 0),
    ];

    private static RoundAuthors AuthorsOfEveryKind() => new(
        "CodeReview", 2, "11111111-1111-1111-1111-111111111111", ObservationKinds.Partial, [AttributionReasons.PeerRunning], ThreeRows(),
        Totals(5), ActivityTotals.Unknown, Totals(6), 7, TwoWindows(), ["peer-gate-1", "peer-gate-2"], [new CheckoutWrites("D:/other-checkout", 4)]);

    private static AuthorsSnapshot Observed() => AuthorsSnapshot.From(AuthorsOfEveryKind() with { State = ObservationKinds.Observed, Reasons = [] }, CallerSessionId.Registry, 1, 1, 1, 2);

    private static RoundRecord Round(AuthorsSnapshot? authors, string stage = "CodeReview", int number = 1) =>
        new(stage, number, "proceed", 0, "answered", new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc))
        {
            StartedUtc = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            Authors = authors,
        };

    private static SessionState State => new("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };

    private static PersistedSession Session(params RoundRecord[] rounds) =>
        new(State, [.. rounds]) { OpenedUtc = new DateTime(2026, 10, 1, 7, 0, 0, DateTimeKind.Utc) };

    private static string Wire(PersistedSession session) => JsonSerializer.Serialize(session, ServerJsonContext.Default.PersistedSession);

    private static PersistedSession Read(string json) => JsonSerializer.Deserialize(json, ServerJsonContext.Default.PersistedSession)!;

    [Fact]
    public void ASnapshotSurvivesTheSessionFile_FieldForField_AndAnUnknownStaysMinusOne()
    {
        var snapshot = AuthorsSnapshot.From(AuthorsOfEveryKind(), CallerSessionId.Environment, 1, 1, 3, 5);

        var loaded = Read(Wire(Session(Round(snapshot)))).Rounds.Single().Authors!;

        loaded.Should().BeEquivalentTo(snapshot, options => options.ComparingRecordsByMembers());
        loaded.Elsewhere.Should().Be(ActivityTotals.Unknown, "-1 is a count nothing measured, and it stays -1 through the file");
        loaded.Elsewhere.Tokens.CacheRead.Should().Be(-1);
        loaded.Windows.Should().HaveCount(2);
        loaded.Windows[0].Reasons.Should().Equal(AttributionReasons.PeerRunning);
        loaded.PeerGates.Should().Equal("peer-gate-1", "peer-gate-2");
        loaded.Rows.Should().HaveCount(3);
        loaded.Rows[0].Agent.Should().Be(AgentKey.Main, "a row's agent keeps E4's `main`; the column's empty string is S2's one mapping");
        loaded.IdSource.Should().Be(CallerSessionId.Environment);
        (loaded.SubmoduleWrites, loaded.UncoveredWrites).Should().Be((3, 5));
    }

    [Fact]
    public void TheSnapshotCarriesEveryTextFreeMemberOfRoundAuthors()
    {
        var snapshotMembers = MemberNames(typeof(AuthorsSnapshot));
        var authorsMembers = MemberNames(typeof(RoundAuthors));
        var expected = authorsMembers.Where(name => name != nameof(RoundAuthors.WritesOutsideTheGateCheckout))
            .Select(name => name == nameof(RoundAuthors.CallerSessionId) ? nameof(AuthorsSnapshot.Session) : name)
            .Concat([nameof(AuthorsSnapshot.IdSource), nameof(AuthorsSnapshot.Reader), nameof(AuthorsSnapshot.Attribution), nameof(AuthorsSnapshot.SubmoduleWrites), nameof(AuthorsSnapshot.UncoveredWrites)]);

        snapshotMembers.Should().BeEquivalentTo(expected, "the snapshot is E4's record minus the roots, plus E5's five — a member E4 adds fails here, naming it");
    }

    [Fact]
    public void FromCopiesEveryMemberOfRoundAuthors()
    {
        var authors = AuthorsOfEveryKind();

        var snapshot = AuthorsSnapshot.From(authors, CallerSessionId.Registry, 1, 1, 0, 0);

        foreach (var member in typeof(RoundAuthors).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != nameof(RoundAuthors.WritesOutsideTheGateCheckout)))
        {
            var name = member.Name == nameof(RoundAuthors.CallerSessionId) ? nameof(AuthorsSnapshot.Session) : member.Name;
            var copied = typeof(AuthorsSnapshot).GetProperty(name)!.GetValue(snapshot);

            copied.Should().BeEquivalentTo(member.GetValue(authors), options => options.ComparingRecordsByMembers(), $"`{name}` is copied from E4's result, not rebuilt or dropped");
        }
    }

    private static IEnumerable<string> MemberNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name);

    [Fact]
    public void AnUnknownSnapshot_HasNoMeasuredZero()
    {
        var unknown = AuthorsSnapshot.Unknown("CodeReview", 1, "", ObservationKinds.Unobserved, [ObservationReasons.TranscriptNotFound], CallerSessionId.Registry, 0, 0);

        unknown.UnobservedLaunches.Should().Be(-1);
        unknown.SubmoduleWrites.Should().Be(-1);
        unknown.UncoveredWrites.Should().Be(-1, "an uncovered count of 0 would say 'covered'");
        unknown.Ambiguous.Should().Be(ActivityTotals.Unknown);
        unknown.Elsewhere.Should().Be(ActivityTotals.Unknown);
        unknown.ClaimedByPeer.Should().Be(ActivityTotals.Unknown);
        unknown.Rows.Should().BeEmpty();
        unknown.Windows.Should().BeEmpty();
        unknown.PeerGates.Should().BeEmpty();
        unknown.Render().Should().Be("unobserved: transcript not found");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(ObservedSession.ReaderVersion, 0)]
    [InlineData(ObservedSession.ReaderVersion, RoundAttribution.Version)]
    public void AnUnknownSnapshot_KeepsTheTwoVersionsItIsGiven_SoItNamesWhichComponentRan(int reader, int attribution)
    {
        var unknown = AuthorsSnapshot.Unknown("PlanReview", 1, "", ObservationKinds.Unobserved, [AuthorsSnapshot.InvalidInput], CallerSessionId.Environment, reader, attribution);

        (unknown.Reader, unknown.Attribution).Should().Be((reader, attribution), "0 means that component did not run; E7 never keeps such a row as current");
    }

    [Fact]
    public void ASessionFileWithoutAuthors_LoadsNull_AndSavingItAgainWritesNoKey()
    {
        var store = new SessionStore(_dir.Path);
        var before = new PersistedSession(State with { RepoPath = _dir.Path }, [Round(null)]) { OpenedUtc = DateTime.UtcNow };
        store.Save(before);
        var file = Directory.GetFiles(_dir.At("sessions"), "session-*.json").Single();
        File.ReadAllText(file).Should().NotContain("\"authors\"", "today's binary writes no key");

        var loaded = store.Load(_dir.Path, State.Branch)!;
        loaded.Rounds.Single().Authors.Should().BeNull("an older file reads 'not recorded', never a default snapshot");
        store.Save(loaded);

        File.ReadAllText(file).Should().NotContain("\"authors\"", "a load and a save must not materialise a snapshot — the progress-tick save rewrites history otherwise");
    }

    [Theory]
    [InlineData("observed", new string[0])]
    [InlineData("partial: read budget", new[] { "read budget" })]
    [InlineData("partial: read budget, peer running", new[] { "read budget", "peer running" })]
    [InlineData("unobserved: no reader for codex", new[] { "no reader for codex" })]
    public void RenderJoinsTheReasonsWithTheJoinerTheStatesRuleReads(string expected, string[] reasons)
    {
        var state = expected.Split(':')[0];
        var snapshot = AuthorsSnapshot.Unknown("CodeReview", 1, "", state, [.. reasons], CallerSessionId.Registry, 0, 0);

        snapshot.Render().Should().Be(expected);
        AuthorsSnapshot.IsWellFormed(snapshot.Render()).Should().BeTrue("a state this record renders is a state its own rule accepts");
    }

    [Fact]
    public void EveryVectorOfTheSharedFile_IsJudgedAsTheFileSays()
    {
        var vectors = AuthorsStateVectors().ToList();

        vectors.Should().Contain(vector => vector.WellFormed).And.Contain(vector => !vector.WellFormed, "a vectors file with only one kind proves nothing");
        foreach (var (value, wellFormed) in vectors)
        {
            AuthorsSnapshot.IsWellFormed(value).Should().Be(wellFormed, $"`{value}` — shared/authors-state-vectors.json says {wellFormed}");
        }
    }

    [Fact]
    public void EveryReasonConstantOfE3E4AndE5_RenderedAlone_IsAccepted()
    {
        var reasons = ReasonConstants().ToList();

        reasons.Should().Contain(AttributionReasons.PeerRunning).And.Contain(ObservationReasons.ReadBudget).And.Contain(AuthorsSnapshot.InvalidInput).And.Contain(AuthorsSnapshot.ReaderFailed);
        foreach (var reason in reasons)
        {
            AuthorsSnapshot.IsWellFormed("partial: " + reason).Should().BeTrue($"`{reason}` is a reason the engine writes, so the rule cannot refuse it");
        }
    }

    [Fact]
    public void E5sReasonsContainNoJoiner()
    {
        var own = typeof(AuthorsSnapshot).GetFields().Where(field => field.IsLiteral && field.FieldType == typeof(string)).Select(field => (string)field.GetRawConstantValue()!).ToList();

        own.Should().NotBeEmpty().And.OnlyContain(reason => reason.Length > 0 && !reason.Contains(", ", StringComparison.Ordinal),
            "E6's split on the joiner is exact only while no reason contains it; E3's and E4's are covered by E4's NoReasonContainsTheJoiner");
    }

    [Fact]
    public void TheSnapshotsJson_HasOnlyTheMembersOfItsRecords_NoComputedOne()
    {
        var json = Wire(Session(Round(AuthorsSnapshot.From(AuthorsOfEveryKind(), CallerSessionId.Registry, 1, 1, 0, 0))));
        using var document = JsonDocument.Parse(json);
        var authors = document.RootElement.GetProperty("rounds")[0].GetProperty("authors");

        KeysOf(authors, typeof(AuthorsSnapshot)).Should().BeEmpty("every JSON member is a constructor parameter of its record; a computed property (isMain, isUnknown, rendered) is a stray key in the file");
        authors.GetProperty("rows")[0].GetProperty("agent").EnumerateObject().Select(member => member.Name).Should().Equal("id");
    }

    [Fact]
    public async Task TheStatusAnswer_HasNoAuthors_AndTheSessionFileStillHasThem()
    {
        var repo = Directory.CreateDirectory(_dir.At("repo")).FullName;
        var store = new SessionStore(_dir.At("data"));
        store.Save(new PersistedSession(State with { RepoPath = repo }, [Round(Observed())]) { OpenedUtc = DateTime.UtcNow });
        var service = new PanelService(
            new PanelSettings { DataDir = _dir.At("data"), Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human) },
            VaultKeys.None("no vault in tests"), default, new CoaiMcp.Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None, Noticing.None);

        var answer = await service.StatusAsync(repo, State.Branch);

        using var document = JsonDocument.Parse(answer);
        var rounds = document.RootElement.GetProperty("rounds");
        rounds.GetArrayLength().Should().Be(1, "the round is in the answer — only its authors are not");
        rounds[0].TryGetProperty("authors", out _).Should().BeFalse("the rows would cost the calling AI up to several KB of context per round");
        File.ReadAllText(Directory.GetFiles(_dir.At("data", "sessions"), "session-*.json").Single()).Should().Contain("\"authors\"", "the file and the page keep it");
    }

    private static IEnumerable<string> ReasonConstants() =>
        new[] { typeof(AttributionReasons), typeof(ObservationReasons), typeof(AuthorsSnapshot) }
            .SelectMany(type => type.GetFields().Where(field => field.IsLiteral && field.FieldType == typeof(string)))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Concat(new[] { "claude", "codex", "gemini", "kimi", "vibe", "agy" }.Select(ObservationReasons.NoReaderFor))
            .Concat([ObservationReasons.NoReaderFor("this vendor")]);

    private static IEnumerable<(string Value, bool WellFormed)> AuthorsStateVectors()
    {
        using var file = File.OpenRead(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "authors-state-vectors.json")));
        using var parsed = JsonDocument.Parse(file);

        return [.. parsed.RootElement.GetProperty("states").EnumerateArray().Select(row => (row.GetProperty("value").GetString()!, row.GetProperty("wellFormed").GetBoolean()))];
    }

    /// <summary>The JSON members of <paramref name="element"/> that are not constructor parameters of <paramref name="type"/>, depth first, named by path.</summary>
    private static IEnumerable<string> KeysOf(JsonElement element, Type type, string path = "authors")
    {
        if (element.ValueKind == JsonValueKind.Array && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>))
        {
            return element.EnumerateArray().SelectMany((item, at) => KeysOf(item, type.GetGenericArguments()[0], $"{path}[{at}]"));
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var parameters = type.GetConstructors().OrderByDescending(constructor => constructor.GetParameters().Length).First().GetParameters()
            .ToDictionary(parameter => JsonNamingPolicy.CamelCase.ConvertName(parameter.Name!), parameter => parameter.ParameterType, StringComparer.Ordinal);

        return element.EnumerateObject().SelectMany(member => parameters.TryGetValue(member.Name, out var memberType)
            ? KeysOf(member.Value, memberType, $"{path}.{member.Name}")
            : [$"{path}.{member.Name}"]);
    }
}
