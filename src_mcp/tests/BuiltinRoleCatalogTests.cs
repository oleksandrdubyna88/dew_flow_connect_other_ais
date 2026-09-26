using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The built-in catalog is the seed file, and nothing else.
/// </summary>
/// <remarks>
/// <para>The roles used to be an enum, five constants and a 26-row array in this half, and a
/// hand-typed mirror of the same rows in the extension, held level by a test that regexed this
/// half's C# source. Now both halves load <c>shared/builtin-roles.json</c> — this half embeds it,
/// the extension generates from it — and each half asserts its own LOADER against the file. A row
/// edited in the seed is a row both programs see; a loader that drops or reorders one goes red here,
/// in the words of the field it lost.</para>
/// <para>Read from disk the way <see cref="TeamServerUrlVectorTests"/> reads its vectors: from the
/// test binary up to the repository root, so the comparison is against the checked-in file and not
/// against the copy the build embedded a moment ago.</para>
/// </remarks>
public sealed class BuiltinRoleCatalogTests
{
    // Named for the FILE rather than for the seed types the core declares: those are visible here
    // through InternalsVisibleTo, and two `SeedRole`s in one file is a question nobody should have
    // to answer while reading an assertion.
    private sealed record FilePrompt(string Id, string Label, string Purpose);

    private sealed record FileRole(string Id, string Name, string Stage, bool ProgrammingTask, IReadOnlyList<FilePrompt> Prompts);

    private static readonly IReadOnlyList<FileRole> Seed = Load();

    private static IReadOnlyList<FileRole> Load()
    {
        // tests/bin/<cfg>/net10.0 → the repository root, then the shared folder both sides read.
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "builtin-roles.json"));

        using var file = File.OpenRead(path);
        using var parsed = JsonDocument.Parse(file);

        return [.. parsed.RootElement.GetProperty("roles").EnumerateArray().Select(r => new FileRole(
            r.GetProperty("id").GetString() ?? "",
            r.GetProperty("name").GetString() ?? "",
            r.GetProperty("stage").GetString() ?? "",
            r.GetProperty("programmingTask").GetBoolean(),
            [.. r.GetProperty("prompts").EnumerateArray().Select(p => new FilePrompt(
                p.GetProperty("id").GetString() ?? "",
                p.GetProperty("label").GetString() ?? "",
                p.GetProperty("purpose").GetString() ?? ""))]))];
    }

    [Fact]
    public void TheEmbeddedSeed_IsTheFileInShared_FieldForField()
    {
        var loaded = RoleCatalog.Builtin.Roles;

        loaded.Select(r => r.Id).Should().Equal(Seed.Select(r => r.Id),
            "the catalog runs the roles in the seed's order, and a loader that reorders them changes every round");

        foreach (var (role, expected) in loaded.Zip(Seed))
        {
            role.Name.Should().Be(expected.Name, $"{role.Id}");
            role.Stage.Should().Be(expected.Stage, $"{role.Id}");
            role.ProgrammingTask.Should().Be(expected.ProgrammingTask, $"{role.Id}");
            role.BuiltIn.Should().BeTrue($"{role.Id} came out of the seed");
            role.Active.Should().BeTrue($"{role.Id}: the seed ships every role switched on");

            role.Prompts.Select(p => (p.Id, p.Label, p.Purpose))
                .Should().Equal(expected.Prompts.Select(p => (p.Id, p.Label, p.Purpose)),
                    $"{role.Id}'s prompts, in the seed's order");
            role.Prompts.Should().OnlyContain(p => p.Role == role.Id && p.BuiltIn,
                $"{role.Id}: every seed prompt belongs to its role and is marked shipped");
        }
    }

    [Fact]
    public void TheCatalog_IsTodaysEightRolesAndThirtyPrompts_InTodaysOrder()
    {
        // The characterization of what `PromptCatalog.All` and `PanelConfig.AllRoles` were the day
        // the catalog became data. Ids are permanent — settings keys, session files and the rounds
        // database are keyed by them — so a change here is a migration, not an edit.
        //
        // Spelled as LITERALS on purpose. Asserting them through `RoleCatalog.PlanRole` and its
        // siblings would pass for a rename that edited the seed and the constant together, which is
        // exactly the change that must not be green: every `COAI_ROUNDS_ARCHITECTURE` and every row
        // already in a coai.db still says the old word. (codex, A1's second code round.)
        RoleCatalog.Builtin.Roles.Select(r => r.Id).Should().Equal(
            "PlanCritique", "Conventions", "Architecture", "SecurityReliability", "UxDxPerformance",
            // Plan 4's two, APPENDED — the order is the order a round runs the roles and the order
            // the panel draws them, so inserting anywhere else would reshuffle five boxes a person
            // has learned the positions of.
            "DocumentReview", "DocumentSummary",
            // The feature stage's one, appended for the same reason (S2.1 of the feature-review plan).
            "FeatureReview");

        // And the constants are those literals, so the rest of the file may keep reading them.
        (RoleCatalog.PlanRole, RoleCatalog.ConventionsRole, RoleCatalog.ArchitectureRole,
                RoleCatalog.SecurityRole, RoleCatalog.UxDxRole, RoleCatalog.ConventionsId)
            .Should().Be(("PlanCritique", "Conventions", "Architecture", "SecurityReliability",
                "UxDxPerformance", "conventions"));

        // Twenty-five, not twenty-six: four roles of six lenses and the conventions role's one.
        // The plan said twenty-six until this assertion was watched failing, which is the whole
        // point of pinning a number instead of a shape. Twenty-nine since plan 4: the document
        // role's three lenses and the summary role's one. Thirty since the feature stage: its one
        // role has one prompt, because a whole-feature review is one question until a lens earns
        // its place in a measurement.
        RoleCatalog.Builtin.Roles.Sum(r => r.Prompts.Count).Should().Be(30);
        RoleCatalog.Builtin.Roles.Select(r => r.Prompts.Count).Should().Equal(
            [6, 1, 6, 6, 6, 3, 1, 1],
            "six choices per section, and the conventions role has one — the shape the operator asked for; "
            + "plan 4's document role has three lenses and its summary role one; the feature role one");

        // And each of the twenty-five by NAME. A prompt id is as permanent as a role id: it is the
        // file under `<dataDir>/prompts/`, the value stored in COAI_PROMPTS_PER_ROUND, and the
        // column in every rounds-database row already written. While `PromptCatalog` existed it was
        // the oracle for these; with it gone, both loaders read one seed and would agree with each
        // other about a typo. The embedded-text check catches a renamed id from the other side —
        // there would be no `<id>.md` — but it cannot say which id was meant. (codex, C2's plan round.)
        RoleCatalog.Builtin.Roles.SelectMany(r => r.Prompts).Select(p => p.Id).Should().Equal([
            "plan-critique", "plan-assumptions", "plan-human-path", "plan-data-loss", "plan-operability", "plan-scope-creep",
            "conventions",
            "architecture", "arch-boundaries", "arch-evolution", "arch-coupling", "arch-naming", "arch-testability",
            "security-reliability", "sec-memory-leaks", "sec-attack", "sec-blast-radius", "sec-concurrency", "sec-supply-chain",
            "uxdx-performance", "perf-scale", "dx-ergonomics", "perf-first-run", "perf-wasted-work", "ux-undo",
            "document-review", "document-decisions", "document-reader", "document-summary",
            "feature-review",
        ]);

        RoleCatalog.Builtin.ById(RoleCatalog.PlanRole)!.Stage.Should().Be(RoleStages.Plan);
        RoleCatalog.Builtin.ById(RoleCatalog.FeatureRole)!.Stage.Should().Be(RoleStages.Feature);
        RoleCatalog.Builtin.Roles.Where(r => r.Id != RoleCatalog.PlanRole && r.Id != RoleCatalog.FeatureRole)
            .Should().OnlyContain(r => r.Stage == RoleStages.Result,
                "every shipped role but the plan one and the feature one belongs to the result stage");

        // And the BUCKET each shipped role is in, which is what a round is now selected by. Pinned
        // as a list rather than as a predicate, because the mistake this guards is a role changing
        // kind — which no predicate about "the document ones" could catch, since it would move with
        // the role it was describing.
        RoleCatalog.Builtin.Roles.Select(r => r.Bucket).Should().Equal([
            RoleBuckets.PlanCode,
            RoleBuckets.ResultCode, RoleBuckets.ResultCode, RoleBuckets.ResultCode, RoleBuckets.ResultCode,
            RoleBuckets.ResultDocument, RoleBuckets.ResultDocument,
            RoleBuckets.FeatureCode,
        ]);
    }

    /// <summary>The Defaults dictionary — the third of the three sites that pick a default — hands the feature role its OWN instance.</summary>
    [Fact]
    public void TheShippedFeatureRole_TakesTheFeatureDefault_NotTheCodeOne()
    {
        new PanelConfig().Roles[RoleCatalog.FeatureRole].Should().BeSameAs(PanelConfig.FeatureDefault);
        new PanelConfig().Roles[RoleCatalog.ArchitectureRole].Should().BeSameAs(PanelConfig.CodeDefault);
        new PanelConfig().Roles[RoleCatalog.PlanRole].Should().BeSameAs(PanelConfig.PlanDefault);
        new PanelConfig().For(RoleCatalog.FeatureRole).Should().BeSameAs(PanelConfig.FeatureDefault);
        // And a role of a person's own at that stage, with no gate written for it: its stage's shipped one.
        var seams = RoleComposition.Compose([new RoleEntry("Seams", Stage: RoleStages.Feature, Prompts: [new PromptEntry("seams-general")])]);
        new PanelConfig { Catalog = seams }.For("Seams").Should().BeSameAs(PanelConfig.FeatureDefault);
    }

    [Fact]
    public void EveryRole_HasItsGeneralPromptFirst_AndEveryPromptIdIsUnique()
    {
        foreach (var role in RoleCatalog.Builtin.Roles)
        {
            role.Prompts.Should().NotBeEmpty($"{role.Id}");
            role.General.Should().BeSameAs(role.Prompts[0], $"{role.Id}: the general prompt is the first one");
            role.Prompts.Count(p => p.Universal).Should().Be(1, $"{role.Id}: exactly one default");
            role.Prompts[0].Universal.Should().BeTrue($"{role.Id}");
            RoleCatalog.Builtin.UniversalFor(role.Id).Should().BeSameAs(role.General);
        }

        var ids = RoleCatalog.Builtin.Roles.SelectMany(r => r.Prompts).Select(p => p.Id).ToList();
        ids.Should().OnlyHaveUniqueItems("a prompt id is a file name under <dataDir>/prompts/, and two roles sharing one would read one file");
        ids.Should().OnlyContain(id => System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]*$"),
            "every shipped id is the slug shape a custom id is held to");
    }

    [Fact]
    public void EveryShippedPrompt_IsEmbeddedInTheBinary()
    {
        // The release asset carries exactly one file, and the first real run died on every
        // review_plan because the prompts were content files the release never packaged. The seed
        // is the list; the binary must carry a body for each entry on it.
        var embedded = typeof(RolePrompts).Assembly.GetManifestResourceNames();

        foreach (var prompt in RoleCatalog.Builtin.Roles.SelectMany(r => r.Prompts))
        {
            embedded.Should().Contain($"CoaiMcp.prompts.{prompt.Id}.md",
                $"the seed names '{prompt.Id}' and the binary must ship its text");
        }
    }

    [Fact]
    public void ForRound_IsTheChoice_OrTheGeneralPrompt_ForEveryRole()
    {
        // The same rule `PromptCatalog.ForRound` had: an explicit, known choice for that round wins;
        // anything else — blank, unknown, a round past the list — is the role's general prompt.
        foreach (var role in RoleCatalog.Builtin.Roles)
        {
            RoleCatalog.Builtin.ForRound(role.Id, 1, []).Should().BeSameAs(role.General, $"{role.Id}, nothing chosen");
            RoleCatalog.Builtin.ForRound(role.Id, 3, ["", "no-such-prompt"]).Should().BeSameAs(role.General, $"{role.Id}, unknown and past the list");

            if (role.Prompts.Count > 1)
            {
                var lens = role.Prompts[1];
                RoleCatalog.Builtin.ForRound(role.Id, 2, ["", lens.Id]).Should().BeSameAs(lens, $"{role.Id}, an explicit choice");
            }
        }
    }

    [Fact]
    public void ById_IsCaseInsensitive_LikeTheEnumParseWas()
    {
        RoleCatalog.Builtin.ById("architecture")!.Id.Should().Be(RoleCatalog.ArchitectureRole);
        RoleCatalog.Builtin.ById("no-such-role").Should().BeNull();
    }

    [Fact]
    public void AnInactiveRole_IsInTheCatalog_AndInNoRound()
    {
        // `Active` is documented as "the role takes part at all; off is still in the catalog", and
        // a member that ignores it would schedule a reviewer the operator switched off. The panel's
        // own switch is a different key (`COAI_ENABLED_*` → `RoleGate.Enabled`); this one is the
        // catalog's, and the round must honour both. Raised by two reviewers on A1's code round.
        var off = RoleCatalog.Builtin.Roles.Single(r => r.Id == RoleCatalog.ArchitectureRole) with { Active = false };
        var catalog = RoleCatalog.From([.. RoleCatalog.Builtin.Roles.Select(r => r.Id == off.Id ? off : r)]);

        catalog.ById(RoleCatalog.ArchitectureRole).Should().NotBeNull("an inactive role is still catalogued");
        catalog.InBucket(RoleBuckets.ResultCode).Should().NotContain(RoleCatalog.ArchitectureRole)
            .And.Contain(RoleCatalog.ConventionsRole, "the roles left on are untouched");
    }

    /// <summary>
    /// A seed this binary cannot trust is a broken BUILD, and it says so loudly.
    /// </summary>
    /// <remarks>
    /// <para>The seed is embedded in the assembly and shipped by us, so every case here is a
    /// release that should never have been cut — the one class of failure doctrine 5 still lets
    /// throw. What a PERSON writes into <c>COAI_ROLES</c> is the opposite case and never reaches
    /// here: story A2's composition answers it with a dropped row and a sentence.</para>
    /// <para>Written against a hand-built seed rather than by corrupting the embedded file,
    /// because the validation is the unit under test and the resource is not reachable from a
    /// test without rebuilding the assembly.</para>
    /// </remarks>
    public sealed class ABrokenSeed
    {
        // A prompt of its own per role, because prompt ids are unique across the WHOLE catalog —
        // a shared default here made the well-formed case fail on the rule it was meant to pass.
        private static SeedPrompt PromptFor(string roleId) =>
            new($"{roleId.ToLowerInvariant()}-general", "General", "What it is for.");

        private static SeedRole Role(string id, params SeedPrompt[] prompts) =>
            new(id, id, RoleStages.Result, ProgrammingTask: true, prompts.Length == 0 ? [PromptFor(id)] : prompts);

        private static Action Loading(params SeedRole[] roles) =>
            () => RoleCatalog.FromSeed(new RoleSeed(roles));

        [Fact]
        public void WithNoRolesAtAll_IsRefused_RatherThanRunningNoReviewers() =>
            Loading().Should().Throw<InvalidOperationException>()
                .WithMessage("*no roles*",
                    "a catalog of nothing disables every round, and a silent one would look like a quiet afternoon");

        [Fact]
        public void WithTwoRolesOfOneId_IsRefused_NamingTheId() =>
            Loading(Role("Architecture"), Role("architecture"))
                .Should().Throw<InvalidOperationException>().WithMessage("*architecture*",
                    "ById matches case-insensitively, so two spellings of one id make the round's role a coin toss");

        [Fact]
        public void WithOnePromptIdUnderTwoRoles_IsRefused_NamingThePrompt() =>
            Loading(
                    Role("Alpha", new SeedPrompt("shared-id", "A", "")),
                    Role("Beta", new SeedPrompt("shared-id", "B", "")))
                .Should().Throw<InvalidOperationException>().WithMessage("*shared-id*",
                    "a prompt id is a file name under <dataDir>/prompts/, so two roles sharing one would read one text");

        [Fact]
        public void WithARoleThatHasNoPrompts_IsRefused_NamingTheRole() =>
            Loading(new SeedRole("Empty", "Empty", RoleStages.Result, true, []))
                .Should().Throw<InvalidOperationException>().WithMessage("*Empty*",
                    "a role's first prompt is its general one, and a role without one has no question to ask");

        [Fact]
        public void WithANullPromptList_IsRefused_RatherThanThrowingAtTheFirstRound() =>
            Loading(new SeedRole("Null", "Null", RoleStages.Result, true, null!))
                .Should().Throw<InvalidOperationException>().WithMessage("*Null*");

        [Fact]
        public void WithAnUnknownStage_IsRefused_NamingIt() =>
            Loading(new SeedRole("Odd", "Odd", "sometime", true, [PromptFor("Odd")]))
                .Should().Throw<InvalidOperationException>().WithMessage("*sometime*",
                    "a stage this build does not know puts the role in no round and says nothing");

        /// <summary>The third stage is a stage: one shared list, read by the seed and by composition (S2.1).</summary>
        [Fact]
        public void WithAFeatureStage_IsAccepted()
        {
            var catalog = RoleCatalog.FromSeed(new RoleSeed([new SeedRole("Whole", "The whole", RoleStages.Feature, true, [PromptFor("Whole")])]));

            catalog.Roles.Single().Bucket.Should().Be(RoleBuckets.FeatureCode);
            RoleStages.All.Should().Equal([RoleStages.Plan, RoleStages.Result, RoleStages.Feature],
                "the list both readers share, in the order a person would list the gates");
        }

        [Fact]
        public void ThatIsWellFormed_LoadsAndMarksEverythingShipped()
        {
            var catalog = RoleCatalog.FromSeed(new RoleSeed([Role("Alpha"), Role("Beta")]));

            catalog.Roles.Select(r => r.Id).Should().Equal("Alpha", "Beta");
            catalog.Roles.Should().OnlyContain(r => r.BuiltIn && r.Active);
            catalog.Dropped.Should().BeEmpty("a seed is refused whole or accepted whole — dropping is A2's verb");
        }
    }
}
