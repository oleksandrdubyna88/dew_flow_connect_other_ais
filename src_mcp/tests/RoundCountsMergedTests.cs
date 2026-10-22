using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static CoaiMcp.Tests.RoundCountsSnapshotFixtures;

namespace CoaiMcp.Tests;

/// <summary>
/// Two stores, ONE count (E6.S1 of <c>todo/PLAN_the_round_counter_tail_E6.md</c>): this side's <c>coai.db</c>, read live, and the snapshots
/// the other side's coai-mcp wrote, merged by <c>round_key</c> and cut at one instant.
/// </summary>
/// <remarks>
/// <para>The cornerstone's risk is a number that is wrong in a plausible way: a key rule that collapses two rounds, or counts one
/// twice, reads as a smaller or larger count like any other. So every identity rule has a test that would give the OTHER number if
/// the rule were the neighbouring one: the integer id alone (two stores each have a round 1), the key alone (a keyless round has none),
/// the directory alone (two distributions share one), and the store's name alone (one file is reached by two spellings).</para>
/// <para>Real SQLite for this side, real files for the others, a constant clock: the point is that the merge counts what the files say.</para>
/// </remarks>
public sealed class RoundCountsMergedTests : IDisposable
{
    private const string Codex = "codex";
    private const string Gemini = "gemini";

    private readonly TempDir _store = TempDir.For("testsToDelete-merged-store-");
    private readonly TempDir _elsewhere = TempDir.For("testsToDelete-merged-others-");
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    private string Dir => _store.Path;

    private string Others => _elsewhere.Path;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _store.Dispose();
        _elsewhere.Dispose();
    }

    private SnapshotStore ThisSide => RoundCountsSnapshot.StoreOf(Dir, Environment.GetEnvironmentVariable);

    private RoundCountsMergedAnswer Merge(params string[] paths) => RoundCountsMerged.Read(Dir, ThisSide, "0.0.0-test", paths, Now);

    private string Snapshot(string folder, RoundCountsSnapshotDocument document) => Write(Path.Combine(Others, folder), document);

    private void Live(string key, DateTime started, string stage = nameof(Stage.CodeReview), params ReviewerState[] reviewers)
    {
        using var db = RoundsDb.Open(Dir, _log)!;
        Record(db, "s" + Guid.NewGuid().ToString("N")[..6], stage, started, key, reviewers.Length == 0 ? [Reviewer(Codex)] : reviewers);
    }

    private static MergedRow RowOf(RoundCountsMergedAnswer answer, string window, string provider) =>
        answer.Windows.Single(w => w.Name == window).Rows.Single(r => r.Provider == provider);

    /// <summary>
    /// Both stores' first round has the integer id 1 — an AUTOINCREMENT id is meaningful only inside its own store. Counted by id (the
    /// way the single-store counter counted) the two collapse to one and the sum is a plausible number that is simply wrong.
    /// </summary>
    [Fact]
    public void TwoStoresWithTheSameIntegerId_AreTwoRounds()
    {
        Live(Key('a'), Now.AddHours(-1));
        var path = Snapshot("wsl", Document(Wsl("Ubuntu"), Now.AddMinutes(-6), Round(1, Key('b'), Now.AddHours(-2), nameof(Stage.CodeReview), Ran(Codex))));

        var row = RowOf(Merge(path), "5h", Codex);

        row.Rounds.Code.Should().Be(2, "round 1 of this side and round 1 of the other side are two rounds");
        row.RoundKeys.Should().BeEquivalentTo([Key('a'), Key('b')]);
        row.Reviewers.Should().Be(2);
    }

    [Fact]
    public void TheSameKeyInTwoStores_IsOneRound()
    {
        Live(Key('a'), Now.AddHours(-1));
        var path = Snapshot("wsl", Document(Wsl("Ubuntu"), Now.AddMinutes(-6), Round(9, Key('a'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex))));

        var row = RowOf(Merge(path), "5h", Codex);

        row.Rounds.Code.Should().Be(1, "one key is one round, wherever it was recorded");
        row.RoundKeys.Should().Equal([Key('a')]);
        row.Reviewers.Should().Be(1, "and its reviewers are counted from one store, not both");
    }

    [Fact]
    public void AKeylessRound_IsKeyedByItsStoreAndId()
    {
        Live(string.Empty, Now.AddHours(-1));
        var path = Snapshot("wsl", Document(Wsl("Ubuntu"), Now.AddMinutes(-6), Round(1, string.Empty, Now.AddHours(-2), nameof(Stage.CodeReview), Ran(Codex))));

        var row = RowOf(Merge(path), "5h", Codex);

        row.Rounds.Code.Should().Be(2, "two keyless rounds are two rounds: an empty key is no identity, so they must not collapse into one");
        row.RoundsWithoutKey.Should().Be(2);
        row.RoundKeys.Should().BeEmpty("an empty key is not listed");
    }

    /// <summary>Two WSL distributions share the default <c>/home/&lt;user&gt;/.local/share/coai-mcp</c>: the directory alone names neither.</summary>
    [Fact]
    public void TwoDistributionsWithTheDefaultDirectory_AreTwoStores()
    {
        var ubuntu = Snapshot("ubuntu", Document(Wsl("Ubuntu"), Now.AddMinutes(-6), Round(1, Key('b'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex))));
        var debian = Snapshot("debian", Document(Wsl("Debian"), Now.AddMinutes(-7), Round(1, Key('c'), Now.AddHours(-2), nameof(Stage.CodeReview), Ran(Codex))));

        var answer = Merge(ubuntu, debian);

        answer.Stores.Select(s => s.WslDistro).Should().Equal(string.Empty, "Ubuntu", "Debian");
        answer.Stores.Select(s => s.Id).Distinct().Should().HaveCount(3, "same directory, different host: three stores");
        RowOf(answer, "5h", Codex).Rounds.Code.Should().Be(2, "both distributions' rounds are counted");
    }

    [Fact]
    public void AKeyInTwoStores_IsOneRound_AttributedToTheFirst()
    {
        Live(Key('a'), Now.AddHours(-1));
        var path = Snapshot("wsl", Document(Wsl("Ubuntu"), Now.AddMinutes(-6), Round(9, Key('a'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex))));

        var answer = Merge(path);

        var row = RowOf(answer, "5h", Codex);
        row.Parts.Select(p => p.Store).Should().Equal(ThisSide.Id).And.HaveCount(1, "only the first store counts the key");
        row.Parts.Sum(p => p.Rounds.Code).Should().Be(row.Rounds.Code, "the parts always sum to the row");
        row.KeyStore.Should().HaveCount(1);
        row.KeyStore[Key('a')].Should().Be(ThisSide.Id, "the key's owner is the first store that holds it");
    }

    [Fact]
    public void AKeyInTwoSnapshots_IsOwnedByTheSameStore_InEitherArgumentOrder()
    {
        var first = Snapshot("one", Document(Wsl("Ubuntu"), Now.AddMinutes(-6), Round(1, Key('a'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex))));
        var second = Snapshot("two", Document(Wsl("Debian"), Now.AddMinutes(-7), Round(1, Key('a'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex), Ran(Codex))));
        var lowest = string.CompareOrdinal(Wsl("Ubuntu").Id, Wsl("Debian").Id) < 0 ? Wsl("Ubuntu").Id : Wsl("Debian").Id;

        var forward = RowOf(Merge(first, second), "5h", Codex);
        var backward = RowOf(Merge(second, first), "5h", Codex);

        forward.KeyStore[Key('a')].Should().Be(lowest, "of the snapshot stores, the lowest id owns a key they share");
        backward.KeyStore.Should().Equal(forward.KeyStore, "the same set of stores gives the same ownership whatever order the paths came in");
        backward.Parts.Should().BeEquivalentTo(forward.Parts, "the parts follow the ownership");
        backward.Reviewers.Should().Be(forward.Reviewers, "and so does the count");
        Merge(first, second).Stores.Select(s => s.Conflicts).Order().Should().Equal(Merge(second, first).Stores.Select(s => s.Conflicts).Order());
    }

    [Fact]
    public void TheStoresAreListedThisSideFirst_ThenInTheOrderTheirFirstPathWasGiven()
    {
        var first = Snapshot("one", Document(Wsl("Ubuntu"), Now.AddMinutes(-6), Round(1, Key('a'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex))));
        var second = Snapshot("two", Document(Wsl("Debian"), Now.AddMinutes(-7), Round(1, Key('b'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex))));

        Merge(first, second).Stores.Select(s => s.WslDistro).Should().Equal(string.Empty, "Ubuntu", "Debian");
        Merge(second, first).Stores.Select(s => s.WslDistro).Should().Equal(string.Empty, "Debian", "Ubuntu");
    }

    [Fact]
    public void CopiesThatDiffer_AreCountedOnceFromTheOwner_AndTheConflictIsSaid_InEitherOrder()
    {
        Live(Key('a'), Now.AddHours(-1), nameof(Stage.CodeReview), Reviewer(Codex));
        var path = Snapshot("wsl", Document(
            Wsl("Ubuntu"), Now.AddMinutes(-6),
            Round(9, Key('a'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex), Ran(Codex), Ran(Gemini, "gemini-3.8-flash-medium"))));

        var answer = Merge(path);

        RowOf(answer, "5h", Codex).Reviewers.Should().Be(1, "counted from the owning store: this side's copy has one codex reviewer");
        answer.Windows.Single(w => w.Name == "5h").Rows.Select(r => r.Provider).Should().Equal(new[] { Codex }, "the other copy's gemini reviewer is not smuggled in");
        answer.Stores[0].Conflicts.Should().Be(1, "the owning store says one of its rounds also appears elsewhere with different contents");
        answer.Stores[1].Conflicts.Should().Be(0);
    }

    [Fact]
    public void ACopyThatDiffers_IsSaid_WhicheverSnapshotCameFirst_AndTheOwnerIsTheLowestId()
    {
        var small = Snapshot("small", Document(Wsl("Ubuntu"), Now.AddMinutes(-6), Round(1, Key('a'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex))));
        var large = Snapshot("large", Document(Wsl("Debian"), Now.AddMinutes(-7), Round(1, Key('a'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex), Ran(Codex))));
        var smallOwns = string.CompareOrdinal(Wsl("Ubuntu").Id, Wsl("Debian").Id) < 0;

        foreach (var answer in new[] { Merge(small, large), Merge(large, small) })
        {
            RowOf(answer, "5h", Codex).Reviewers.Should().Be(smallOwns ? 1 : 2, "counted from the owning store, the lowest id, whichever path came first");
            answer.Stores.Single(s => s.WslDistro == "Ubuntu").Conflicts.Should().Be(smallOwns ? 1 : 0, "the conflict is said against the store that owns the round");
            answer.Stores.Single(s => s.WslDistro == "Debian").Conflicts.Should().Be(smallOwns ? 0 : 1);
        }
    }

    [Fact]
    public void CopiesThatAgree_SayNoConflict()
    {
        Live(Key('a'), Now.AddHours(-1));
        var path = Snapshot("wsl", Document(Wsl("Ubuntu"), Now.AddMinutes(-6), Round(9, Key('a'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex))));

        Merge(path).Stores.Should().OnlyContain(s => s.Conflicts == 0);
    }

    [Fact]
    public void TwoSpellingsOfOneSnapshot_AreOneStore_NamingBothPaths()
    {
        var document = Document(Wsl("Ubuntu"), Now.AddMinutes(-6), Round(1, Key('b'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex)));
        var one = Snapshot("spelling-one", document);
        var two = Snapshot("spelling-two", document);

        var answer = Merge(one, two);

        answer.Stores.Should().HaveCount(2, "this side and one other");
        answer.Stores[1].Paths.Should().Equal(one, two);
        RowOf(answer, "5h", Codex).Rounds.Code.Should().Be(1, "its rounds are counted once");
        answer.StoresNotIncluded.Should().BeEmpty();
    }

    [Fact]
    public void OfTwoSpellings_TheNewerWrittenIsTheOneCounted()
    {
        var older = Snapshot("older", Document(Wsl("Ubuntu"), Now.AddMinutes(-30), Round(1, Key('b'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex))));
        var newer = Snapshot("newer", Document(
            Wsl("Ubuntu"), Now.AddMinutes(-6),
            Round(1, Key('b'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex)), Round(2, Key('c'), Now.AddMinutes(-20), nameof(Stage.CodeReview), Ran(Codex))));

        var answer = Merge(older, newer);

        RowOf(answer, "5h", Codex).Rounds.Code.Should().Be(2, "the newer document is the one counted, whichever path came first");
        answer.Stores[1].AsOf.Should().Be(Now.AddMinutes(-6).ToString("O"));
        answer.Stores[1].Paths.Should().Equal(older, newer);
    }

    /// <summary>Two documents of ONE store written at the same instant but saying different things: nothing in them says which is newer, so the path decides (the ordinally smallest).</summary>
    [Fact]
    public void TwoSnapshotsOfOneStoreWrittenAtTheSameInstant_GiveTheSameAnswerInEitherOrder()
    {
        var written = Now.AddMinutes(-6);
        var a = Snapshot("tie-a", Document(Wsl("Ubuntu"), written, Round(1, Key('b'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex))));
        var z = Snapshot("tie-z", Document(Wsl("Ubuntu"), written, Round(1, Key('c'), Now.AddHours(-2), nameof(Stage.CodeReview), Ran(Codex), Ran(Codex))));

        var forward = Merge(a, z);
        var backward = Merge(z, a);

        backward.Windows.Should().BeEquivalentTo(forward.Windows, "the counted document must not depend on the order the paths came in");
        backward.Stores.Select(s => (s.Id, s.AsOf, s.Conflicts, s.Quiet)).Should().Equal(forward.Stores.Select(s => (s.Id, s.AsOf, s.Conflicts, s.Quiet)));
        backward.Stores[1].Paths.Should().BeEquivalentTo(forward.Stores[1].Paths, "both spellings are named either way");
        RowOf(forward, "5h", Codex).RoundKeys.Should().Equal([Key('b')], "on a tie the document at the ordinally smallest path is the one counted");
    }

    [Fact]
    public void ASnapshotOfThisSidesOwnStore_IsNotCountedTwice()
    {
        Live(Key('a'), Now.AddHours(-1));
        var mine = Document(ThisSide, Now.AddMinutes(-5), Round(1, Key('a'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex)));
        var path = Snapshot("through-another-door", mine);

        var answer = Merge(path);

        answer.Stores.Should().ContainSingle("it is this side, reached through another spelling");
        answer.Stores[0].Paths.Should().Equal(path);
        answer.Stores[0].Live.Should().BeTrue();
        RowOf(answer, "5h", Codex).Rounds.Code.Should().Be(1);
    }

    [Fact]
    public void AQuietSnapshot_IsAPartOfNought_AsOfItsWrite_NeverNotIncluded()
    {
        var written = Now.AddDays(-8);
        var path = Snapshot("quiet", Document(Wsl("Ubuntu"), written, Round(1, Key('b'), written.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex))));

        var answer = Merge(path);

        answer.StoresNotIncluded.Should().BeEmpty("a week of silence is a fact about the store, not a failure to read it");
        var quiet = answer.Stores.Should().HaveCount(2).And.Subject.Last();
        quiet.Quiet.Should().BeTrue();
        quiet.AsOf.Should().Be(written.ToString("O"));
        answer.Windows.SelectMany(w => w.Rows).Should().BeEmpty("nothing of it is inside any window");
    }

    [Fact]
    public void ALiveSnapshot_IsNotQuiet()
    {
        var path = Snapshot("live", Document(Wsl("Ubuntu"), Now.AddHours(-3), Round(1, Key('b'), Now.AddHours(-4), nameof(Stage.CodeReview), Ran(Codex))));

        Merge(path).Stores[1].Quiet.Should().BeFalse();
    }

    [Fact]
    public void AMissingSnapshot_IsNotIncluded_WithTheReason()
    {
        Live(Key('a'), Now.AddHours(-1));
        var missing = Path.Combine(Others, "nothing", RoundCountsSnapshot.FileName);

        var answer = Merge(missing);

        answer.StoresNotIncluded.Should().Equal(new StoreNotIncluded(missing, "missing", missing));
        answer.Stores.Should().ContainSingle();
        RowOf(answer, "5h", Codex).Rounds.Code.Should().Be(1, "this side's rows are intact");
    }

    [Fact]
    public void AnUnreadableOne_IsNotIncluded_WithTheReason()
    {
        Directory.CreateDirectory(Others);
        var torn = Path.Combine(Others, "torn.json");
        File.WriteAllText(torn, "{");

        var answer = Merge(torn);

        var refusal = answer.StoresNotIncluded.Should().ContainSingle().Which;
        refusal.Path.Should().Be(torn);
        refusal.Reason.Should().Be("unreadable");
        refusal.Detail.Should().NotBeEmpty();
        answer.Stores.Should().ContainSingle();
    }

    [Theory]
    [InlineData("""{"schema":"one"}""")]
    [InlineData("""{"schema":0}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""{"schema":1}""")]
    [InlineData("""{"schema":1,"writtenUtc":"yesterday","store":{"id":"x","platform":"","host":"","wslDistro":"","side":"","directory":""},"fromUtc":"","truncated":false,"rounds":[]}""")]
    public void ADocumentThatIsNotASnapshot_IsUnreadable_NotAZero(string text)
    {
        Directory.CreateDirectory(Others);
        var path = Path.Combine(Others, "odd.json");
        File.WriteAllText(path, text);

        var answer = Merge(path);

        answer.StoresNotIncluded.Should().ContainSingle().Which.Reason.Should().Be("unreadable");
        answer.Stores.Should().ContainSingle();
    }

    [Fact]
    public void AStoreIdThatIsNotThirtyTwoHexDigits_IsUnreadable()
    {
        var bad = Document(Wsl("Ubuntu") with { Id = "../../not-an-id" }, Now.AddMinutes(-6), Round(1, Key('b'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex)));
        var path = Snapshot("bad-id", bad);

        Merge(path).StoresNotIncluded.Should().ContainSingle().Which.Reason.Should().Be("unreadable", "an id is echoed into the answer and keys a dictionary");
    }

    [Fact]
    public void ARoundKeyThatIsNotAKey_IsUnreadable()
    {
        var bad = Document(Wsl("Ubuntu"), Now.AddMinutes(-6), Round(1, "not-a-key", Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex)));

        Merge(Snapshot("bad-key", bad)).StoresNotIncluded.Should().ContainSingle().Which.Reason.Should().Be("unreadable");
    }

    [Fact]
    public void ANewerSchema_IsNotIncluded_NamingBothSchemas()
    {
        Directory.CreateDirectory(Others);
        var path = Path.Combine(Others, "newer.json");
        File.WriteAllText(path, """{"schema":2,"coaiMcpVersion":"0.99.0","somethingNew":{"a":1}}""");

        var answer = Merge(path);

        var refusal = answer.StoresNotIncluded.Should().ContainSingle().Which;
        refusal.Reason.Should().Be("newer-schema");
        refusal.Detail.Should().Contain("schema 2").And.Contain("reads schema 1").And.Contain("update coai-mcp").And.Contain("0.99.0");
        answer.Stores.Should().ContainSingle();
    }

    [Fact]
    public void AnUnknownField_IsIgnored_ByAReaderOfThisSchema()
    {
        var document = Document(Wsl("Ubuntu"), Now.AddMinutes(-6), Round(1, Key('b'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran(Codex)));
        var text = RoundCountsSnapshot.Serialize(document).Replace("{\"schema\":1", "{\"schema\":1,\"aFieldFromTheFuture\":{\"x\":[1,2,3]}", StringComparison.Ordinal);
        text.Should().Contain("aFieldFromTheFuture", "the fixture really carries the extra property");
        Directory.CreateDirectory(Others);
        var path = Path.Combine(Others, "extra.json");
        File.WriteAllText(path, text);

        var answer = Merge(path);

        answer.StoresNotIncluded.Should().BeEmpty();
        RowOf(answer, "5h", Codex).Rounds.Code.Should().Be(1, "read whole");
    }

    /// <summary>One <c>now</c> for every store: a round in a snapshot 4 h 59 m before it is in 5 h, one exactly 5 h before is not.</summary>
    [Fact]
    public void TheWindows_AreCutAtOneNow_ForEveryStore()
    {
        var path = Snapshot("edge", Document(
            Wsl("Ubuntu"), Now.AddMinutes(-1),
            Round(1, Key('b'), Now.AddHours(-5).AddMinutes(1), nameof(Stage.CodeReview), Ran(Codex)),
            Round(2, Key('c'), Now.AddHours(-5), nameof(Stage.CodeReview), Ran(Codex))));

        var answer = Merge(path);

        RowOf(answer, "5h", Codex).Rounds.Code.Should().Be(1, "4 h 59 m ago is in; exactly 5 h ago is on the edge and belongs to the next window up");
        RowOf(answer, "24h", Codex).Rounds.Code.Should().Be(2);
    }

    [Fact]
    public void ASnapshotWrittenLongBeforeNow_IsCutAtNow_NotAtItsOwnWrite()
    {
        // Written 3 h before `now`. The round 5 h 30 m before `now` was 2 h 30 m old when its store wrote it, so a cut at the snapshot's own
        // write would call it a 5 h round; at `now` it is not.
        var path = Snapshot("old-write", Document(
            Wsl("Ubuntu"), Now.AddHours(-3),
            Round(1, Key('b'), Now.AddHours(-4), nameof(Stage.CodeReview), Ran(Codex)),
            Round(2, Key('c'), Now.AddHours(-5).AddMinutes(-30), nameof(Stage.CodeReview), Ran(Codex))));

        var answer = Merge(path);

        RowOf(answer, "5h", Codex).Rounds.Code.Should().Be(1);
        RowOf(answer, "24h", Codex).Rounds.Code.Should().Be(2);
    }

    [Fact]
    public void PerStoreParts_SumToTheRow_ForEveryStageAndWindow()
    {
        Live(Key('a'), Now.AddHours(-1), nameof(Stage.PlanReview));
        Live(Key('d'), Now.AddHours(-2), nameof(Stage.CodeReview), Reviewer(Codex), Reviewer(Codex, role: "SecurityReliability"));
        var path = Snapshot("wsl", Document(
            Wsl("Ubuntu"), Now.AddMinutes(-6),
            Round(1, Key('b'), Now.AddHours(-1), nameof(Stage.PlanReview), Ran(Codex)),
            Round(2, Key('c'), Now.AddHours(-30), nameof(Stage.DocumentReview), Ran(Codex))));

        var answer = Merge(path);

        foreach (var window in answer.Windows)
        {
            foreach (var row in window.Rows)
            {
                row.Parts.Sum(p => p.Rounds.Plan).Should().Be(row.Rounds.Plan, $"{window.Name}: plan");
                row.Parts.Sum(p => p.Rounds.Code).Should().Be(row.Rounds.Code, $"{window.Name}: code");
                row.Parts.Sum(p => p.Rounds.Document).Should().Be(row.Rounds.Document, $"{window.Name}: document");
                row.Parts.Sum(p => p.Reviewers).Should().Be(row.Reviewers, $"{window.Name}: reviewers");
            }
        }

        RowOf(answer, "7d", Codex).Parts.Select(p => p.Store).Should().Equal(new[] { ThisSide.Id, Wsl("Ubuntu").Id }, "parts come in stores order");
        RowOf(answer, "5h", Codex).Parts.Select(p => p.Store).Should().Equal(ThisSide.Id, Wsl("Ubuntu").Id);
    }

    [Fact]
    public void ThisSideAlone_IsTheSameCountAsThePlainMode()
    {
        Live(Key('a'), Now.AddHours(-1));
        Live(string.Empty, Now.AddHours(-7), nameof(Stage.PlanReview), Reviewer(Gemini, "gemini-3.8-flash-medium"));
        var plain = RoundCounts.Read(Dir, Now);

        var merged = Merge();

        merged.Stores.Should().ContainSingle().Which.Live.Should().BeTrue();
        merged.Stores[0].Paths.Should().BeEmpty();
        merged.Stores[0].AsOf.Should().Be(Now.ToString("O"), "the live store is as of now");
        merged.Windows.Select(w => (w.Name, w.FromUtc, w.ToUtc)).Should().Equal(plain.Windows.Select(w => (w.Name, w.FromUtc, w.ToUtc)));
        foreach (var (window, plainWindow) in merged.Windows.Zip(plain.Windows))
        {
            window.Rows.Select(r => (r.Provider, r.Rounds, r.Reviewers, r.RoundsWithoutKey))
                .Should().Equal(plainWindow.Rows.Select(r => (r.Provider, r.Rounds, r.Reviewers, r.RoundsWithoutKey)), window.Name);
            window.Rows.Select(r => string.Join(',', r.RoundKeys)).Should().Equal(plainWindow.Rows.Select(r => string.Join(',', r.RoundKeys)), window.Name);
        }

        merged.ModelsKnownSince.Should().Be(plain.ModelsKnownSince);
    }

    [Fact]
    public void ModelsKnownSince_IsThisSides_AndAModelOfASnapshotRoundIsCounted()
    {
        Live(Key('a'), Now.AddHours(-1));
        var path = Snapshot("wsl", Document(Wsl("Ubuntu"), Now.AddMinutes(-6), Round(1, Key('b'), Now.AddHours(-2), nameof(Stage.CodeReview), Ran(Codex, "gpt-6-luna"))));

        var answer = Merge(path);

        answer.ModelsKnownSince.Should().Be(Now.AddHours(-1).ToString("O"), "D11: the instant is this side's");
        RowOf(answer, "5h", Codex).Models.Should().Equal([new ModelRounds("gpt-6-luna", 2)]);
    }

    [Fact]
    public void AMissingDatabase_IsThisSideWithNoRounds_AndTheSnapshotsAreStillCounted()
    {
        var path = Snapshot("wsl", Document(Wsl("Ubuntu"), Now.AddMinutes(-6), Round(1, Key('b'), Now.AddHours(-2), nameof(Stage.CodeReview), Ran(Codex))));

        var answer = Merge(path);

        File.Exists(Path.Combine(Dir, RoundsDb.FileName)).Should().BeFalse("a reader never makes the store");
        RowOf(answer, "5h", Codex).Rounds.Code.Should().Be(1);
        answer.Stores.Should().HaveCount(2);
    }
}
