using CoaiMcp.Core.Findings;
using CoaiMcp.Store;

namespace CoaiMcp.Tests;

/// <summary>
/// A decision on a numbered finding, for a test that knows the number it means.
/// </summary>
/// <remarks>
/// <para>Thin on purpose: <see cref="DecisionAt.Accept"/> answers null for a number that names no
/// finding, which is right for a caller reading an index off the wire and only noise for a test
/// that wrote the list two lines earlier. These unwrap it and fail loudly instead.</para>
/// <para><b>Nothing here derives an ordinal from a list position.</b> The first version of this
/// helper did — <c>Select((decision, ordinal) =&gt; …)</c> — and the code round was right that it
/// reintroduced, inside the tests, the exact mapping the change removed from the projection: a test
/// using it could not have caught the defect. Every caller now names its finding number.</para>
/// </remarks>
internal static class Decisions
{
    internal static DecisionAt Accept(IReadOnlyList<Finding> pending, int ordinal) =>
        DecisionAt.Accept(pending, ordinal) ?? throw new ArgumentOutOfRangeException(
            nameof(ordinal), ordinal, $"this round has {pending.Count} finding(s)");

    internal static DecisionAt Reject(IReadOnlyList<Finding> pending, int ordinal, string reason) =>
        DecisionAt.Reject(pending, ordinal, reason) ?? throw new ArgumentOutOfRangeException(
            nameof(ordinal), ordinal, $"this round has {pending.Count} finding(s)");
}
