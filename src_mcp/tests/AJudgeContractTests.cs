using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Roles;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s1 t3 — the judge's contract: its answer has ONE shape (<c>shared/judge-output.schema.json</c>), read strictly, with the gate's four severity words (d1); the three rules of the judge (S8) and a blank evidence text (S12)
/// are errors with a sentence (d2); and the schema file and the reader's tables say the same (d3).
/// </summary>
public sealed class AJudgeContractTests
{
    private static JsonObject Valid() => JsonNode.Parse("""
        {"status":"ok","verdict":"revise","summary":"The plan is sound but one rule is missing.",
         "findings":[{"id":"f1","severity":"major","title":"A rule is missing","detail":"The reader never checks the id.","evidence":[{"file":"src/a.cs","line":4,"text":"var id = x;"}],"fix":"check the id"},
                     {"id":"f2","severity":"nit","title":"A name","detail":"The name is unclear."}],
         "decision":{"chosen":"option A","why":"it is smaller","rejected":[{"option":"option B","why":"it needs a second reader"}]}}
        """)!.AsObject();

    private static JudgeRead ReadOf(JsonObject answer) => JudgeOutputReader.Read(JsonDocument.Parse(answer.ToJsonString()).RootElement);

    private static IReadOnlyList<string> ProblemsOf(JsonObject answer) => ReadOf(answer).Should().BeOfType<JudgeRead.Wrong>().Subject.Problems;

    private static JsonObject Finding(JsonObject answer, int index = 0) => answer["findings"]![index]!.AsObject();

    private static JsonObject Approve()
    {
        var answer = Valid();
        answer["verdict"] = "approve";
        answer["findings"] = new JsonArray();

        return answer;
    }

    // ---------- d1: the reader ----------

    [Fact]
    public void D1_AValidAnswer_IsRead_WithEveryValue()
    {
        var answer = ReadOf(Valid()).Should().BeOfType<JudgeRead.Answer>().Subject.Value;

        answer.Verdict.Should().Be("revise");
        answer.Summary.Should().StartWith("The plan is sound");
        answer.Findings.Select(finding => (finding.Id, finding.Severity)).Should().Equal(("f1", "major"), ("f2", "nit"));
        answer.Findings[0].Evidence.Should().Equal(new EvidenceItem("src/a.cs", 4, "var id = x;"));
        answer.Findings[0].Fix.Should().Be("check the id");
        answer.Findings[1].Evidence.Should().BeEmpty("evidence is optional");
        answer.Findings[1].Fix.Should().BeEmpty("a fix is optional");
        answer.Decision.Made.Should().BeTrue();
        (answer.Decision.Chosen, answer.Decision.Rejected[0].Option).Should().Be(("option A", "option B"));
    }

    [Fact]
    public void D1_AnApproveWithNoFindingAndNoDecision_IsValid_TheDecisionIsOnlyWhenAsked()
    {
        var answer = Approve();
        answer.Remove("decision");

        var read = ReadOf(answer).Should().BeOfType<JudgeRead.Answer>().Subject.Value;

        read.Decision.Made.Should().BeFalse();
        read.Decision.Should().BeSameAs(JudgeDecision.None);
        read.Findings.Should().BeEmpty();
    }

    [Fact]
    public void D1_ARefusal_NeedsItsReason_AndCarriesIt()
    {
        ReadOf(JsonNode.Parse("""{"status":"refused","reason":"I cannot judge a file I may not read"}""")!.AsObject()).Should().BeOfType<JudgeRead.Refused>().Which.Reason.Should().Contain("may not read");
        ProblemsOf(JsonNode.Parse("""{"status":"refused"}""")!.AsObject()).Should().ContainSingle().Which.Should().StartWith("reason:");
        ProblemsOf(JsonNode.Parse("""{"status":"refused","reason":"no","verdict":"approve"}""")!.AsObject()).Should().Contain(problem => problem.StartsWith("verdict:", StringComparison.Ordinal) && problem.Contains("unknown key", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("blocking")]
    [InlineData("major")]
    [InlineData("minor")]
    [InlineData("nit")]
    public void D1_TheFourSeverityWordsOfTheGate_AreAccepted(string severity)
    {
        var answer = Valid();
        Finding(answer)["severity"] = severity;

        ReadOf(answer).Should().BeOfType<JudgeRead.Answer>();
    }

    [Theory]
    [InlineData("critical")]
    [InlineData("high")]
    [InlineData("Blocking")]
    [InlineData("")]
    public void D1_AnotherSeverityWord_IsAnError_ThatNamesTheLegalOnes(string severity)
    {
        var answer = Valid();
        Finding(answer)["severity"] = severity;

        ProblemsOf(answer).Should().ContainSingle().Which.Should().StartWith("findings[0].severity:").And.Contain("blocking, major, minor, nit");
    }

    [Fact]
    public void D1_TheSeverityWords_AreTheGatesOwn()
    {
        JudgeOutputReader.Severities.Should().Equal("blocking", "major", "minor", "nit");
        JudgeOutputReader.Verdicts.Should().Equal("approve", "revise", "reject");
        CoaiMcp.Core.Findings.FindingSchema.Json.Should().Contain("\"enum\": [\"blocking\", \"major\", \"minor\", \"nit\"]", "the judge's words are the ones the gate's schema lists");
    }

    [Fact]
    public void D1_AnotherVerdict_IsAnError_ThatNamesTheLegalOnes()
    {
        var answer = Valid();
        answer["verdict"] = "maybe";

        ProblemsOf(answer).Should().ContainSingle().Which.Should().StartWith("verdict:").And.Contain("approve, revise, reject").And.Contain("'maybe'");
        answer.Remove("verdict");
        ProblemsOf(answer).Should().Contain(problem => problem.StartsWith("verdict:", StringComparison.Ordinal));
    }

    [Fact]
    public void D1_AnUnknownMember_IsAnError_ThatNamesIt_AtEveryLevel()
    {
        var answer = Valid();
        answer["extra"] = 1;
        Finding(answer)["confidence"] = 0.9;
        Finding(answer)["evidence"]![0]!["verified"] = true;
        answer["decision"]!["because"] = "x";
        answer["decision"]!["rejected"]![0]!["note"] = "x";

        var problems = ProblemsOf(answer);

        foreach (var place in new[] { "extra:", "findings[0].confidence:", "findings[0].evidence[0].verified:", "decision.because:", "decision.rejected[0].note:" })
        {
            problems.Should().Contain(problem => problem.StartsWith(place, StringComparison.Ordinal) && problem.Contains("unknown key", StringComparison.Ordinal), place);
        }
    }

    [Fact]
    public void D1_AFindingIdThatIsNotFAndANumber_IsAnError()
    {
        foreach (var bad in new[] { "1", "F1", "f0", "f01", "f", "finding-1", "f1x", "" })
        {
            var answer = Valid();
            Finding(answer)["id"] = bad;

            ProblemsOf(answer).Should().Contain(problem => problem.StartsWith("findings[0].id:", StringComparison.Ordinal), $"id '{bad}'");
        }

        foreach (var good in new[] { "f1", "f9", "f10", "f123" })
        {
            var answer = Valid();
            Finding(answer)["id"] = good;
            ReadOf(answer).Should().BeOfType<JudgeRead.Answer>(good);
        }
    }

    [Fact]
    public void D1_TheLimits_AreHeld_AndEachErrorNamesTheMember()
    {
        var answer = Valid();
        answer["summary"] = new string('s', 1501);
        Finding(answer)["title"] = new string('t', 121);
        Finding(answer)["detail"] = new string('d', 1501);
        Finding(answer)["fix"] = new string('f', 601);
        answer["decision"]!["chosen"] = new string('c', 121);
        answer["decision"]!["why"] = new string('w', 1501);
        answer["decision"]!["rejected"]![0]!["option"] = new string('o', 121);

        var problems = ProblemsOf(answer);

        foreach (var place in new[] { "summary:", "findings[0].title:", "findings[0].detail:", "findings[0].fix:", "decision.chosen:", "decision.why:", "decision.rejected[0].option:" })
        {
            problems.Should().Contain(problem => problem.StartsWith(place, StringComparison.Ordinal) && problem.Contains("characters, at most", StringComparison.Ordinal), place);
        }

        answer["summary"] = new string('s', 1500);
        Finding(answer)["title"] = new string('t', 120);
        Finding(answer)["detail"] = new string('d', 1500);
        Finding(answer)["fix"] = new string('f', 600);
        answer["decision"]!["chosen"] = new string('c', 120);
        answer["decision"]!["why"] = new string('w', 1500);
        answer["decision"]!["rejected"]![0]!["option"] = new string('o', 120);
        ReadOf(answer).Should().BeOfType<JudgeRead.Answer>("the limits themselves pass");
    }

    [Fact]
    public void D1_TheLimits_CountUnicodeCodePoints_NotUtf16Units_SoTheyAgreeWithTheJsonSchema()
    {
        // An emoji outside the basic plane is TWO UTF-16 units and ONE character: the schema's maxLength counts characters.
        var answer = Valid();
        answer["summary"] = string.Concat(Enumerable.Repeat("\U0001F600", 800));
        Finding(answer)["title"] = string.Concat(Enumerable.Repeat("\U0001F600", 120));
        Finding(answer)["fix"] = string.Concat(Enumerable.Repeat("\U0001F600", 600));

        ReadOf(answer).Should().BeOfType<JudgeRead.Answer>("800 emoji are 800 characters, under the limit of 1,500, though they are 1,600 UTF-16 units");

        answer["summary"] = string.Concat(Enumerable.Repeat("\U0001F600", 1501));
        ProblemsOf(answer).Should().ContainSingle().Which.Should().StartWith("summary:").And.Contain("1501 characters, at most 1500");
    }

    [Fact]
    public void D1_TheCounts_AreHeld_At30Findings_5Evidence_And8Rejected()
    {
        var thirtyOne = Approve();
        thirtyOne["verdict"] = "revise";
        for (var i = 1; i <= 31; i++)
        {
            thirtyOne["findings"]!.AsArray().Add(new JsonObject { ["id"] = $"f{i}", ["severity"] = "nit", ["title"] = "t", ["detail"] = "d" });
        }

        ProblemsOf(thirtyOne).Should().ContainSingle().Which.Should().StartWith("findings:").And.Contain("31 items").And.Contain("from 0 to 30");
        thirtyOne["findings"]!.AsArray().RemoveAt(30);
        ReadOf(thirtyOne).Should().BeOfType<JudgeRead.Answer>("exactly 30 findings is the limit and passes");

        var sixEvidence = Valid();
        var list = Finding(sixEvidence)["evidence"]!.AsArray();
        while (list.Count < 6)
        {
            list.Add(JsonNode.Parse("""{"file":"a.cs","line":1,"text":"x"}"""));
        }

        ProblemsOf(sixEvidence).Should().ContainSingle().Which.Should().StartWith("findings[0].evidence:").And.Contain("6 items").And.Contain("from 0 to 5");

        var nine = Valid();
        var rejected = nine["decision"]!["rejected"]!.AsArray();
        while (rejected.Count < 9)
        {
            rejected.Add(JsonNode.Parse("""{"option":"o","why":"w"}"""));
        }

        ProblemsOf(nine).Should().ContainSingle().Which.Should().StartWith("decision.rejected:").And.Contain("9 items").And.Contain("from 0 to 8");
    }

    [Fact]
    public void D1_EveryFaultOfTheShape_IsListedAtOnce_WithItsPlace()
    {
        var answer = Valid();
        answer["status"] = "done";
        answer.Remove("summary");
        Finding(answer, 1).Remove("detail");
        answer["decision"] = "A";
        answer["findings"]!.AsArray().Add(5);

        var problems = ProblemsOf(answer);

        problems.Should().Contain(problem => problem.StartsWith("status:", StringComparison.Ordinal) && problem.Contains("'done'", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("summary:", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("findings[1].detail:", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("decision:", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("findings[2]:", StringComparison.Ordinal));
        JudgeOutputReader.Read(JsonDocument.Parse("[1]").RootElement).Should().BeOfType<JudgeRead.Wrong>();
    }

    // ---------- d2 / S8: the three rules; S12: a blank evidence text ----------

    [Fact]
    public void D2_AnApproveWithABlockingFinding_IsAnError_WithASentence()
    {
        var answer = Valid();
        answer["verdict"] = "approve";
        Finding(answer)["severity"] = "blocking";

        var problems = ProblemsOf(answer);

        problems.Should().ContainSingle().Which.Should().StartWith("verdict:").And.Contain("\"approve\" cannot stand with a blocking finding").And.Contain("f1").And.Contain("\"revise\" or \"reject\"");
        answer["verdict"] = "revise";
        ReadOf(answer).Should().BeOfType<JudgeRead.Answer>("the same finding with a verdict that fits it reads clean");
        answer["verdict"] = "reject";
        ReadOf(answer).Should().BeOfType<JudgeRead.Answer>();
    }

    [Fact]
    public void D2_AnApproveWithAMajorFinding_IsNotAnErrorOfTheRule()
    {
        var answer = Valid();
        answer["verdict"] = "approve";

        ReadOf(answer).Should().BeOfType<JudgeRead.Answer>("only a BLOCKING finding contradicts an approve");
    }

    [Theory]
    [InlineData("revise")]
    [InlineData("reject")]
    public void D2_AReviseOrRejectWithNoFinding_IsAnError_WithASentence(string verdict)
    {
        var answer = Approve();
        answer["verdict"] = verdict;

        ProblemsOf(answer).Should().ContainSingle().Which.Should().StartWith("findings:").And.Contain($"the verdict \"{verdict}\" needs at least one finding").And.Contain("\"approve\"");
    }

    [Fact]
    public void D2_AFindingIdUsedTwice_IsAnError_NamingTheId()
    {
        var answer = Valid();
        Finding(answer, 1)["id"] = "f1";

        ProblemsOf(answer).Should().ContainSingle().Which.Should().StartWith("findings:").And.Contain("'f1'").And.Contain("more than once");
        Finding(answer, 1)["id"] = "f2";
        ReadOf(answer).Should().BeOfType<JudgeRead.Answer>();
    }

    [Fact]
    public void D2_TheRules_AreAllListedAtOnce_WithTheShapeErrors()
    {
        var answer = Valid();
        answer["verdict"] = "approve";
        Finding(answer)["severity"] = "blocking";
        Finding(answer, 1)["id"] = "f1";
        Finding(answer, 1)["title"] = new string('t', 121);

        var problems = ProblemsOf(answer);

        problems.Should().Contain(problem => problem.Contains("cannot stand with a blocking finding", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.Contains("more than once", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("findings[1].title:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData("  ")]
    [InlineData("​")]
    public void D2_ABlankEvidenceText_IsAnErrorOfTheReader_NamingTheMember(string blank)
    {
        var answer = Valid();
        Finding(answer)["evidence"]![0]!["text"] = blank;

        ProblemsOf(answer).Should().Contain(problem => problem.StartsWith("findings[0].evidence[0].text:", StringComparison.Ordinal) && problem.Contains("never empty or only white space", StringComparison.Ordinal));
    }

    [Fact]
    public void D2_AMissingEvidenceText_APathThatLeavesTheRepository_AndALine_AreErrorsToo()
    {
        var answer = Valid();
        Finding(answer)["evidence"]![0]!.AsObject().Remove("text");
        Finding(answer)["evidence"]!.AsArray().Add(JsonNode.Parse("""{"file":"../x.cs","line":0,"text":"x"}"""));

        var problems = ProblemsOf(answer);

        problems.Should().Contain(problem => problem.StartsWith("findings[0].evidence[0].text:", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("findings[0].evidence[1].file:", StringComparison.Ordinal) && problem.Contains("leaves the repository", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("findings[0].evidence[1].line:", StringComparison.Ordinal));
    }

    // ---------- d3: the schema file and the reader say the same ----------

    private static JsonElement Schema()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "judge-output.schema.json")));

        return document.RootElement.Clone();
    }

    private static IEnumerable<string> Keys(JsonElement def) => def.GetProperty("properties").EnumerateObject().Select(property => property.Name);

    private static IEnumerable<string> Required(JsonElement def) => def.GetProperty("required").EnumerateArray().Select(key => key.GetString()!);

    private static JsonElement Arm(string status) => Schema().GetProperty("allOf").EnumerateArray()
        .Single(arm => arm.GetProperty("if").GetProperty("properties").GetProperty("status").GetProperty("const").GetString() == status).GetProperty("then");

    private static JsonElement Defs(string name) => Schema().GetProperty("$defs").GetProperty(name);

    private static IEnumerable<string> Words(JsonElement list) => list.GetProperty("enum").EnumerateArray().Select(word => word.GetString()!);

    [Fact]
    public void D3_TheSchemaKeys_AreTheReadersKeys_ForEveryArmAndEveryDefinition()
    {
        var schema = Schema();

        Keys(schema).Should().BeEquivalentTo([.. JudgeOutputReader.OkKeys.Concat(JudgeOutputReader.RefusedKeys).Distinct()]);
        Keys(Arm("ok")).Should().BeEquivalentTo(JudgeOutputReader.OkKeys);
        Keys(Arm("refused")).Should().BeEquivalentTo(JudgeOutputReader.RefusedKeys);
        Required(Arm("ok")).Should().BeEquivalentTo("verdict", "summary", "findings");
        Required(Arm("refused")).Should().BeEquivalentTo("reason");
        Keys(Defs("finding")).Should().BeEquivalentTo(JudgeOutputReader.FindingKeys);
        Required(Defs("finding")).Should().BeEquivalentTo("id", "severity", "title", "detail");
        Keys(Defs("decision")).Should().BeEquivalentTo(JudgeOutputReader.DecisionKeys);
        Required(Defs("decision")).Should().BeEquivalentTo("chosen", "why");
        Keys(Defs("evidence")).Should().BeEquivalentTo(RoleOutputShape.EvidenceKeys);
        Required(Defs("evidence")).Should().BeEquivalentTo(RoleOutputShape.EvidenceKeys);
        var rejected = Defs("decision").GetProperty("properties").GetProperty("rejected").GetProperty("items");
        Keys(rejected).Should().BeEquivalentTo(JudgeOutputReader.RejectedKeys);
        Required(rejected).Should().BeEquivalentTo(JudgeOutputReader.RejectedKeys);
        foreach (var definition in new[] { schema, Arm("ok"), Arm("refused"), Defs("finding"), Defs("decision"), Defs("evidence"), rejected })
        {
            definition.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        }

        Words(schema.GetProperty("properties").GetProperty("status")).Should().Equal("ok", "refused");
    }

    [Fact]
    public void D3_TheSchemaWords_AndLimits_AreTheReadersTables()
    {
        var properties = Schema().GetProperty("properties");
        var finding = Defs("finding").GetProperty("properties");
        var decision = Defs("decision").GetProperty("properties");
        var rejected = decision.GetProperty("rejected");

        Words(properties.GetProperty("verdict")).Should().Equal(JudgeOutputReader.Verdicts);
        Words(finding.GetProperty("severity")).Should().Equal(JudgeOutputReader.Severities);
        finding.GetProperty("id").GetProperty("pattern").GetString().Should().Be(JudgeOutputReader.IdPattern);
        properties.GetProperty("summary").GetProperty("maxLength").GetInt32().Should().Be(JudgeOutputReader.MaxSummary);
        properties.GetProperty("findings").GetProperty("maxItems").GetInt32().Should().Be(JudgeOutputReader.MaxFindings);
        finding.GetProperty("title").GetProperty("maxLength").GetInt32().Should().Be(JudgeOutputReader.MaxTitle);
        finding.GetProperty("detail").GetProperty("maxLength").GetInt32().Should().Be(JudgeOutputReader.MaxDetail);
        finding.GetProperty("evidence").GetProperty("minItems").GetInt32().Should().Be(JudgeOutputReader.MinEvidence);
        finding.GetProperty("evidence").GetProperty("maxItems").GetInt32().Should().Be(JudgeOutputReader.MaxEvidence);
        finding.GetProperty("fix").GetProperty("maxLength").GetInt32().Should().Be(JudgeOutputReader.MaxFix);
        decision.GetProperty("chosen").GetProperty("maxLength").GetInt32().Should().Be(JudgeOutputReader.MaxChosen);
        decision.GetProperty("why").GetProperty("maxLength").GetInt32().Should().Be(JudgeOutputReader.MaxDecisionWhy);
        rejected.GetProperty("maxItems").GetInt32().Should().Be(JudgeOutputReader.MaxRejected);
        rejected.GetProperty("items").GetProperty("properties").GetProperty("option").GetProperty("maxLength").GetInt32().Should().Be(JudgeOutputReader.MaxOption);
        rejected.GetProperty("items").GetProperty("properties").GetProperty("why").GetProperty("maxLength").GetInt32().Should().Be(JudgeOutputReader.MaxRejectedWhy);
        var evidence = Defs("evidence").GetProperty("properties");
        evidence.GetProperty("text").GetProperty("maxLength").GetInt32().Should().Be(RoleEvidence.MaxTextLength);
        evidence.GetProperty("file").GetProperty("maxLength").GetInt32().Should().Be(RoleEvidence.MaxPathLength);
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

        foreach (var key in Required(Defs("finding")))
        {
            var answer = Valid();
            Finding(answer).Remove(key);
            ProblemsOf(answer).Should().Contain(problem => problem.Contains(key, StringComparison.Ordinal), $"a finding without {key}");
        }

        foreach (var key in Required(Defs("decision")))
        {
            var answer = Valid();
            answer["decision"]!.AsObject().Remove(key);
            ProblemsOf(answer).Should().Contain(problem => problem.Contains(key, StringComparison.Ordinal), $"a decision without {key}");
        }

        foreach (var key in Required(Defs("evidence")))
        {
            var answer = Valid();
            Finding(answer)["evidence"]![0]!.AsObject().Remove(key);
            ProblemsOf(answer).Should().Contain(problem => problem.Contains(key, StringComparison.Ordinal), $"an evidence item without {key}");
        }

        ProblemsOf(JsonNode.Parse("""{"status":"refused"}""")!.AsObject()).Should().Contain(problem => problem.Contains("reason", StringComparison.Ordinal));
        ReadOf(Valid()).Should().BeOfType<JudgeRead.Answer>("the positive of the negatives above: the same file reads clean");
    }
}
