using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A prompt file a launch wrote for its child is gone once the launch's turn ends — answered, failed or timed out
/// (todo/PLAN_one_model_catalog.md, epic 2: "a prompt file is deleted in finally").
/// </summary>
/// <remarks>
/// An api, local or Team server reviewer reads its prompt from a <c>.prompt</c> file in the round's answers directory,
/// and that directory was swept only after six hours. The prompt holds the change under review, and from epic 2 the
/// person's own system prompt for the row: neither should sit on disk after the reviewer that needed it is done.
/// </remarks>
public sealed class APromptFileDoesNotOutliveItsLaunchTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-prompt-life-").FullName;
    private readonly ReviewerExecutor _executor = new(new ProcessLauncher());

    public static TheoryData<string, string[], int> Endings => new()
    {
        { "answered", ["emit", """{"findings":[]}"""], 60 },
        { "failed", ["stderr-exit", "the vendor said no", "3"], 60 },
        { "timed out", ["sleep", "30000"], 1 },
    };

    [Theory]
    [MemberData(nameof(Endings))]
    public async Task ThePromptFileIsDeleted_HoweverTheLaunchEnded(string ending, string[] verb, int seconds)
    {
        var prompt = Path.Combine(_dir, $"{ending.Replace(' ', '-')}.prompt");
        await File.WriteAllTextAsync(prompt, "the change, and the person's own instruction", TestContext.Current.CancellationToken);
        var invocation = FakeCliInvocations.Invoke("vendor", verb, TimeSpan.FromSeconds(seconds)) with { TempFiles = [prompt] };

        await new BoundedScheduler().RunAllAsync([new ReviewerWork(invocation)], _executor, TestContext.Current.CancellationToken);

        File.Exists(prompt).Should().BeFalse($"a launch that {ending} has no further use for its prompt file");
    }

    [Fact]
    public async Task AFileAlreadyGone_IsNotAFailure()
    {
        var invocation = FakeCliInvocations.Invoke("vendor", ["emit", """{"findings":[]}"""]) with { TempFiles = [Path.Combine(_dir, "never-written.prompt")] };

        var results = await new BoundedScheduler().RunAllAsync([new ReviewerWork(invocation)], _executor, TestContext.Current.CancellationToken);

        results.Should().ContainSingle().Which.Item2.Should().BeOfType<ReviewerOutcome.Ok>();
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
