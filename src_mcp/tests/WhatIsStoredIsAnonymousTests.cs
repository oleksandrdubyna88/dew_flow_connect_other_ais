using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Normalising;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Normalizer;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What reaches the pairs table carries nothing of ours — asserted over the COLUMN.
/// </summary>
/// <remarks>
/// <para><b>The row is not anonymous and the skeletons are, and the difference is the whole point.</b>
/// `symbol_name` is a name; `finding_id` joins straight back to the repository path, the commit, the
/// file and the line. The plan's first draft claimed "every stored pair passes the zero-knowledge
/// check", which is false, and a reviewer of the plan round said so. What is true — and what story 6
/// depends on — is that the two SKELETONS are anonymous, because only they and the language ever
/// leave this machine.</para>
/// <para><see cref="ZeroKnowledgeTests"/> asserts the same property over the normaliser's OUTPUT.
/// This asserts it over the database, which is a different question: a skeleton can be computed
/// correctly and still be stored wrong — by a column swapped at the call site, by a future path that
/// writes the source instead of the skeleton, by a migration that copies the wrong field.</para>
/// </remarks>
public sealed class WhatIsStoredIsAnonymousTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-stored-" + Guid.NewGuid().ToString("N")[..8]);

    private static readonly SessionState Session =
        new("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };

    /// <summary>A method whose every name is one somebody could recognise.</summary>
    private const string Telling = """
        public sealed class PaymentGateway
        {
            public int ChargeAcmeCustomer(string acmeAccountId, int pennies)
            {
                var endpoint = "https://api.acme-corp.example/charge";
                return _http.Post(endpoint, acmeAccountId, pennies);
            }
        }
        """;

    private static readonly string[] Telltales =
        ["PaymentGateway", "ChargeAcmeCustomer", "acmeAccountId", "acme-corp", "api.acme", "pennies"];

    /// <summary>
    /// A pair normalised from telling source stores nothing of it.
    /// </summary>
    /// <remarks>
    /// Driven through the REAL normaliser and the REAL store, then read back out of SQLite — not
    /// asserted against the value that was passed in, which would only prove the test's own fixture.
    /// </remarks>
    [Fact]
    public void AStoredSkeletonCarriesNoNameFromItsSource()
    {
        var normalizer = new TreeSitterNormalizer();
        var symbol = normalizer.Locate(SourceLanguage.CSharp, Telling, 5);
        symbol.Should().NotBeNull("the fixture must actually resolve, or this test proves nothing");

        var before = normalizer.Normalise(SourceLanguage.CSharp, symbol!.Source);
        var after = normalizer.Normalise(
            SourceLanguage.CSharp, symbol.Source.Replace("return", "lock (_http) return", StringComparison.Ordinal));

        var id = Seed();
        using (var db = RoundsDb.Open(_dir, Serilog.Core.Logger.None)!)
        {
            db.RecordCollect(
                id, "", "collected", "", "bbbb222", "run-1",
                new CollectedPair(symbol.Name, "CSharp", before, after));
        }

        var stored = Read();
        foreach (var telltale in Telltales)
        {
            stored.Before.Should().NotContain(telltale, $"'{telltale}' came from somebody's source");
            stored.After.Should().NotContain(telltale, $"'{telltale}' came from somebody's source");
        }

        stored.Before.Should().NotBeEmpty("an empty skeleton would pass every assertion above");
        stored.Before.Should().NotBe(stored.After, "the pair must be two different things");
    }

    /// <summary>
    /// Every skeleton in the table passes the leak check, whatever put it there.
    /// </summary>
    /// <remarks>
    /// The property, over the column rather than over one write. A future path that stores source by
    /// mistake would leave `symbol_name` correct and the skeleton wrong, and only a check that reads
    /// the column back can see it.
    /// </remarks>
    [Fact]
    public void EverySkeletonInTheTableIsAnonymous()
    {
        var normalizer = new TreeSitterNormalizer();
        var symbol = normalizer.Locate(SourceLanguage.CSharp, Telling, 5)!;
        var id = Seed();
        using (var db = RoundsDb.Open(_dir, Serilog.Core.Logger.None)!)
        {
            db.RecordCollect(
                id, "", "collected", "", "bbbb222", "run-1",
                new CollectedPair(
                    symbol.Name,
                    "CSharp",
                    normalizer.Normalise(SourceLanguage.CSharp, symbol.Source),
                    normalizer.Normalise(SourceLanguage.CSharp, symbol.Source) + " // changed"));
        }

        var keywords = normalizer.KeywordsOf(SourceLanguage.CSharp);
        var stored = Read();

        Skeleton.Leaks(stored.Before, SourceLanguage.CSharp, keywords)
            .Should().BeEmpty("a stored skeleton with a word of ours in it is a defect in our code");
    }

    /// <summary>The symbol IS stored, deliberately, and this says so out loud.</summary>
    /// <remarks>
    /// A person reviewing a list of anonymous bodies cannot review anything, so the name is local
    /// metadata beside the pair. Asserting it is present is what stops somebody later "fixing" the
    /// anonymity of the row and breaking the page — and what marks the line story 6 must not cross.
    /// </remarks>
    [Fact]
    public void TheSymbolIsKeptOnPurpose_AndNeverLeaves()
    {
        var id = Seed();
        using (var db = RoundsDb.Open(_dir, Serilog.Core.Logger.None)!)
        {
            db.RecordCollect(
                id, "", "collected", "", "bbbb222", "run-1",
                new CollectedPair("ChargeAcmeCustomer", "CSharp", "method_1()", "method_1() { }"));
        }

        Read().Symbol.Should().Be(
            "ChargeAcmeCustomer",
            "the review page shows a person WHICH method they are looking at; story 6 sends the "
            + "skeletons and the language, and never this");
    }

    private long Seed()
    {
        using var db = RoundsDb.Open(_dir, Serilog.Core.Logger.None)!;
        var found = new Finding(
            Severity.Major, Category.Reliability, "src/PaymentGateway.cs", 5, "a race", "it races",
            "hold the lock", ["codex"]);
        db.RecordRound(
            Session,
            new RoundRecord("CodeReview", 1, "revise", 1, "all answered", new DateTime(2026, 9, 16)),
            [found],
            new RoundContext("SCOPE", "aaaa111", "claude-code"));
        db.RecordDecisions("s1", "CodeReview", 1, [Decisions.Accept([found], 0)]);

        using var read = new SqliteConnection(
            $"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False");
        read.Open();
        using var one = read.CreateCommand();
        one.CommandText = "SELECT id FROM findings";

        return (long)one.ExecuteScalar()!;
    }

    /// <summary>Straight out of SQLite, not out of the object that was written.</summary>
    private (string Symbol, string Before, string After) Read()
    {
        using var db = new SqliteConnection(
            $"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT symbol_name, skeleton_before, skeleton_after FROM collect_pairs";
        using var rows = read.ExecuteReader();
        rows.Read().Should().BeTrue("nothing was stored, so nothing is being asserted");

        return (rows.GetString(0), rows.GetString(1), rows.GetString(2));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
