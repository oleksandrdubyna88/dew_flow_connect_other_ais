using CoaiMcp.Core.Api;
using CoaiMcp.Core.Findings;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The vendor modules (<c>core/Api/*Vendor.cs</c>): one class per vendor behind <see cref="IApiVendor"/>,
/// resolved by name in one place, each declaring what its vendor can be told and what calibration
/// settled — and spelling the one thing a row of the dialect table cannot, a thinking switch.
/// </summary>
/// <remarks>
/// Every capability value here is the vendor's own reference or a measurement, cited on the module. The
/// bytes the modules send for the measured rows are pinned separately by <c>ApiVendorGoldensTests</c>.
/// </remarks>
public sealed class ApiVendorModulesTests
{
    private static readonly ApiTurn Turn = new("m", "review this", FindingSchema.Json, 4242, "medium", 8192);

    // ---------- the registry: the one place a name becomes a type ----------

    [Theory]
    [InlineData("xai", "grok-4.7", typeof(XaiVendor), "xai")]
    [InlineData("qwen", "qwen3.8-max", typeof(QwenVendor), "qwen")]
    [InlineData("deepseek", "deepseek-v4-pro", typeof(DeepSeekVendor), "deepseek")]
    [InlineData("glm", "glm-5.3", typeof(GlmVendor), "glm")]
    [InlineData("openai", "gpt-5", typeof(OpenAiCompatibleVendor), "openai")]
    [InlineData("", "gpt-5", typeof(OpenAiCompatibleVendor), "openai")]
    [InlineData(" XAI ", "grok-4.7", typeof(XaiVendor), "xai")]
    public void A_module_name_resolves_to_its_module(string dialect, string model, Type module, string name)
    {
        var vendor = ApiVendors.Resolve(dialect, model);

        vendor.Should().BeOfType(module);
        vendor!.Name.Should().Be(name);
    }

    /// <summary>
    /// A row written before the modules existed names the <c>dashscope</c> ROW — every calibrated Alibaba
    /// row does — and resolves to the module measured on that EXACT model; any other model of the row,
    /// a sibling included, runs generically with nothing declared.
    /// </summary>
    /// <remarks>
    /// Exact, never a family prefix — the GLM consultation's catch (2026-09-27): the vendor documents
    /// <c>glm-5.2</c> with a thinking switch and a <c>medium</c> level that <c>glm-5.3</c> has not, so a
    /// prefix match would have refused settings the sibling accepts.
    /// </remarks>
    [Theory]
    [InlineData("qwen3.8-max", typeof(QwenVendor), "qwen")]
    [InlineData(" Qwen3.8-Max ", typeof(QwenVendor), "qwen")]
    [InlineData("deepseek-v4-pro", typeof(DeepSeekVendor), "deepseek")]
    [InlineData("glm-5.3", typeof(GlmVendor), "glm")]
    [InlineData("glm-5.2", typeof(OpenAiCompatibleVendor), "dashscope")]
    [InlineData("qwen3.5-plus", typeof(OpenAiCompatibleVendor), "dashscope")]
    [InlineData("kimi-k2", typeof(OpenAiCompatibleVendor), "dashscope")]
    [InlineData("", typeof(OpenAiCompatibleVendor), "dashscope")]
    public void The_dashscope_row_resolves_by_the_exact_measured_model_and_any_other_model_runs_generically(string model, Type module, string name)
    {
        var vendor = ApiVendors.Resolve("dashscope", model);

        vendor.Should().BeOfType(module);
        vendor!.Name.Should().Be(name);
        vendor.Dialect.Name.Should().Be("dashscope");
    }

    [Fact]
    public void A_sibling_model_on_the_dashscope_row_is_not_held_to_the_measured_models_capabilities()
    {
        var sibling = ApiVendors.Resolve("dashscope", "glm-5.2")!;

        sibling.Refusal(new ApiRowSettings("medium")).Should().BeEmpty(
            "glm-5.2 documents medium; nothing measured, nothing refused — the vendor answers");
        sibling.Refusal(new ApiRowSettings(Thinking: ThinkingSetting.Off)).Should().Be(
            "no calibrated module spells a thinking switch on the 'dashscope' row — run this model with thinking on, or name a module measured on it in the row's dialect field",
            "the vendor documents a switch for glm-5.2, but nothing here knows how to spell it until a module is measured on that model");
    }

    /// <summary>
    /// The other entrance (the GLM consultation's second turn): a row that NAMES a module with a model it was
    /// not measured on is set aside to the generic module over the module's own row — the measured transport
    /// kept, nothing declared about the model — and <c>SetAside</c> says so, so the downgrade is never silent.
    /// </summary>
    [Fact]
    public void A_named_module_is_set_aside_for_a_model_it_was_not_measured_on_and_says_so()
    {
        var glm52 = ApiVendors.Resolve("glm", "glm-5.2")!;
        glm52.Should().BeOfType<OpenAiCompatibleVendor>().Which.Dialect.Name.Should().Be("dashscope");
        glm52.Capabilities.Should().Be(ApiCapabilities.Undeclared);
        ApiVendors.SetAside("glm", "glm-5.2").Should().Be(
            "the 'glm' module was measured on glm-5.3 and declares that model's levels and defaults — 'glm-5.2' runs on the same "
            + "'dashscope' row with nothing declared (any effort sent verbatim, no thinking switch); name glm-5.3, or a module measured on the model, for calibrated settings");

        var grok46 = ApiVendors.Resolve("xai", "grok-4.6")!;
        grok46.Should().BeOfType<OpenAiCompatibleVendor>().Which.Dialect.Name.Should().Be("xai");
        grok46.Headers("c0ffee").Should().ContainKey("x-grok-conv-id", "the measured transport — the routing header — is the row's, and stays");
        ApiVendors.SetAside("xai", "grok-4.6").Should().Contain("measured on grok-4.7");

        ApiVendors.Resolve("glm", "GLM-5.3").Should().BeOfType<GlmVendor>("its own model, in any case");
        ApiVendors.Resolve("glm", "").Should().BeOfType<OpenAiCompatibleVendor>("no model named: the endpoint picks one this build cannot vouch for");
        ApiVendors.SetAside("glm", "").Should().Contain("a row that names no model");
        ApiVendors.SetAside("glm", "glm-5.3").Should().BeEmpty();
        ApiVendors.SetAside("dashscope", "glm-5.2").Should().BeEmpty("nothing was named, nothing was set aside");
        ApiVendors.SetAside("openai", "anything").Should().BeEmpty("the generic module was measured on no model and speaks for any");
    }

    [Fact]
    public void Each_module_names_the_one_model_it_was_measured_on()
    {
        XaiVendor.Instance.MeasuredModel.Should().Be("grok-4.7");
        QwenVendor.Instance.MeasuredModel.Should().Be("qwen3.8-max");
        DeepSeekVendor.Instance.MeasuredModel.Should().Be("deepseek-v4-pro");
        GlmVendor.Instance.MeasuredModel.Should().Be("glm-5.3");
        OpenAiCompatibleVendor.Generic.MeasuredModel.Should().BeEmpty();
    }

    [Fact]
    public void A_row_of_the_dialect_table_with_no_module_runs_generically_and_an_unknown_name_is_null()
    {
        ApiVendors.Resolve("local", "qwen3:32b").Should().BeOfType<OpenAiCompatibleVendor>().Which.Dialect.Name.Should().Be("local");
        ApiVendors.Resolve("grokish", "grok-4.7").Should().BeNull("the shim refuses a name this build has never heard of, before any request");
        ApiVendors.Names.Should().Equal("openai", "xai", "qwen", "deepseek", "glm");
    }

    // ---------- capabilities: what the panel's dropdown lists, from the module and nowhere else ----------

    [Fact]
    public void Each_module_declares_its_vendors_effort_levels_and_thinking_switch()
    {
        XaiVendor.Instance.Capabilities.Should().BeEquivalentTo(new ApiCapabilities(false, ["low", "medium", "high", "xhigh"], false),
            "xAI: four levels, high the default, and 'reasoning cannot be disabled'");
        QwenVendor.Instance.Capabilities.Should().BeEquivalentTo(new ApiCapabilities(true, ["low", "medium", "xhigh"], true, "none"),
            "qwen3.8-max: low 4,096 / medium 16,384 / xhigh 262,144 with high and max mapped onto xhigh; none switches thinking off; effort and thinking_budget exclude each other");
        DeepSeekVendor.Instance.Capabilities.Should().BeEquivalentTo(new ApiCapabilities(true, ["low", "medium", "high"], false),
            "deepseek-v4-pro: a hybrid model with enable_thinking; low and medium behave as high");
        GlmVendor.Instance.Capabilities.Should().BeEquivalentTo(new ApiCapabilities(false, ["low", "high", "max"], false),
            "glm-5.3: 'supports only the thinking mode, which cannot be disabled'");
        OpenAiCompatibleVendor.Generic.Capabilities.Should().Be(ApiCapabilities.Undeclared, "nothing measured, nothing declared");
    }

    [Fact]
    public void Each_module_carries_what_calibration_settled_as_its_defaults()
    {
        QwenVendor.Instance.Defaults.Should().Be(new ApiDefaults("medium", true, 65536, 3, 20));
        DeepSeekVendor.Instance.Defaults.Should().Be(new ApiDefaults("high", true, 65536, 3, 20));
        GlmVendor.Instance.Defaults.Should().Be(new ApiDefaults("high", true, 65536, 3, 20));
        XaiVendor.Instance.Defaults.Should().Be(new ApiDefaults("medium", true, 8192, 3, 20),
            "grok-4.7 under the twenty-minute rule: one documented level below the vendor's high - js3 9.0 min, ts2 12.2 min");
        OpenAiCompatibleVendor.Generic.Defaults.Should().Be(ApiDefaults.Uncalibrated);
        foreach (var module in ApiVendors.All)
        {
            module.Defaults.ThinkingOn.Should().BeTrue($"{module.Name}: thinking off is never a calibrated default");
        }
    }

    [Fact]
    public void Each_module_names_the_price_list_its_models_are_looked_up_under()
    {
        XaiVendor.Instance.PriceRoute.Should().Be("xai");
        QwenVendor.Instance.PriceRoute.Should().Be("dashscope");
        DeepSeekVendor.Instance.PriceRoute.Should().Be("dashscope");
        GlmVendor.Instance.PriceRoute.Should().Be("dashscope");
        OpenAiCompatibleVendor.Generic.PriceRoute.Should().BeEmpty("the generic row has no list of its own; the lookup asks by model alone");
    }

    // ---------- validation: a row's settings the vendor does not take are refused with a sentence ----------

    [Fact]
    public void An_effort_the_vendor_does_not_take_is_refused_naming_the_levels_it_does()
    {
        XaiVendor.Instance.Refusal(new ApiRowSettings("ultra"))
            .Should().Be("xai does not take reasoning effort 'ultra' — it accepts low, medium, high, xhigh");
        QwenVendor.Instance.Refusal(new ApiRowSettings("high"))
            .Should().Be("qwen does not take reasoning effort 'high' — it accepts low, medium, xhigh (and 'none' switches thinking off)",
                "high is the vendor's alias of xhigh, not a level of its own — the dropdown offers what the module declares");
        GlmVendor.Instance.Refusal(new ApiRowSettings("medium")).Should().Contain("low, high, max");
    }

    [Fact]
    public void An_accepted_effort_the_engine_word_and_an_unset_effort_pass_on_every_module()
    {
        QwenVendor.Instance.Refusal(new ApiRowSettings("Medium")).Should().BeEmpty("the vendor's own level, in any case");
        QwenVendor.Instance.Refusal(new ApiRowSettings("none")).Should().BeEmpty("the thinking switch spelled as an effort");
        DeepSeekVendor.Instance.Refusal(new ApiRowSettings("medium")).Should().BeEmpty();
        OpenAiCompatibleVendor.Generic.Refusal(new ApiRowSettings("anything")).Should().BeEmpty("nothing declared, nothing refused — the vendor answers");
        foreach (var module in ApiVendors.All)
        {
            module.Refusal(new ApiRowSettings("engine")).Should().BeEmpty($"{module.Name}: 'engine' sends nothing");
            module.Refusal(ApiRowSettings.None).Should().BeEmpty($"{module.Name}: unset is the default");
        }
    }

    [Fact]
    public void Thinking_off_is_refused_by_a_module_whose_vendor_has_no_switch()
    {
        var off = new ApiRowSettings(Thinking: ThinkingSetting.Off);

        XaiVendor.Instance.Refusal(off).Should().Be("xai has no thinking switch — the vendor documents no way to turn reasoning off for this family");
        GlmVendor.Instance.Refusal(off).Should().Contain("glm has no thinking switch");
        OpenAiCompatibleVendor.Generic.Refusal(off).Should().Contain("no calibrated module spells a thinking switch on the 'openai' row");
        QwenVendor.Instance.Refusal(off).Should().BeEmpty();
        DeepSeekVendor.Instance.Refusal(off).Should().BeEmpty();
    }

    // ---------- the effective settings: row over environment over calibrated default ----------

    [Fact]
    public void The_row_wins_over_the_environment_which_wins_over_the_calibrated_default()
    {
        var qwen = QwenVendor.Instance;

        ApiEffective.Of(qwen, new ApiRowSettings("low", ThinkingSetting.Off, 12), new ApiOverrides("high", 8192, 20))
            .Should().Be(new ApiEffective("low", false, 8192, 3, 12), "the row set all three");
        ApiEffective.Of(qwen, ApiRowSettings.None, new ApiOverrides("high", 8192, 15))
            .Should().Be(new ApiEffective("high", true, 8192, 3, 15), "the environment set them, as every calibration run did");
        ApiEffective.Of(qwen, ApiRowSettings.None, ApiOverrides.None)
            .Should().Be(new ApiEffective("medium", true, 65536, 3, 20), "nothing set: the calibrated defaults");
        ApiEffective.Of(qwen, new ApiRowSettings(Thinking: ThinkingSetting.On), ApiOverrides.None).ThinkingOn.Should().BeTrue();
    }

    // ---------- the switch on the wire: what a row of the table cannot spell ----------

    [Fact]
    public void Qwen_spells_thinking_off_as_the_vendors_none_level_sent_verbatim()
    {
        var on = QwenVendor.Instance.RequestBody(Turn);
        var off = QwenVendor.Instance.RequestBody(Turn with { ThinkingOn = false });
        var asEffort = QwenVendor.Instance.RequestBody(Turn with { Effort = "none" });

        on.Should().Contain("\"reasoning_effort\":\"medium\"").And.NotContain("none");
        off.Should().Contain("\"reasoning_effort\":\"none\"").And.NotContain("medium",
            "the row's map would have omitted 'none'; the switch sends it to the wire");
        asEffort.Should().Be(off, "'none' as the effort IS the switch");
        off.Replace("\"reasoning_effort\":\"none\"", "\"reasoning_effort\":\"medium\"").Should().Be(on, "nothing else moves");
    }

    [Fact]
    public void DeepSeek_spells_thinking_off_as_enable_thinking_false_beside_the_standard_fields()
    {
        var on = DeepSeekVendor.Instance.RequestBody(Turn with { Effort = "high" });
        var off = DeepSeekVendor.Instance.RequestBody(Turn with { Effort = "high", ThinkingOn = false });

        on.Should().NotContain("enable_thinking");
        off.Should().Contain("\"enable_thinking\":false").And.Contain("\"reasoning_effort\":\"high\"");
        off.Replace("\"enable_thinking\":false,", string.Empty).Should().Be(on, "the switch is one field more, nothing else moves");
    }

    [Fact]
    public void A_module_without_a_switch_sends_the_measured_body_whatever_the_turn_says_about_thinking()
    {
        GlmVendor.Instance.RequestBody(Turn with { ThinkingOn = false }).Should().Be(GlmVendor.Instance.RequestBody(Turn));
        XaiVendor.Instance.RequestBody(Turn with { ThinkingOn = false }).Should().Be(XaiVendor.Instance.RequestBody(Turn));
        XaiVendor.Instance.RequestBody(Turn with { Effort = "" }).Should().NotContain("reasoning_effort", "no effort configured sends no field");
    }

    [Fact]
    public void The_three_Alibaba_modules_share_one_transport_and_send_one_body_for_one_turn()
    {
        var turn = Turn with { Effort = "high" };

        QwenVendor.Instance.RequestBody(turn).Should().Be(DeepSeekVendor.Instance.RequestBody(turn)).And.Be(GlmVendor.Instance.RequestBody(turn));
        QwenVendor.Instance.RequestBody(turn).Should().Contain("\"max_completion_tokens\":65536", "the row's floor: there the ceiling bounds the thinking too");
    }

    // ---------- headers and classification, per module ----------

    [Fact]
    public void Only_the_xai_module_sends_a_routing_header_and_only_with_a_conversation()
    {
        XaiVendor.Instance.Headers("c0ffee").Should().Equal(new Dictionary<string, string> { ["x-grok-conv-id"] = "c0ffee" });
        XaiVendor.Instance.Headers(string.Empty).Should().BeEmpty();
        QwenVendor.Instance.Headers("c0ffee").Should().BeEmpty("the implicit cache is content-addressed");
        OpenAiCompatibleVendor.Generic.Headers("c0ffee").Should().BeEmpty();
    }

    [Theory]
    [InlineData("xai")]
    [InlineData("qwen")]
    [InlineData("deepseek")]
    [InlineData("glm")]
    [InlineData("openai")]
    public void Every_module_classifies_a_status_the_same_way(string name)
    {
        var vendor = ApiVendors.All.Single(m => m.Name == name);

        vendor.Classify(200, "{}").Should().Be(ApiOutcome.Answered);
        vendor.Classify(401, "nope").Should().Be(ApiOutcome.KeyRefused);
        vendor.Classify(403, "nope").Should().Be(ApiOutcome.KeyRefused);
        vendor.Classify(400, """{"error":"Incorrect API key provided: sk-x"}""").Should().Be(ApiOutcome.KeyRefused, "xAI's 400 to a bad key");
        vendor.Classify(400, """{"error":"Argument not supported on this model: frequencyPenalty"}""").Should().Be(ApiOutcome.Failed);
        vendor.Classify(429, "slow down").Should().Be(ApiOutcome.RateLimited);
        vendor.Classify(503, "later").Should().Be(ApiOutcome.RateLimited);
        vendor.Classify(500, "boom").Should().Be(ApiOutcome.Failed);
    }

    [Fact]
    public void Every_module_reads_an_answer_through_the_one_reader()
    {
        const string response = """{"choices":[{"message":{"content":"{\"findings\":[]}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":10,"completion_tokens":5,"total_tokens":40,"completion_tokens_details":{"reasoning_tokens":25}}}""";
        foreach (var module in ApiVendors.All)
        {
            var name = module.Name;
            var answer = module.ReadAnswer(response);
            answer.Content.Should().Be("{\"findings\":[]}", name);
            answer.Usage.TokensOut.Should().Be(30, $"{name}: total − prompt when the vendor files reasoning outside the completion");
            answer.ReasoningTokens.Should().Be(25, name);
            answer.WasCut.Should().BeFalse(name);
        }
    }
}
