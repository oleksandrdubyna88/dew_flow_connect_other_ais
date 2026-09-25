using CoaiMcp.Core.Cadence;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Where a plan's cadence is kept, and the two ways it may fail — loudly (<c>todo/PLAN_consult_on_a_cadence.md</c>,
/// epic 2, story 2.3).
/// </summary>
/// <remarks>
/// A record that cannot be READ fails CLOSED (D7): a gate that read an unreadable file as empty would
/// wave through a group nobody consulted on. And the lock is held across the whole read–modify–write
/// (the epic-1-3 consultation, point 6): a lock around the write alone loses one of two closes that
/// arrive together.
/// </remarks>
public sealed class CadenceStoreTests : IDisposable
{
    private const string Repo = "d:/work/repo/.git";
    private const string Plan = "todo/PLAN_x.md";

    private readonly string _data = Directory.CreateTempSubdirectory("coai-cadence-").FullName;
    private readonly ProcessLauncher _launcher = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void APlanNothingWasRecordedFor_ReadsAsANewState()
    {
        var state = new CadenceStore(_data).Load(Repo, Plan);

        state.Closed.Should().BeEmpty();
        state.RiskAnswered.Should().BeFalse();
    }

    [Fact]
    public void WhatIsWritten_IsReadBack_UnderTheSameKey()
    {
        var store = new CadenceStore(_data);
        store.Update(Repo, Plan, state => state
            .WithClosed(4, "proceed", "2026-09-25T10:00:00Z")
            .WithRisk([new RiskItem(7, "7.2", "moves money")], string.Empty, "2026-09-25T10:00:00Z"));

        var read = store.Load(Repo, "research/PLAN_x.md");

        read.IsClosed(4).Should().BeTrue("a promoted plan keeps its record");
        read.RiskItems.Should().ContainSingle().Which.Should().Be(new RiskItem(7, "7.2", "moves money"));
        read.RiskAnswered.Should().BeTrue();
    }

    [Fact]
    public void AnotherRepository_OrAnotherPlan_IsAnotherRecord()
    {
        var store = new CadenceStore(_data);
        store.Update(Repo, Plan, state => state.WithClosed(1, "proceed", "t"));

        store.Load("d:/other/.git", Plan).Closed.Should().BeEmpty();
        store.Load(Repo, "todo/PLAN_y.md").Closed.Should().BeEmpty();
    }

    [Fact]
    public void ADelimiterInsideAKeyPart_CannotMakeTwoIdentitiesOne()
    {
        // Epic 2's code round (codex): "repo#plan" made ("/work/a#b", "c") and ("/work/a", "b#c") the
        // same key, so one repository's cadence could be read as another's.
        var store = new CadenceStore(_data);

        store.FileFor("/work/a#b", "c").Should().NotBe(store.FileFor("/work/a", "b#c"));
    }

    [Fact]
    public void AnUnreadableRecord_FailsClosed_NamingTheFile()
    {
        var store = new CadenceStore(_data);
        store.Update(Repo, Plan, state => state.WithClosed(1, "proceed", "t"));
        File.WriteAllText(store.FileFor(Repo, Plan), "{ this is not json");

        var reading = () => store.Load(Repo, Plan);

        reading.Should().Throw<CadenceStoreException>().Which.Message.Should().Contain(store.FileFor(Repo, Plan));
    }

    [Fact]
    public void AnUpdateOverAnUnreadableRecord_FailsClosed_AndWritesNothing()
    {
        var store = new CadenceStore(_data);
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(_data, "cadence")).FullName, Path.GetFileName(store.FileFor(Repo, Plan))), "garbage");

        var updating = () => store.Update(Repo, Plan, state => state.WithClosed(1, "proceed", "t"));

        updating.Should().Throw<CadenceStoreException>();
        File.ReadAllText(store.FileFor(Repo, Plan)).Should().Be("garbage", "a record nobody could read is not overwritten with a guess");
    }

    [Fact]
    public async Task ManyClosesAtOnce_AllLand()
    {
        // Two servers on one data directory close two epics of one plan in the same instant — the
        // gatePer=epic case, one session per epic. A lock around the write alone loses one of them.
        var epics = Enumerable.Range(1, 12).ToList();

        await Parallel.ForEachAsync(epics, TestContext.Current.CancellationToken, (epic, _) =>
        {
            new CadenceStore(_data).Update(Repo, Plan, state => state.WithClosed(epic, "proceed", "t"));
            return ValueTask.CompletedTask;
        });

        new CadenceStore(_data).Load(Repo, Plan).Closed.Select(c => c.Number).Should().Equal(epics);
    }

    [Fact]
    public void AnUpdateThatCannotTakeItsTurn_Throws_RatherThanWritingBlind()
    {
        var store = new CadenceStore(_data);
        store.Update(Repo, Plan, state => state.WithClosed(1, "proceed", "t"));
        var turnFile = SessionTurn.LockFileFor(store.FileFor(Repo, Plan));

        using (new FileStream(turnFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var updating = () => store.Update(Repo, Plan, state => state.WithClosed(2, "proceed", "t"));

            updating.Should().Throw<CadenceStoreException>().Which.Message.Should().Contain("turn");
        }

        store.Load(Repo, Plan).IsClosed(2).Should().BeFalse();
    }

    [Fact]
    public void AStrandedTempFile_IsNeitherReadNorAccumulated()
    {
        // A server killed between writing the temp and moving it leaves one `.tmp` beside the record.
        // The record is still the last whole one, and the next write reuses that same temp name.
        var store = new CadenceStore(_data);
        store.Update(Repo, Plan, state => state.WithClosed(1, "proceed", "t"));
        File.WriteAllText(store.FileFor(Repo, Plan) + ".tmp", "half a wri");

        store.Load(Repo, Plan).IsClosed(1).Should().BeTrue();
        store.Update(Repo, Plan, state => state.WithClosed(2, "proceed", "t"));

        Directory.GetFiles(Path.Combine(_data, "cadence"), "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task TwoWorktreesOfOneRepository_HaveOneIdentity()
    {
        // The epic-1-3 consultation, point 8: this very plan is being built in a worktree whose path
        // differs from the main checkout's. Keyed by path, its epics would be counted in two records.
        await using var repo = await TempGitRepo.InitAsync(_launcher);
        await repo.WriteAsync("a.txt", "a");
        await repo.CommitAsync("base");
        var linked = Path.Combine(Path.GetTempPath(), "coai-cadence-wt-" + Guid.NewGuid().ToString("N")[..8]);
        await repo.GitAsync("worktree", "add", "-b", "second", linked);
        try
        {
            var context = new ContextAssembler(_launcher);

            var main = await context.CommonDirAsync(repo.Path, TestContext.Current.CancellationToken);
            var other = await context.CommonDirAsync(linked, TestContext.Current.CancellationToken);

            main.Should().NotBeEmpty();
            other.Should().Be(main);
            main.Should().Be(main.ToLowerInvariant(), "normalised as the review trees normalise it").And.NotContain("\\");
        }
        finally
        {
            await repo.GitAsync("worktree", "remove", "--force", linked);
        }
    }

    [Fact]
    public async Task ADirectoryThatIsNoRepository_HasTheFallbackIdentityOfItsPath()
    {
        var plain = Directory.CreateTempSubdirectory("coai-cadence-plain-").FullName;
        try
        {
            var identity = await new ContextAssembler(_launcher).CommonDirAsync(plain, TestContext.Current.CancellationToken);

            identity.Should().Be(RepositoryIdentity.Normalised(plain), "git's refusal is not an identity, and neither is an empty string");
        }
        finally
        {
            Directory.Delete(plain, recursive: true);
        }
    }
}
