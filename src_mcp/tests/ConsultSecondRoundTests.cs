using CoaiMcp.Core.Consultation;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>What story 1's SECOND code round found — the fixes nobody could reach without a vendor
/// that does not exist yet, and the one race the pid check does not cover.</summary>
public sealed class ConsultSecondRoundTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-consult-round2-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    private ConsultationRecord Record(string status, string memory, DateTime updated, string repo = "D:/repo") =>
        new(ConsultationStore.NewId(), "caller-1", CallerIdentity.Claude, "no-session", repo, "main", "0123abc",
            "codex", "gpt-5.6", "codex", memory, 5, ConsultationStore.Stamp(DateTime.UtcNow))
        {
            Status = status,
            UpdatedUtc = ConsultationStore.Stamp(updated),
            RunnerPid = Environment.ProcessId,
        };

    [Fact]
    public void TheMemoryModeIsReadFromTheRECORD_NotFromWhateverTheBuildDeclaresNow()
    {
        // A server upgrade that changed an adapter's memory mode would otherwise make an OPEN
        // consultation stop carrying its transcript — or start carrying one to a vendor that holds it.
        Record(ConsultationStatuses.Open, ConsultationMemories.WeRemember, DateTime.UtcNow)
            .WeCarryTheConversation.Should().BeTrue();
        Record(ConsultationStatuses.Open, ConsultationMemories.VendorRemembers, DateTime.UtcNow)
            .WeCarryTheConversation.Should().BeFalse();
    }

    [Fact]
    public async Task TheSweepLeavesAnIdleRecordALONE_WhileSomebodyHoldsThatRepositorysLock()
    {
        // The one race the pid check does not cover: another server can hold the lock, having READ an
        // open record, while this sweep closes it and clears its handle — after which that server's
        // write resurrects a consultation the sweep had ended.
        var store = new ConsultationStore(_data);
        var record = Record(ConsultationStatuses.Open, ConsultationMemories.VendorRemembers, DateTime.UtcNow.AddHours(-2));
        store.Write(record with { Handle = "0198-held" });

        using (var held = await RepositoryLock.TryTakeAsync(_data, record.RepoPath, TimeSpan.Zero, TestContext.Current.CancellationToken))
        {
            held.Should().NotBeNull("the test's own premise: the lock is taken");

            store.Sweep(_ => true, DateTime.UtcNow, TimeSpan.FromMinutes(15), TimeSpan.FromDays(7)).Should().Be(0);
            store.Read(record.Id)!.Status.Should().Be(ConsultationStatuses.Open);
            store.Read(record.Id)!.Handle.Should().Be("0198-held");
        }

        // Released, and the same sweep now closes it.
        store.Sweep(_ => true, DateTime.UtcNow, TimeSpan.FromMinutes(15), TimeSpan.FromDays(7)).Should().Be(1);
        store.Read(record.Id)!.Status.Should().Be(ConsultationStatuses.Closed);
    }

    [Fact]
    public void TheCarryBudgetIsTheOneTheADAPTERDeclared()
    {
        // WeRemember carries a budget; a private constant that ignored it would be a second field with
        // no reader, which is the defect the previous round caught one layer up.
        var turns = Enumerable.Range(0, 30).Select(i => ($"problem {i} " + new string('q', 500), $"advice {i} " + new string('a', 500))).ToList();

        var small = ConsultantPrompt.Transcript(turns, 3_000);
        var large = ConsultantPrompt.Transcript(turns, 40_000);

        small.Length.Should().BeLessThan(4_000);
        large.Length.Should().BeGreaterThan(small.Length);
        small.Should().Contain("problem 29", "the newest turn is carried at any budget");
    }

    [Fact]
    public void ABudgetOfZeroFallsBackRatherThanCarryingNothing()
    {
        // A record written before the field existed has zero in it, and zero must not mean "carry no
        // conversation at all" — that would be the silent memory loss this whole arm exists to prevent.
        var transcript = ConsultantPrompt.Transcript([("why", "because")], 0);

        transcript.Should().Contain("why").And.Contain("because");
    }

    [Fact]
    public async Task AnUnreadableHooksDirectory_DoesNotTakeTheWholeSnapshotDown()
    {
        // The snapshot's own failure would reach the client as an exception rather than a sentence —
        // and it would be OUR failure, on a check that exists to report somebody else's.
        var repo = Directory.CreateTempSubdirectory("coai-hooks-").FullName;
        try
        {
            var git = Directory.CreateDirectory(Path.Combine(repo, ".git")).FullName;
            File.WriteAllText(Path.Combine(git, "HEAD"), "ref: refs/heads/main\n");
            // A `hooks` that is a FILE where a directory is expected: enumerating it throws.
            File.WriteAllText(Path.Combine(git, "hooks"), "not a directory");

            var snapshot = new FilesystemInvariant(new ProcessLauncher());
            var act = async () => await snapshot.SnapshotAsync(repo, TestContext.Current.CancellationToken);

            // It still refuses — this is not a git repository — but with git's OWN words, through the
            // named ContextException the tool turns into a sentence, never an IO exception from a
            // directory walk.
            await act.Should().ThrowAsync<CoaiMcp.Runners.Context.ContextException>();
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }
}
