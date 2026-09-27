using System.Text.Json;
using CoaiMcp.Api;
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
/// S3.6 and S3.7 of PLAN_feature_review.md on the server's side: a vault key is a vendor the panel can
/// offer by NAME, a row may read a key filed under another name, and an <c>api</c> turn is priced from
/// the rates its row carried — at the cached rate for cached tokens, at the tier past the threshold, and
/// "no price set" rather than $0 when the row has none.
/// </summary>
/// <remarks>
/// The rates in the fixtures are the ones measured on 2026-09-26 for our routes: <c>xai/grok-4.7</c>
/// 2.00 / 0.50 / 6.00 per million with every rate doubled from 200K prompt tokens, and
/// <c>dashscope/qwen3.8-max</c> 2.00 / 0.25 / 6.00.
/// </remarks>
public sealed class AnApiReviewerIsPricedAndKeyedTests : IDisposable
{
    private const string Key = "sk-priced-0123456789abcdefghijklmnopqrstuv";

    private static readonly TokenPrice Grok = new(new TokenRates(2.00, 0.50, 6.00), 200_000, new TokenRates(4.00, 1.00, 12.00));

    private readonly ApiEndpointStub _stub = ApiEndpointStub.Start();
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-api-priced-").FullName;

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

    // ---------- the arithmetic ----------

    [Fact]
    public void Cached_tokens_are_priced_at_the_cached_rate_and_taken_out_of_the_fresh_input()
    {
        // 100 000 in, 60 000 of them cached, 10 000 out:
        // 40 000 × 2.00 + 60 000 × 0.50 + 10 000 × 6.00 = 0.08 + 0.03 + 0.06 = 0.17
        Grok.CostOf(new Usage(100_000, 10_000, null, 60_000)).Should().BeApproximately(0.17, 1e-9);
    }

    [Fact]
    public void A_turn_whose_prompt_reaches_the_tier_is_priced_at_the_tier_rates()
    {
        // 250 000 in (none cached), 1 000 out, at 4.00 / 12.00: 1.00 + 0.012
        Grok.CostOf(new Usage(250_000, 1_000, null)).Should().BeApproximately(1.012, 1e-9);
        // One token under the threshold is the base rate: 199 999 × 2.00 / 1M
        Grok.CostOf(new Usage(199_999, 0, null)).Should().BeApproximately(0.399998, 1e-9);
    }

    [Fact]
    public void A_row_with_no_cached_rate_prices_cached_tokens_at_the_input_rate_never_at_zero()
    {
        var noCachedRate = new TokenPrice(new TokenRates(2.00, 0, 6.00), 0, TokenRates.None);

        noCachedRate.CostOf(new Usage(1_000_000, 0, null, 1_000_000)).Should().BeApproximately(2.00, 1e-9);
    }

    [Fact]
    public void No_price_is_no_cost_not_a_zero_one()
    {
        TokenPrice.None.CostOf(new Usage(5_000, 500, null)).Should().BeNull();
        CostText.Of(null, noPriceSet: true).Should().Be(", no price set");
        CostText.Of(null, noPriceSet: false, nothing: " (no cost reported)").Should().Be(" (no cost reported)");
        CostText.Of(0.25, noPriceSet: false).Should().Be(", $0.2500");
    }

    /// <summary>
    /// A vendor reporting more cached tokens than prompt tokens never yields a negative fresh input (epic 3's
    /// code round, #5): every prompt token is priced at the cached rate and nothing is refunded.
    /// </summary>
    [Fact]
    public void Cached_above_the_input_is_clamped_never_a_negative_fresh_input()
    {
        // 1 000 in, 5 000 "cached", 0 out: 1 000 × 0.50 / 1M — not (1 000 − 5 000) × 2.00 + 5 000 × 0.50
        Grok.CostOf(new Usage(1_000, 0, null, TokensCached: 5_000)).Should().BeApproximately(0.0005, 1e-9);
        Grok.CostOf(new Usage(0, 0, null, TokensCached: 5_000)).Should().Be(0);
    }

    /// <summary>
    /// The cost is worked out in the PARENT (epic 3's code round, #23): the shim reports raw tokens, the
    /// invocation carries the row's price, and <c>ApiRuntime.ReadUsage</c> prices the line — the same
    /// arithmetic for an answered call and a failed one. No rate rides on the command line any more.
    /// </summary>
    [Fact]
    public void A_price_rides_on_the_invocation_and_the_parent_prices_the_raw_tokens()
    {
        var invocation = Build(Grok);

        invocation.Price.Should().Be(Grok);
        invocation.Request.Arguments.Should().NotContain(a => a.StartsWith("--price-") || a.StartsWith("--tier-"));
        var priced = new ApiRuntime("grok", _stub.Endpoint).ReadUsage(
            invocation, new ProcessResult(70, "{\"tokensIn\":100000,\"tokensOut\":10000,\"tokensCached\":60000,\"tokensReasoning\":9000}", "", false));
        priced.CostUsd.Should().BeApproximately(0.17, 1e-9, "a failed call's raw tokens are priced exactly like an answered one's");
        priced.TokensReasoning.Should().Be(9000);
        priced.NoPriceSet.Should().BeFalse();
    }

    // ---------- the turn, through the real shim ----------

    [Fact]
    public async Task An_api_turn_with_tokens_and_a_priced_row_writes_a_cost_to_the_ledger()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, ApiEndpointStub.Completion("{\"findings\":[]}", 100_000, 10_000, cachedTokens: 60_000));

        var (result, invocation) = await RunShimAsync(Grok);
        var usage = new ApiRuntime("grok", _stub.Endpoint).ReadUsage(invocation, result);
        new UsageLedger(_dir).Record(invocation, new ReviewerOutcome.Ok(new NormalisedReview([], []), false, usage),
            "grok-4.7", "FeatureReview", TimeSpan.FromSeconds(4));

        result.ExitCode.Should().Be(0, $"stderr said: {result.StdErr}; {_stub.Journal()}");
        usage.CostUsd.Should().BeApproximately(0.17, 1e-9);
        usage.NoPriceSet.Should().BeFalse();
        var line = LedgerLine();
        line.GetProperty("costUsd").GetDouble().Should().BeApproximately(0.17, 1e-9);
        line.GetProperty("costNote").GetString().Should().BeEmpty();
        result.StdOut.Should().NotContain(Key);
        result.StdOut.Should().NotContain("costUsd", "the shim reports raw tokens; the money is worked out in the parent (#23)");
    }

    [Fact]
    public async Task An_api_turn_from_a_row_with_no_price_says_no_price_set_and_never_0()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, ApiEndpointStub.Completion("{\"findings\":[]}", 900, 40));

        var (result, invocation) = await RunShimAsync(TokenPrice.None);
        var usage = new ApiRuntime("grok", _stub.Endpoint).ReadUsage(invocation, result);
        new UsageLedger(_dir).Record(invocation, new ReviewerOutcome.Ok(new NormalisedReview([], []), false, usage),
            "grok-4.7", "FeatureReview", TimeSpan.FromSeconds(4));

        usage.CostUsd.Should().BeNull();
        usage.NoPriceSet.Should().BeTrue();
        var line = LedgerLine();
        line.GetProperty("costUsd").ValueKind.Should().Be(JsonValueKind.Null, "an unpriced run is not a free one");
        line.GetProperty("costNote").GetString().Should().Be("no price set");
    }

    [Fact]
    public void The_price_stays_in_the_parent_and_the_key_still_only_through_the_environment()
    {
        var invocation = Build(Grok);

        invocation.Request.Arguments.Should().NotContain(a => a.StartsWith("--price-") || a.StartsWith("--tier-"), "the shim is handed no rate");
        invocation.Price.TierFromTokens.Should().Be(200_000, "the tier is the parent's to apply, per request");
        invocation.Request.Arguments.Should().NotContain(Key);
        invocation.Request.Environment[ApiRuntime.KeyVariable].Should().Be(Key);
    }

    [Fact]
    public void A_CLI_on_a_subscription_is_never_marked_as_missing_a_price()
    {
        // Only a metered run can lack a price; codex's tokens are the panel's list-price estimate.
        new Usage(10, 2, null).NoPriceSet.Should().BeFalse();
        new Usage(10, 2, null).Add(new Usage(1, 1, null)).NoPriceSet.Should().BeFalse();
        new Usage(10, 2, null).Add(new Usage(1, 1, null, NoPriceSet: true)).NoPriceSet.Should().BeTrue();
    }

    // ---------- the row on the wire ----------

    [Fact]
    public void A_row_carries_its_price_and_the_name_of_the_key_it_reads()
    {
        var row = PanelSettings.ParseVendors(
            """[{"id":"qwen-2","runtime":"api","baseUrl":"https://x.example/v1","key":"Qwen","price":{"in":2,"cached":0.25,"out":6}}]""")
            .Should().ContainSingle().Subject;

        row.KeyName.Should().Be("qwen");
        row.Price.Should().Be(new TokenPrice(new TokenRates(2, 0.25, 6), 0, TokenRates.None));
        row.Identity().VaultName.Should().Be("qwen");
    }

    [Fact]
    public void A_row_that_names_no_key_reads_its_own_id_as_it_always_has()
    {
        var row = PanelSettings.ParseVendors("""[{"id":"grok","runtime":"api","baseUrl":"https://api.x.ai/v1"}]""").Single();

        row.KeyName.Should().Be("grok");
        row.Price.Should().Be(TokenPrice.None);
    }

    [Fact]
    public void A_nonsense_price_is_no_price_and_half_a_tier_is_no_tier()
    {
        var rows = PanelSettings.ParseVendors(
            """[{"id":"a","runtime":"api","price":{"in":-2,"out":-1}},{"id":"b","runtime":"api","price":{"in":2,"out":6,"tierFrom":200000}}]""");

        rows[0].Price.Should().Be(TokenPrice.None);
        rows[1].Price.TierFromTokens.Should().Be(0, "a threshold with no tier rates would price a long request at zero");
    }

    // ---------- the vault's names, and never its values ----------

    [Fact]
    public async Task Providers_reports_the_vaults_key_names_and_never_a_value()
    {
        var service = Service(new VaultKeys(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["qwen"] = Key, ["grok"] = "sk-other-0123456789abcdefghijkl" },
            string.Empty));

        var answer = await service.ProvidersAsync(TestContext.Current.CancellationToken);

        answer.Should().NotContain(Key).And.NotContain("sk-other");
        var names = JsonDocument.Parse(answer).RootElement.GetProperty("vaultKeyNames").EnumerateArray().Select(e => e.GetString());
        names.Should().Equal("grok", "qwen");
    }

    [Fact]
    public async Task A_second_row_on_one_key_is_runnable_with_that_key()
    {
        var service = Service(new VaultKeys(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["qwen"] = Key }, string.Empty));

        var answer = JsonDocument.Parse(await service.ProvidersAsync(TestContext.Current.CancellationToken)).RootElement;

        var row = answer.GetProperty("providers").EnumerateArray().Single(p => p.GetProperty("provider").GetString() == "qwen-2");
        row.GetProperty("auth").GetString().Should().Be("vault key", "the row reads the key filed under 'qwen'");
    }

    [Fact]
    public async Task A_vault_that_cannot_be_read_names_no_keys_and_says_why()
    {
        var service = Service(VaultKeys.None("creds config refused (exit 1)"));

        var answer = JsonDocument.Parse(await service.ProvidersAsync(TestContext.Current.CancellationToken)).RootElement;

        answer.GetProperty("vaultKeyNames").GetArrayLength().Should().Be(0);
        answer.GetProperty("vaultRead").GetBoolean().Should().BeFalse();
        answer.GetProperty("vaultNote").GetString().Should().Contain("refused");
        answer.GetProperty("providers").EnumerateArray().Single().GetProperty("note").GetString()
            .Should().Contain("under 'qwen'", "the missing key is named by the name the row reads");
    }

    [Fact]
    public void A_round_hands_the_second_row_the_key_it_names_and_its_price()
    {
        var service = Service(new VaultKeys(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["qwen"] = Key }, string.Empty),
            ",\"price\":{\"in\":2,\"cached\":0.25,\"out\":6}");
        var scratch = Directory.CreateTempSubdirectory("coai-api-roster-").FullName;

        var work = service.Roster.BuildWork(
            [RoleCatalog.ArchitectureRole], scratch, "ctx", round: 1, stage: Stage.CodeReview, readsCheckout: true).Reviewers;

        var reviewer = work.Should().ContainSingle().Subject.Invocation;
        reviewer.Request.Environment[ApiRuntime.KeyVariable].Should().Be(Key, "the row 'qwen-2' reads the key filed under 'qwen'");
        reviewer.Price.Should().Be(new TokenPrice(new TokenRates(2, 0.25, 6), 0, TokenRates.None), "the row's price rides on the invocation, for the parent to price the turn with");
        reviewer.Request.Arguments.Should().NotContain(a => a.StartsWith("--price-"));
    }

    /// <summary>
    /// One reviewer, one conversation key: the launch and its repair carry the same one (a repair is the same
    /// conversation asked again), and another context is another key — so a vendor that routes its prompt
    /// cache by the key (xAI, 2026-09-26: 1,152 cached tokens on every turn of a byte-identical prefix
    /// without one) sees every turn of a review on the same server.
    /// </summary>
    [Fact]
    public void A_round_gives_each_reviewer_one_conversation_key_for_every_launch_of_it()
    {
        var service = Service(new VaultKeys(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["qwen"] = Key }, string.Empty));
        var scratch = Directory.CreateTempSubdirectory("coai-api-conv-").FullName;

        var reviewer = service.Roster.BuildWork(
            [RoleCatalog.ArchitectureRole], scratch, "ctx", round: 1, stage: Stage.CodeReview, readsCheckout: true).Reviewers.Single();
        var other = service.Roster.BuildWork(
            [RoleCatalog.ArchitectureRole], scratch, "another ctx", round: 1, stage: Stage.CodeReview, readsCheckout: true).Reviewers.Single();

        var key = KeyOf(reviewer.Invocation.Request.Arguments);
        key.Should().MatchRegex("^[0-9a-f]{32}$");
        KeyOf(reviewer.Repair!.Request.Arguments).Should().Be(key, "the repair continues the same conversation");
        KeyOf(other.Invocation.Request.Arguments).Should().NotBe(key, "another context is another conversation");
    }

    private static string KeyOf(IReadOnlyList<string> arguments)
    {
        var at = arguments.ToList().IndexOf("--conversation");

        return at >= 0 ? arguments[at + 1] : string.Empty;
    }

    /// <summary>
    /// A feature round caps an api reviewer's WHOLE conversation at the panel's <c>FeatureApiReview</c> (the
    /// operator's 20 minutes, 2026-09-27 — launch to final answer, every turn); a code round's reviewer keeps
    /// the derived <c>timeout × (1 + follow-ups)</c>, which is what the absence of a cap means.
    /// </summary>
    [Fact]
    public void A_feature_round_caps_an_api_reviewers_whole_conversation_and_a_code_round_does_not()
    {
        var service = Service(new VaultKeys(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["qwen"] = Key }, string.Empty), ",\"feature\":true");
        var scratch = Directory.CreateTempSubdirectory("coai-api-cap-").FullName;

        var feature = service.Roster.BuildWork(
            [RoleCatalog.FeatureRole], scratch, "ctx", round: 1, stage: Stage.FeatureReview, readsCheckout: false).Reviewers.Single();
        var code = service.Roster.BuildWork(
            [RoleCatalog.ArchitectureRole], scratch, "ctx", round: 1, stage: Stage.CodeReview, readsCheckout: true).Reviewers.Single();

        feature.ConversationCap.Should().Be(TimeSpan.FromMinutes(20), "the whole-review limit for an api reviewer on the feature stage, at the panel's default");
        code.ConversationCap.Should().BeNull("a code round's reviewer keeps the derived cap");
    }

    private PanelService Service(VaultKeys keys, string rowTail = "") =>
        new(
            new PanelSettings
            {
                Providers = PanelSettings.ParseVendors(
                    "[{\"id\":\"qwen-2\",\"runtime\":\"api\",\"model\":\"qwen3.8-max\",\"baseUrl\":\"https://token-plan.example/v1\",\"key\":\"qwen\"" + rowTail + "}]"),
                Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
                DataDir = _dir,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
            },
            keys,
            default,
            new RecordingLauncher(stdOut: string.Empty),
            Logger.None,
            Noticing.None);

    private ReviewerInvocation Build(TokenPrice price)
    {
        var schema = Path.Combine(_dir, "schema.json");
        File.WriteAllText(schema, FindingSchema.Json);

        return new ApiRuntime("grok", _stub.Endpoint).Build(
            RoleCatalog.ArchitectureRole, "review this", _dir, schema, _dir,
            new ReviewerSettings("grok") { Model = "grok-4.7", ApiKey = Key, Price = price, Timeout = TimeSpan.FromMinutes(2) });
    }

    /// <summary>The adapter's argv and environment, run as the REAL binary — the wiring is what is under test.</summary>
    private async Task<(ProcessResult Result, ReviewerInvocation Invocation)> RunShimAsync(TokenPrice price)
    {
        var invocation = Build(price);
        var request = new ProcessRequest(
            ServerBinary.Path,
            [.. invocation.Request.Arguments.SkipWhile(a => a != "--ask-api")],
            _dir)
        {
            Environment = invocation.Request.Environment,
            Timeout = TimeSpan.FromMinutes(2),
        };

        return (await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken), invocation);
    }

    private JsonElement LedgerLine()
    {
        var lines = File.ReadAllLines(Path.Combine(_dir, "usage.jsonl"));

        return JsonDocument.Parse(lines.Should().ContainSingle().Subject).RootElement;
    }
}
