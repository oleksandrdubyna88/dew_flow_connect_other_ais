using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A session file keeps the members a NEWER server wrote into it, when an OLDER server loads it and saves it again
/// (<c>research/PLAN_a_session_file_keeps_what_a_newer_server_wrote.md</c>).
/// </summary>
/// <remarks>
/// <para>Several <c>coai-mcp</c> processes share one data folder, and a window that was not reloaded after an update runs the
/// older binary. That binary read the file with a context that skips what it does not know, and wrote back only what it
/// knew — so a member a newer release added was gone after one save. The fixtures here plant members no type declares
/// (<c>futureRoot</c>, <c>futureRound</c>, …): they stand for what a newer release will add, and the test is that they come back
/// out of <see cref="SessionStore.Save"/> with the same value.</para>
/// <para>Every fixture carries <c>"rejections": []</c> because it is built by serialising a real session — a hand-made file
/// without the member loads with a default <c>ImmutableArray</c> and the next save throws (plan §3, side finding).</para>
/// </remarks>
public sealed class ASessionFileKeepsWhatANewerServerWroteTests : IDisposable
{
    private const string Repo = "D:/fixture-bag";
    private const string Branch = "main";

    private readonly TempDir _data = TempDir.For("session-bag-");

    /// <summary>The placeholder for the first role of <c>state.config.roles</c>, whatever it is called.</summary>
    private const string FirstRole = "{role}";

    /// <summary>One unknown member: where it sits (a path of object names; an index is written <c>name[0]</c>), its name, its JSON.</summary>
    private sealed record Planting(string[] Where, string Name, string Json)
    {
        public string Label => $"{(Where.Length == 0 ? "root" : string.Join('.', Where))} unknown member {Name}";
    }

    /// <summary>One at the root, in the state, its config, the security lane, one role gate, a round and a reviewer row.</summary>
    private static readonly Planting[] Planted =
    [
        new Planting([], "futureRoot", """{"a":1,"b":[1,2,{"c":true}]}"""),
        new Planting(["state"], "futureState", "\"text\""),
        new Planting(["state", "config"], "futureConfig", "42.5"),
        new Planting(["state", "config", "securityLane"], "futureLane", """["x",null]"""),
        new Planting(["state", "config", "roles", FirstRole], "futureGate", """{"n":null,"m":[]}"""),
        new Planting(["rounds[0]"], "futureRound", """{"claude":{"lines":12,"files":[1,2,3]},"codex":{"lines":0,"files":[]}}"""),
        new Planting(["rounds[0]", "reviewerStates[0]"], "futureReviewer", "\"r\""),
    ];

    private readonly List<TempDir> _extra = [];

    public void Dispose()
    {
        _data.Dispose();
        _extra.ForEach(d => d.Dispose());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PersistedSession Baseline(string repo)
    {
        var state = new SessionState("bag-1", repo, Branch, new PanelConfig(OnExhausted: StagePolicy.Human)) { Stage = Stage.CodeReview };
        var reviewer = new ReviewerState("codex", "correctness", ReviewerState.Done, 2);
        var round = new RoundRecord("CodeReview", 1, "proceed", 0, "codex", new DateTime(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc))
        {
            ReviewerStates = [reviewer],
            Sha = "abc123",
        };

        return new PersistedSession(state, [round])
        {
            OpenedUtc = new DateTime(2026, 10, 18, 8, 0, 0, DateTimeKind.Utc),
            PlanText = "THE SCOPE",
        };
    }

    /// <summary>A file as an OLDER server finds it: the baseline session with every <see cref="Planted"/> member added.</summary>
    private static string Plant(SessionStore store, IEnumerable<Planting> unknowns, string repo = Repo)
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(Baseline(repo), ServerJsonContext.Default.PersistedSession))!.AsObject();
        foreach (var unknown in unknowns)
        {
            Locate(json, unknown.Where)[unknown.Name] = JsonNode.Parse(unknown.Json);
        }

        var file = store.FileFor(repo, Branch);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, json.ToJsonString());
        return file;
    }

    private static JsonObject Locate(JsonObject root, string[] where)
    {
        JsonNode node = root;
        foreach (var step in where)
        {
            node = Step(node, step);
        }

        return node.AsObject();
    }

    private static JsonNode Step(JsonNode node, string step)
    {
        if (step == FirstRole)
        {
            return node.AsObject().First().Value!;
        }

        var open = step.IndexOf('[');
        return open < 0
            ? node[step]!
            : node[step[..open]]![int.Parse(step[(open + 1)..step.IndexOf(']')])]!;
    }

    private static void AssertEveryUnknownIsThere(string file, IEnumerable<Planting> unknowns)
    {
        var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        // One scope: a loss at every level is reported together, not only the first one met.
        using var scope = new FluentAssertions.Execution.AssertionScope();
        foreach (var unknown in unknowns)
        {
            var parent = Locate(json, unknown.Where);
            parent.TryGetPropertyValue(unknown.Name, out var kept).Should().BeTrue(unknown.Label);
            JsonNode.DeepEquals(kept, JsonNode.Parse(unknown.Json)).Should().BeTrue(
                $"{unknown.Label} must come back with the value it had, not {kept?.ToJsonString()}");
        }
    }

    [Fact]
    public void AnOlderServersReSave_KeepsEveryMemberItDoesNotKnow()
    {
        var store = new SessionStore(_data);
        var file = Plant(store, Planted);

        var loaded = store.Load(Repo, Branch)!;
        store.Save(loaded);

        AssertEveryUnknownIsThere(file, Planted);
    }

    [Fact]
    public void AWithCopy_KeepsTheUnknownMembers()
    {
        var store = new SessionStore(_data);
        var file = Plant(store, Planted);

        var loaded = store.Load(Repo, Branch)!;
        var copy = loaded with
        {
            OpenedUtc = loaded.OpenedUtc.AddMinutes(1),
            State = loaded.State with { RoundsRunThisStage = 3 },
            Rounds =
            [
                .. loaded.Rounds.Select(r => r with
                {
                    Note = "copied",
                    ReviewerStates = [.. r.ReviewerStates.Select(v => v with { Note = "copied" })],
                }),
            ],
        };
        store.Save(copy);

        AssertEveryUnknownIsThere(file, Planted);
        JsonNode.Parse(File.ReadAllText(file))!["state"]!["roundsRunThisStage"]!.GetValue<int>()
            .Should().Be(3, "the copy's own change must land too, or the test saved nothing");
    }

    /// <summary>Opens a planted file, lets the test change it, writes it back — for the shapes a plain planting cannot say.</summary>
    private static void Edit(string file, Action<JsonObject> change)
    {
        var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        change(json);
        File.WriteAllText(file, json.ToJsonString());
    }

    /// <summary>How many times an object spells one member name, in any case — what a re-save must keep at ONE.</summary>
    private static int Spelled(string file, string[] where, string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        var element = document.RootElement;
        foreach (var step in where)
        {
            element = StepIn(element, step);
        }

        return element.EnumerateObject().Count(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static JsonElement StepIn(JsonElement element, string step)
    {
        var open = step.IndexOf('[');
        return open < 0
            ? element.GetProperty(step)
            : element.GetProperty(step[..open])[int.Parse(step[(open + 1)..step.IndexOf(']')])];
    }

    /// <summary>The names an object was written with must be exactly those of its type's JSON contract that held a value.</summary>
    private static void AssertWrittenAsTheContract<T>(JsonNode? written, T value, JsonTypeInfo<T> info, string level)
    {
        var expected = info.Properties.Where(p => !p.IsExtensionData && p.Get?.Invoke(value!) is not null).Select(p => p.Name);
        written!.AsObject().Select(p => p.Key).Should().BeEquivalentTo(expected, level);
    }

    [Fact]
    public void AFileWithNoUnknownMember_HasNoBag_AndWritesWhatItWroteBefore()
    {
        var store = new SessionStore(_data);
        var file = Plant(store, []);

        var loaded = store.Load(Repo, Branch)!;
        var gate = loaded.State.Config.Roles.First();
        loaded.Unknown.Should().BeNull();
        loaded.State.Unknown.Should().BeNull();
        loaded.State.Config.Unknown.Should().BeNull();
        loaded.State.Config.SecurityLane.Unknown.Should().BeNull();
        gate.Value.Unknown.Should().BeNull();
        loaded.Rounds[0].Unknown.Should().BeNull();
        loaded.Rounds[0].ReviewerStates[0].Unknown.Should().BeNull();

        store.Save(loaded);

        var written = JsonNode.Parse(File.ReadAllText(file))!;
        AssertWrittenAsTheContract(written, loaded, ServerJsonContext.Default.PersistedSession, "root");
        AssertWrittenAsTheContract(written["state"], loaded.State, ServerJsonContext.Default.SessionState, "state");
        AssertWrittenAsTheContract(written["state"]!["config"], loaded.State.Config, ServerJsonContext.Default.PanelConfig, "config");
        AssertWrittenAsTheContract(
            written["state"]!["config"]!["roles"]![gate.Key], gate.Value, ServerJsonContext.Default.RoleGate, "a role gate");
        AssertWrittenAsTheContract(written["rounds"]![0], loaded.Rounds[0], ServerJsonContext.Default.RoundRecord, "a round");
        AssertWrittenAsTheContract(
            written["rounds"]![0]!["reviewerStates"]![0], loaded.Rounds[0].ReviewerStates[0], ServerJsonContext.Default.ReviewerState, "a reviewer row");
    }

    [Fact]
    public void AComputedMember_IsWrittenOnceWithItsComputedValue()
    {
        // Members with no setter are written for the readers' sake and never read back. A stale one in the file must not
        // fall into a bag, or the save would write it twice — and the stale copy would win the next read.
        var store = new SessionStore(_data);
        var file = Plant(store, []);
        Edit(file, json =>
        {
            json["rounds"]![0]!["loggedNote"] = "STALE";
            json["state"]!["isDocumentSession"] = true;
            json["state"]!["isFeatureSession"] = true;
        });

        var loaded = store.Load(Repo, Branch)!;
        store.Save(loaded);

        Spelled(file, ["rounds[0]"], "loggedNote").Should().Be(1);
        Spelled(file, ["state"], "isDocumentSession").Should().Be(1);
        Spelled(file, ["state"], "isFeatureSession").Should().Be(1);
        var written = JsonNode.Parse(File.ReadAllText(file))!;
        written["rounds"]![0]!["loggedNote"]!.GetValue<string>().Should().BeEmpty("a round with no note and one repeat has no logged note");
        written["state"]!["isDocumentSession"]!.GetValue<bool>().Should().BeFalse();
        written["state"]!["isFeatureSession"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void ANewlyKnownMember_BindsAndIsWrittenOnce()
    {
        // The case "a later version ADDS the member an older file holds as unknown": the same name in another case is a
        // KNOWN member to this build, so it binds and is never put aside.
        var store = new SessionStore(_data);
        var file = Plant(store, []);
        Edit(file, json =>
        {
            json.Remove("planText");
            json["PLANTEXT"] = "UPPER";
        });

        var loaded = store.Load(Repo, Branch)!;
        loaded.PlanText.Should().Be("UPPER");
        loaded.Unknown.Should().BeNull("the name is known in any case");
        store.Save(loaded);

        Spelled(file, [], "planText").Should().Be(1);
        JsonNode.Parse(File.ReadAllText(file))!["planText"]!.GetValue<string>().Should().Be("UPPER");
    }

    private static JsonElement Value(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>A bag with what it held and one more entry — a copy, never the dictionary it was given.</summary>
    private static Dictionary<string, JsonElement> WithEntry(Dictionary<string, JsonElement>? bag, string name, string json) =>
        new(bag ?? []) { [name] = Value(json) };

    /// <summary>
    /// The session as CODE can make it and deserialisation never does: a stale copy of a KNOWN member put into the bag of every
    /// level — session <c>planText</c>, state <c>stage</c>, config <c>onExhausted</c>, a gate's <c>threshold</c>, a round's
    /// <c>verdict</c> and a reviewer row's <c>status</c>.
    /// </summary>
    private static PersistedSession WithForgedKnownNames(PersistedSession loaded)
    {
        var (role, gate) = loaded.State.Config.Roles.First();
        var roles = loaded.State.Config.Roles.ToDictionary(p => p.Key, p => p.Value);
        roles[role] = gate with { Unknown = WithEntry(gate.Unknown, "threshold", "99") };
        var config = loaded.State.Config with
        {
            Roles = roles,
            Unknown = WithEntry(loaded.State.Config.Unknown, "onExhausted", "\"Continue\""),
        };
        var round = loaded.Rounds[0] with
        {
            Unknown = WithEntry(loaded.Rounds[0].Unknown, "verdict", "\"OLD\""),
            ReviewerStates =
            [
                loaded.Rounds[0].ReviewerStates[0] with
                {
                    Unknown = WithEntry(loaded.Rounds[0].ReviewerStates[0].Unknown, "status", "\"OLD\""),
                },
            ],
        };

        return loaded with
        {
            Unknown = WithEntry(loaded.Unknown, "planText", "\"OLD\""),
            State = loaded.State with { Unknown = WithEntry(loaded.State.Unknown, "stage", "\"PlanReview\""), Config = config },
            Rounds = [round],
        };
    }

    /// <summary>Each forged name with where it sits, and the TYPED value that must win.</summary>
    private static readonly (string[] Where, string Name, string Typed)[] Forged =
    [
        ([], "planText", "THE SCOPE"),
        (["state"], "stage", "CodeReview"),
        (["state", "config"], "onExhausted", "Human"),
        (["rounds[0]"], "verdict", "proceed"),
        (["rounds[0]", "reviewerStates[0]"], "status", "done"),
    ];

    [Fact]
    public void AKnownNameInABag_IsDroppedOnSave_AndTheTypedValueWins()
    {
        var store = new SessionStore(_data);
        var file = Plant(store, Planted);

        store.Save(WithForgedKnownNames(store.Load(Repo, Branch)!));

        foreach (var (where, name, typed) in Forged)
        {
            Spelled(file, where, name).Should().Be(1, $"{string.Join('.', where)} {name} must be written once, by the typed member");
            Locate(JsonNode.Parse(File.ReadAllText(file))!.AsObject(), where)[name]!.GetValue<string>()
                .Should().Be(typed, $"the typed {name} must win over the bag's stale copy");
        }

        var roles = JsonNode.Parse(File.ReadAllText(file))!["state"]!["config"]!["roles"]!.AsObject();
        Spelled(file, ["state", "config", "roles", roles.First().Key], "threshold").Should().Be(1, "a gate's threshold is written once");
        roles.First().Value!["threshold"]!.GetValue<int>().Should().NotBe(99, "the typed threshold must win");
        AssertEveryUnknownIsThere(file, Planted);

        var reloaded = store.Load(Repo, Branch)!;
        reloaded.PlanText.Should().Be("THE SCOPE");
        reloaded.Rounds[0].Verdict.Should().Be("proceed");
        reloaded.Rounds[0].ReviewerStates[0].Status.Should().Be(ReviewerState.Done);
    }

    [Fact]
    public void ARetiredMember_IsDroppedOnSave()
    {
        var retired = new RetiredMembers([(typeof(RoundRecord), "futureRound"), (typeof(PersistedSession), "FUTUREROOT")]);
        var store = new SessionStore(_data) { Retired = retired };
        var file = Plant(store, Planted);

        store.Save(store.Load(Repo, Branch)!);

        var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        json.ContainsKey("futureRoot").Should().BeFalse("a retired name is dropped, whatever its case");
        json["rounds"]![0]!.AsObject().ContainsKey("futureRound").Should().BeFalse("futureRound was registered as retired");
        AssertEveryUnknownIsThere(file, Planted.Where(p => p.Name is not ("futureRound" or "futureRoot")));
    }

    [Fact]
    public void TheScrubLeavesTheSnapshotItWasGivenUnchanged()
    {
        var store = new SessionStore(_data);
        Plant(store, Planted);
        var forged = WithForgedKnownNames(store.Load(Repo, Branch)!);
        var before = JsonSerializer.Serialize(forged, ServerJsonContext.Default.PersistedSession);

        store.Save(forged);

        forged.Unknown.Should().ContainKey("planText", "the caller's bag is shared by every with-copy: it is not scrubbed in place");
        forged.State.Unknown.Should().ContainKey("stage");
        forged.State.Config.Unknown.Should().ContainKey("onExhausted");
        forged.State.Config.Roles.First().Value.Unknown.Should().ContainKey("threshold");
        forged.Rounds[0].Unknown.Should().ContainKey("verdict");
        forged.Rounds[0].ReviewerStates[0].Unknown.Should().ContainKey("status");
        JsonSerializer.Serialize(forged, ServerJsonContext.Default.PersistedSession).Should().Be(before);
    }

    /// <summary>
    /// The JSON member names of each type whose file members are kept — COMMITTED here, so that a release which removes or renames
    /// one is stopped until it tells the scrub (<see cref="RetiredMembers"/>).
    /// </summary>
    private static readonly (JsonTypeInfo Info, string[] Members)[] Committed =
    [
        (ServerJsonContext.Default.PersistedSession,
            ["state", "rounds", "openedUtc", "pending", "planText", "plan", "epic", "cadenceRepoId", "featureBase", "carried", "caller", "usedPrompts"]),
        (ServerJsonContext.Default.SessionState,
            ["sessionId", "repoPath", "branch", "config", "stage", "roundsRunThisStage", "escalationsUsed", "awaitingResolve", "planProceeded", "advanceOnResolve", "humanGate", "holdQuestions", "requestQuestions", "rejections", "secondRound", "document", "isDocumentSession", "feature", "isFeatureSession"]),
        (ServerJsonContext.Default.PanelConfig,
            ["onExhausted", "autonomous", "isAutonomous", "predatesAuto", "catalog", "securityLane", "roles"]),
        (ServerJsonContext.Default.RoleGate,
            ["maxRounds", "threshold", "enabled"]),
        (ServerJsonContext.Default.RoundRecord,
            ["stage", "number", "verdict", "gatingCount", "reviewers", "completedUtc", "status", "startedUtc", "runnerPid", "sha", "planKey", "epicNumber", "cadenceNote", "note", "key", "admittedBy", "repeats", "loggedNote", "reviewerStates", "caller", "size", "gitlinks", "planSource", "subject", "tokensIn", "tokensOut", "costUsd", "costNote", "usageNote", "authors"]),
        (ServerJsonContext.Default.ReviewerState,
            ["provider", "role", "status", "findings", "note", "seconds", "model", "effort", "warning"]),
    ];

    [Fact]
    public void EveryBaggedTypesMembers_AreTheListedOnesOrRetired()
    {
        // A rename matters most: the old name in old files becomes a bag entry, and the new property starts empty.
        var problems = Committed.SelectMany(c => MemberProblems(c.Info, c.Members)).ToList();

        string.Join("; ", problems).Should().BeEmpty("a member that went away is named in RetiredMembers, a member that came is added to the list");
    }

    [Fact]
    public void TheMemberListCheckStillSeesTheTypes()
    {
        // The check compares two lists, and a list that is empty on both sides would pass for ever.
        Committed.Should().OnlyContain(c => c.Members.Length > 0 && c.Info.Properties.Count > 0);
        Committed.Select(c => c.Info.Type).Should().BeEquivalentTo(
            [typeof(PersistedSession), typeof(SessionState), typeof(PanelConfig), typeof(RoleGate), typeof(RoundRecord), typeof(ReviewerState)]);
        Committed.Select(c => c.Info.Properties.Count(p => p.IsExtensionData)).Should().OnlyContain(
            n => n == 1, "each type lists its bag among its properties, which is why the list check filters it out");
    }

    private static IEnumerable<string> MemberProblems(JsonTypeInfo info, string[] committed)
    {
        var actual = info.Properties.Where(p => !p.IsExtensionData).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retired = RetiredMembers.Shipped.Of(info.Type);
        var listed = committed.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. listed.Except(actual).Except(retired).Select(n => $"{info.Type.Name}.{n} is gone and is not in RetiredMembers"),
            .. actual.Except(listed).Select(n => $"{info.Type.Name}.{n} is new and is not in the committed list"),
            .. retired.Intersect(actual).Select(n => $"{info.Type.Name}.{n} is retired but still a member"),
        ];
    }

    [Fact]
    public void ACatalogAnOlderBuildWroteIntoTheConfig_IsNotCarriedForEver()
    {
        // PanelConfig.Catalog is [JsonIgnore]d since the catalog stopped being persisted, and earlier files still hold one.
        // It is a member of the contract that is never written, so it is not "something a newer server wrote".
        var store = new SessionStore(_data);
        var file = Plant(store, [new Planting(["state", "config"], "catalog", """{"roles":["old"]}""")]);

        store.Save(store.Load(Repo, Branch)!);

        JsonNode.Parse(File.ReadAllText(file))!["state"]!["config"]!.AsObject().ContainsKey("catalog").Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "Human")]
    [InlineData(false, "Human")]
    [InlineData(false, "GoodEnough")]
    [InlineData(true, "GoodEnough")]
    public void AutonomousAbsentFalseAndTrue_ComeBackFromALoadAndASave_AsTheyWere(bool? autonomous, string onExhausted)
    {
        // The member policy auto adds to a config. A build that has the bag but does not know the member must keep it as it
        // found it — absent stays absent, a value stays that value — because "absent" is what marks a hold raised before
        // policy auto existed (research/PLAN_auto_never_asks_a_person_E1_S1_spec.md, D2 and residual A8). Written against the tree before the member
        // exists, on purpose: that tree IS such a build, and after the member lands this same test pins this build's own load and save.
        var store = new SessionStore(_data);
        var file = Plant(store, []);
        Edit(file, json =>
        {
            var config = json["state"]!["config"]!.AsObject();
            config["onExhausted"] = onExhausted;
            if (autonomous is { } value)
            {
                config["autonomous"] = value;
            }
            else
            {
                config.Remove("autonomous");
            }
        });

        store.Save(store.Load(Repo, Branch)!);
        store.Save(store.Load(Repo, Branch)!);

        var written = JsonNode.Parse(File.ReadAllText(file))!;
        var kept = written["state"]!["config"]!.AsObject();
        Spelled(file, ["state", "config"], "autonomous").Should().Be(
            autonomous is null ? 0 : 1, "an absent marker stays absent and a written one is written once");
        if (autonomous is { } expected)
        {
            kept["autonomous"]!.GetValue<bool>().Should().Be(expected, "the value comes back as it was");
        }

        kept["onExhausted"]!.GetValue<string>().Should().Be(onExhausted);
        written["state"]!["sessionId"]!.GetValue<string>().Should().Be("bag-1", "the same session, not a new one");
        written["rounds"]!.AsArray().Should().ContainSingle("the one round is kept");
    }

    /// <summary>The state's config object of a planted file, to edit.</summary>
    private static JsonObject ConfigOf(JsonObject json) => json["state"]!["config"]!.AsObject();

    [Theory]
    [InlineData("Human", false)]
    [InlineData("GoodEnough", false)]
    [InlineData("Human", true)]
    public void AFileWithNoAutonomous_ReadsAsNull(string onExhausted, bool writtenNull)
    {
        var store = new SessionStore(_data);
        var file = Plant(store, []);
        Edit(file, json =>
        {
            var config = ConfigOf(json);
            config["onExhausted"] = onExhausted;
            config.Remove("autonomous");
            if (writtenNull)
            {
                config["autonomous"] = null;
            }
        });

        var config = store.Load(Repo, Branch)!.State.Config;

        config.Autonomous.Should().BeNull("a file with no marker predates policy auto");
        config.PredatesAuto.Should().BeTrue();
        config.IsAutonomous.Should().BeFalse();
        config.Unknown.Should().BeNull();
        config.OnExhausted.ToString().Should().Be(onExhausted);
    }

    [Fact]
    public void TheNaivePositionalDefault_WouldReadAbsenceAsAuto_AndThatIsThePoint()
    {
        // The refuted shape (spec M1): a constructor parameter with a declared default reads a file with no member as the default.
        // Policy auto's marker would then be true for every file written before it existed — the opposite of its meaning.
        var naive = JsonSerializer.Deserialize("""{"onExhausted":"Human"}""", NaiveContext.Default.NaiveConfig)!;

        naive.Autonomous.Should().BeTrue("the declared default fills an absent member, which is why the file constructor has none");
        naive.OnExhausted.Should().Be(StagePolicy.Human);
    }

    [Theory]
    [InlineData("autonomous", false)]
    [InlineData("autonomous", true)]
    [InlineData("AUTONOMOUS", false)]
    public void AStoredMarker_BindsToTheMember_AndNeverToTheBag(string spelled, bool value)
    {
        var store = new SessionStore(_data);
        var file = Plant(store, []);
        Edit(file, json =>
        {
            var config = ConfigOf(json);
            config.Remove("autonomous");
            config[spelled] = value;
        });

        var loaded = store.Load(Repo, Branch)!;

        loaded.State.Config.Autonomous.Should().Be(value);
        loaded.State.Config.Unknown.Should().BeNull("a member this build knows is never put aside, whatever its case");
        store.Save(loaded);
        Spelled(file, ["state", "config"], "autonomous").Should().Be(1);
    }

    [Fact]
    public void AComputedName_IsNotTheMarker()
    {
        var store = new SessionStore(_data);
        var file = Plant(store, []);
        Edit(file, json =>
        {
            var config = ConfigOf(json);
            config.Remove("autonomous");
            config["isAutonomous"] = true;
            config["predatesAuto"] = false;
        });

        var loaded = store.Load(Repo, Branch)!;

        loaded.State.Config.Autonomous.Should().BeNull("a computed name is not the marker");
        loaded.State.Config.Unknown.Should().BeNull("a computed name is a member of the contract: it is ignored, not kept");
        store.Save(loaded);
        Spelled(file, ["state", "config"], "isAutonomous").Should().Be(0);
        Spelled(file, ["state", "config"], "predatesAuto").Should().Be(0);
        Spelled(file, ["state", "config"], "autonomous").Should().Be(0);
    }

    [Fact]
    public void AKnownMarkerInABag_IsDroppedOnSave_AndTheTypedValueWins()
    {
        var store = new SessionStore(_data);
        var file = Plant(store, []);

        // (a) the typed marker says false, the bag says true.
        Edit(file, json => ConfigOf(json)["autonomous"] = false);
        var typed = store.Load(Repo, Branch)!;
        store.Save(typed with
        {
            State = typed.State with { Config = typed.State.Config with { Unknown = WithEntry(typed.State.Config.Unknown, "autonomous", "true") } },
        });
        Spelled(file, ["state", "config"], "autonomous").Should().Be(1);
        JsonNode.Parse(File.ReadAllText(file))!["state"]!["config"]!["autonomous"]!.GetValue<bool>().Should().BeFalse("the typed value wins");

        // (b) no typed marker: a bag cannot make one.
        Edit(file, json => ConfigOf(json).Remove("autonomous"));
        var absent = store.Load(Repo, Branch)!;
        absent.State.Config.Autonomous.Should().BeNull();
        store.Save(absent with
        {
            State = absent.State with { Config = absent.State.Config with { Unknown = WithEntry(absent.State.Config.Unknown, "autonomous", "true") } },
        });
        Spelled(file, ["state", "config"], "autonomous").Should().Be(0, "a bag cannot make the marker");
    }

    [Fact]
    public void AFileWithNoConfig_IsAFileFromBeforeAuto()
    {
        var store = new SessionStore(_data);
        var file = Plant(store, []);
        Edit(file, json => json["state"]!.AsObject().Remove("config"));

        var config = store.Load(Repo, Branch)!.State.Config;

        config.Autonomous.Should().BeNull("a session with no config was written before policy auto");
        config.PredatesAuto.Should().BeTrue();
        config.OnExhausted.Should().Be(StagePolicy.Human);
        config.Roles.Keys.Should().BeEquivalentTo(PanelConfig.AllRoles);
        config.Catalog.Should().BeSameAs(RoleCatalog.Builtin);
    }

    public static TheoryData<string> ConfigsThisBuildMakes() =>
    [
        "new()", "continue", "human", "escalate", "good_enough", "uniform", "uniform-human", "env-absent", "env-human", "file-config-under-new",
    ];

    private static PanelConfig MadeBy(string label) => label switch
    {
        "new()" => new PanelConfig(),
        "continue" => new PanelConfig(OnExhausted: StagePolicy.Continue),
        "human" => new PanelConfig(OnExhausted: StagePolicy.Human),
        "escalate" => new PanelConfig(OnExhausted: StagePolicy.Escalate),
        "good_enough" => new PanelConfig(OnExhausted: StagePolicy.GoodEnough),
        "uniform" => PanelConfig.Uniform(3, 2),
        "uniform-human" => PanelConfig.Uniform(3, 2, StagePolicy.Human),
        "env-absent" => PanelSettings.FromEnvironment(_ => null).Rounds,
        "env-human" => PanelSettings.FromEnvironment(name => name == "COAI_ON_EXHAUSTED" ? "human" : null).Rounds,
        "file-config-under-new" => new PanelConfig(roles: null, autonomous: null).UnderPolicyOf(new PanelConfig()),
        _ => throw new ArgumentOutOfRangeException(nameof(label)),
    };

    [Theory]
    [MemberData(nameof(ConfigsThisBuildMakes))]
    public void AConfigThisBuildMade_IsNeverSavedWithoutTheMarker(string label)
    {
        var config = MadeBy(label);
        var store = new SessionStore(_data);
        var session = Baseline(Repo);

        store.Save(session with { State = session.State with { Config = config } });

        var file = store.FileFor(Repo, Branch);
        Spelled(file, ["state", "config"], "autonomous").Should().Be(1, $"{label} must carry its marker");
        JsonNode.Parse(File.ReadAllText(file))!["state"]!["config"]!["autonomous"]!.GetValue<bool>()
            .Should().Be(config.Autonomous!.Value, "the file holds what the config says");
    }

    [Fact]
    public void AMarkerOfAnotherType_IsNotReadAsASession()
    {
        // The serializer's strictness policy auto relies on: half a file is not read, so no release decides on it (spec F13, M4).
        var store = new SessionStore(_data);
        var file = Plant(store, []);
        Edit(file, json => ConfigOf(json)["autonomous"] = "false");

        store.Load(Repo, Branch).Should().BeNull();
    }

    [Fact]
    public void ATypesContractNames_AreWorkedOutOncePerType()
    {
        // Every save walks every bag; a bag on each round of a long session asked the type for its names again each time
        // (code gate 2026-10-19). The names of a contract cannot change while the process runs, so they are worked out once.
        var first = SessionScrub.KnownNamesOf(ServerJsonContext.Default.RoundRecord);
        var second = SessionScrub.KnownNamesOf(ServerJsonContext.Default.RoundRecord);

        second.Should().BeSameAs(first, "a second bag of the same type must not rebuild the set");
        first.Should().Contain("verdict").And.Contain("VERDICT", "the names are compared without case")
            .And.NotContain("unknown", "the bag is the container, not a member of the contract");
    }

    [Fact]
    public void ABagWithNothingToDrop_IsWrittenFromTheDictionaryItWasGiven()
    {
        // The scrub copies a bag only when a name has to go; a bag of truly unknown members goes out as it came in.
        var store = new SessionStore(_data);
        Plant(store, Planted);
        var loaded = store.Load(Repo, Branch)!;
        var bag = loaded.Rounds[0].Unknown;

        var scrubbed = SessionScrub.Scrubbed(loaded, RetiredMembers.Shipped);

        scrubbed.Should().BeSameAs(loaded, "nothing in any bag had to go, so no level is rebuilt");
        scrubbed.Rounds[0].Unknown.Should().BeSameAs(bag);
    }

    [Theory]
    [InlineData("securityLane")]
    [InlineData("roles")]
    public void AConfigWrittenBeforeAMemberExisted_StillSaves_AndKeepsItsUnknowns(string absent)
    {
        // The scrub walks into the config's gates. A file written before the security lane (2026-10-01) or with no role
        // budgets has no such member, and the save that read it must still write it — the walk may not be the thing that
        // turns an old file into a NullReferenceException on every open.
        var store = new SessionStore(_data);
        var kept = Planted.Where(p => !p.Where.Contains(absent) && !p.Where.Contains(FirstRole)).ToArray();
        var file = Plant(store, kept);
        Edit(file, json => json["state"]!["config"]!.AsObject().Remove(absent));

        var save = () => store.Save(store.Load(Repo, Branch)!);

        save.Should().NotThrow();
        AssertEveryUnknownIsThere(file, kept);
    }

    private PanelService Service() =>
        new(
            new PanelSettings { Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human), DataDir = _data },
            VaultKeys.None("no vault in tests"),
            default,
            new ProcessLauncher(),
            Logger.None, Noticing.None);

    /// <summary>A real repository with one commit on <c>main</c> — <c>open</c> resolves the branch and prunes the repository it is given.</summary>
    private async Task<string> RepositoryWithOneCommit()
    {
        var repo = TempDir.For("session-bag-repo-");
        _extra.Add(repo);
        var launcher = new ProcessLauncher();
        await File.WriteAllTextAsync(repo.At("a.txt"), "x\n", Ct);
        foreach (var args in new string[][] { ["init", "-b", "main"], ["add", "."], ["commit", "-m", "one"] })
        {
            var result = await launcher.RunAsync(
                new ProcessRequest("git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], repo.Path), Ct);
            result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
        }

        return repo.Path;
    }

    [Fact]
    public async Task OpenAndStatus_CarryNoUnknownMember()
    {
        // The unknown members belong to the FILE: a caller reads what this server knows, as before — and the file keeps them.
        var repo = await RepositoryWithOneCommit();
        var store = new SessionStore(_data);
        var file = Plant(store, Planted, repo);
        var service = Service();

        var opened = await service.OpenAsync(repo, Branch);
        var status = await service.StatusAsync(repo, Branch);

        foreach (var answer in new[] { opened, status })
        {
            JsonNode.Parse(answer)!["rounds"]!.AsArray().Should().ContainSingle("the planted round is in the answer");
            foreach (var name in new[] { "futureRound", "futureReviewer" })
            {
                answer.Should().NotContain($"\"{name}\"", $"the answer names no member this build does not know ({name})");
            }
        }

        JsonNode.Parse(opened)!["rounds"]![0]!["verdict"]!.GetValue<string>().Should().Be("proceed", "the members it knows are still answered");
        AssertEveryUnknownIsThere(file, Planted);
    }
}

/// <summary>The naive shape (spec M1): a declared default on a constructor parameter. Used by one test, to show what it reads.</summary>
internal sealed record NaiveConfig(StagePolicy OnExhausted = StagePolicy.GoodEnough, bool? Autonomous = true);

/// <summary>A context with the session context's options — the shape a session file is read with.</summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
[System.Text.Json.Serialization.JsonSerializable(typeof(NaiveConfig))]
internal sealed partial class NaiveContext : System.Text.Json.Serialization.JsonSerializerContext;
