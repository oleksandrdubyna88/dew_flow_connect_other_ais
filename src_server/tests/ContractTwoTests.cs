using System.Net;
using System.Net.Http.Json;
using CoaiServer;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// Contract 2 (todo/PLAN_one_model_catalog.md, epic 2, story 5): a review request may carry the row's effort and the
/// person's system prompt, and the server decides what it TAKES — and says what it did not.
/// </summary>
/// <remarks>
/// <para>The client composes the whole prompt and the server relays it, so taking a system prompt means placing it
/// before the prompt's own finding contract, which stays last. It is taken only when the operator allows it (off by
/// default), is held in memory only — the job store keeps nothing on disk — and its record carries a hash, never the
/// text.</para>
/// <para>Effort is applied where the vendor's runtime lists levels (claude), clamped to the operator's cap, and dropped
/// with its reason where it is unmeasured (codex).</para>
/// </remarks>
[Collection(ServerCollection.Name)]
public sealed class ContractTwoTests
{
    private const string Vendors = """
        [{ "id": "claude", "runtime": "claude", "models": ["opus"], "slots": ["a"] },
         { "id": "codex", "runtime": "codex", "models": ["gpt-5.6-luna"], "slots": ["b"] }]
        """;

    private const string Prompt = "You are an independent reviewer.\n\n## The finding contract\n\nReturn ONLY a JSON object.";

    private const string Instruction = "Cite every line you rely on.";

    private static TeamServer Server(IDictionary<string, string?>? settings = null)
    {
        var server = new TeamServer(settings);
        File.WriteAllText(Path.Combine(server.DataDir, "vendors.json"), Vendors);

        return server;
    }

    private static ReviewRequestDto Request(string vendor = "claude", string model = "opus", string prompt = Prompt) =>
        new(vendor, model, prompt, "Architecture", 60);

    private static async Task<(ReviewAcceptedDto Accepted, JobRecord Job, HttpResponseMessage Response)> Submit(TeamServer server, ReviewRequestDto request)
    {
        var response = await server.ClientFor($"dev@{TeamServer.Domain}").PostAsJsonAsync("/api/reviews", request);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var accepted = (await response.Content.ReadFromJsonAsync<ReviewAcceptedDto>())!;
        var job = server.Services.GetRequiredService<JobStore>().Find(accepted.Id, $"dev@{TeamServer.Domain}")!;

        return (accepted, job, response);
    }

    [Fact]
    public async Task TheServer_SpeaksContractTwo()
    {
        using var server = Server();

        var (_, _, response) = await Submit(server, Request());

        response.Headers.GetValues(ContractVersion.Header).Should().ContainSingle().Which.Should().Be("2");
    }

    [Fact]
    public async Task ClaudesEffort_IsApplied_AndCodexsIsDroppedWithItsReason()
    {
        using var server = Server();

        var (claude, claudeJob, _) = await Submit(server, Request() with { Effort = "high" });
        var (codex, codexJob, _) = await Submit(server, Request("codex", "gpt-5.6-luna") with { Effort = "high" });

        claudeJob.Effort.Should().Be("high");
        claude.Dropped.Should().BeNullOrEmpty();
        codexJob.Effort.Should().BeEmpty("codex is unmeasured — nobody has shown which levels it takes");
        codex.Dropped.Should().ContainSingle(note => note.Field == "effort").Which.Reason.Should().Contain("codex");
    }

    [Fact]
    public async Task AnEffortPastTheOperatorsCap_IsClamped_AndSaid()
    {
        using var server = Server(new Dictionary<string, string?> { ["Coai__MaxEffort"] = "medium" });

        var (accepted, job, _) = await Submit(server, Request() with { Effort = "max" });

        job.Effort.Should().Be("medium");
        accepted.Clamped.Should().ContainSingle(note => note.Field == "effort").Which.Reason.Should().Contain("medium");
    }

    [Fact]
    public async Task ASystemPrompt_IsDropped_WhileTheOperatorSwitchIsOff()
    {
        using var server = Server();

        var (accepted, job, _) = await Submit(server, Request() with { SystemPrompt = Instruction });

        job.Prompt.Should().NotContain(Instruction);
        job.SystemPromptSha.Should().BeEmpty();
        accepted.Dropped.Should().ContainSingle(note => note.Field == "systemPrompt").Which.Reason.Should().Contain("Coai:AcceptClientSystemPrompt");
    }

    [Fact]
    public async Task WithTheSwitchOn_ItGoesBeforeTheContract_AndTheRecordKeepsOnlyItsHash()
    {
        using var server = Server(new Dictionary<string, string?> { ["Coai__AcceptClientSystemPrompt"] = "true" });

        var (accepted, job, _) = await Submit(server, Request() with { SystemPrompt = Instruction });

        accepted.Dropped.Should().BeNullOrEmpty();
        job.Prompt.IndexOf(Instruction, StringComparison.Ordinal).Should().BeGreaterThan(0)
            .And.BeLessThan(job.Prompt.IndexOf("## The finding contract", StringComparison.Ordinal), "the contract stays last");
        job.SystemPromptSha.Should().HaveLength(64).And.NotContain(Instruction);
    }

    [Fact]
    public async Task APromptWithNoContractHeading_OrAPromptPastTheLimit_HasTheFieldDropped()
    {
        using var server = Server(new Dictionary<string, string?> { ["Coai__AcceptClientSystemPrompt"] = "true" });

        var (headless, headlessJob, _) = await Submit(server, Request(prompt: "review this, no contract here") with { SystemPrompt = Instruction });
        var (tooLong, _, _) = await Submit(server, Request() with { SystemPrompt = new string('x', 8193) });

        headlessJob.Prompt.Should().NotContain(Instruction, "never guessed at");
        headless.Dropped.Should().ContainSingle(note => note.Field == "systemPrompt").Which.Reason.Should().Contain("finding contract");
        tooLong.Dropped.Should().ContainSingle(note => note.Field == "systemPrompt").Which.Reason.Should().Contain("8192");
    }
}
