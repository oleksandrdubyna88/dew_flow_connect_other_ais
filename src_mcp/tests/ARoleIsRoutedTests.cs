using System.Text.Json;
using CoaiMcp.Core.Roles;
using CoaiMcp.Core.Work;
using CoaiMcp.Roles;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E1 s3 t2, scenario A12 — the router is a pure function and <c>shared/role-route-vectors.json</c> is its contract: every vector gets the models, in the order, or the refusal it expects. Beside the
/// vectors: the table they run on IS the shipped claude table (so the vectors cannot drift from <c>shared/role-defaults.json</c>), the router and the validator ask the SAME important rule, and the router's
/// source holds no IO.
/// </summary>
public sealed class ARoleIsRoutedTests
{
    private sealed record Vector(string Name, string Table, RouteRequest Request, IReadOnlyList<string> Models, string Why, string Refusal);

    private static JsonElement Document() => JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "role-route-vectors.json"))).RootElement.Clone();

    private static RouteTable TableOf(JsonElement table) => new(
        [.. table.GetProperty("roles").EnumerateObject().Select(role => (role.Name, (IReadOnlyList<ModelRef>)[.. role.Value.EnumerateArray().Select(ModelOf)]))],
        [.. table.GetProperty("bands").EnumerateArray().Select(band => new Band(band.GetProperty("min").GetInt32(), band.GetProperty("max").GetInt32(), band.GetProperty("model").GetString()!, band.GetProperty("vendor").GetString()!))]);

    private static ModelRef ModelOf(JsonElement model) => new(model.GetProperty("model").GetString()!, model.GetProperty("vendor").GetString()!);

    private static IReadOnlyList<Vector> Vectors() =>
    [
        .. Document().GetProperty("vectors").EnumerateArray().Select(vector =>
        {
            var task = vector.TryGetProperty("task", out var facts) ? facts : default;
            var request = new RouteRequest(
                vector.GetProperty("role").GetString()!,
                vector.TryGetProperty("model", out var model) ? model.GetString()! : string.Empty,
                task.ValueKind == JsonValueKind.Object && task.TryGetProperty("kind", out var kind) ? kind.GetString()! : string.Empty,
                task.ValueKind == JsonValueKind.Object && task.TryGetProperty("complexity", out var complexity) ? complexity.GetInt32() : RouteRequest.NoComplexity,
                task.ValueKind == JsonValueKind.Object && task.TryGetProperty("security", out var security) && security.GetBoolean());
            var expect = vector.GetProperty("expect");

            return new Vector(
                vector.GetProperty("name").GetString()!,
                vector.GetProperty("table").GetString()!,
                request,
                expect.TryGetProperty("models", out var models) ? [.. models.EnumerateArray().Select(entry => entry.GetString()!)] : [],
                expect.TryGetProperty("why", out var why) ? why.GetString()! : string.Empty,
                expect.TryGetProperty("refused", out var refused) ? refused.GetString()! : string.Empty);
        }),
    ];

    public static TheoryData<string> VectorNames => [.. Vectors().Select(vector => vector.Name)];

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void AVector_GetsTheRouteOrTheRefusalItExpects(string name)
    {
        var vector = Vectors().Single(one => one.Name == name);
        var table = TableOf(Document().GetProperty("tables").GetProperty(vector.Table));

        var result = RoleRouter.Route(vector.Request, table);

        if (vector.Refusal.Length > 0)
        {
            result.Should().BeOfType<RouteResult.Refused>().Which.Reason.Should().Contain(vector.Refusal);

            return;
        }

        var routed = result.Should().BeOfType<RouteResult.Routed>(result is RouteResult.Refused refused ? refused.Reason : string.Empty).Subject;
        routed.Models.Select(model => $"{model.Model}@{model.Vendor}").Should().Equal(vector.Models);
        if (vector.Why.Length > 0)
        {
            routed.Why.Should().Contain(vector.Why);
        }
    }

    [Fact]
    public void VectorNames_AreUnique_AndThereIsOneForEveryRuleOfTheRouter()
    {
        var vectors = Vectors();

        vectors.Select(vector => vector.Name).Should().OnlyHaveUniqueItems();
        // The rules, in the spec's order: important (three triggers, an explicit model in and out), explicit model, band, role list, no duplicates, vendor routing, empty list.
        new[] { "security: true", "kind: security", "complexity 10", "caller named", "band", "coder list", "ba list", "vendor-routing", "COAI_ROLES_MODELS" }
            .Should().OnlyContain(word => vectors.Any(vector => vector.Why.Contains(word) || vector.Refusal.Contains(word)), "each rule of the router has a vector that names it");
    }

    [Fact]
    public void TheVectorsDefaultTable_IsTheShippedClaudeTable_WithTheCodexRowOfImportant()
    {
        var shipped = RoleDefaults.Current.MainAi["claude"];
        var vectors = TableOf(Document().GetProperty("tables").GetProperty("default"));

        vectors.Roles.OrderBy(role => role.Role, StringComparer.Ordinal)
            .Select(role => (role.Role, Models: string.Join(",", role.Models.Select(model => $"{model.Model}@{model.Vendor}"))))
            .Should().Equal(shipped.Roles.OrderBy(role => role.Role, StringComparer.Ordinal)
                .Select(role => (role.Role, Models: string.Join(",", role.Models.Select(model => $"{model.Model}@{model.Vendor}")))));
        vectors.Bands.Should().Equal(shipped.Bands);
    }

    [Fact]
    public void TheRouter_AsksTheSameImportantRuleTheValidatorDoes_ForEveryKindComplexityAndFlag()
    {
        var table = TableOf(Document().GetProperty("tables").GetProperty("default"));

        foreach (var kind in new[] { string.Empty, "code", "security", "docs" })
        {
            foreach (var complexity in Enumerable.Range(RouteRequest.NoComplexity, 12))
            {
                foreach (var flag in new[] { false, true })
                {
                    var important = ImportantRule.Reason(kind, complexity, flag).Length > 0;
                    var result = RoleRouter.Route(new RouteRequest("coder", string.Empty, kind, complexity, flag), table);

                    var models = result.Should().BeOfType<RouteResult.Routed>().Subject.Models.Select(model => model.Model).ToList();
                    (models.Contains("astra")).Should().Be(important, $"kind '{kind}', complexity {complexity}, security {flag}: the codex entry is on the important list only");
                }
            }
        }
    }

    [Fact]
    public void TheRoutersSource_HoldsNoIoAndNoClock()
    {
        var folder = Path.Combine(RepoPaths.Root(), "src_mcp", "core", "Roles");
        var forbidden = new[] { "System.IO", "File.", "Directory.", "Process", "HttpClient", "Environment.", "DateTime", "TimeProvider", "Stopwatch", "Random", "Task.", "Thread" };

        foreach (var file in Directory.EnumerateFiles(folder, "*.cs"))
        {
            var code = string.Join('\n', File.ReadAllLines(file).Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal) && !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            foreach (var word in forbidden)
            {
                code.Should().NotContain(word, $"{Path.GetFileName(file)} is the pure core: it takes tables and answers a value");
            }
        }
    }
}
