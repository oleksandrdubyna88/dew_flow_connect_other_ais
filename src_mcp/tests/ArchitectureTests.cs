using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;
using CoaiMcp.Core.Findings;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// The core stays pure — the epic-02 DoD line, held by a test instead of a habit. Process and
/// network use surface as distinct assembly references, so their absence is checkable; filesystem
/// types live inside the core library set and are held out by review instead.
/// </summary>
public sealed class ArchitectureTests
{
    [Fact]
    public void Core_ReferencesNoProcessOrNetworkAssemblies()
    {
        var references = typeof(Finding).Assembly.GetReferencedAssemblies().Select(a => a.Name);

        references.Should().NotContain(
            ["System.Diagnostics.Process", "System.Net.Http", "System.Net.Sockets", "System.Net.Primitives"],
            "the pure core must not spawn, call, or listen — that is what the runners are for");
    }

    /// <summary>
    /// tree-sitter is named by exactly one project, and `CoaiMcp.Normalizer` is it.
    /// </summary>
    /// <remarks>
    /// <para>The third condition of the operator's approval of <c>TreeSitter.DotNet</c> on
    /// 2026-09-15: an individual's native dependency may be taken, but it may not spread. Everything
    /// else speaks to <c>IAstNormalizer</c>, which lives in the pure core and mentions no parser, no
    /// language, no node and no P/Invoke.</para>
    /// <para>It is also what keeps the declined alternative cheap. If this dependency ever has to go,
    /// a parser per language — Roslyn for C#, the TypeScript compiler API for the rest — is a second
    /// implementation of one interface rather than a second design.</para>
    /// <para>A reference is the checkable form of "names it": a project that so much as declares a
    /// <c>TreeSitter.Node</c> acquires one, and a project that does not cannot have leaked.</para>
    /// </remarks>
    [Fact]
    public void OnlyTheNormalizerNamesTreeSitter()
    {
        var elsewhere = new[]
        {
            typeof(Finding).Assembly,                               // CoaiMcp.Core
            typeof(Runners.Processes.ProcessLauncher).Assembly,     // CoaiMcp.Runners
            typeof(Store.RoundsDb).Assembly,                        // coai-mcp itself
        };

        foreach (var assembly in elsewhere)
        {
            assembly.GetReferencedAssemblies().Select(a => a.Name).Should().NotContain(
                "TreeSitter",
                $"{assembly.GetName().Name} must reach the parser through IAstNormalizer, never directly");
        }

        typeof(Normalizer.TreeSitterNormalizer).Assembly.GetReferencedAssemblies().Select(a => a.Name)
            .Should().Contain("TreeSitter", "and the one project that implements the seam does name it");
    }

    /// <summary>
    /// The round engine asks the MCP-facing service nothing: what it needs is handed to it.
    /// </summary>
    /// <remarks>
    /// <para>The rule <c>todo/PLAN_the_round_engine_leaves_the_panel_service.md</c> names: a moved member
    /// that still reaches back into <c>PanelService</c> has not found its seam. Every unit the engine is
    /// made of is held to it, not only <c>RoundEngine</c> itself — a back-reference in the roster or the
    /// orders is the same road one hop away. The files are the ones under <c>Server/Rounds/</c>, found
    /// rather than listed, so a unit added there tomorrow is held to it too.</para>
    /// <para>Read from the CODE with comments and string contents removed, so a sentence that names the
    /// service is not a reference. The companion below proves the scan can see the name at all.</para>
    /// </remarks>
    [Fact]
    public void TheRoundEngine_DoesNotReferenceThePanelService()
    {
        foreach (var file in RoundEngineFiles())
        {
            NamesThePanelService.IsMatch(ProductionSources.CodeOf(file)).Should().BeFalse(
                $"{file} must be handed what it needs rather than reach back into the service that owns the MCP surface");
        }
    }

    [Fact]
    public void TheEngineScan_FindsItsFiles_AndSeesTheNameWhereItIsWritten()
    {
        RoundEngineFiles().Should().Contain(RoundEngineFile,
            "the scan found no engine files, so the prohibition above asserts nothing");
        NamesThePanelService.IsMatch(ProductionSources.CodeOf(ThePanelServiceFile)).Should().BeTrue(
            "the same pattern must match where the name IS written, or it could never fail");
    }

    private const string RoundEngineFile = "src_mcp/src/Server/Rounds/RoundEngine.cs";

    private const string ThePanelServiceFile = "src_mcp/src/Server/PanelService.cs";

    /// <summary>The name as a whole word, so <c>PanelServiceHost</c> is not it.</summary>
    private static readonly Regex NamesThePanelService =
        new(@"\bPanelService\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static IReadOnlyList<string> RoundEngineFiles() =>
        [.. ProductionSources.Files().Where(file => file.StartsWith("src_mcp/src/Server/Rounds/", StringComparison.Ordinal))];
}
