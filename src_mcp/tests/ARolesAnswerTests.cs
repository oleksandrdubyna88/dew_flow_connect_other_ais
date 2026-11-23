using System.Text.Json;
using CoaiMcp.Roles;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// v2 E3.S7 t1 — the roles answer: the owner's Claude table, ordered failover lists, bands, Haiku kinds and the work method, with a person's <c>COAI_ROLES_*</c> over them, and no model
/// name for codex or agy that this repository does not already hold.
/// </summary>
public sealed class ARolesAnswerTests
{
    private static readonly string NoCodexRow = """[{"id":"claude","runtime":"claude"}]""";

    private static Func<string, string?> Env(params (string Name, string Value)[] values) =>
        name => values.Where(one => one.Name == name).Select(one => one.Value).FirstOrDefault();

    private static JsonElement Answer(Func<string, string?> env, string client = "claude-code", bool hook = false) =>
        JsonDocument.Parse(RolesAnswer.Json(RolesAnswer.Compute(env, client, "2.1.330", hook))).RootElement.Clone();

    private static string[] Models(JsonElement answer, string role) =>
        [.. answer.GetProperty("roles").GetProperty(role).GetProperty("models").EnumerateArray().Select(one => $"{one.GetProperty("model").GetString()}:{one.GetProperty("vendor").GetString()}")];

    [Fact]
    public void Claude_HasTheOwnersTable()
    {
        var answer = Answer(Env());

        answer.GetProperty("mainAi").GetProperty("vendor").GetString().Should().Be("claude");
        answer.GetProperty("mainAi").GetProperty("cli").GetString().Should().Be("claude");
        answer.GetProperty("mainAi").GetProperty("version").GetString().Should().Be("2.1.330");
        Models(answer, "orchestrator").Should().Equal("opus:claude");
        Models(answer, "coder").Should().Equal("sonnet:claude");
        Models(answer, "scout").Should().Equal("sonnet:claude"); // a role the setting did not name keeps its default
        Models(answer, "ba").Should().Equal("fable:claude", "opus:claude"); // a second model to fail over to (epoch 3 E1 s2, risk 8)
        answer.GetProperty("complexityBands").EnumerateArray().Select(band => $"{band.GetProperty("min").GetInt32()}-{band.GetProperty("max").GetInt32()}={band.GetProperty("model").GetString()}")
            .Should().Equal("0-2=haiku", "3-6=sonnet", "7-8=opus", "9-10=fable");
        answer.GetProperty("haikuKinds").EnumerateArray().Select(kind => kind.GetString()).Should().Contain(["translate", "summarize", "rename", "changelog", "simple-docs"]);
        var method = answer.GetProperty("workMethod");
        method.GetProperty("mvpEpics").GetProperty("min").GetInt32().Should().Be(1);
        method.GetProperty("mvpEpics").GetProperty("max").GetInt32().Should().Be(2);
        method.GetProperty("maxEpicsPerEpoch").GetInt32().Should().Be(5);
        method.GetProperty("minConsultsPerEpoch").GetInt32().Should().Be(1);
        method.GetProperty("minConsultsPerEpic").GetInt32().Should().Be(0);
        answer.GetProperty("settingsRevision").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TheImportantRole_FailsOverToCodexOnlyWhenACodexRowIsConfigured()
    {
        Models(Answer(Env()), "important").Should().Equal("fable:claude", "astra:codex");
        Models(Answer(Env(("COAI_VENDORS", """[{"id":"my-codex","runtime":"codex"}]"""))), "important").Should().Equal("fable:claude", "astra:codex");
        Models(Answer(Env(("COAI_VENDORS", NoCodexRow))), "important").Should().Equal("fable:claude");
        Models(Answer(Env(("COAI_VENDORS", """[{"id":"antigravity","runtime":"antigravity"}]"""))), "important").Should().Equal("fable:claude");
    }

    [Theory]
    [InlineData("codex-mcp-client", "codex")]
    [InlineData("antigravity-client", "agy")]
    public void CodexAndAgy_HaveNoDefaultModels_AndSayWhy(string client, string vendor)
    {
        var answer = Answer(Env(), client);

        answer.GetProperty("mainAi").GetProperty("vendor").GetString().Should().Be(vendor);
        foreach (var role in new[] { "orchestrator", "coder", "scout", "important", "ba" })
        {
            Models(answer, role).Should().BeEmpty($"no measured model list for {vendor} exists in this repository, so none is made up");
        }

        answer.GetProperty("complexityBands").GetArrayLength().Should().Be(0);
        answer.GetProperty("notes").EnumerateArray().Select(note => note.GetString()).Should().Contain(note => note!.Contains("no measured model list"));
    }

    [Fact]
    public void EveryModelNameTheDefaultsFileHolds_IsOneTheRepositoryAlreadyNames()
    {
        // The defaults of claude are the owner's (server.md §2, §20); the only other vendor's name is `astra`, with the place it comes from written beside it. A new name has to be added here on purpose.
        var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "role-defaults.json"))).RootElement;
        var names = file.GetProperty("mainAi").EnumerateObject()
            .SelectMany(vendor => vendor.Value.GetProperty("roles").EnumerateObject().SelectMany(role => role.Value.EnumerateArray())
                .Concat(vendor.Value.GetProperty("complexityBands").EnumerateArray()))
            .Select(entry => (Model: entry.GetProperty("model").GetString(), Vendor: entry.GetProperty("vendor").GetString(), HasSource: entry.TryGetProperty("source", out _)))
            .ToList();

        names.Where(one => one.Vendor != "claude").Should().OnlyContain(one => one.Model == "astra" && one.HasSource, "a codex or agy name needs its source written beside it");
        names.Where(one => one.Vendor == "claude").Select(one => one.Model).Distinct().Should().BeEquivalentTo(["opus", "sonnet", "haiku", "fable"]);
        foreach (var vendor in new[] { "codex", "agy" })
        {
            file.GetProperty("mainAi").GetProperty(vendor).GetProperty("roles").EnumerateObject().Should().OnlyContain(role => role.Value.GetArrayLength() == 0, $"{vendor} has no measured list");
        }
    }

    [Fact]
    public void ASetting_ReplacesTheDefaultOfItsKey_AndTheOrderIsKept()
    {
        var answer = Answer(Env(
            (RolesKeys.Models, """{"coder":[{"model":"opus","vendor":"claude"},{"model":"sonnet","vendor":"claude"}]}"""),
            (RolesKeys.Bands, """[{"min":0,"max":5,"model":"sonnet","vendor":"claude"},{"min":6,"max":10,"model":"opus","vendor":"claude"}]"""),
            (RolesKeys.HaikuKinds, """["Translate"," lint "]"""),
            (RolesKeys.WorkMethod, """{"maxEpicsPerEpoch":3,"mvpEpics":{"min":2,"max":2}}""")));

        Models(answer, "coder").Should().Equal("opus:claude", "sonnet:claude");
        Models(answer, "scout").Should().Equal("sonnet:claude"); // a role the setting did not name keeps its default
        answer.GetProperty("complexityBands").GetArrayLength().Should().Be(2);
        answer.GetProperty("haikuKinds").EnumerateArray().Select(kind => kind.GetString()).Should().Equal("translate", "lint");
        answer.GetProperty("workMethod").GetProperty("maxEpicsPerEpoch").GetInt32().Should().Be(3);
        answer.GetProperty("workMethod").GetProperty("mvpEpics").GetProperty("min").GetInt32().Should().Be(2);
        answer.GetProperty("workMethod").GetProperty("minConsultsPerEpoch").GetInt32().Should().Be(1, "a work-method number the setting did not name keeps its default");
        answer.GetProperty("unrecognised").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void ACodexMainAi_TakesItsTableFromThePersonsSettings()
    {
        var answer = Answer(Env((RolesKeys.Models, """{"coder":[{"model":"x-model","vendor":"codex"}]}""")), "codex-mcp-client");

        Models(answer, "coder").Should().Equal("x-model:codex");
        Models(answer, "scout").Should().BeEmpty();
    }

    [Theory]
    [InlineData(RolesKeys.Models, "{not json")]
    [InlineData(RolesKeys.Models, """{"janitor":[{"model":"a","vendor":"b"}]}""")]
    [InlineData(RolesKeys.Models, """{"coder":[]}""")]
    [InlineData(RolesKeys.Models, """{"coder":[{"model":"a"}]}""")]
    [InlineData(RolesKeys.Bands, """[{"min":0,"max":5,"model":"a","vendor":"claude"},{"min":5,"max":10,"model":"b","vendor":"claude"}]""")]
    [InlineData(RolesKeys.Bands, """[{"min":0,"max":11,"model":"a","vendor":"claude"}]""")]
    [InlineData(RolesKeys.Bands, """[{"min":4,"max":2,"model":"a","vendor":"claude"}]""")]
    [InlineData(RolesKeys.HaikuKinds, """["ok",""]""")]
    [InlineData(RolesKeys.WorkMethod, """{"maxEpicsPerEpoch":"five"}""")]
    [InlineData(RolesKeys.WorkMethod, """{"invented":1}""")]
    [InlineData(RolesKeys.WorkMethod, """{"mvpEpics":{"min":3,"max":2}}""")]
    [InlineData(RolesKeys.MainAi, "gemini-pro")]
    public void ASettingThatDoesNotRead_IsNamed_AndItsDefaultStands(string key, string value)
    {
        var plain = Answer(Env());
        var answer = Answer(Env((key, value)));

        answer.GetProperty("unrecognised").EnumerateArray().Select(problem => problem.GetProperty("key").GetString()).Should().Contain(key);
        answer.GetProperty("unrecognised").EnumerateArray().First().GetProperty("sentence").GetString().Should().Contain(key);
        if (key != RolesKeys.MainAi)
        {
            // Every other part of the answer is the default one — nothing was half applied.
            answer.GetProperty("roles").GetRawText().Should().Be(plain.GetProperty("roles").GetRawText());
            answer.GetProperty("complexityBands").GetRawText().Should().Be(plain.GetProperty("complexityBands").GetRawText());
            answer.GetProperty("workMethod").GetRawText().Should().Be(plain.GetProperty("workMethod").GetRawText());
            answer.GetProperty("haikuKinds").GetRawText().Should().Be(plain.GetProperty("haikuKinds").GetRawText());
        }
    }

    [Fact]
    public void OneBadRole_CostsThatRoleOnly()
    {
        var answer = Answer(Env((RolesKeys.Models, """{"coder":[{"model":"opus","vendor":"claude"}],"scout":[]}""")));

        Models(answer, "coder").Should().Equal("opus:claude");
        Models(answer, "scout").Should().Equal("sonnet:claude"); // a role the setting did not name keeps its default
        answer.GetProperty("unrecognised").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void TheSettingsRevision_IsTheSameForTheSameSettings_AndChangesWithAnyOfThem()
    {
        string Revision(params (string, string)[] values) => Answer(Env(values)).GetProperty("settingsRevision").GetString()!;

        var plain = Revision();
        Revision().Should().Be(plain);
        var changed = Revision((RolesKeys.HaikuKinds, """["translate"]"""));
        changed.Should().NotBe(plain);
        Revision((RolesKeys.HaikuKinds, """["translate"]""")).Should().Be(changed);
        Revision((RolesKeys.WorkMethod, """{"maxEpicsPerEpoch":4}""")).Should().NotBe(changed).And.NotBe(plain);
    }

    [Fact]
    public void TheMainAi_IsFoundFromTheClientThenTheEnvironment_AndAnUnknownOneSaysSo()
    {
        Answer(Env(), "Codex").GetProperty("mainAi").GetProperty("vendor").GetString().Should().Be("codex");
        Answer(Env(("CODEX_THREAD_ID", "t1")), string.Empty).GetProperty("mainAi").GetProperty("vendor").GetString().Should().Be("codex");
        Answer(Env(("CLAUDECODE", "1")), string.Empty).GetProperty("notes").GetArrayLength().Should().Be(0);
        Answer(Env(), "claude-code", hook: false).GetProperty("mainAi").GetProperty("vendor").GetString().Should().Be("claude");
        Answer(Env((RolesKeys.MainAi, "agy")), "claude-code").GetProperty("mainAi").GetProperty("vendor").GetString().Should().Be("agy", "a person's setting outranks the client's name");

        var unknown = Answer(Env(), string.Empty);
        unknown.GetProperty("mainAi").GetProperty("vendor").GetString().Should().Be("claude");
        unknown.GetProperty("notes").EnumerateArray().Select(note => note.GetString()).Should().Contain(note => note!.Contains("could not be told"));
    }

    [Fact]
    public void TheHookText_HoldsTheRoleTable_TheBands_AndTheWorkMethodSentence()
    {
        var text = RolesAnswer.HookText(RolesAnswer.Compute(Env(), string.Empty, string.Empty, claudeHook: true));

        text.Should().Contain("orchestrator: opus").And.Contain("coder: sonnet").And.Contain("scout: sonnet").And.Contain("ba: fable");
        text.Should().Contain("important: fable, then astra (codex)", "the failover order, with the other vendor named");
        text.Should().Contain("0-2 haiku").And.Contain("9-10 fable");
        text.Should().Contain("Work method: build the MVP in 1-2 epics first, then polish; at most 5 epics per epoch");
        text.Length.Should().BeLessThan(1500, "a hook's text is read at every session start, so it stays short");
    }
}
