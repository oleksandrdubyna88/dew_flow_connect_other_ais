using Xunit;
using FluentAssertions;
using CoaiBench.Running;

namespace CoaiBench.Tests;

/// <summary>
/// What the session file says, which is not what the answer said.
/// </summary>
/// <remarks>
/// This check exists because its absence was believed for an afternoon. A round came back with
/// twelve findings, numbered, and an instruction to resolve them — while its record had never been
/// written, so the session still said `running`, `pending` was empty, and every index pointed into a
/// list nobody had made. The answer looked perfect. Only the disk knew, and nobody was reading it.
/// </remarks>
public sealed class OnDiskTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-ondisk-").FullName;

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

    private const string Repo = "D:/repo";
    private const string Branch = "abc123";

    /// <summary>A session for THIS run — the state block is what makes it this run's rather than a neighbour's.</summary>
    private void Session(string body)
    {
        var sessions = Path.Combine(_dir, "sessions");
        Directory.CreateDirectory(sessions);
        File.WriteAllText(
            Path.Combine(sessions, "session-abc.json"),
            $$"""{ "state": { "repoPath": "{{Repo}}", "branch": "{{Branch}}" }, {{body}} }""");
    }

    private SessionOnDisk Read() => OnDisk.Read(_dir, Repo, Branch);

    [Fact]
    public void AFinishedSessionWithNothingLeftPending_IsClean()
    {
        // What a run leaves behind when it worked: the bench resolved every finding straight after
        // each stage, so `pending` is EMPTY. The first definition demanded pending > 0 and was
        // therefore false for every run that had done its job — and true, yesterday, only because
        // it was reading a neighbour's forty findings out of the shared directory.
        Session(""" "rounds": [ { "status": "done", "verdict": "proceed" } ], "pending": [] """);

        var read = Read();

        read.Clean.Should().BeTrue();
        read.Pending.Should().Be(0, "informational — what the bench left unresolved, which should be nothing");
    }

    [Fact]
    public void ARoundStillRunning_IsNotClean()
    {
        // The exact shape of the afternoon's defect: the answer was handed over, the record was not.
        Session(""" "rounds": [ { "status": "running" } ], "pending": [] """);

        Read().Clean.Should().BeFalse("a round the server still calls running was never finished on disk");
    }

    [Fact]
    public void NoSessionsDirectory_SaysSoRatherThanPassing()
    {
        var read = Read();

        read.Clean.Should().BeFalse();
        read.Note.Should().Contain("wrote nothing");
    }

    [Fact]
    public void ATornFileIsNamed_NotSilentlyCountedAsZero()
    {
        Session(""" "rounds": [ """);

        var read = Read();

        read.Clean.Should().BeFalse();
        read.Note.Should().Contain("does not parse");
    }

    [Fact]
    public void ItReadsWhileTheServerIsWriting()
    {
        // A reader that forbids writing is what killed six rounds here in one afternoon. The bench
        // must not reintroduce it from the outside: this holds the file the way a server does.
        Session(""" "rounds": [ { "status": "done" } ], "pending": [ { "title": "one" } ] """);
        var file = Directory.EnumerateFiles(Path.Combine(_dir, "sessions")).Single();

        using var held = new FileStream(
            file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        Read().Clean.Should().BeTrue();
    }
}
