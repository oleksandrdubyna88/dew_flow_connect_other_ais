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
/// <para><b>What the scan cannot see, stated.</b> It reads code lines for the explicit spelling.
/// A comment is skipped; a call split so that <c>JsonlLedger.AppendLine(</c> never appears on one
/// line would evade it, and so would a target-typed <c>new(</c> for the minting check — which is
/// why the type's constructor is <c>internal</c> and only <c>coai-mcp</c> is granted it, and why
/// the minting check also asks the ASSEMBLY which methods answer the type. The companions assert
/// that each scan still finds its known instances, so a reformat cannot turn a guard into a pass.</para>
/// <para><c>NoticeTool</c> (<c>src_mcp/tests_notices</c>) calls the append too and is not scanned:
/// it is a test asset built by the solution and never published, and its call IS the measurement.</para>
/// </remarks>
public sealed class TheOneAppendTests
{
    /// <summary>Every directory that ships in a binary. Tests, the fake CLI and the notice tool are not here.</summary>
    private static readonly string[] ProductionRoots =
    [
        "src_mcp/src", "src_mcp/core", "src_mcp/runners", "src_mcp/service_defaults", "src_mcp/storage",
        "src_mcp/normalizer", "src_server/src", "src_bugs/src", "src_bench/CoaiBench",
    ];

    private static readonly string[] SanctionedAppenders =
    [
        "src_mcp/runners/Reviewers/UsageLedger.cs",
        "src_mcp/src/Server/ServerNotices.cs",
    ];

    [Fact]
    public void ExactlyTwoProductionCallSites_ReachJsonlLedgerAppendLine()
    {
        var sites = CodeLinesContaining("JsonlLedger.AppendLine(");

        sites.Keys.Should().BeEquivalentTo(SanctionedAppenders,
            "the spending ledger and the notices writer are the two sanctioned callers; anything else "
            + "is a second road onto the disk that bypasses the serialiser and the path rule — "
            + "found: {0}", string.Join(", ", sites.Keys));
        foreach (var (file, lines) in sites)
        {
            lines.Should().HaveCount(1, $"{file} appends in one place");
        }
    }

    [Fact]
    public void TheNoticesFileName_IsSpelledInOneProductionFile()
    {
        // The literal, quoted, so a message that MENTIONS the file (CredentialWords' refusal does)
        // is not a second spelling of its name.
        var spellings = CodeLinesContaining("\"server-notices.jsonl\"");

        spellings.Keys.Should().Equal(["src_mcp/src/Server/ServerNotices.cs"],
            "the extension spells the same name once in notificationsFile.ts, and a second spelling "
            + "here is how the two halves come to write and read different files");
    }

    [Fact]
    public void TheResolvedDirectory_IsMintedByTheResolverAndNothingElse()
    {
        // Two halves. The SOURCE: the explicit constructor call appears in one production file.
        CodeLinesContaining("new ResolvedDataDir(").Keys.Should().Equal(["src_mcp/src/Server/PanelSettings.cs"],
            "PanelSettings.DataDirectoryFor mints the type; a second minting site can hand the writer "
            + "a directory whose guarantee came from a resolver it does not share");

        // The ASSEMBLY: the only members of coai-mcp that ANSWER the type are the resolver and its
        // pass-through. A target-typed `new(` hides from the scan above and not from this.
        var answering = typeof(PanelSettings).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            .Where(m => m.ReturnType == typeof(ResolvedDataDir))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        answering.Should().BeEquivalentTo(
            [$"{nameof(PanelSettings)}.{nameof(PanelSettings.DataDirectoryFor)}", $"{nameof(SettingsFile)}.{nameof(SettingsFile.DataDirFrom)}"]);
    }

    [Fact]
    public void TheScansStillFindTheirKnownInstances_SoAReformatCannotTurnAGuardIntoAPass()
    {
        // The companions, in one place: each equality above would also fail on an empty scan, but
        // saying so explicitly is what makes the file readable when one of them goes red.
        CodeLinesContaining("JsonlLedger.AppendLine(").Should().HaveCount(2);
        CodeLinesContaining("\"server-notices.jsonl\"").Should().HaveCount(1);
        CodeLinesContaining("new ResolvedDataDir(").Should().HaveCount(1);
        CodeLinesContaining("JsonlLedger.AppendLine(")["src_mcp/src/Server/ServerNotices.cs"]
            .Should().ContainSingle().Which.Should().Contain("ServerNoticeLine.Of(",
                "the notices writer hands the serialiser's line to the append on the same line, "
                + "which is what makes the bytes test and this census say the same thing");
    }

    /// <summary>Every CODE line (comments skipped) under the production roots that contains the text, by repository-relative file.</summary>
    private static Dictionary<string, List<string>> CodeLinesContaining(string text)
    {
        var root = NoSourceFileCarriesAControlByteTests.RepositoryRoot();
        var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var relative in ProductionRoots)
        {
            var directory = Path.Combine(root, relative);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (IsGenerated(file))
                {
                    continue;
                }

                var hits = File.ReadAllLines(file)
                    .Where(line => IsCode(line) && line.Contains(text, StringComparison.Ordinal))
                    .ToList();
                if (hits.Count > 0)
                {
                    found[Path.GetRelativePath(root, file).Replace('\\', '/')] = hits;
                }
            }
        }

        return found;
    }

    private static bool IsCode(string line) =>
        line.TrimStart() is { Length: > 0 } code && !code.StartsWith("//", StringComparison.Ordinal);

    private static bool IsGenerated(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
