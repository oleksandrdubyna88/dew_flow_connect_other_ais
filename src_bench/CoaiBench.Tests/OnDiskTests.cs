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

    private void Session(string json)
    {
        var sessions = Path.Combine(_dir, "sessions");
        Directory.CreateDirectory(sessions);
        File.WriteAllText(Path.Combine(sessions, "session-abc.json"), json);
    }

    [Fact]
    public void AFinishedRoundWithPendingFindings_IsResolvable()
    {
        Session("""
            { "rounds": [ { "status": "done", "verdict": "proceed" } ],
              "pending": [ { "title": "one" }, { "title": "two" } ] }
            """);

        var read = OnDisk.Read(_dir);

        read.Resolvable.Should().BeTrue();
        read.Pending.Should().Be(2);
    }

    [Fact]
    public void ARoundStillRunning_IsNot()
    {
        // The exact shape of the afternoon's defect: the answer was handed over, the record was not.
        Session("""{ "rounds": [ { "status": "running" } ], "pending": [] }""");

        OnDisk.Read(_dir).Resolvable.Should().BeFalse("its findings cannot be indexed into anything");
    }

    [Fact]
    public void AFinishedRoundWithNothingPending_IsNotResolvableEither()
    {
        Session("""{ "rounds": [ { "status": "done" } ], "pending": [] }""");

        OnDisk.Read(_dir).Resolvable.Should().BeFalse();
    }

    [Fact]
    public void NoSessionsDirectory_SaysSoRatherThanPassing()
    {
        var read = OnDisk.Read(_dir);

        read.Resolvable.Should().BeFalse();
        read.Note.Should().Contain("wrote nothing");
    }

    [Fact]
    public void ATornFileIsNamed_NotSilentlyCountedAsZero()
    {
        Session("{ this is not json");

        var read = OnDisk.Read(_dir);

        read.Resolvable.Should().BeFalse();
        read.Note.Should().Contain("does not parse");
    }

    [Fact]
    public void ItReadsWhileTheServerIsWriting()
    {
        // A reader that forbids writing is what killed six rounds here in one afternoon. The bench
        // must not reintroduce it from the outside: this holds the file the way a server does.
        Session("""{ "rounds": [ { "status": "done" } ], "pending": [ { "title": "one" } ] }""");
        var file = Directory.EnumerateFiles(Path.Combine(_dir, "sessions")).Single();

        using var held = new FileStream(
            file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        OnDisk.Read(_dir).Resolvable.Should().BeTrue();
    }
}
