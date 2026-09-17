namespace CoaiBugs.Tests;

/// <summary>
/// Reading a <see cref="Usage"/> in a test, without undoing the distinction it was made to keep.
/// </summary>
/// <remarks>
/// <para><b>Why these refuse instead of defaulting.</b> A code-round finding — accepted — was that
/// the read model collapsed "no key has this id" into "zero submissions", which the family doctrine
/// forbids: absent is not zero. The production type answers a union now
/// (<see cref="Usage.NoSuchKey"/> or <see cref="Usage.Known"/>), and a test helper that quietly
/// returned <c>0</c> for the absent case would put the same collapse back one layer up, where it
/// would be even harder to see — a test asserting "0 submissions" would pass for a key that was
/// never issued.</para>
/// <para>So a test that means "this key exists and has done nothing" asserts the whole union value;
/// a test that means "count what this existing key did" uses <see cref="Count"/>, which throws if
/// the key is absent rather than answering a number that was never true.</para>
/// </remarks>
internal static class UsageFacts
{
    /// <summary>What an EXISTING key has sent. Throws when there is no such key.</summary>
    internal static int Count(this Usage usage) =>
        usage is Usage.Known known
            ? known.Submissions.Value
            : throw new InvalidOperationException(
                "no key has this id, which is not the same fact as a count of zero — assert the "
                + "union itself if that is what the test means");

    /// <summary>The stored month of an EXISTING key, or empty when it has never been used.</summary>
    internal static string Month(this Usage usage) =>
        usage switch
        {
            Usage.Known { LastSeen: LastSeen.In seen } => seen.Month.Value,
            Usage.Known => string.Empty,
            _ => throw new InvalidOperationException("no key has this id, so it has no month"),
        };
}
