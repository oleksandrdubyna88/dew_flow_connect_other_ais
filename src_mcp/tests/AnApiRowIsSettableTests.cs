using System.Text.Json;
using CoaiMcp.Api;
using CoaiMcp.Core.Api;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The operator's per-model settings on the server's side (2026-09-27): an <c>api</c> row may set an
/// effort, a thinking switch and a whole-review limit over its module's calibrated defaults; the module
/// validates them before a launch; and <c>providers</c> reports the module's capabilities and the row's
/// effective settings so the panel can render a dropdown from the module and a "reset to calibrated
/// default" — names and values only, never a key.
/// </summary>
public sealed class AnApiRowIsSettableTests : IDisposable
{
    private const string Key = "sk-settable-0123456789abcdefghijklmnopqrstuv";

    private readonly ApiEndpointStub _stub = ApiEndpointStub.Start();
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-api-row-").FullName;

    public void Dispose()
    {
        _stub.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ---------- the row's settings arrive from the settings file ----------

    [Fact]
    public void A_rows_effort_thinking_and_review_minutes_are_read_and_absent_is_unset()
    {
        var set = PanelSettings.ParseVendors(Row(",\"effort\":\" Low \",\"thinking\":false,\"reviewMinutes\":12")).Single();
        var bare = PanelSettings.ParseVendors(Row(string.Empty)).Single();

        set.Api.Should().Be(new ApiRowSettings("low", ThinkingSetting.Off, 12));
        bare.Api.Should().Be(ApiRowSettings.None, "a row written before the settings existed sets nothing");
        PanelSettings.ParseVendors(Row(",\"thinking\":true,\"reviewMinutes\":0")).Single().Api
            .Should().Be(new ApiRowSettings(string.Empty, ThinkingSetting.On, 0), "zero minutes is unset, not a zero-minute review");
    }

    // ---------- the roster launches with the row's settings, and refuses what the module refuses ----------

    [Fact]
    public void A_feature_reviewer_runs_with_the_rows_effort_and_its_own_review_limit()
    {
        var reviewer = FeatureReviewer(Service(",\"effort\":\"low\",\"reviewMinutes\":12"));

        reviewer.Invocation.Request.Arguments.Should().ContainInOrder("--reasoning-effort", "low");
        reviewer.Invocation.Effort.Should().Be("low");
        reviewer.ConversationCap.Should().Be(TimeSpan.FromMinutes(12), "the row's own whole-review limit");
        reviewer.Invocation.Request.Arguments.Should().NotContain("--thinking", "thinking stays on unless a row turned it off");
    }

    [Fact]
    public void A_row_that_set_nothing_runs_at_its_modules_calibrated_defaults()
    {
        var reviewer = FeatureReviewer(Service(string.Empty));

        reviewer.Invocation.Request.Arguments.Should().ContainInOrder("--reasoning-effort", "medium");
        reviewer.Invocation.Effort.Should().Be("medium", "qwen3.8-max was calibrated at medium");
        reviewer.Invocation.Request.Arguments.Should().ContainInOrder("--max-tokens", "65536");
        reviewer.ConversationCap.Should().Be(TimeSpan.FromMinutes(20));
    }

    [Fact]
    public void The_environment_still_overrides_the_default_and_the_row_overrides_the_environment()
    {
        var overrides = new ApiOverrides("xhigh", 8192, 15);

        var fromEnvironment = FeatureReviewer(Service(string.Empty, overrides));
        fromEnvironment.Invocation.Request.Arguments.Should().ContainInOrder("--reasoning-effort", "xhigh");
        fromEnvironment.Invocation.Request.Arguments.Should().ContainInOrder("--max-tokens", "8192");
        fromEnvironment.ConversationCap.Should().Be(TimeSpan.FromMinutes(15));

        var fromRow = FeatureReviewer(Service(",\"effort\":\"low\",\"reviewMinutes\":12", overrides));
        fromRow.Invocation.Request.Arguments.Should().ContainInOrder("--reasoning-effort", "low");
        fromRow.ConversationCap.Should().Be(TimeSpan.FromMinutes(12));
    }

    [Fact]
    public void Thinking_off_on_a_module_with_a_switch_reaches_the_shim_and_the_wire()
    {
        var reviewer = FeatureReviewer(Service(",\"thinking\":false"));

        reviewer.Invocation.Request.Arguments.Should().ContainInOrder("--thinking", "off");
    }

    [Fact]
    public void An_effort_the_module_refuses_keeps_the_reviewer_out_of_the_round_with_the_sentence()
    {
        var work = Work(Service(",\"effort\":\"ultra\""));

        work.Reviewers.Should().BeEmpty("nothing is launched to be answered 400 after a round trip");
        work.Excluded.Should().ContainSingle().Which.Reason
            .Should().Be("qwen does not take reasoning effort 'ultra' — it accepts low, medium, xhigh (and 'none' switches thinking off)");
    }

    [Fact]
    public void Thinking_off_on_a_module_without_a_switch_keeps_the_reviewer_out_of_the_round()
    {
        var work = Work(Service(",\"thinking\":false", row: "{\"id\":\"grok\",\"runtime\":\"api\",\"model\":\"grok-4.7\",\"baseUrl\":\"https://api.example/v1\",\"dialect\":\"xai\",\"feature\":true{TAIL}}"));

        work.Reviewers.Should().BeEmpty();
        work.Excluded.Should().ContainSingle().Which.Reason.Should().Contain("xai has no thinking switch");
    }

    // ---------- the adapter and the shim carry the switch ----------

    [Fact]
    public void The_adapter_spells_the_switch_only_when_thinking_is_off()
    {
        var runtime = new ApiRuntime("qwen", "https://api.example/v1");
        var schema = Path.Combine(_dir, "schema.json");
        File.WriteAllText(schema, FindingSchema.Json);
        var settings = new ReviewerSettings("qwen") { Model = "qwen3.8-max", ApiKey = Key, Dialect = "dashscope", Timeout = TimeSpan.FromMinutes(5) };

        runtime.Build("r", "p", _dir, schema, _dir, settings).Request.Arguments.Should().NotContain("--thinking");
        runtime.Build("r", "p", _dir, schema, _dir, settings with { ThinkingOn = false }).Request.Arguments.Should().ContainInOrder("--thinking", "off");
    }

    [Fact]
    public async Task The_shim_sends_the_modules_spelling_of_thinking_off()
    {
        var stderr = new List<string>();
        var prompt = Path.Combine(_dir, "prompt.txt");
        var schema = Path.Combine(_dir, "schema.json");
        File.WriteAllText(prompt, "review this");
        File.WriteAllText(schema, FindingSchema.Json);

        var code = await AskApiMode.RunAsync(
            [
                "--ask-api", "--vendor", "qwen", "--endpoint", _stub.Endpoint, "--model", "qwen3.8-max", "--dialect", "dashscope",
                "--prompt-file", prompt, "--schema-file", schema, "--out", Path.Combine(_dir, "answer.json"),
                "--timeout-seconds", "20", "--max-tokens", "8192", "--reasoning-effort", "medium", "--thinking", "off",
            ],
            stderr.Add, new StringWriter(), name => name == ApiRuntime.KeyVariable ? Key : null);

        code.Should().Be(0, string.Join("\n", stderr));
        _stub.Requests.Should().ContainSingle().Which.Body.Should().Contain("\"reasoning_effort\":\"none\"").And.NotContain("medium");
    }

    // ---------- providers: the module's capabilities and the row's effective settings, never a key ----------

    [Fact]
    public async Task Providers_reports_the_module_its_capabilities_its_defaults_and_the_rows_effective_settings()
    {
        var answer = await Service(",\"effort\":\"low\",\"reviewMinutes\":12").ProvidersAsync(TestContext.Current.CancellationToken);

        answer.Should().NotContain(Key);
        var api = JsonDocument.Parse(answer).RootElement.GetProperty("providers").EnumerateArray()
            .Single(p => p.GetProperty("provider").GetString() == "qwen-2").GetProperty("api");
        api.GetProperty("module").GetString().Should().Be("qwen");
        api.GetProperty("priceRoute").GetString().Should().Be("dashscope");
        api.GetProperty("capabilities").GetProperty("effortLevels").EnumerateArray().Select(e => e.GetString()).Should().Equal("low", "medium", "xhigh");
        api.GetProperty("capabilities").GetProperty("thinkingSwitchable").GetBoolean().Should().BeTrue();
        api.GetProperty("capabilities").GetProperty("thinkingOffLevel").GetString().Should().Be("none");
        api.GetProperty("defaults").GetProperty("effort").GetString().Should().Be("medium", "what 'reset to calibrated default' restores");
        api.GetProperty("defaults").GetProperty("reviewMinutes").GetInt32().Should().Be(20);
        api.GetProperty("effective").GetProperty("effort").GetString().Should().Be("low", "the row's own setting");
        api.GetProperty("effective").GetProperty("reviewMinutes").GetInt32().Should().Be(12);
        api.GetProperty("effective").GetProperty("thinkingOn").GetBoolean().Should().BeTrue();
        api.GetProperty("refusal").GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task Providers_names_the_refusal_of_a_row_the_module_will_not_launch_and_says_nothing_for_a_cli_row()
    {
        var answer = JsonDocument.Parse(await Service(
                ",\"effort\":\"ultra\"",
                row: "{\"id\":\"qwen-2\",\"runtime\":\"api\",\"model\":\"qwen3.8-max\",\"baseUrl\":\"https://api.example/v1\",\"dialect\":\"dashscope\",\"key\":\"qwen\",\"feature\":true{TAIL}},"
                    + "{\"id\":\"codex\",\"runtime\":\"codex\",\"model\":\"\",\"baseUrl\":\"\"}")
            .ProvidersAsync(TestContext.Current.CancellationToken)).RootElement;

        var rows = answer.GetProperty("providers").EnumerateArray().ToList();
        rows.Single(p => p.GetProperty("provider").GetString() == "qwen-2").GetProperty("api").GetProperty("refusal").GetString()
            .Should().Contain("does not take reasoning effort 'ultra'");
        rows.Single(p => p.GetProperty("provider").GetString() == "codex").TryGetProperty("api", out _)
            .Should().BeFalse("a CLI row has no module, and a row written before the modules reads exactly as it did");
    }

    [Fact]
    public async Task Providers_says_when_a_rows_named_module_was_set_aside_for_its_model()
    {
        var answer = JsonDocument.Parse(await Service(
                string.Empty,
                row: "{\"id\":\"glm-x\",\"runtime\":\"api\",\"model\":\"glm-5.2\",\"baseUrl\":\"https://api.example/v1\",\"dialect\":\"glm\",\"key\":\"qwen\",\"feature\":true{TAIL}}")
            .ProvidersAsync(TestContext.Current.CancellationToken)).RootElement;

        var api = answer.GetProperty("providers").EnumerateArray().Single().GetProperty("api");
        api.GetProperty("module").GetString().Should().Be("dashscope", "the generic module over the named module's row");
        api.GetProperty("measuredModel").GetString().Should().BeEmpty();
        api.GetProperty("capabilities").GetProperty("effortLevels").GetArrayLength().Should().Be(0, "nothing declared for a model nobody measured");
        api.GetProperty("note").GetString().Should().Contain("the 'glm' module was measured on glm-5.3").And.Contain("'glm-5.2' runs on the same 'dashscope' row");
        api.GetProperty("refusal").GetString().Should().BeEmpty();
    }

    // ---------- the environment's effort is held to the module (the calibration branch's code round) ----------

    private const string GlmRow =
        "{\"id\":\"glm\",\"runtime\":\"api\",\"model\":\"glm-5.3\",\"baseUrl\":\"https://api.example/v1\",\"dialect\":\"dashscope\",\"key\":\"qwen\",\"feature\":true{TAIL}}";

    /// <summary>
    /// <c>COAI_LOCAL_REASONING_EFFORT=none</c> meant "send no effort" on every dialect row before the modules
    /// (each maps <c>none</c> to an omitted field) — the calibration harness ran qwen that way — and on the qwen
    /// module <c>none</c> is the thinking-OFF switch. A blanket environment value must not throw a row's
    /// thinking switch: it keeps its old meaning, the vendor's own default depth with thinking on.
    /// </summary>
    [Fact]
    public void An_environment_none_sends_no_effort_and_never_switches_qwens_thinking_off()
    {
        var reviewer = FeatureReviewer(Service(string.Empty, new ApiOverrides("none")));

        reviewer.Invocation.Request.Arguments.Should().NotContain("none", "none on qwen is the thinking-off switch");
        reviewer.Invocation.Request.Arguments.Should().NotContain("--thinking");
    }

    /// <summary>
    /// An environment effort the module does not declare is still SENT — every calibration run relied on that
    /// (qwen's ran <c>high</c>) — but the report names it, so a vendor's refusal that follows has its cause on
    /// the card rather than only in a failed round.
    /// </summary>
    [Fact]
    public async Task An_environment_effort_the_module_does_not_declare_is_sent_and_named_in_the_report()
    {
        var service = Service(string.Empty, new ApiOverrides("medium"), row: GlmRow);

        FeatureReviewer(service).Invocation.Request.Arguments.Should().ContainInOrder("--reasoning-effort", "medium");

        var api = JsonDocument.Parse(await service.ProvidersAsync(TestContext.Current.CancellationToken)).RootElement
            .GetProperty("providers").EnumerateArray().Single().GetProperty("api");
        api.GetProperty("refusal").GetString().Should().BeEmpty("the environment is the operator's knob, not the row's setting");
        api.GetProperty("note").GetString().Should().Contain("COAI_LOCAL_REASONING_EFFORT").And.Contain("medium");
        FeatureReviewer(Service(string.Empty, new ApiOverrides("high"), row: GlmRow)).Invocation.Request.Arguments
            .Should().ContainInOrder("--reasoning-effort", "high");
    }

    /// <summary>A row's own <c>none</c> on qwen IS the thinking switch — and the report says thinking is off, as the wire does.</summary>
    [Fact]
    public async Task A_rows_own_none_on_qwen_is_reported_as_thinking_off()
    {
        var api = JsonDocument.Parse(await Service(",\"effort\":\"none\"").ProvidersAsync(TestContext.Current.CancellationToken)).RootElement
            .GetProperty("providers").EnumerateArray().Single().GetProperty("api");

        api.GetProperty("effective").GetProperty("thinkingOn").GetBoolean().Should().BeFalse("the wire sends qwen's thinking-off level");
    }

    // ---------- the stream switch (todo/PLAN_api_streaming.md) ----------

    [Fact]
    public void A_rows_stream_switch_is_read_and_absent_or_false_is_off()
    {
        PanelSettings.ParseVendors(Row(",\"stream\":true")).Single().Api.Stream.Should().BeTrue();
        PanelSettings.ParseVendors(Row(",\"stream\":false")).Single().Api.Stream.Should().BeFalse();
        PanelSettings.ParseVendors(Row(string.Empty)).Single().Api.Stream.Should().BeFalse("a row written before the switch streams nothing");
    }

    [Fact]
    public void A_reviewer_on_a_streaming_row_is_launched_with_the_stream_and_one_without_is_launched_as_always()
    {
        FeatureReviewer(Service(",\"stream\":true")).Invocation.Request.Arguments.Should().ContainInOrder("--stream", "on");
        FeatureReviewer(Service(string.Empty)).Invocation.Request.Arguments.Should().NotContain("--stream");
    }

    [Fact]
    public void The_adapter_spells_the_stream_only_when_the_row_asked()
    {
        var runtime = new ApiRuntime("qwen", "https://api.example/v1");
        var schema = Path.Combine(_dir, "schema.json");
        File.WriteAllText(schema, FindingSchema.Json);
        var settings = new ReviewerSettings("qwen") { Model = "qwen3.8-max", ApiKey = Key, Dialect = "dashscope", Timeout = TimeSpan.FromMinutes(5) };

        runtime.Build("r", "p", _dir, schema, _dir, settings).Request.Arguments.Should().NotContain("--stream");
        runtime.Build("r", "p", _dir, schema, _dir, settings with { Stream = true }).Request.Arguments.Should().ContainInOrder("--stream", "on");
    }

    [Fact]
    public void A_consultant_or_a_check_on_a_streaming_row_streams_too()
    {
        // The consultant's turn and the model card's Check run through ConsultantTurnInputs (ConsultantCheck): a switch on
        // the card that the Check then ignored would measure a row that is not the one configured.
        var row = PanelSettings.ParseVendors(Row(",\"stream\":true")).Single();

        ConsultantTurnInputs.Settings(row, row.Model, TimeSpan.FromMinutes(5), _dir, VaultKeys.None("t"), ApiOverrides.None).Stream.Should().BeTrue();
    }

    [Fact]
    public void One_helper_hands_an_api_row_its_effective_settings_effort_ceiling_thinking_and_stream()
    {
        var effective = new ApiEffective("low", ThinkingOn: false, MaxTokens: 4096, FollowUps: 3, ReviewMinutes: 20, Stream: true);

        var settings = new ReviewerSettings("qwen").WithApi(effective);

        (settings.ReasoningEffort, settings.MaxTokens, settings.ThinkingOn, settings.Stream).Should().Be(("low", 4096, false, true));
    }

    [Fact]
    public void Every_place_an_api_row_becomes_a_reviewer_uses_that_one_helper()
    {
        // The roster, the consultant (and Check) and the question consultant each turned an api row into launch settings
        // by hand; a field added to one and not another is the drift this guards (the stream switch's plan review).
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var byHand = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where((file) => !file.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where((file) => File.ReadAllText(file).Contains("ThinkingOn = api", StringComparison.Ordinal))
            .Select(Path.GetFileName);
        var helped = new[] { "RosterBuilder.cs", "ConsultantTurnInputs.cs", "QuestionFanOut.cs" }
            .Where((name) => Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).Any((file) => File.ReadAllText(file).Contains(".WithApi(", StringComparison.Ordinal)));

        byHand.Should().BeEquivalentTo(["ReviewerRuntime.cs"], "an api row's settings are carried field by field only inside ReviewerSettings.WithApi itself");
        helped.Should().BeEquivalentTo(["RosterBuilder.cs", "ConsultantTurnInputs.cs", "QuestionFanOut.cs"]);
    }

    // ---------- helpers ----------

    private const string QwenRow =
        "{\"id\":\"qwen-2\",\"runtime\":\"api\",\"model\":\"qwen3.8-max\",\"baseUrl\":\"https://api.example/v1\",\"dialect\":\"dashscope\",\"key\":\"qwen\",\"feature\":true{TAIL}}";

    private static string Row(string tail, string row = QwenRow) => "[" + row.Replace("{TAIL}", tail) + "]";

    private PanelService Service(string rowTail, ApiOverrides? overrides = null, string row = QwenRow) =>
        new(
            new PanelSettings
            {
                Providers = PanelSettings.ParseVendors(Row(rowTail, row)),
                Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
                DataDir = _dir,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                ApiOverrides = overrides ?? ApiOverrides.None,
            },
            new VaultKeys(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["qwen"] = Key, ["grok"] = Key }, string.Empty),
            default,
            new RecordingLauncher(stdOut: string.Empty),
            Logger.None,
            Noticing.None);

    private static ReviewerWork FeatureReviewer(PanelService service) => Work(service).Reviewers.Single();

    private static RoundWork Work(PanelService service) =>
        service.Roster.BuildWork(
            [RoleCatalog.FeatureRole], Directory.CreateTempSubdirectory("coai-api-row-work-").FullName, "ctx",
            round: 1, stage: Stage.FeatureReview, readsCheckout: false);
}
