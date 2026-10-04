using System.Text.Json;
using CoaiMcp.Runners.Consultation;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// coai-mcp's own consultant list is held to <c>shared/feature-availability.json</c>, the file the extension
/// generates its picker from (PLAN_one_model_catalog.md D4, E1.2).
/// </summary>
/// <remarks>
/// Until E2.1 has coai-mcp read the file itself, <see cref="ConsultantResolution.Consulting"/> is a second copy,
/// and the extension's copy is generated from the file. This is the one test that compares the two halves —
/// before it, each half pinned its own literal, and a runtime added to one would have been offered by the panel
/// and refused by the server with every test green.
/// </remarks>
public sealed class FeatureAvailabilityTests
{
    private static JsonElement Shared()
    {
        var path = Path.Combine(ProductionSources.RepositoryRoot(), "shared", "feature-availability.json");
        using var parsed = JsonDocument.Parse(File.ReadAllBytes(path));

        return parsed.RootElement.Clone();
    }

    [Fact]
    public void TheConsultingRuntimes_AreTheSharedFilesConsultantList_InItsOrder()
    {
        var shared = Shared().GetProperty("features").GetProperty("consultant")
            .EnumerateArray().Select(runtime => runtime.GetString()!).ToList();

        shared.Should().NotBeEmpty("a file that did not load names no runtime");
        ConsultantResolution.Consulting.Should().Equal(shared,
            "the panel offers exactly the file's list, so the server must run exactly that list");
    }
}
