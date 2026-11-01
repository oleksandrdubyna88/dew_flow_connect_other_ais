using CoaiMcp.Store;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// Looks one thing up in an <see cref="ObservedSession"/> and FAILS WITH WORDS when it is not there: a bare <c>Single</c> over an
/// empty list says "Sequence contains no matching element", which is a setup error to read, not the symptom.
/// </summary>
internal static class ObservedSessionLookups
{
    public static ObservedAgent Agent(this ObservedSession session, string key)
    {
        session.Agents.Where(a => a.Key == key).Should().ContainSingle($"the session should hold agent '{key}' (it holds: {string.Join(", ", session.Agents.Select(a => a.Key))})");

        return session.Agents.Single(a => a.Key == key);
    }

    public static ObservedMessage Message(this ObservedSession session, string idOrSuffix)
    {
        session.Messages.Where(m => m.MessageId.EndsWith(idOrSuffix, StringComparison.Ordinal)).Should()
            .ContainSingle($"the session should hold the message ending '{idOrSuffix}' (it holds {session.Messages.Count} message(s))");

        return session.Messages.Single(m => m.MessageId.EndsWith(idOrSuffix, StringComparison.Ordinal));
    }
}
