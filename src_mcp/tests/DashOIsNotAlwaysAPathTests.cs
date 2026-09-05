using Xunit;
using FluentAssertions;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Tests;

/// <summary>
/// `-o` means two different things to two vendors, and the stand-in believed only one of them.
/// </summary>
/// <remarks>
/// <para>Codex takes <c>-o &lt;absolute path&gt;.json</c> — where to WRITE the answer. Gemini takes
/// <c>-o json</c> — what FORMAT to answer in. The fake CLI read every <c>-o</c> as codex's, so a
/// gemini-shaped launch made it write a file literally called <c>json</c>, in its working
/// directory.</para>
/// <para>Every reviewer of a round shares that directory. Three gemini reviewers therefore opened
/// one file at once, and on Windows — where a share mode is enforced rather than advisory — the
/// losers died. It cost two release attempts, reported as
/// <c>gemini/SecurityReliability: exit -532462766</c> with a stack tail that named neither the
/// exception nor the path, and it never reproduced on a developer machine.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class DashOIsNotAlwaysAPathTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-dasho-").FullName;
    private readonly ProcessLauncher _launcher = new();

    public DashOIsNotAlwaysAPathTests()
    {
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", """{"findings": []}""");
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", """{"findings": []}""");
    }

    public void Dispose()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_OUTFILE_TEXT", "FAKECLI_STDOUT"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>The gemini argv, in the shape `GeminiRuntime` actually builds it.</summary>
    private ProcessRequest Gemini() =>
        new(FakeCliInvocations.Exe, ["-o", "json", "--approval-mode", "plan"], _dir)
        {
            StdIn = "the prompt",
            Timeout = TimeSpan.FromSeconds(30),
        };

    [Fact]
    public async Task AFormatNameIsNotAFileName()
    {
        await _launcher.RunAsync(Gemini(), TestContext.Current.CancellationToken);

        File.Exists(Path.Combine(_dir, "json")).Should().BeFalse(
            "`-o json` asks for a format; only a rooted path asks for a file");
    }

    [Fact]
    public async Task ThreeReviewersShareAWorkingDirectoryAndDoNotFightOverIt()
    {
        // The failure exactly: one round, one working directory, three gemini reviewers launched
        // together. On Windows the losers of a file race die rather than queue.
        var runs = await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => _launcher.RunAsync(Gemini(), TestContext.Current.CancellationToken)));

        runs.Should().AllSatisfy(r => r.ExitCode.Should().Be(0, "a reviewer must not die on its neighbour"));
    }

    [Fact]
    public async Task ARootedPathIsStillWrittenToTheCodexWay()
    {
        // The behaviour this must not break: codex's `-o` is a real destination and the answer
        // arrives there rather than on stdout.
        var answer = Path.Combine(_dir, "codex-Architecture.json");
        await _launcher.RunAsync(
            new ProcessRequest(FakeCliInvocations.Exe, ["exec", "-o", answer, "-"], _dir)
            {
                StdIn = "the prompt",
                Timeout = TimeSpan.FromSeconds(30),
            },
            TestContext.Current.CancellationToken);

        File.Exists(answer).Should().BeTrue();
        (await File.ReadAllTextAsync(answer, TestContext.Current.CancellationToken))
            .Should().Contain("findings");
    }
}
