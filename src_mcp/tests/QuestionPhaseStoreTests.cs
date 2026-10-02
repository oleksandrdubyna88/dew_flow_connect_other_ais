using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The batches the person has been asked since the plan's proceed, keyed repo common-dir + plan key when a
/// plan is known, else the caller session (<c>todo/PLAN_question_consultant.md</c> D14 (b), S3 acceptance 2).
/// </summary>
public sealed class QuestionPhaseStoreTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _data = Directory.CreateTempSubdirectory("coai-qphase-").FullName;

    private QuestionPhaseStore Store => new(_data);

    private static readonly QuestionPhaseKey PlanKey = new QuestionPhaseKey.Plan("c:/work/a/.git", "plan_x.md");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    [Fact]
    public void OneEscalationId_CountsOnce()
    {
        var store = Store;

        store.Count(PlanKey, "e1", Now).Should().BeTrue("the first time it is new");
        store.Count(PlanKey, "e1", Now).Should().BeFalse("the same question asked again — a retry after a lost reply — is not a second batch");
        store.Count(PlanKey, "e2", Now).Should().BeTrue();

        store.Observe(PlanKey, released: false, Now).Batches.Should().Be(2);
    }

    [Fact]
    public void TwoBranchesOfOnePlan_ShareTheCount()
    {
        // The key carries the repository (its common dir) and the plan's file name — never the branch: with one gate
        // per epic every epic is its own session on its own branch, and the person was asked under the plan.
        var fromOneBranch = new QuestionPhaseKey.Plan("c:/work/a/.git", "todo/PLAN_X.md".Split('/')[^1].ToLowerInvariant());
        var fromAnother = new QuestionPhaseKey.Plan("c:/work/a/.git", "research/plan_x.md".Split('/')[^1]);

        Store.FileFor(fromOneBranch).Should().Be(Store.FileFor(fromAnother), "one plan, one record, whatever folder or branch it is read from");
        Store.Count(fromOneBranch, "e1", Now);
        Store.Count(fromAnother, "e2", Now);

        Store.Observe(fromOneBranch, released: false, Now).Batches.Should().Be(2);
    }

    [Fact]
    public void TheCallerKey_IsItsOwnRecord_ApartFromAnyPlan()
    {
        var caller = new QuestionPhaseKey.Caller("caller-1");

        Store.FileFor(caller).Should().NotBe(Store.FileFor(PlanKey));
        Store.Count(caller, "e1", Now);

        Store.Observe(caller, released: false, Now).Batches.Should().Be(1);
        Store.Observe(PlanKey, released: false, Now).Batches.Should().Be(0);
    }

    [Fact]
    public void AnUnreadableStore_Allows_ByReadingAsUnreadable()
    {
        var store = Store;
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(store.FileFor(PlanKey), "{ torn");

        var read = store.Observe(PlanKey, released: false, Now);

        read.Readable.Should().BeFalse("a record that exists and cannot be read is said, never read as zero batches and never as the gate's refusal");
        read.Why.Should().Contain(Path.GetFileName(store.FileFor(PlanKey)));
        read.Batches.Should().Be(0);
    }

    [Fact]
    public void AMissingRecord_IsZeroBatches_AndReadable()
    {
        var read = Store.Observe(PlanKey, released: false, Now);

        read.Readable.Should().BeTrue();
        read.Batches.Should().Be(0);
    }

    [Fact]
    public void APlanKeyReusedAfterItsRelease_StartsAFreshCount()
    {
        var store = Store;
        store.Count(PlanKey, "e1", Now);
        store.Count(PlanKey, "e2", Now);

        store.Observe(PlanKey, released: true, Now).Batches.Should().Be(2, "the release is observed and stamped; the count is what it was");
        var reused = store.Observe(PlanKey, released: false, Now.AddDays(3));
        reused.Batches.Should().Be(0, "D14 (b): a plan key seen un-released after its release is a new piece of work under an old name");

        store.Count(PlanKey, "e1", Now.AddDays(3)).Should().BeTrue("the old ids went with the old count");
        store.Observe(PlanKey, released: false, Now.AddDays(3)).Batches.Should().Be(1);
    }

    [Fact]
    public void ARecordIsSwept_ThirtyDaysAfterItsLastWrite_AndNotBefore()
    {
        var store = Store;
        store.Count(PlanKey, "e1", Now);
        var caller = new QuestionPhaseKey.Caller("caller-1");
        store.Count(caller, "e1", Now);
        File.SetLastWriteTimeUtc(store.FileFor(PlanKey), Now - QuestionPhaseStore.Retention - TimeSpan.FromHours(1));
        File.SetLastWriteTimeUtc(store.FileFor(caller), Now - QuestionPhaseStore.Retention + TimeSpan.FromHours(1));

        store.Sweep(Now).Should().Be(1);

        File.Exists(store.FileFor(PlanKey)).Should().BeFalse();
        File.Exists(store.FileFor(caller)).Should().BeTrue();
    }
}
