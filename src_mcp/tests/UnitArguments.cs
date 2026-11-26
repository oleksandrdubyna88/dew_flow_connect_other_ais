using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// The rewrites a test makes to the finished <c>systemd-run</c> argument list (the test-only door <c>CoderBoundary.Degrade</c>): take a property away, or put another in its place. A rewrite that does not find what it is told to change FAILS the test, so a degrade can never prove nothing.
/// </summary>
internal static class UnitArguments
{
    /// <summary>The list without that argument — and without the <c>-p</c> before it when it is a property.</summary>
    internal static string[] Drop(IReadOnlyList<string> arguments, string what)
    {
        var at = arguments.ToList().IndexOf(what);
        at.Should().BeGreaterThan(-1, $"the degrade must find '{what}' in the real argument list, or it proves nothing");
        var drop = at > 0 && arguments[at - 1] == "-p" ? 2 : 1;

        return [.. arguments.Take(at - (drop - 1)), .. arguments.Skip(at + 1)];
    }

    /// <summary>The list with <paramref name="what"/> replaced by <paramref name="with"/>.</summary>
    internal static string[] Replace(IReadOnlyList<string> arguments, string what, string with)
    {
        var at = arguments.ToList().IndexOf(what);
        at.Should().BeGreaterThan(-1, $"the degrade must find '{what}' in the real argument list, or it proves nothing");

        return [.. arguments.Take(at), with, .. arguments.Skip(at + 1)];
    }

    /// <summary>The <c>-p</c> properties of a <c>systemd-run</c> argument list, in order.</summary>
    internal static IReadOnlyList<string> PropertiesOf(IReadOnlyList<string> arguments) =>
        [.. arguments.Select((argument, at) => (argument, at)).Where(pair => pair.argument == "-p" && pair.at + 1 < arguments.Count).Select(pair => arguments[pair.at + 1])];
}
