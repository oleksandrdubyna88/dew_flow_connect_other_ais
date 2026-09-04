using Xunit;
using FluentAssertions;
using CoaiBench.Running;

namespace CoaiBench.Tests;

/// <summary>
/// A run starts from a clean session, or it measures the previous campaign.
/// </summary>
/// <remarks>
/// It cost a whole run to learn: a campaign wrote into a directory an earlier one had used, `open`
/// handed back the old session with its stored configuration, and every round came out on the
/// DEFAULT rounds and thresholds while the operator's settings said otherwise. The numbers looked
/// like a product problem and were a bench problem.
/// </remarks>
public sealed class SessionsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-sessions-reset-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Write(string name, string repoPath, string branch)
    {
        var sessions = Path.Combine(_dir, "sessions");
        Directory.CreateDirectory(sessions);
        var file = Path.Combine(sessions, name);
        File.WriteAllText(file, $$"""
            { "state": { "repoPath": "{{repoPath.Replace("\\", "\\\\")}}", "branch": "{{branch}}" },
              "rounds": [], "pending": [] }
            """);

        return file;
    }

    [Fact]
    public void TheSessionForThisRepoAndBranch_IsRemoved()
    {
        var file = Write("session-aaa.json", "D:/repo", "abc123");

        Sessions.Reset(_dir, "D:/repo", "abc123").Should().BeTrue();
        File.Exists(file).Should().BeFalse();
    }

    [Fact]
    public void AnotherBranchIsLeftAlone()
    {
        var mine = Write("session-aaa.json", "D:/repo", "abc123");
        var theirs = Write("session-bbb.json", "D:/repo", "feat/somebody-else");

        Sessions.Reset(_dir, "D:/repo", "abc123");

        File.Exists(mine).Should().BeFalse();
        File.Exists(theirs).Should().BeTrue("the real data directory is shared with the person's own work");
    }

    [Fact]
    public void AnotherRepositoryIsLeftAlone()
    {
        var theirs = Write("session-bbb.json", "D:/other", "abc123");

        Sessions.Reset(_dir, "D:/repo", "abc123");

        File.Exists(theirs).Should().BeTrue();
    }

    [Fact]
    public void SeparatorsAndCaseAreNotADifferentRepository()
    {
        // The session key is canonicalised by the server; a bench that compares raw strings would
        // silently fail to reset and measure the previous campaign again.
        var file = Write("session-aaa.json", @"D:\Repo", "abc123");

        Sessions.Reset(_dir, "d:/repo", "abc123").Should().BeTrue();
        File.Exists(file).Should().BeFalse();
    }

    [Fact]
    public void NothingToReset_IsNotAFailure() =>
        Sessions.Reset(_dir, "D:/repo", "abc123").Should().BeFalse();

    [Fact]
    public void AFileThatDoesNotParse_IsLeftWhereItIs()
    {
        var sessions = Path.Combine(_dir, "sessions");
        Directory.CreateDirectory(sessions);
        var file = Path.Combine(sessions, "session-torn.json");
        File.WriteAllText(file, "{ not json");

        Sessions.Reset(_dir, "D:/repo", "abc123");

        File.Exists(file).Should().BeTrue("a file we cannot read is not a file we may delete");
    }
}
