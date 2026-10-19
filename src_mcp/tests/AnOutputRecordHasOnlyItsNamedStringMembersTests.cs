using System.Collections.Immutable;
using System.Reflection;
using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;
using static CoaiMcp.Tests.AssignmentViews;

namespace CoaiMcp.Tests;

/// <summary>
/// An output record has only its NAMED string members (plan gate 2026-10-16): a new string member fails by name here before any data can
/// carry text. <c>CheckoutWrites.Root</c> is the one path, in memory only. S5's sentinel test is the data-side half.
/// </summary>
public sealed class AnOutputRecordHasOnlyItsNamedStringMembersTests
{
    private static IEnumerable<string> StringMembers(Type type) => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.PropertyType == typeof(string) || property.PropertyType == typeof(ImmutableArray<string>))
        .Select(property => property.Name).Order(StringComparer.Ordinal);

    [Theory]
    [InlineData(typeof(AuthorRow), "Association,Effort,Model,Requested,Stage,Who,Window,AgentType")]
    [InlineData(typeof(ActivityTotals), "")]
    [InlineData(typeof(WindowAuthors), "Reasons,State,Window")]
    [InlineData(typeof(RoundAuthors), "CallerSessionId,PeerGates,Reasons,Stage,State")]
    [InlineData(typeof(CheckoutWrites), "Root")]
    public void TheStringMembersEqualTheNamedAllowlist(Type type, string allowed) =>
        StringMembers(type).Should().Equal(allowed.Split(',', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
}
