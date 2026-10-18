using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A session file keeps the members a NEWER server wrote into it, when an OLDER server loads it and saves it again
/// (<c>todo/PLAN_a_session_file_keeps_what_a_newer_server_wrote.md</c>).
/// </summary>
/// <remarks>
/// <para>Several <c>coai-mcp</c> processes share one data folder, and a window that was not reloaded after an update runs the
/// older binary. That binary read the file with a context that skips what it does not know, and wrote back only what it
/// knew — so a member a newer release added was gone after one save. The fixtures here plant members no type declares
/// (<c>futureRoot</c>, <c>authors</c>, …): they stand for what a newer release will add, and the test is that they come back
/// out of <see cref="SessionStore.Save"/> with the same value.</para>
/// <para>Every fixture carries <c>"rejections": []</c> because it is built by serialising a real session — a hand-made file
/// without the member loads with a default <c>ImmutableArray</c> and the next save throws (plan §3, side finding).</para>
/// </remarks>
public sealed class ASessionFileKeepsWhatANewerServerWroteTests : IDisposable
{
    private const string Repo = "D:/fixture-bag";
    private const string Branch = "main";

    private readonly TempDir _data = TempDir.For("testsToDelete-session-bag-");

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
        new Planting(["rounds[0]"], "authors", """{"claude":{"lines":12,"files":[1,2,3]},"codex":{"lines":0,"files":[]}}"""),
        new Planting(["rounds[0]", "reviewerStates[0]"], "futureReviewer", "\"r\""),
    ];

    public void Dispose() => _data.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PersistedSession Baseline()
    {
        var state = new SessionState("bag-1", Repo, Branch, new PanelConfig()) { Stage = Stage.CodeReview };
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
    private string Plant(SessionStore store, IEnumerable<Planting> unknowns)
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(Baseline(), ServerJsonContext.Default.PersistedSession))!.AsObject();
        foreach (var unknown in unknowns)
        {
            Locate(json, unknown.Where)[unknown.Name] = JsonNode.Parse(unknown.Json);
        }

        var file = store.FileFor(Repo, Branch);
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
        var expected = info.Properties.Where(p => p.Get?.Invoke(value!) is not null).Select(p => p.Name);
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
}
