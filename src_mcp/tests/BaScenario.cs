using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CoaiMcp.Server.RoleJobs;

namespace CoaiMcp.Tests;

/// <summary>
/// A small plan folder laid into a test repository, and the answers a BA model would give for it (epoch 3 E1 s4): a feature with one epoch and two epics, the first (<see cref="Epic"/>) the one to plan. The answers are built here
/// as JSON objects, and FakeCli says them — no model is called.
/// </summary>
internal static class BaScenario
{
    internal const string Plan = "plan";

    internal const string Epic = "e1-core";

    internal const string EpicFolder = Plan + "/epoch-1/" + Epic;

    internal const string EpicFile = EpicFolder + "/epic.yaml";

    private static readonly Dictionary<string, string> Files = new()
    {
        ["plan/feature.yaml"] = "id: demo\ntitle: \"Demo\"\nepochs:\n  - epoch-1\n",
        ["plan/epoch-1/epoch.yaml"] = "id: epoch-1\ntitle: \"One\"\ndependsOn: []\nepics:\n  - e1-core\n  - e2-more\n",
        ["plan/epoch-1/e1-core/epic.yaml"] = "id: e1-core\ntitle: \"Core\"\nmvp: true\ndependsOn: []\ndoneWhen:\n  - \"it works\"\nspec: SPEC.md\n",
        ["plan/epoch-1/e1-core/SPEC.md"] = "# Spec of the core\n\nBuild the core: two parts, the second after the first.\n",
        ["plan/epoch-1/e2-more/epic.yaml"] = "id: e2-more\ntitle: \"More\"\nmvp: false\ndependsOn:\n  - e1-core\n",
        ["plan/architecture/design.md"] = "# Design\n",
        ["src/readme.txt"] = "source\n",
    };

    /// <summary>Lays the plan folder into <paramref name="repo"/>.</summary>
    internal static void Lay(string repo)
    {
        foreach (var (relative, text) in Files)
        {
            Write(repo, relative, text);
        }
    }

    internal static void Write(string repo, string relative, string text)
    {
        var path = Path.Combine(repo, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    internal static JsonObject Task(string id, string title = "A task", string[]? dependsOn = null, string[]? files = null, bool parallel = false, int complexity = 3) => new()
    {
        ["id"] = id,
        ["title"] = title,
        ["kind"] = "code",
        ["complexity"] = complexity,
        ["dependsOn"] = new JsonArray([.. (dependsOn ?? []).Select(entry => (JsonNode)entry)]),
        ["parallel"] = parallel,
        ["files"] = new JsonArray([.. (files ?? [$"src/{id}.cs"]).Select(entry => (JsonNode)entry)]),
        ["dod"] = new JsonArray(new JsonObject { ["id"] = "d1", ["item"] = $"{id} is proved by a test" }),
    };

    internal static JsonObject Story(string id, string title, string[] dependsOn, params JsonObject[] tasks) => new()
    {
        ["id"] = id,
        ["title"] = title,
        ["dependsOn"] = new JsonArray([.. dependsOn.Select(entry => (JsonNode)entry)]),
        ["tasks"] = new JsonArray([.. tasks.Select(task => (JsonNode)task)]),
    };

    /// <summary>The valid plan: two stories, three tasks.</summary>
    internal static JsonObject Valid() => new()
    {
        ["status"] = "ok",
        ["epic"] = Epic,
        ["stories"] = new JsonArray(
            Story("s1-first", "First part", [], Task("t1-a"), Task("t2-b", dependsOn: ["t1-a"])),
            Story("s2-second", "Second part", ["s1-first"], Task("t1-c"))),
        ["notes"] = new JsonArray("the second part waits for the first"),
    };

    /// <summary>The same plan with a string complexity, a <c>yes</c> for a boolean and a story id that climbs out of its folder — three faults the reader finds at once.</summary>
    internal static JsonObject Wrong()
    {
        var answer = Valid();
        answer["stories"]![0]!["id"] = "s1-../../../outside";
        answer["stories"]![0]!["tasks"]![0]!["complexity"] = "5";
        answer["stories"]![0]!["tasks"]![1]!["parallel"] = "yes";

        return answer;
    }

    /// <summary>A plan the reader accepts and the rules do not: a task that depends on one that is not there.</summary>
    internal static JsonObject BrokenRule()
    {
        var answer = Valid();
        answer["stories"]![0]!["tasks"]![1]!["dependsOn"] = new JsonArray("t9-nowhere");

        return answer;
    }

    /// <summary>Every file of the repository (not <c>.git</c>) with the hash of its bytes — what "nothing else changed" is judged on.</summary>
    internal static IReadOnlyDictionary<string, string> Tree(string repo) =>
        Directory.EnumerateFiles(repo, "*", SearchOption.AllDirectories)
            .Select(path => (Rel: Path.GetRelativePath(repo, path).Replace('\\', '/'), Path: path))
            .Where(file => !file.Rel.StartsWith(".git/", StringComparison.Ordinal))
            .ToDictionary(file => file.Rel, file => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file.Path))), StringComparer.Ordinal);

    /// <summary>The descriptor of the real BA work with its waits short; the test's route is on the job.</summary>
    internal static RoleDescriptor Descriptor(Action<string>? boundary = null) =>
        new(RoleDescriptors.Ba, "the BA", () => new BaWork { AttemptTimeout = TimeSpan.FromSeconds(30), Ladder = [TimeSpan.FromMinutes(5)], Boundary = boundary }, RouteAs: "ba", Check: BaEpic.Refusal);
}
