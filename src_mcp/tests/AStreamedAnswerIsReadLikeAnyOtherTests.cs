using CoaiMcp.Core.Api;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A streamed completion (<c>"stream": true</c>, server-sent events) assembled into the ONE answer shape the
/// non-streaming path reads (todo/PLAN_api_streaming.md, S4 and S5): <see cref="SseEvents"/> turns lines into
/// payloads by the SSE rules, <see cref="StreamAssembler"/> turns payloads into a completion that
/// <see cref="CompletionReader"/> reads unchanged — or into a failure, or a broken stream, never an answer it is not.
/// </summary>
/// <remarks>
/// The chunk shapes are the vendors' documented ones (research/RESULTS_api_streaming_vendors.md): usage in a last
/// <c>choices: []</c> chunk (OpenAI, Alibaba, Ollama), in every chunk (xAI), beside the <c>finish_reason</c> (Z.ai);
/// reasoning as <c>reasoning_content</c> (Alibaba, DeepSeek, Z.ai) or <c>reasoning</c> (OpenRouter, Ollama); keep-alive
/// comments (DeepSeek, OpenRouter); an error inside a 200 stream (OpenRouter). Synthetic until a live stream is recorded.
/// </remarks>
public sealed class AStreamedAnswerIsReadLikeAnyOtherTests
{
    private static StreamOutcome Assemble(string sse)
    {
        var events = new SseEvents();
        var assembler = new StreamAssembler();
        foreach (var line in sse.Split('\n'))
        {
            if (events.Add(line) is { } payload)
            {
                assembler.Add(payload);
            }
        }
        if (events.Finish() is { } last)
        {
            assembler.Add(last);
        }

        return assembler.Finish(events.Done);
    }

    private static string Chunk(string delta, string finish = "null") =>
        $"data: {{\"id\":\"c1\",\"model\":\"m\",\"choices\":[{{\"index\":0,\"delta\":{delta},\"finish_reason\":{finish}}}]}}\n\n";

    private const string UsageChunk =
        "data: {\"id\":\"c1\",\"choices\":[],\"usage\":{\"prompt_tokens\":120,\"completion_tokens\":45,\"total_tokens\":165,"
        + "\"completion_tokens_details\":{\"reasoning_tokens\":30}}}\n\n";

    [Fact]
    public void The_lines_that_are_not_data_are_skipped_and_DONE_ends_the_stream()
    {
        var sse = ": keep-alive\n\nevent: message\nid: 7\nretry: 100\n"
            + Chunk("{\"content\":\"Hel\"}") + "data:" + Chunk("{\"content\":\"lo\"}")[6..] + ": OPENROUTER PROCESSING\n\n"
            + Chunk("{}", "\"stop\"") + "data: [DONE]\n\n" + Chunk("{\"content\":\" after the end\"}");

        var outcome = Assemble(sse);

        outcome.End.Should().Be(StreamEnd.Answered);
        CompletionReader.Read(outcome.Completion).Content.Should().Be("Hello");
    }

    [Fact]
    public void Carriage_returns_are_line_ends_and_several_data_lines_are_one_payload()
    {
        var events = new SseEvents();

        events.Add("data: {\"a\":\r").Should().BeNull();
        events.Add("data: 1}\r").Should().BeNull();
        events.Add("\r").Should().Be("{\"a\":\n1}");
    }

    [Fact]
    public void The_answer_is_joined_and_the_reasoning_is_counted_apart_never_in_the_content()
    {
        var sse = Chunk("{\"role\":\"assistant\",\"reasoning_content\":\"think \"}") + Chunk("{\"reasoning\":\"more\"}")
            + Chunk("{\"content\":\"{\\\"findings\\\":\"}") + Chunk("{\"content\":\"[]}\"}", "\"stop\"") + UsageChunk + "data: [DONE]\n\n";

        var outcome = Assemble(sse);
        var answer = CompletionReader.Read(outcome.Completion);

        answer.Content.Should().Be("{\"findings\":[]}");
        outcome.ReasoningChars.Should().Be("think more".Length);
        outcome.ContentChars.Should().Be("{\"findings\":[]}".Length);
        answer.FinishReason.Should().Be("stop");
    }

    [Fact]
    public void Usage_is_read_from_a_last_empty_choices_chunk()
    {
        var answer = CompletionReader.Read(Assemble(Chunk("{\"content\":\"ok\"}", "\"stop\"") + UsageChunk + "data: [DONE]\n\n").Completion);

        answer.Usage.TokensIn.Should().Be(120);
        answer.Usage.TokensOut.Should().Be(45);
        answer.Usage.TokensReasoning.Should().Be(30);
        answer.Usage.NotCaptured.Should().BeFalse();
    }

    [Fact]
    public void Usage_in_every_chunk_is_the_last_one_and_usage_beside_the_finish_reason_counts()
    {
        var everyChunk = "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"a\"}}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":1}}\n\n"
            + "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"b\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":2}}\n\n"
            + "data: [DONE]\n\n";

        CompletionReader.Read(Assemble(everyChunk).Completion).Usage.TokensOut.Should().Be(2);
    }

    [Fact]
    public void An_error_inside_the_stream_is_a_failure_even_after_a_stop()
    {
        var sse = Chunk("{\"content\":\"partial\"}", "\"stop\"")
            + "data: {\"error\":{\"message\":\"Rate limit exceeded\",\"code\":429},\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"error\"}]}\n\n"
            + "data: [DONE]\n\n";

        var outcome = Assemble(sse);

        outcome.End.Should().Be(StreamEnd.Failed);
        outcome.ErrorText.Should().Contain("Rate limit exceeded");
    }

    [Theory]
    [InlineData("error")]
    [InlineData("network_error")]
    [InlineData("sensitive")]
    [InlineData("insufficient_system_resource")]
    [InlineData("aborted")]
    public void A_finish_reason_that_reports_a_failure_is_a_failure(string reason)
    {
        var outcome = Assemble(Chunk("{\"content\":\"part\"}") + Chunk("{}", $"\"{reason}\"") + "data: [DONE]\n\n");

        outcome.End.Should().Be(StreamEnd.Failed);
        outcome.ErrorText.Should().Contain(reason);
    }

    [Fact]
    public void A_stream_that_ended_with_a_finish_reason_but_no_DONE_and_no_usage_keeps_its_answer_and_an_unknown_usage()
    {
        var outcome = Assemble(Chunk("{\"content\":\"done\"}", "\"stop\""));
        var answer = CompletionReader.Read(outcome.Completion);

        outcome.End.Should().Be(StreamEnd.Answered);
        answer.Content.Should().Be("done");
        answer.Usage.NotCaptured.Should().BeTrue("a usage nobody reported is unknown, never zero");
    }

    [Fact]
    public void A_stream_that_ended_with_neither_DONE_nor_a_finish_reason_is_broken_never_an_answer()
    {
        var outcome = Assemble(Chunk("{\"content\":\"half an ans\"}"));

        outcome.End.Should().Be(StreamEnd.Broken);
        outcome.ContentChars.Should().Be("half an ans".Length);
    }

    [Fact]
    public void A_cut_at_the_ceiling_reads_as_the_cut_it_is()
    {
        var answer = CompletionReader.Read(Assemble(Chunk("{\"content\":\"{\\\"fi\"}", "\"length\"") + "data: [DONE]\n\n").Completion);

        answer.WasCut.Should().BeTrue();
    }

    [Fact]
    public void A_finish_reason_sent_twice_is_read_once_the_last_one_that_says_something()
    {
        var sse = Chunk("{\"content\":\"x\"}", "\"stop\"")
            + "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":null}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1}}\n\n"
            + "data: [DONE]\n\n";

        var outcome = Assemble(sse);

        outcome.End.Should().Be(StreamEnd.Answered);
        CompletionReader.Read(outcome.Completion).FinishReason.Should().Be("stop");
    }

    [Fact]
    public void A_chunk_that_is_not_a_json_object_is_passed_over_not_fatal()
    {
        var outcome = Assemble("data: not json\n\ndata: [1,2]\n\n" + Chunk("{\"content\":\"fine\"}", "\"stop\"") + "data: [DONE]\n\n");

        CompletionReader.Read(outcome.Completion).Content.Should().Be("fine");
    }
}
