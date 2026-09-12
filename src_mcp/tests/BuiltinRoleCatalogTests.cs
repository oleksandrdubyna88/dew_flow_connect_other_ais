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
    private sealed record SeedPrompt(string Id, string Label, string Purpose);

    private sealed record SeedRole(string Id, string Name, string Stage, bool ProgrammingTask, SeedPrompt[] Prompts);

    private static readonly SeedRole[] Seed = Load();

    private static SeedRole[] Load()
    {
        // tests/bin/<cfg>/net10.0 → the repository root, then the shared folder both sides read.
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "builtin-roles.json"));

        using var file = File.OpenRead(path);
        using var parsed = JsonDocument.Parse(file);

        return [.. parsed.RootElement.GetProperty("roles").EnumerateArray().Select(r => new SeedRole(
            r.GetProperty("id").GetString() ?? "",
            r.GetProperty("name").GetString() ?? "",
            r.GetProperty("stage").GetString() ?? "",
            r.GetProperty("programmingTask").GetBoolean(),
            [.. r.GetProperty("prompts").EnumerateArray().Select(p => new SeedPrompt(
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
    public void TheCatalog_IsTodaysFiveRolesAndTwentyFivePrompts_InTodaysOrder()
    {
        // The characterization of what `PromptCatalog.All` and `PanelConfig.AllRoles` were the day
        // the catalog became data. Ids are permanent — settings keys, session files and the rounds
        // database are keyed by them — so a change here is a migration, not an edit.
        RoleCatalog.Builtin.Roles.Select(r => r.Id).Should().Equal(
            RoleCatalog.PlanRole, RoleCatalog.ConventionsRole, RoleCatalog.ArchitectureRole,
            RoleCatalog.SecurityRole, RoleCatalog.UxDxRole);

        // Twenty-five, not twenty-six: four roles of six lenses and the conventions role's one.
        // The plan said twenty-six until this assertion was watched failing, which is the whole
        // point of pinning a number instead of a shape.
        RoleCatalog.Builtin.Roles.Sum(r => r.Prompts.Count).Should().Be(25);
        RoleCatalog.Builtin.Roles.Select(r => r.Prompts.Count).Should().Equal(
            [6, 1, 6, 6, 6],
            "six choices per section, and the conventions role has one — the shape the operator asked for");

        RoleCatalog.Builtin.ById(RoleCatalog.PlanRole)!.Stage.Should().Be(RoleStages.Plan);
        RoleCatalog.Builtin.Roles.Where(r => r.Id != RoleCatalog.PlanRole)
            .Should().OnlyContain(r => r.Stage == RoleStages.Result && r.ProgrammingTask,
                "every shipped role but the plan one reviews a diff");
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
}
