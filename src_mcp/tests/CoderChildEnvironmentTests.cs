using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What reaches a coder's process (epoch 3 E2 s2 t3, d4): the minimal list of <see cref="ProcessEnvironment.Minimal"/>, the claude key when the row has one,
/// and nothing else — no <c>COAI_</c> name, no other vendor's key, no <c>CLAUDE_</c> name that steers the CLI.
/// </summary>
/// <remarks>
/// Observed on a REAL child (the fake CLI's <c>env-names</c> verb prints every variable it was started with), the way <see cref="ChildEnvironmentTests"/> observes a
/// question launch — a dictionary asserted in-process would be a test of the dictionary. The request is the one <c>ClaudeConsultant.Build</c> made for a coder launch,
/// with only the program and its arguments swapped for the fake's, so the environment under test is the product's own. In the <c>fakecli-env</c> collection for the
/// reason <see cref="ChildEnvironmentTests"/> gives: the canaries are variables of this very process.
/// </remarks>
[Collection("fakecli-env")]
public sealed class CoderChildEnvironmentTests
{
    private static readonly StringComparer Names = ProcessEnvironment.NameComparer;

    [Fact]
    public async Task ACoderProcess_SeesExactlyTheMinimalList_AndNothingTheParentConfigured()
    {
        using var coai = new Canary("COAI_CODER_CANARY");
        using var claudeSteering = new Canary("CLAUDE_CODE_CANARY");
        using var pluginDirs = new Canary("CLAUDE_CODE_PLUGIN_DIRS_CANARY");
        using var codexKey = new Canary("OPENAI_API_KEY_CANARY");
        using var otherKey = new Canary("GEMINI_API_KEY_CANARY");
        using var secret = new Canary("MY_SECRET_TOKEN");
        using var proxy = new Canary("HTTPS_PROXY_CANARY");

        var names = await NamesSeenByTheChildAsync(apiKey: string.Empty);

        names.Should().NotBeEmpty();
        string[] planted = [coai.Name, claudeSteering.Name, pluginDirs.Name, codexKey.Name, otherKey.Name, secret.Name, proxy.Name];
        names.Should().NotContain(planted, "none of the parent's own configuration or other vendors' keys reaches a coder");
        names.Should().NotContain(name => name.StartsWith("COAI_", StringComparison.Ordinal) || name.StartsWith("CLAUDE", StringComparison.Ordinal) || name.StartsWith("ANTHROPIC", StringComparison.Ordinal),
            "with no key on the row, no name of this product or of the CLI's own steering is passed");

        var expected = ProcessEnvironment.Minimal.Where(name => Environment.GetEnvironmentVariable(name) is not null).ToList();
        expected.Should().Contain(name => Names.Equals(name, "PATH"), "the CLI is found through PATH, so the list must not be empty of it");
        names.Except(ChildEnvironmentTests.SetByTheChildItself, Names).Should().BeEquivalentTo(expected,
            "exactly the names of the minimal list that the parent has — the list and nothing else, and every one of them");
    }

    [Fact]
    public async Task TheRowsOwnClaudeKey_IsTheOnlyAddition()
    {
        var names = await NamesSeenByTheChildAsync(apiKey: "k-0123456789abcdef");

        var extra = names.Except(ChildEnvironmentTests.SetByTheChildItself, Names).Where(name => !ProcessEnvironment.Minimal.Contains(name)).ToList();
        extra.Should().Equal("ANTHROPIC_API_KEY");
    }

    [Fact]
    public void TheCoderRequest_AsksForTheMinimalList_AndDoesNotInherit()
    {
        var request = BuiltRequest(string.Empty);

        request.InheritsEnvironment.Should().BeFalse();
        request.Passthrough.Should().BeEquivalentTo(ProcessEnvironment.Minimal);
        request.Environment.Should().BeEmpty("no key on the row: nothing is handed over");
    }

    [Fact]
    public void TheMinimalList_CarriesNoName_ThatSteersClaudeOrReachesAnotherVendor()
    {
        foreach (var isWindows in (bool[])[true, false])
        {
            ProcessEnvironment.MinimalFor(isWindows).Should().NotContain(name =>
                name.StartsWith("COAI_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("ANTHROPIC", StringComparison.OrdinalIgnoreCase)
                || name.Contains("API_KEY", StringComparison.OrdinalIgnoreCase)
                || name.Contains("TOKEN", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static ProcessRequest BuiltRequest(string apiKey)
    {
        var worktree = Path.Combine(Path.GetFullPath("coder-root"), CoderWorktree.FolderOf("job-env"));
        var launch = new ConsultantLaunch(Path.GetFullPath("main-checkout"), "go", string.Empty, Path.GetFullPath("answers"),
            new ReviewerSettings("claude") { ClaudeCli = ClaudeCapability.WithRestricted, ApiKey = apiKey })
        {
            Confinement = new LaunchConfinement.Coder("job-env", worktree),
        };

        return new ClaudeConsultant(new ClaudeRuntime()).Build(launch).Request;
    }

    private static async Task<List<string>> NamesSeenByTheChildAsync(string apiKey)
    {
        var built = BuiltRequest(apiKey);
        var request = built with
        {
            Executable = FakeCliInvocations.Exe,
            Arguments = ["env-names"],
            WorkingDirectory = AppContext.BaseDirectory,
            StdIn = string.Empty,
            Timeout = TimeSpan.FromMinutes(1),
        };

        var result = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0, "a child must still START on the minimal list alone; it said: {0}", result.StdErr);

        return [.. result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r'))];
    }

    /// <summary>A variable that exists in THIS process for one test and is gone when the test is.</summary>
    private sealed class Canary : IDisposable
    {
        public string Name { get; }

        public Canary(string prefix)
        {
            Name = $"{prefix}_{Guid.NewGuid():N}";
            Environment.SetEnvironmentVariable(Name, "the parent's own configuration");
        }

        public void Dispose() => Environment.SetEnvironmentVariable(Name, null);
    }
}
