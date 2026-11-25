using System.Text.Json.Nodes;
using CoaiMcp.Server.RoleJobs;

namespace CoaiMcp.Tests;

/// <summary>The judge's side of the scenario of <see cref="ReadOnlyScenario"/> (epoch 3 E3 s2 t2): an answer whose evidence is all true, and the descriptor of the real judge work.</summary>
internal static class JudgeScenario
{
    /// <summary>A judge answer whose evidence is all true: a revise with two findings and a decision.</summary>
    internal static JsonObject Judge() => new()
    {
        ["status"] = "ok",
        ["verdict"] = "revise",
        ["summary"] = "The router is fine; one rule is missing.",
        ["findings"] = new JsonArray(
            new JsonObject { ["id"] = "f1", ["severity"] = "major", ["title"] = "No check", ["detail"] = "Route returns its input.", ["evidence"] = new JsonArray(ReadOnlyScenario.Item(ReadOnlyScenario.Router, 4, "Route(string role) => role;")), ["fix"] = "check the role" },
            new JsonObject { ["id"] = "f2", ["severity"] = "nit", ["title"] = "A name", ["detail"] = "The name is short." }),
        ["decision"] = new JsonObject
        {
            ["chosen"] = "keep one router",
            ["why"] = "it is smaller",
            ["rejected"] = new JsonArray(new JsonObject { ["option"] = "two routers", ["why"] = "it needs a second table" }),
        },
    };

    /// <summary>The descriptor of the real judge work with its waits short; the test's route is on the job.</summary>
    internal static RoleDescriptor JudgeDescriptor() =>
        new(RoleDescriptors.Judge, "the judge", () => new JudgeWork { AttemptTimeout = TimeSpan.FromSeconds(30), Ladder = [TimeSpan.FromMinutes(5)] }, RouteAs: "important", Check: ReadOnlyRoleRequest.Refusal, NeedsBrief: true);
}
