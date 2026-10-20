using System.Reflection;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Two structural rules of the round-authors report, held by tests so a later edit cannot quietly undo them: an expected failure travels as
/// ONE outcome shape (<see cref="Outcome{T}"/>, the C# doctrine §5) and never as a <c>(bool ok, T value, string why)</c> tuple per call site;
/// and the store is opened through the one read-only open the counter uses, never a second spelling of its connection string.
/// </summary>
public sealed class RoundAuthorsShapeTests
{
    private static readonly Assembly Server = typeof(RoundAuthorsMerge).Assembly;

    [Fact]
    public void NoMethodOfTheReport_ReturnsAnOutcomeWearingATupleDisguise()
    {
        var offenders = Server.GetTypes()
            .Where(type => type.Name.StartsWith("RoundAuthors", StringComparison.Ordinal) && !type.Name.StartsWith("RoundAuthorsTable", StringComparison.Ordinal) && !type.Name.StartsWith("RoundAuthorsLog", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => IsTupleWithAString(method.ReturnType))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .ToList();

        offenders.Should().BeEmpty("a refusal is an Outcome<T>, one shape for the whole report (doctrine §5)");
        Server.GetTypes().Select(type => type.Name).Should().NotContain(["MergeOutcome", "Step`1"], "no parallel outcome type beside Outcome<T>");
    }

    [Fact]
    public void TheReportsRead_OpensTheStoreThroughTheOneReadOnlyOpen()
    {
        var source = File.ReadAllText(Path.Combine(ProductionSources.RepositoryRoot(), "src_mcp", "src", "Store", "RoundAuthorsQuery.cs"));

        source.Should().NotContain("new SqliteConnection(", "the read-only open string is spelled once, in RoundCounts.OpenReadOnly");
        source.Should().Contain("RoundCounts.OpenReadOnly(");
    }

    private static bool IsTupleWithAString(Type type) =>
        type.IsGenericType && type.FullName is { } name && name.StartsWith("System.ValueTuple`", StringComparison.Ordinal)
        && type.GetGenericArguments().Contains(typeof(string));
}
