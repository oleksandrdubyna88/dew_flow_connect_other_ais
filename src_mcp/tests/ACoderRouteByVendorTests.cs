using CoaiMcp.Core.Roles;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Roles;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s7 t4 — the route of a coder keeps the vendors the table SHIPS and drops the others with the reason; the words "claude only" leave the product. d1: the kept and the dropped, the refusal that names <c>COAI_ROLES_MODELS</c>, a named model on a vendor that is not
/// shipped. d2: the four combinations of ship and refuse for two vendors, with an injected table. d3: the vendor-routing rule still refuses a Claude model on another vendor's CLI, and the words that say what is shipped. d5: the failover entries of the claude row.
/// d4 (the codex and agy main-AI rows) is not built: the owner has not answered which measured model takes which role (the story's todo).
/// </summary>
public sealed class ACoderRouteByVendorTests
{
    private static readonly ModelRef Sonnet = new("sonnet", "claude");
    private static readonly ModelRef Opus = new("opus", "claude");
    private static readonly ModelRef Terra = new("terra", "codex");
    private static readonly ModelRef Argon = new("gemini-4-argon-medium", "agy");

    private static CoderBoundaryTable Table(string codex, bool codexReleased, string agy, bool agyReleased) => CoderBoundaryTable.Parse($$"""
        { "version": 1, "judgement": "j.md", "shared": [],
          "vendors": [ {{Row("codex", codex, codexReleased)}}, {{Row("agy", agy, agyReleased)}} ] }
        """);

    private static string Row(string id, string verdict, bool released) =>
        $$"""{ "id": "{{id}}", "verdict": "{{verdict}}", "results": "r.md", "releases": {{(released ? """[ { "release": "1.0.0", "resultsHeading": "H", "judgementHeading": "J", "sha256": "0000000000000000000000000000000000000000000000000000000000000000" } ]""" : "[]")}}, "properties": [], "pending": [] }""";

    private static CoderVendorSet Set(string codex, bool codexReleased, string agy, bool agyReleased) =>
        CoderVendorSet.Of(Table(codex, codexReleased, agy, agyReleased), new RouteVendor("codex"), new RouteVendor("agy"));

    private static RouteResult.Routed Routed(params ModelRef[] models) => new(models, "the band, then the coder list");

    // ---------- d1 ----------

    [Fact]
    public void D1_TheRoute_KeepsTheShippedVendors_AndDropsTheOthers_WithTheReasonInTheWhy()
    {
        var set = Set("ship", true, "refuse", false);

        var narrowed = CoderRoute.Narrow(Routed(Sonnet, Terra, Argon), vendors: set).Should().BeOfType<RouteResult.Routed>().Subject;

        narrowed.Models.Should().Equal(Sonnet, Terra);
        narrowed.Why.Should().Contain("not shipped for their vendor").And.Contain("agy/gemini-4-argon-medium").And.Contain("shipped: claude, codex");
    }

    [Fact]
    public void D1_ARouteWithNoShippedVendor_IsRefused_AndNamesWhereTheListsAreWritten()
    {
        var narrowed = CoderRoute.Narrow(Routed(Terra, Argon), vendors: Set("refuse", false, "refuse", true));

        var said = narrowed.Should().BeOfType<RouteResult.Refused>().Subject.Reason;
        said.Should().Contain("COAI_ROLES_MODELS").And.Contain("shipped coder vendors are claude").And.Contain("codex/terra").And.NotContain("claude only");
    }

    [Fact]
    public void D1_AModelTheCallerNamed_OnAVendorThatIsNotShipped_IsRefusedWithASentenceThatNamesTheVendor()
    {
        var said = CoderRoute.Narrow(Routed(Terra, Sonnet), "terra", Set("refuse", true, "ship", false)).Should().BeOfType<RouteResult.Refused>().Subject.Reason;

        said.Should().StartWith("the coder cannot run terra on codex: ").And.Contain("the verdict for 'codex' is refuse").And.NotContain("claude only");
        CoderRoute.Narrow(Routed(Terra, Sonnet), "terra", Set("ship", true, "ship", false)).Should().BeOfType<RouteResult.Routed>().Which.Models.Should().Equal(Terra, Sonnet);
    }

    // ---------- d2 ----------

    [Theory]
    [InlineData("ship", true, "ship", true, "claude,codex,agy")]
    [InlineData("ship", true, "refuse", true, "claude,codex")]
    [InlineData("refuse", true, "ship", true, "claude,agy")]
    [InlineData("refuse", false, "refuse", false, "claude")]
    [InlineData("ship", false, "ship", false, "claude,codex,agy")]
    public void D2_EachCombination_OfShipAndRefuse_ForTwoVendors_RoutesExactlyTheShippedVendors(string codex, bool codexReleased, string agy, bool agyReleased, string expected)
    {
        var narrowed = CoderRoute.Narrow(Routed(Sonnet, Opus, Terra, Argon), vendors: Set(codex, codexReleased, agy, agyReleased)).Should().BeOfType<RouteResult.Routed>().Subject;

        narrowed.Models.Select(model => model.Vendor).Distinct().Should().Equal(expected.Split(','), "the route holds exactly the shipped vendors, in the router's order");
        var shipped = expected.Split(',');
        narrowed.Models.Should().OnlyContain(model => shipped.Contains(model.Vendor), "a refused vendor is in no route");
    }

    [Fact]
    public void D2_TheProductsOwnTable_ShipsNeitherVendorYet_SoAProductRouteIsClaudeAlone()
    {
        CoderRoute.Narrow(Routed(Sonnet, Terra, Argon)).Should().BeOfType<RouteResult.Routed>().Which.Models.Should().Equal(Sonnet);
    }

    // ---------- d3 ----------

    [Theory]
    [InlineData("sonnet", "codex")]
    [InlineData("claude-opus-5-5", "agy")]
    [InlineData("opus", "codex")]
    public void D3_AClaudeModelOnCodexOrAgy_IsStillRefusedByTheVendorRoutingRule(string model, string vendor)
    {
        VendorRouting.Refusal(model, vendor).Should().NotBeEmpty();
    }

    [Fact]
    public void D3_TheWordsClaudeOnly_AreNotInTheRefusalsOfTheCoderRoadNorTheToolText()
    {
        var root = RepoPaths.Root();
        foreach (var file in new[] { "src/Server/RoleJobs/CoderRoute.cs", "runners/Consultation/CoderLaunches.cs", "src/Tools.cs" })
        {
            File.ReadAllText(Path.Combine(root, "src_mcp", file)).Should().NotContain("claude only", $"{file} says which vendors are shipped, not 'claude only'");
        }

        File.ReadAllText(Path.Combine(root, "src_mcp", "src", "Tools.cs")).Should().Contain("coder boundary").And.Contain("shipped", "the tool description names what the coder runs on");
    }

    // ---------- d5 ----------

    [Fact]
    public void D5_TheClaudeRow_HasTheFailoverEntriesOfSectionD_BehindTheRowsOfTheirVendors()
    {
        var claude = RoleDefaults.Current.MainAi["claude"];
        string Names(string role) => string.Join(",", claude.Roles.Single(one => one.Role == role).Models.Select(model => $"{model.Model}@{model.Vendor}{(model.WhenVendorRow.Length > 0 ? "?" + model.WhenVendorRow : string.Empty)}"));

        Names("scout").Should().Be("sonnet@claude,haiku@claude,terra@codex?codex");
        Names("coder").Should().Be("sonnet@claude,opus@claude,terra@codex?codex,gemini-4-argon-medium@agy?agy");
    }

    [Fact]
    public void D5_TheRolesTool_AnswersAnEntryOnlyWhereItsVendorHasARow()
    {
        string Coder(RolesResult answer) => string.Join(",", answer.Roles.Single(role => role.Role == "coder").Models.Select(model => $"{model.Model}@{model.Vendor}"));

        Coder(RolesAnswer.Compute(_ => null, "claude-code", "1", claudeHook: false)).Should().Be("sonnet@claude,opus@claude,terra@codex", "the default configuration has a codex row and no agy row: the agy entry is not offered");
    }

    /// <summary>A vendor of these tests: claude's unit and argv, a prompt of its own. It stands in for a real vendor in a set, and launches nothing.</summary>
    private sealed class RouteVendor(string id) : ICoderVendor
    {
        public string Id => id;

        public string PromptId => $"coder-{id}";

        public string PromptResource => CoderVendors.Claude.PromptResource;

        public CoderSignInRecipe SignIn => CoderVendors.Claude.SignIn;

        public IConsultantRuntime Runtime => CoderVendors.Claude.Runtime;

        public string FixClause => "x";

        public bool PointsAtInstructionBlock => true;

        public CoderTurnVerdict CheckTurn(ProcessResult process, string expectedSession) => new CoderTurnVerdict.Fine();

        public Task<CoderCapability> ProbeAsync(IProcessLauncher launcher, string executable, string workingDirectory, CancellationToken ct) => CoderVendors.Claude.ProbeAsync(launcher, executable, workingDirectory, ct);

        public IReadOnlyList<string> FreshArgv(string model, string worktree) => CoderVendors.Claude.FreshArgv(model, worktree);

        public IReadOnlyList<string> ResumedArgv(string model, string worktree, string sessionId) => CoderVendors.Claude.ResumedArgv(model, worktree, sessionId);

        public IReadOnlyList<string> UnitProperties(BoundaryHost host, BoundaryJob job, CoderProgram program, TimeSpan runtimeMax) => CoderVendors.Claude.UnitProperties(host, job, program, runtimeMax);

        public string WorkingDirectory(BoundaryJob job) => CoderVendors.Claude.WorkingDirectory(job);

        public IReadOnlyList<KeyValuePair<string, string>> Environment(BoundaryHost host, BoundaryJob job) => CoderVendors.Claude.Environment(host, job);

        public CoderHomeLayout Home(BoundaryJob job) => CoderVendors.Claude.Home(job);

        public CoderProgram Program(string executable, string path, BoundaryJob job) => CoderVendors.Claude.Program(executable, path, job);
    }
}
