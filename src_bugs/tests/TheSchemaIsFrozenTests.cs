using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// Step 1 of the corpus schema is what <c>bugs-v0.1.0</c> shipped, byte for byte, and stays so.
/// </summary>
/// <remarks>
/// <para><b>Why a fixture from the released tree and not the constant itself.</b> A test whose
/// expectation is built from the current step 1 passes whatever was done to step 1 — that is the
/// hole. The fixture is the SQL <c>git show f7e5170b:src_bugs/src/Corpus.cs</c> evaluates its
/// <c>Schema</c> to, copied once into <c>fixtures/</c>, and it is what the file on the first host
/// already contains at <c>user_version = 0</c>. Editing the constant goes red here; editing the
/// fixture to match is a diff a reviewer sees under a name that says what it is.</para>
/// <para><b>Its teeth are proved by editing step 1</b> — a column appended, the first test red,
/// restored, green — which is the check the testing rule asks of every guard that was written after
/// the code it guards.</para>
/// <para>Line endings are normalised on both sides, because a raw string literal carries the
/// checkout's own — CRLF under <c>autocrlf</c>, LF on the CI runner — and that is not a schema
/// change. Nothing else is normalised: a changed column, comment or space is red.</para>
/// </remarks>
public sealed class TheSchemaIsFrozenTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-frozen-" + Guid.NewGuid().ToString("N")[..8]);

    public TheSchemaIsFrozenTests() => Directory.CreateDirectory(_dir);

    /// <summary>Copied beside the runner by the test project; a walk up to the repository would not survive a relocated build.</summary>
    internal static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "fixtures", "corpus-schema-step1-bugs-v0.1.0.sql");

    private static string Lines(string sql) =>
        sql.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    [Fact]
    public void StepOneIsTheReleasedSchemaByteForByte() =>
        Lines(CorpusSchema.Tables).Should().Be(
            Lines(File.ReadAllText(FixturePath)),
            "step 1 shipped in bugs-v0.1.0 (f7e5170b) and is on a host; a step that has run is never "
            + "edited — append a step N+1 instead");

    [Fact]
    public void StepOneIsTheFirstStepAndTheListOnlyGrows()
    {
        CorpusSchema.Steps[0].Should().BeSameAs(
            CorpusSchema.Tables,
            "a file at user_version 0 runs this first, and it must be the statement its tables came from");
        CorpusSchema.Steps.Should().HaveCountGreaterThan(1, "step 2 is this story's whole change");
    }

    /// <summary>The fixture itself is checked: real SQL producing the released tables, and nothing newer.</summary>
    /// <remarks>
    /// A fixture that did not run would make <c>TheMigrationTests</c> migrate an empty file and
    /// prove nothing; one that already carried step 2 would make them prove nothing in the other
    /// direction.
    /// </remarks>
    [Fact]
    public void TheFixtureRunsAndProducesTheReleasedTablesAndNothingNewer()
    {
        var path = Path.Combine(_dir, "released.db");

        TestSql.Run(path, File.ReadAllText(FixturePath));

        TestSql.Column(path, "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name")
            .Should().Equal(["api_keys", "corpus", "quarantine"]);
        TestSql.Column(path, "SELECT name FROM pragma_table_info('api_keys')")
            .Should().NotContain("last_seen_month", "the fixture is the shape BEFORE step 2")
            .And.Contain("submissions", "and it is the released shape, not an empty table");
        TestSql.Scalar(path, "PRAGMA user_version").Should().Be("0", "the released build never stamped the file");
    }

    public void Dispose() => Scratch.Delete(_dir);
}
