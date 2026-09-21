using System.Reflection;
using CoaiMcp.Server;
using CoaiMcp.ServiceDefaults;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// There is exactly ONE append in this repository, and an ALLOWLIST says which two things reach it.
/// </summary>
/// <remarks>
/// <para><b>Why an allowlist and not a name count.</b> The first draft of story 1.4 proposed counting
/// <c>ServerNotices.Name</c> in one production file, and a reviewer was right that it proves
/// nothing: a second production caller could reach <c>JsonlLedger.AppendLine</c> with a computed
/// path, bypass <c>ServerNoticeLine.Of</c>, and leave every test green. So this is the ratchet
/// shape the repository already uses for <c>notification-sites.json</c>: every production call
/// site is enumerated and compared with a list of exactly two, and a third fails with its file
/// named.</para>
/// <para><b>What the code round changed.</b> The scan read one LINE at a time, and codex named two
/// ways past it. A call written across two lines — which a long qualified name invites — was
/// invisible, so the scan now searches a file's code as one string with its newlines collapsed. And
/// <c>using static CoaiMcp.ServiceDefaults.JsonlLedger;</c> makes <c>AppendLine(path, line)</c> legal
/// with the type nowhere on the line, which no spelling-based scan can catch — so that import is
/// refused outright, by the test below.</para>
/// <para>The third road was closed by the COMPILER rather than by a test:
/// <see cref="ResolvedDataDir"/>'s constructor is private since the code round, so
/// <c>ServerNotices.Append(new(DataRootFor(env)), notice)</c> — a target-typed <c>new</c> that names
/// the type nowhere — does not build. What remains to count is its one named factory.</para>
/// <para>Comments are skipped throughout, so a sentence ABOUT the append is not a call to it.
/// <c>NoticeTool</c> (<c>src_mcp/tests_notices</c>) calls the append too and is not scanned: it is a
/// test asset built by the solution and never published, and its call IS the measurement.</para>
/// </remarks>
public sealed class TheOneAppendTests
{
    private static readonly string[] SanctionedAppenders =
    [
        "src_mcp/runners/Reviewers/UsageLedger.cs",
        "src_mcp/src/Server/ServerNotices.cs",
    ];

    [Fact]
    public void ExactlyTwoProductionCallSites_ReachJsonlLedgerAppendLine()
    {
        var sites = ProductionSources.FilesMentioning("JsonlLedger.AppendLine(");

        sites.Keys.Should().BeEquivalentTo(SanctionedAppenders,
            "the spending ledger and the notices writer are the two sanctioned callers; anything else "
            + "is a second road onto the disk that bypasses the serialiser and the path rule — "
            + "found: {0}", string.Join(", ", sites.Keys));
        sites.Values.Should().AllBeEquivalentTo(1, "each of them appends in one place");
    }

    [Fact]
    public void NoProductionFileImportsTheLedgerStatically_WhichWouldHideACallFromTheCensus()
    {
        // codex's second bypass: `using static ...JsonlLedger;` makes `AppendLine(path, line)` legal
        // with the type named nowhere. No spelling-based census can see that call, so the import is
        // refused instead — which is a cheaper rule than a semantic scan and is exactly as strict.
        var imports = ProductionSources.FilesMentioning("using static");

        imports.Keys.Where(file => ProductionSources.CodeOf(file).Contains("JsonlLedger", StringComparison.Ordinal))
            .Should().BeEmpty("the census counts a QUALIFIED call; a static import hides one from it");
    }

    [Fact]
    public void TheNoticesFileName_IsSpelledInOneProductionFile()
    {
        // The literal, quoted, so a message that MENTIONS the file (CredentialWords' refusal does)
        // is not a second spelling of its name — and asked of the SPELLING view, because a name in
        // this codebase is a string literal and the code view now drops what is inside one. Story
        // 2.1's lexer turned that on and this test went red, which is how a scanner shared between
        // two censuses is supposed to fail: loudly, in the census whose question changed meaning.
        var spellings = ProductionSources.FilesSpelling("\"server-notices.jsonl\"");

        spellings.Keys.Should().Equal(["src_mcp/src/Server/ServerNotices.cs"],
            "the extension spells the same name once in notificationsFile.ts, and a second spelling "
            + "here is how the two halves come to write and read different files");
    }

    [Fact]
    public void TheResolvedDirectory_IsMintedByTheResolverAndNothingElse()
    {
        // The SOURCE: the one named door appears in one production file.
        ProductionSources.FilesMentioning("ResolvedDataDir.For(").Keys.Should().Equal(["src_mcp/src/Server/PanelSettings.cs"],
            "PanelSettings.DataDirectoryFor mints the type; a second minting site can hand the writer "
            + "a directory whose guarantee came from a resolver it does not share");

        // The COMPILER: there is no other door. A public or internal constructor would let a
        // target-typed `new` mint one with the type named nowhere, which is the form the code round
        // found and which no scan above could ever have seen.
        typeof(ResolvedDataDir)
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Should().OnlyContain(constructor => constructor.IsPrivate,
                "a constructor anything else can reach is a minting site no census can count");

        // The ASSEMBLY: the only members of coai-mcp that ANSWER the type are the resolver and its
        // pass-through.
        var answering = typeof(PanelSettings).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.ReturnType == typeof(ResolvedDataDir))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .ToList();

        answering.Should().BeEquivalentTo(
            [$"{nameof(PanelSettings)}.{nameof(PanelSettings.DataDirectoryFor)}", $"{nameof(SettingsFile)}.{nameof(SettingsFile.DataDirFrom)}"]);
    }

    [Fact]
    public void TheScansStillFindTheirKnownInstances_SoAReformatCannotTurnAGuardIntoAPass()
    {
        // The companions, in one place: each equality above would also fail on an empty scan, but
        // saying so explicitly is what makes the file readable when one of them goes red.
        ProductionSources.FilesMentioning("JsonlLedger.AppendLine(").Should().HaveCount(2);
        ProductionSources.FilesSpelling("\"server-notices.jsonl\"").Should().HaveCount(1);
        ProductionSources.FilesMentioning("ResolvedDataDir.For(").Should().HaveCount(1);
        ProductionSources.CodeOf("src_mcp/src/Server/ServerNotices.cs").Should().Contain("ServerNoticeLine.Of(",
            "the notices writer hands the serialiser's line to the append, which is what makes the "
            + "bytes test and this census say the same thing");
    }

    [Fact]
    public void TheCensusVisitsEverySourceRoot_NotAListSomebodyHasToRemember()
    {
        // The code round's finding, as its own check: a project added outside a hand-written list is
        // never scanned, and a third write road inside it leaves every test above green. The roots
        // are discovered, so this asserts the DISCOVERY finds what is on disk rather than asserting
        // a list against itself.
        var root = NoSourceFileCarriesAControlByteTests.RepositoryRoot();
        var found = ProductionSources.Roots().Select(directory => Path.GetFileName(directory)).ToList();

        found.Should().BeEquivalentTo(
            Directory.EnumerateDirectories(root, "src_*").Select(Path.GetFileName),
            "every src_* directory is scanned, including one added tomorrow");
        found.Should().HaveCountGreaterThan(2, "this repository has several source roots; finding "
            + "one or none means the walk started in the wrong place and every census above is empty");
        ProductionSources.Files().Should().HaveCountGreaterThan(200,
            "and the roots must contain production files, or the scans are asserting nothing");
        ProductionSources.Files().Should().NotContain(file => file.Contains("/tests", StringComparison.Ordinal),
            "a test project is not production: its calls to the append are the tests OF the append");
    }

    // The scanner's OWN behaviour — the join rule, the two views, the interpolation holes — is
    // asserted in ProductionSourcesTests. It answers two censuses now, and its rules were being
    // tested inside whichever one happened to earn them.
}
