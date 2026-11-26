using System.Text.Json.Nodes;
using CoaiMcp.Server.RoleJobs;

namespace CoaiMcp.Tests;

/// <summary>
/// A small repository laid for the scout and the judge (epoch 3 E3 s2), and the answers a model would give about it — built as JSON objects and said by FakeCli, so no model is called. The files hold known lines, so evidence can
/// be true, out of range, or not on its line; <c>.env</c> holds a marker no job folder may ever hold.
/// </summary>
internal static class ReadOnlyScenario
{
    internal const string Router = "src/Router.cs";

    internal const string RouterLine2 = "public static class Router";

    internal const string RouterLine4 = "    public static string  Route(string role) => role;";

    internal const string Secret = "TOP-SECRET-MARKER-31337";

    internal const string Brief = "Which class decides the route of a role?";

    private static readonly Dictionary<string, string> Files = new()
    {
        [Router] = "namespace Demo;\n" + RouterLine2 + "\n{\n" + RouterLine4 + "\n}\n",
        ["README.md"] = "# Demo\n\nThe router is in src/Router.cs.\n",
        [".env"] = $"KEY={Secret}\n",
        ["docs/notes.txt"] = "one\ntwo\n",
    };

    /// <summary>Lays the files into <paramref name="repo"/>.</summary>
    internal static void Lay(string repo)
    {
        foreach (var (relative, text) in Files)
        {
            BaScenario.Write(repo, relative, text);
        }
    }

    internal static JsonObject Item(string file, int line, string text) => new() { ["file"] = file, ["line"] = line, ["text"] = text };

    /// <summary>A scout answer whose evidence is all true: two facts, three items, two files that exist and one that does not, one unknown line.</summary>
    internal static JsonObject Scout() => new()
    {
        ["status"] = "ok",
        ["facts"] = new JsonArray(
            new JsonObject
            {
                ["claim"] = "Router decides the route",
                ["evidence"] = new JsonArray(Item(Router, 2, RouterLine2), Item(Router, 4, "public static string Route(string role) => role;")),
            },
            new JsonObject { ["claim"] = "the README says where", ["evidence"] = new JsonArray(Item("README.md", 3, "The router is in src/Router.cs.")) }),
        ["files"] = new JsonArray(Router, "README.md", "src/Missing.cs"),
        ["unknown"] = new JsonArray("where the bands are written"),
    };

    /// <summary>The scout answer with the evidence of its first fact replaced.</summary>
    internal static JsonObject ScoutWith(params JsonObject[] firstFactEvidence)
    {
        var answer = Scout();
        answer["facts"]![0]!["evidence"] = new JsonArray([.. firstFactEvidence.Select(item => (JsonNode)item)]);

        return answer;
    }

    /// <summary>The descriptor of the real scout work with its waits short; the test's route is on the job.</summary>
    internal static RoleDescriptor ScoutDescriptor() =>
        new(RoleDescriptors.Scout, "the scout", () => new ScoutWork { AttemptTimeout = TimeSpan.FromSeconds(30), Ladder = [TimeSpan.FromMinutes(5)] }, RouteAs: "scout", Check: ReadOnlyRoleRequest.Refusal, NeedsBrief: true);
}
