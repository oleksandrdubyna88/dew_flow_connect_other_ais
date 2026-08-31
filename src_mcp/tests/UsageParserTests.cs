using Xunit;
using CoaiMcp.Core.Findings;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// The usage parser against the envelopes the vendor CLIs actually print. Every payload here was
/// captured from a real run on 2026-08-31, not imagined — the whole point of the schema-less scan
/// is that it survives shapes we did not design.
/// </summary>
public sealed class UsageParserTests
{
    /// <summary>Trimmed from a real `claude -p --output-format json` reply.</summary>
    private const string ClaudeEnvelope = """
    {"type":"result","subtype":"success","is_error":false,"duration_ms":1316,"num_turns":1,
     "result":"pong","total_cost_usd":0.048924999999999996,
     "usage":{"input_tokens":10,"cache_creation_input_tokens":24054,"cache_read_input_tokens":0,
              "output_tokens":44,"service_tier":"standard"},
     "modelUsage":{"claude-haiku-4-5-20251001":{"inputTokens":532,"outputTokens":57,"costUSD":0.048924999999999996}}}
    """;

    [Fact]
    public void Claude_ReportsTokensAndItsOwnPrice()
    {
        var usage = UsageParser.Parse(ClaudeEnvelope);

        usage.TokensIn.Should().Be(24064, "the cache tokens are billed and are NOT inside input_tokens");
        usage.TokensOut.Should().Be(44);
        usage.CostUsd.Should().BeApproximately(0.048925, 0.000001, "claude prices its own run");
    }

    [Fact]
    public void Codex_StreamedEvents_CountedOnce_AndItsSubtotalsNotDoubleBilled()
    {
        // Captured verbatim from `codex exec --json -m gpt-5.6-terra` on 2026-08-31. Two traps
        // live in this payload: the event stream repeats a growing count (summing would
        // multiply-bill the same tokens), and `cached_input_tokens` / `reasoning_output_tokens`
        // are SUBSETS of the two totals beside them — adding them would bill 24k for a 14k call.
        var events = string.Join('\n',
            """{"type":"thread.started","thread_id":"01a05910-5929-70b2-bf33-aa6a075f745d"}""",
            """{"type":"turn.started"}""",
            """{"type":"token_count","usage":{"input_tokens":9000,"cached_input_tokens":0,"output_tokens":2,"reasoning_output_tokens":0}}""",
            """{"type":"item.completed","item":{"id":"item_0","type":"agent_message","text":"pong"}}""",
            """{"type":"turn.completed","usage":{"input_tokens":14149,"cached_input_tokens":9984,"cache_write_input_tokens":0,"output_tokens":5,"reasoning_output_tokens":0}}""");

        var usage = UsageParser.Parse(events);

        usage.TokensIn.Should().Be(14149);
        usage.TokensOut.Should().Be(5);
        usage.CostUsd.Should().BeNull("codex does not price the run, and inventing a price is worse than none");
    }

    [Fact]
    public void Gemini_StatsEnvelope_IsReadThroughItsOwnNames()
    {
        var envelope = """
        {"response":"{\"findings\":[]}",
         "stats":{"models":{"gemini-flash-latest":{"tokens":{"prompt":8123,"candidates":410,"total":8533}}}}}
        """;

        var usage = UsageParser.Parse(envelope);

        usage.TokensIn.Should().Be(8123);
        usage.TokensOut.Should().Be(410);
    }

    [Fact]
    public void NoJsonAtAll_IsZero_NotAGuess()
    {
        var usage = UsageParser.Parse("the model wrote prose and no numbers");

        usage.Should().Be(Usage.None);
    }

    [Fact]
    public void BareWordsOutsideAUsageBlock_AreIgnored()
    {
        // "prompt" and "output" are far too common to trust anywhere but under tokens/usage.
        var usage = UsageParser.Parse("""{"config":{"prompt":42},"limits":{"output":7}}""");

        usage.Should().Be(Usage.None);
    }

    [Fact]
    public void TwoLaunches_Add_BecauseBothWereBilled()
    {
        var first = UsageParser.Parse("""{"usage":{"input_tokens":100,"output_tokens":10},"total_cost_usd":0.01}""");
        var second = UsageParser.Parse("""{"usage":{"input_tokens":200,"output_tokens":20},"total_cost_usd":0.02}""");

        var total = first.Add(second);

        total.TokensIn.Should().Be(300);
        total.TokensOut.Should().Be(30);
        total.CostUsd.Should().BeApproximately(0.03, 0.000001);
    }

    [Fact]
    public void OnePricedVendorAndOneSilentOne_StillReportsTheMoneyThatWasSpent()
    {
        var priced = new Usage(100, 10, 0.05);
        var silent = new Usage(900, 90, null);

        priced.Add(silent).CostUsd.Should().BeApproximately(0.05, 0.000001,
            "a silent vendor is unknown, not free — the known spend is still shown");
    }
}
