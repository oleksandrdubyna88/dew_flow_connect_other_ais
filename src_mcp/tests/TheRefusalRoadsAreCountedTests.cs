using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every road a refusal can leave by, and every way a reviewer can end — counted, not remembered.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> §8 of the server-notices plan: <i>"'Every refusal' and 'every
/// reviewer failure' are claims a list can make and a codebase can quietly break."</i> Story 2.2 is
/// about to promise that every refusal returned to a calling AI is written down, and that is a
/// promise about a population nothing counts. The extension answered the same question for itself
/// and wrote down what it cost not to: a plan published 109 call sites, a table that summed to 113
/// and a sentence saying 95, and every test was green because no test knew the number.</para>
///
/// <para><b>The guarantee is a BOUNDARY, not a count of calls.</b> The plan round took the count
/// apart: codex, that removing one call and adding an uninstrumented one leaves the number where it
/// was; gemini, that the count is not obtainable by a text scan while <c>_log.Error(</c> is
/// everywhere. So <c>ErrorAnswer</c> — the only shape a refusal takes on the wire — may be NAMED in
/// one production file, and both services reach it through <see cref="CoaiMcp.Server.Refusal"/>.
/// "Every refusal passes through one instrumented point" is then a fact about the type.</para>
///
/// <para><b>And the code round found that the guard could not fail.</b> Six reviewers, independently:
/// the import check searched for <c>using staticCoaiMcp.Server.Refusal</c> — no space — because the
/// scanner used to glue lines with nothing between them, and an ordinary single-line
/// <c>using static CoaiMcp.Server.Refusal;</c> was never matched. Planting one proved it: the test
/// passed. It is a regex over the joined code now, covering <c>global using</c>, an alias, and
/// <c>global::</c>, and the teeth are proved by a test that plants each form in a temporary tree.</para>
///
/// <para><b>The inventory is generated, not typed.</b> <c>shared/refusal-sites.json</c> is written
/// when <c>COAI_RECORD_REFUSAL_SITES=1</c> — and that run then FAILS, saying so, because a run that
/// writes its own expectation is a run that cannot check anything. Without the variable the file is
/// compared read-only, so a read-only CI checkout is never rewritten and a stale file is never
/// accepted.</para>
/// </remarks>
public sealed class TheRefusalRoadsAreCountedTests
{
    /// <summary>The one place the wire shape of a refusal may be named.</summary>
    private const string TheBoundary = "src_mcp/src/Server/Refusal.cs";

    /// <summary>Where the record is DECLARED, which is the one other place its name may appear.</summary>
    private const string TheDeclaration = "src_mcp/src/Server/ServerJsonContext.cs";

    /// <summary>The recording escape, named once.</summary>
    private const string Recording = "COAI_RECORD_REFUSAL_SITES";

    /// <summary>Where the generated inventory lives, beside the other things both halves share.</summary>
    private static string Inventory =>
        Path.Combine(ProductionSources.RepositoryRoot(), "shared", "refusal-sites.json");

    /// <summary>
    /// A static import or an alias of one of the refusal types, in every spelling C# allows.
    /// </summary>
    /// <remarks>
    /// <c>using static X;</c>, <c>global using static X;</c>, <c>using R = X;</c>,
    /// <c>using R = global::X;</c> — with any whitespace. Each of them lets a call be written with
    /// the type named nowhere, which no census that reads spellings can see. The names are matched on
    /// word boundaries so that <c>RoundRefusals</c> is not <c>Refusal</c>.
    /// </remarks>
    private static readonly Regex HidesARefusalHelper = new(
        @"\busing\s+(static\s+|\w+\s*=\s*)(global::)?[\w.]*\b(Refusal|PanelService|ConsultationService)\b",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    [Fact]
    public void TheRefusalAnswerIsNamedInOnlyTwoPlaces()
    {
        // The guarantee story 2.2 rests on, and the code round widened it from one spelling to the
        // TYPE: `new ErrorAnswer(` was the only shape searched for, and `ErrorAnswer answer = new(…)`
        // builds one without ever writing that. A file that cannot NAME the type cannot build one,
        // however it spells the construction.
        var named = ProductionSources.FilesMentioning("ErrorAnswer");

        named.Keys.Should().BeEquivalentTo([TheBoundary, TheDeclaration],
            "an ErrorAnswer is the only shape a refusal takes on the wire, so the places that can "
            + "build one are the places instrumentation has to reach — found: {0}",
            string.Join(", ", named.Keys));
        ProductionSources.FilesMentioning("new ErrorAnswer(").Keys.Should().Equal([TheBoundary],
            "and the one construction is in the boundary, which is what the scan must still find");
    }

    [Fact]
    public void NoProductionFileHidesARefusalHelperBehindAnImport()
    {
        var hiding = ProductionSources.Files()
            .Where(file => HidesARefusalHelper.IsMatch(ProductionSources.CodeOf(file)))
            .ToList();

        hiding.Should().BeEmpty(
            "a static import or an alias lets `Answer(…)` be written with the type named nowhere, "
            + "which is a road past every census that reads spellings — found: {0}",
            string.Join(", ", hiding));
    }

    [Theory]
    [InlineData("using static CoaiMcp.Server.Refusal;", "an ordinary static import")]
    [InlineData("global using static CoaiMcp.Server.Refusal;", "a global one, which is the same thing one file away")]
    [InlineData("using  static   CoaiMcp.Server.Refusal ;", "whitespace, which the first version required to be absent")]
    [InlineData("using R = CoaiMcp.Server.Refusal;", "an alias")]
    [InlineData("using R = global::CoaiMcp.Server.Refusal;", "an alias through the global namespace")]
    [InlineData("using static CoaiMcp.Server.PanelService;", "the other two types the rule names")]
    public void TheImportGuardHasTeeth(string import, string why)
    {
        // THE TEST THE FIRST VERSION DID NOT HAVE, and its absence is why six reviewers found the
        // same defect: the guard searched for a spelling with no space in it and could not fail.
        // Planting a real import in the tree proved it passed. These are the forms, asserted against
        // the rule itself rather than against a planted file, so the suite stays hermetic.
        HidesARefusalHelper.IsMatch(ProductionSources.Joined(import)).Should().BeTrue(why);
    }

    [Theory]
    [InlineData("using CoaiMcp.Server;", "an ordinary import of the namespace is not a hiding place")]
    [InlineData("var x = RoundRefusals.NoCodeRoles;", "and RoundRefusals is not Refusal")]
    [InlineData("using static System.Math;", "nor is any other static import")]
    public void TheImportGuardDoesNotCryWolf(string code, string why)
    {
        HidesARefusalHelper.IsMatch(ProductionSources.Joined(code)).Should().BeFalse(why);
    }

    [Fact]
    public void EveryReviewerEndingIsInTheInventory()
    {
        // Asked of the TYPE SYSTEM, not of source text: a seventh ending is a seventh subtype, and
        // reflection finds it the moment it exists. `ReviewerOutcome` is public in CoaiMcp.Runners
        // and this suite already constructs three of its cases by name.
        var endings = ReviewerEndings();

        endings.Should().BeEquivalentTo(Recorded().ReviewerEndings,
            "a reviewer ending nothing writes down is a round that failed in a way the page cannot "
            + "show — story 2.3 writes these, and this is what says the list is all of them");
        endings.Should().HaveCountGreaterThan(3, "the scan found almost nothing, so it is looking in "
            + "the wrong assembly and the comparison above means nothing");
    }

    [Fact]
    public void TheInventoryIsWhatTheCensusProduces()
    {
        var census = Census();
        if (Environment.GetEnvironmentVariable(Recording) == "1")
        {
            File.WriteAllText(Inventory, JsonSerializer.Serialize(census, Shape) + "\n");

            // RED on purpose. A run that writes the file it is about to check has checked nothing,
            // and a CI job with this variable set by accident would otherwise go green over a
            // rewritten source of truth. (The code round: silent state mutation in CI.)
            Assert.Fail($"{Inventory} was REGENERATED because {Recording}=1. Re-run without it to "
                + "check the census against the file, and commit the file with the change that "
                + "moved it.");
        }

        File.Exists(Inventory).Should().BeTrue(
            $"{Inventory} is the committed census; generate it with {Recording}=1 if it is missing");

        var recorded = Recorded();

        recorded.Boundary.Should().Be(census.Boundary);
        recorded.ReviewerEndings.Should().Equal(census.ReviewerEndings);
        recorded.Callers.Should().BeEquivalentTo(census.Callers,
            "the counts are information rather than a guard, but a file that disagrees with the scan "
            + $"is a file somebody edited by hand — regenerate it with {Recording}=1");
    }

    [Fact]
    public void TheScanFindsItsKnownInstances_SoAnEmptyCensusCannotPass()
    {
        ProductionSources.Files().Should().HaveCountGreaterThan(200,
            "the walk found almost no production files, so every census here is asserting nothing");
        ProductionSources.FilesMentioning("new ErrorAnswer(").Should().ContainKey(TheBoundary,
            "the scan must SEE the construction it exists to hold to one place — a companion that "
            + "searched the bare type name would go on passing after the shape it looks for changed");
    }

    /// <summary>Every terminal state a reviewer run can be in, by name, in a stable order.</summary>
    private static IReadOnlyList<string> ReviewerEndings() =>
        [.. typeof(ReviewerOutcome).Assembly.GetTypes()
            .Where(type => type.IsSealed && typeof(ReviewerOutcome).IsAssignableFrom(type))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)];

    /// <summary>
    /// What the scan says today: who reaches the boundary, and how many refusals each of them has.
    /// </summary>
    /// <remarks>
    /// The FILES are the guarantee — a third one is a third road — and the counts beside them are
    /// information for the documents, which have quoted a number nothing produced before. They are
    /// the unqualified calls of each service's own <c>Error</c> helper, which is the rule that does
    /// not count the logger and does not count the declaration.
    /// </remarks>
    private static RefusalSites Census() =>
        new(TheBoundary,
            [.. ProductionSources.FilesMentioning("Refusal.Answer(")
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new RefusalCaller(
                    pair.Key,
                    ProductionSources.UnqualifiedCalls(ProductionSources.CodeOf(pair.Key), "Error")))],
            ReviewerEndings());

    /// <summary>What the committed inventory says.</summary>
    private static RefusalSites Recorded() =>
        JsonSerializer.Deserialize<RefusalSites>(File.ReadAllText(Inventory), Shape)
            ?? throw new InvalidOperationException($"{Inventory} parsed to nothing");

    private static readonly JsonSerializerOptions Shape = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>One file that reaches the refusal boundary, and how many refusals it has.</summary>
    public sealed record RefusalCaller(string File, int Refusals);

    /// <summary>The census, as the inventory carries it.</summary>
    public sealed record RefusalSites(
        string Boundary,
        IReadOnlyList<RefusalCaller> Callers,
        IReadOnlyList<string> ReviewerEndings);
}
