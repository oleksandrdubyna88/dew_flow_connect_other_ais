using System.Reflection;
using CoaiMcp.Core.Notices;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every property of <see cref="ServerNotice"/> is one the line knows how to write.
/// </summary>
/// <remarks>
/// <para><b>The defect this exists for is silent and one line long.</b> A later story adds a
/// property to <c>ServerNotice</c> — a correlation id, a duration, whatever the work needs — sets it
/// at a call site, and forgets to add it to <c>ServerNoticeLine</c>'s field list. The record then
/// carries the value, the line does not, and nothing says so: the compiler is happy, the suite is
/// green, and the field is simply absent from every notice on disk.</para>
/// <para>It is worse than an ordinary omission because of the parity harness. Its adapter treats any
/// input property it does not recognise as a <c>more</c> entry, so a field missing from the named
/// list would still appear in the compared line — under <c>more</c> rather than by name — and the
/// parity check would stay green about a field that had silently changed shape.</para>
/// <para>So the two lists are compared against each other rather than against a reader's memory.
/// A reviewer named this on story 1.2's code round.</para>
/// </remarks>
public sealed class EveryNoticeFieldReachesTheLineTests
{
    /// <summary>
    /// The JSON name for a property, by the rule the serialiser uses: the C# name, lower-camel.
    /// </summary>
    /// <remarks>
    /// Derived rather than listed, because a second list of names is the thing this test exists to
    /// prevent. <c>Utc</c> becomes <c>utc</c>, <c>Class</c> becomes <c>class</c>, and a future
    /// <c>CorrelationId</c> would become <c>correlationId</c> — which is the name the extension's
    /// parser would look for, since the TypeScript interface is camel-cased too.
    /// </remarks>
    private static string JsonName(string property) =>
        char.ToLowerInvariant(property[0]) + property[1..];

    [Fact]
    public void EveryPropertyOfTheRecord_IsAFieldTheLineWrites()
    {
        var properties = typeof(ServerNotice)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != nameof(ServerNotice.More) && p.Name != "EqualityContract")
            .Select(p => JsonName(p.Name))
            .ToArray();

        properties.Should().HaveCountGreaterThan(10,
            "the reflection found almost no properties, so this comparison is asserting nothing");

        properties.Should().BeSubsetOf(ServerNoticeLine.NamedFields,
            "a property the record carries and the line does not write is a value set at a call "
            + "site and dropped in silence — and the parity harness would not catch it, because its "
            + "adapter files an unrecognised property under `more` and compares it happily");
    }

    [Fact]
    public void EveryFieldTheLineWrites_IsAPropertyOfTheRecord()
    {
        // The other direction, which is the one that rots quietly: a name left in the list after the
        // property it read was renamed or removed. Nothing would fail — the reader would simply
        // never return a value — and the field would vanish from every notice.
        var properties = typeof(ServerNotice)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => JsonName(p.Name))
            .ToArray();

        ServerNoticeLine.NamedFields.Should().BeSubsetOf(properties,
            "the line writes a field the record has no property for, so that field can only ever be "
            + "absent");
    }
}
