using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Roles;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s1 t2 — the scout's contract: its answer has ONE shape (<c>shared/scout-output.schema.json</c>), read strictly (d1), a blank evidence text, claim or unknown line is an error that names the member (d2, S12), and the schema
/// file and the reader's tables say the same (d3). The contract tests of the BA and the coder are their own files and are not touched.
/// </summary>
public sealed class AScoutContractTests
{
    private static JsonObject Valid() => JsonNode.Parse("""
        {"status":"ok",
         "facts":[{"claim":"the router is pure","evidence":[{"file":"src/Router.cs","line":12,"text":"static class RoleRouter"},{"file":"src/Router.cs","line":30,"text":"no IO"}]},
                  {"claim":"a second fact","evidence":[{"file":"README.md","line":1,"text":"# Title"}]}],
         "files":["src/Router.cs","README.md"],
         "unknown":["where the bands are written"]}
        """)!.AsObject();

    private static ScoutRead ReadOf(JsonObject answer) => ScoutOutputReader.Read(JsonDocument.Parse(answer.ToJsonString()).RootElement);

    private static IReadOnlyList<string> ProblemsOf(JsonObject answer) => ReadOf(answer).Should().BeOfType<ScoutRead.Wrong>().Subject.Problems;

    private static JsonObject Fact(JsonObject answer, int index = 0) => answer["facts"]![index]!.AsObject();

    private static JsonObject Evidence(JsonObject answer, int fact = 0, int index = 0) => answer["facts"]![fact]!["evidence"]![index]!.AsObject();

    // ---------- d1: the reader ----------

    [Fact]
    public void D1_AValidAnswer_IsRead_WithEveryValue()
    {
        var answer = ReadOf(Valid()).Should().BeOfType<ScoutRead.Answer>().Subject.Value;

        answer.Facts.Select(fact => fact.Claim).Should().Equal("the router is pure", "a second fact");
        answer.Facts[0].Evidence.Should().Equal(new EvidenceItem("src/Router.cs", 12, "static class RoleRouter"), new EvidenceItem("src/Router.cs", 30, "no IO"));
        answer.Files.Should().Equal("src/Router.cs", "README.md");
        answer.Unknown.Should().Equal("where the bands are written");
    }

    [Fact]
    public void D1_AnEmptyAnswerIsValid_NoFactsNoFilesNothingUnknown()
    {
        ReadOf(JsonNode.Parse("""{"status":"ok","facts":[],"files":[],"unknown":[]}""")!.AsObject()).Should().BeOfType<ScoutRead.Answer>();
    }

    [Fact]
    public void D1_ARefusal_NeedsItsReason_AndCarriesIt()
    {
        ReadOf(JsonNode.Parse("""{"status":"refused","reason":"the question asks me to change a file"}""")!.AsObject()).Should().BeOfType<ScoutRead.Refused>().Which.Reason.Should().Contain("change a file");
        ProblemsOf(JsonNode.Parse("""{"status":"refused"}""")!.AsObject()).Should().ContainSingle().Which.Should().StartWith("reason:");
        ProblemsOf(JsonNode.Parse("""{"status":"refused","reason":"  "}""")!.AsObject()).Should().ContainSingle().Which.Should().StartWith("reason:");
    }

    [Fact]
    public void D1_AnUnknownMember_IsAnError_ThatNamesIt_AtEveryLevel()
    {
        var answer = Valid();
        answer["extra"] = 1;
        Fact(answer)["confidence"] = "high";
        Evidence(answer)["verified"] = true;

        var problems = ProblemsOf(answer);

        problems.Should().Contain(problem => problem.StartsWith("extra:", StringComparison.Ordinal) && problem.Contains("unknown key", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("facts[0].confidence:", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("facts[0].evidence[0].verified:", StringComparison.Ordinal), "a model's own verified is never read: it is an unknown key");
    }

    [Fact]
    public void D1_TheArmsAreDistinct_AReasonOnAnOkAnswer_AndFactsOnARefusal_AreUnknownKeys()
    {
        var ok = Valid();
        ok["reason"] = "also";
        ProblemsOf(ok).Should().Contain(problem => problem.StartsWith("reason:", StringComparison.Ordinal) && problem.Contains("unknown key", StringComparison.Ordinal));

        ProblemsOf(JsonNode.Parse("""{"status":"refused","reason":"no","facts":[]}""")!.AsObject()).Should().Contain(problem => problem.StartsWith("facts:", StringComparison.Ordinal) && problem.Contains("unknown key", StringComparison.Ordinal));
    }

    [Fact]
    public void D1_AFactWithNoEvidence_IsAnError_ThatNamesTheFact()
    {
        var answer = Valid();
        Fact(answer)["evidence"] = new JsonArray();

        ProblemsOf(answer).Should().ContainSingle().Which.Should().StartWith("facts[0].evidence:").And.Contain("0 items").And.Contain("from 1 to 5");
        var missing = Valid();
        Fact(missing).Remove("evidence");
        ProblemsOf(missing).Should().Contain(problem => problem.StartsWith("facts[0].evidence:", StringComparison.Ordinal));
    }

    [Fact]
    public void D1_TheCounts_AreHeld_At40Facts_5Evidence_60Files_And20Unknown()
    {
        var item = """{"file":"a.cs","line":1,"text":"x"}""";
        var fortyOne = JsonNode.Parse("""{"status":"ok","facts":[],"files":[],"unknown":[]}""")!.AsObject();
        for (var i = 0; i < 41; i++)
        {
            fortyOne["facts"]!.AsArray().Add(new JsonObject { ["claim"] = $"c{i}", ["evidence"] = new JsonArray(JsonNode.Parse(item)) });
        }

        ProblemsOf(fortyOne).Should().ContainSingle().Which.Should().StartWith("facts:").And.Contain("41 items").And.Contain("from 0 to 40");
        fortyOne["facts"]!.AsArray().RemoveAt(40);
        ReadOf(fortyOne).Should().BeOfType<ScoutRead.Answer>("exactly 40 facts is the limit and passes");

        var sixEvidence = Valid();
        var list = Fact(sixEvidence)["evidence"]!.AsArray();
        while (list.Count < 6)
        {
            list.Add(JsonNode.Parse(item));
        }

        ProblemsOf(sixEvidence).Should().ContainSingle().Which.Should().StartWith("facts[0].evidence:").And.Contain("6 items");

        var files = Valid();
        files["files"] = new JsonArray([.. Enumerable.Range(0, 61).Select(i => (JsonNode)$"src/f{i}.cs")]);
        ProblemsOf(files).Should().ContainSingle().Which.Should().StartWith("files:").And.Contain("61 items").And.Contain("from 0 to 60");

        var unknown = Valid();
        unknown["unknown"] = new JsonArray([.. Enumerable.Range(0, 21).Select(i => (JsonNode)$"u{i}")]);
        ProblemsOf(unknown).Should().ContainSingle().Which.Should().StartWith("unknown:").And.Contain("21 items").And.Contain("from 0 to 20");
    }

    [Fact]
    public void D1_ATextOverItsLength_IsAnError_ThatNamesTheMember()
    {
        var answer = Valid();
        Fact(answer)["claim"] = new string('c', 401);
        Evidence(answer)["text"] = new string('t', 301);
        answer["unknown"] = new JsonArray(new string('u', 301));

        var problems = ProblemsOf(answer);

        problems.Should().Contain(problem => problem.StartsWith("facts[0].claim:", StringComparison.Ordinal) && problem.Contains("401 characters, at most 400", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("facts[0].evidence[0].text:", StringComparison.Ordinal) && problem.Contains("301 characters, at most 300", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("unknown[0]:", StringComparison.Ordinal) && problem.Contains("at most 300", StringComparison.Ordinal));
        Fact(answer)["claim"] = new string('c', 400);
        Evidence(answer)["text"] = new string('t', 300);
        answer["unknown"] = new JsonArray(new string('u', 300));
        ReadOf(answer).Should().BeOfType<ScoutRead.Answer>("the limits themselves pass");
    }

    [Fact]
    public void D1_ALine_IsAWholeNumberFromOne_AndAPathIsJudgedAsText()
    {
        foreach (var bad in new JsonNode?[] { 0, -3, 1.5, "7", null })
        {
            var answer = Valid();
            Evidence(answer)["line"] = bad;

            ProblemsOf(answer).Should().Contain(problem => problem.StartsWith("facts[0].evidence[0].line:", StringComparison.Ordinal), $"line {bad?.ToJsonString() ?? "null"}");
        }

        var paths = Valid();
        Evidence(paths)["file"] = "../outside.cs";
        Evidence(paths, 0, 1)["file"] = ".env";
        Evidence(paths, 1, 0).Remove("file");
        paths["files"] = new JsonArray("/etc/passwd", "ok/file.cs", 5);

        var problems = ProblemsOf(paths);

        problems.Should().Contain(problem => problem.StartsWith("facts[0].evidence[0].file:", StringComparison.Ordinal) && problem.Contains("leaves the repository", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("facts[0].evidence[1].file:", StringComparison.Ordinal) && problem.Contains("credential file", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("facts[1].evidence[0].file:", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("files[0]:", StringComparison.Ordinal) && problem.Contains("inside the repository", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("files[2]:", StringComparison.Ordinal));
        problems.Should().NotContain(problem => problem.StartsWith("files[1]:", StringComparison.Ordinal));
    }

    [Fact]
    public void D1_EveryFaultOfTheShape_IsListedAtOnce_WithItsPlace()
    {
        var answer = Valid();
        answer["status"] = "done";
        answer.Remove("files");
        Fact(answer, 1)["claim"] = 5;
        answer["unknown"] = "none";

        var problems = ProblemsOf(answer);

        problems.Should().Contain(problem => problem.StartsWith("status:", StringComparison.Ordinal) && problem.Contains("'done'", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("files:", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("facts[1].claim:", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("unknown:", StringComparison.Ordinal));
        ScoutOutputReader.Read(JsonDocument.Parse("[1]").RootElement).Should().BeOfType<ScoutRead.Wrong>();
        var noStatus = Valid();
        noStatus.Remove("status");
        ProblemsOf(noStatus).Should().Contain(problem => problem.StartsWith("status:", StringComparison.Ordinal));
    }

    // ---------- d2 / S12: a blank text is an error, and the member is named ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData("  ")]
    [InlineData("​")]
    public void D2_ABlankEvidenceText_IsAnErrorOfTheReader_NamingTheMember(string blank)
    {
        var answer = Valid();
        Evidence(answer, 1, 0)["text"] = blank;

        var problems = ProblemsOf(answer);

        problems.Should().Contain(problem => problem.StartsWith("facts[1].evidence[0].text:", StringComparison.Ordinal) && problem.Contains("never empty or only white space", StringComparison.Ordinal));
    }

    [Fact]
    public void D2_AMissingOrNonStringEvidenceText_IsTheSameError()
    {
        var missing = Valid();
        Evidence(missing).Remove("text");
        var number = Valid();
        Evidence(number)["text"] = 5;

        ProblemsOf(missing).Should().Contain(problem => problem.StartsWith("facts[0].evidence[0].text:", StringComparison.Ordinal));
        ProblemsOf(number).Should().Contain(problem => problem.StartsWith("facts[0].evidence[0].text:", StringComparison.Ordinal));
    }

    [Fact]
    public void D2_ABlankClaim_AndABlankUnknownLine_AreErrorsAsWell()
    {
        var answer = Valid();
        Fact(answer)["claim"] = "   ";
        answer["unknown"] = new JsonArray("fine", "\t ");

        var problems = ProblemsOf(answer);

        problems.Should().Contain(problem => problem.StartsWith("facts[0].claim:", StringComparison.Ordinal) && problem.Contains("not only white space", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("unknown[1]:", StringComparison.Ordinal) && problem.Contains("non-empty string", StringComparison.Ordinal));
        problems.Should().NotContain(problem => problem.StartsWith("unknown[0]:", StringComparison.Ordinal));
    }

    // ---------- d3: the schema file and the reader say the same ----------

    private static JsonElement Schema()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "scout-output.schema.json")));

        return document.RootElement.Clone();
    }

    private static IEnumerable<string> Keys(JsonElement def) => def.GetProperty("properties").EnumerateObject().Select(property => property.Name);

    private static IEnumerable<string> Required(JsonElement def) => def.GetProperty("required").EnumerateArray().Select(key => key.GetString()!);

    private static JsonElement Arm(string status) => Schema().GetProperty("allOf").EnumerateArray()
        .Single(arm => arm.GetProperty("if").GetProperty("properties").GetProperty("status").GetProperty("const").GetString() == status).GetProperty("then");

    private static JsonElement Defs(string name) => Schema().GetProperty("$defs").GetProperty(name);

    [Fact]
    public void D3_TheSchemaKeys_AreTheReadersKeys_ForEveryArmAndEveryDefinition()
    {
        var schema = Schema();

        Keys(schema).Should().BeEquivalentTo([.. ScoutOutputReader.OkKeys.Concat(ScoutOutputReader.RefusedKeys).Distinct()]);
        Keys(Arm("ok")).Should().BeEquivalentTo(ScoutOutputReader.OkKeys);
        Keys(Arm("refused")).Should().BeEquivalentTo(ScoutOutputReader.RefusedKeys);
        Required(Arm("ok")).Should().BeEquivalentTo("facts", "files", "unknown");
        Required(Arm("refused")).Should().BeEquivalentTo("reason");
        Keys(Defs("fact")).Should().BeEquivalentTo(ScoutOutputReader.FactKeys);
        Required(Defs("fact")).Should().BeEquivalentTo(ScoutOutputReader.FactKeys);
        Keys(Defs("evidence")).Should().BeEquivalentTo(RoleOutputShape.EvidenceKeys);
        Required(Defs("evidence")).Should().BeEquivalentTo(RoleOutputShape.EvidenceKeys);
        foreach (var definition in new[] { schema, Arm("ok"), Arm("refused"), Defs("fact"), Defs("evidence") })
        {
            definition.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        }

        schema.GetProperty("properties").GetProperty("status").GetProperty("enum").EnumerateArray().Select(status => status.GetString()).Should().Equal("ok", "refused");
    }

    [Fact]
    public void D3_TheSchemaLimits_AreTheReadersTables()
    {
        var properties = Schema().GetProperty("properties");

        properties.GetProperty("facts").GetProperty("maxItems").GetInt32().Should().Be(ScoutOutputReader.MaxFacts);
        properties.GetProperty("facts").GetProperty("minItems").GetInt32().Should().Be(0);
        properties.GetProperty("files").GetProperty("maxItems").GetInt32().Should().Be(ScoutOutputReader.MaxFiles);
        properties.GetProperty("files").GetProperty("items").GetProperty("maxLength").GetInt32().Should().Be(RoleEvidence.MaxPathLength);
        properties.GetProperty("unknown").GetProperty("maxItems").GetInt32().Should().Be(ScoutOutputReader.MaxUnknown);
        properties.GetProperty("unknown").GetProperty("items").GetProperty("maxLength").GetInt32().Should().Be(ScoutOutputReader.MaxUnknownText);
        var fact = Defs("fact").GetProperty("properties");
        fact.GetProperty("claim").GetProperty("maxLength").GetInt32().Should().Be(ScoutOutputReader.MaxClaim);
        fact.GetProperty("evidence").GetProperty("minItems").GetInt32().Should().Be(ScoutOutputReader.MinEvidence);
        fact.GetProperty("evidence").GetProperty("maxItems").GetInt32().Should().Be(ScoutOutputReader.MaxEvidence);
        var evidence = Defs("evidence").GetProperty("properties");
        evidence.GetProperty("text").GetProperty("maxLength").GetInt32().Should().Be(RoleEvidence.MaxTextLength);
        evidence.GetProperty("text").GetProperty("minLength").GetInt32().Should().Be(1);
        evidence.GetProperty("file").GetProperty("maxLength").GetInt32().Should().Be(RoleEvidence.MaxPathLength);
        evidence.GetProperty("line").GetProperty("minimum").GetInt32().Should().Be(1);
    }

    [Fact]
    public void D3_TheReader_RefusesEveryMissingRequiredKey_OfTheSchema()
    {
        foreach (var key in Required(Arm("ok")))
        {
            var answer = Valid();
            answer.Remove(key);
            ProblemsOf(answer).Should().Contain(problem => problem.Contains(key, StringComparison.Ordinal), $"an answer without {key}");
        }

        foreach (var key in Required(Defs("fact")))
        {
            var answer = Valid();
            Fact(answer).Remove(key);
            ProblemsOf(answer).Should().Contain(problem => problem.Contains(key, StringComparison.Ordinal), $"a fact without {key}");
        }

        foreach (var key in Required(Defs("evidence")))
        {
            var answer = Valid();
            Evidence(answer).Remove(key);
            ProblemsOf(answer).Should().Contain(problem => problem.Contains(key, StringComparison.Ordinal), $"an evidence item without {key}");
        }

        ProblemsOf(JsonNode.Parse("""{"status":"refused"}""")!.AsObject()).Should().Contain(problem => problem.Contains("reason", StringComparison.Ordinal));
        ReadOf(Valid()).Should().BeOfType<ScoutRead.Answer>("the positive of the negatives above: the same file reads clean");
    }
}
