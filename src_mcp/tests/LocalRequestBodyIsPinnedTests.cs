using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The local completion body, byte for byte — pinned BEFORE it was refactored onto the dialect table.
/// </summary>
/// <remarks>
/// <para>Story S1.2 of <c>PLAN_feature_review.md</c> moves <c>LocalAsk.RequestBody</c> onto
/// <c>ChatRequest.Body(dialect, …)</c>, which reads <c>shared/api-dialects.json</c>. Every decision in the
/// local body was MEASURED against a real engine — the sampling in the request, the frequency penalty,
/// the schema demanded rather than requested, the bounded free text — and a refactor that changed one
/// byte of it would silently re-run those measurements on every local round. So the whole body is
/// written down here first, and the refactor is held to it.</para>
/// <para>The expected text is what the pre-refactor code produced on 2026-09-25 for these exact inputs,
/// captured by running this test once with a placeholder and copying the observed bytes in. It is a
/// LITERAL on purpose: a golden that recomputes its expectation through the code under test pins
/// nothing.</para>
/// </remarks>
public sealed class LocalRequestBodyIsPinnedTests
{
    private const string Schema =
        """{"type":"object","properties":{"findings":{"type":"array","items":{"type":"object","properties":{"title":{"type":"string","description":"one line"},"why":{"type":"string"},"severity":{"type":"string"}}}}}}""";

    private const string Prompt = "Review this plan.\nIt has \"quotes\", `ticks` and a <tag> & an apostrophe's.";

    [Fact]
    public void TheLocalBody_IsByteIdentical_ToWhatShippedBeforeTheDialectTable()
    {
        var body = LocalAsk.RequestBody("qwen3:32b", Prompt, Schema, seed: 4242, reasoningEffort: "none", maxTokens: 8192);

        body.Should().Be(Golden.WithEffort);
    }

    [Fact]
    public void WithNoEffortAndNoCeiling_TheFieldsAreAbsent_NotNull()
    {
        var body = LocalAsk.RequestBody("qwen3:32b", Prompt, Schema, seed: 4242, reasoningEffort: "", maxTokens: 0);

        body.Should().Be(Golden.Bare);
    }

    [Fact]
    public void Engine_SendsNoEffortField_WhateverItsCase()
    {
        LocalAsk.RequestBody("qwen3:32b", Prompt, Schema, 4242, "Engine", 0).Should().Be(Golden.Bare);
    }

    /// <summary>The observed bytes. Do not regenerate these from the code under test.</summary>
    private static class Golden
    {
        /// <summary>The six-character escape the writer emits for one character — `\` `u` and four hex digits.</summary>
        /// <remarks>
        /// Built from the code point rather than spelled, because a `\u` escape written into a source
        /// file in this repository has reached disk as the raw character twice before (and did so again
        /// while this test was being written). A backslash made from its code point cannot be unescaped
        /// by anything that reads the file.
        /// </remarks>
        private static string U(char c) => (char)92 + "u" + ((int)c).ToString("X4");

        private static readonly string NewLine = (char)92 + "n";

        private static readonly string Content =
            "Review this plan." + NewLine + "It has " + U('"') + "quotes" + U('"') + ", " + U('`') + "ticks" + U('`')
            + " and a " + U('<') + "tag" + U('>') + " " + U('&') + " an apostrophe" + U('\'') + "s.";

        private const string Tail =
            "\"messages\":[{\"role\":\"user\",\"content\":\"{CONTENT}\"}],\"response_format\":{\"type\":\"json_schema\",\"json_schema\":{\"name\":\"findings\",\"strict\":true,\"schema\":{\"type\":\"object\",\"properties\":{\"findings\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"title\":{\"type\":\"string\",\"description\":\"one line (at most 200 characters)\",\"maxLength\":200},\"why\":{\"type\":\"string\",\"maxLength\":1000,\"description\":\"(at most 1000 characters)\"},\"severity\":{\"type\":\"string\"}}},\"maxItems\":10}}}}}}";

        public static readonly string WithEffort =
            "{\"model\":\"qwen3:32b\",\"stream\":false,\"temperature\":0,\"seed\":4242,\"frequency_penalty\":0.2,\"max_tokens\":8192,\"reasoning_effort\":\"none\","
            + Tail.Replace("{CONTENT}", Content);

        public static readonly string Bare =
            "{\"model\":\"qwen3:32b\",\"stream\":false,\"temperature\":0,\"seed\":4242,\"frequency_penalty\":0.2,"
            + Tail.Replace("{CONTENT}", Content);
    }
}
