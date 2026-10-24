using System.Text.RegularExpressions;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// One release of the installed claude CLI (<c>2.1.309</c>) — or <see cref="Unknown"/> when nobody asked or the CLI did not name exactly one.
/// A typed value instead of a raw string, so "no release" is a state the code can ask about (<see cref="IsKnown"/>) and not an empty string
/// every reader has to remember to test.
/// </summary>
/// <remarks>The default value IS <see cref="Unknown"/>. A known release can only come from <see cref="Parse"/>, so two releases are equal exactly when they print the same.</remarks>
public readonly partial record struct ClaudeRelease
{
    private readonly string? _printed;

    private ClaudeRelease(string printed) => _printed = printed;

    /// <summary>No release: nobody asked, or the answer named none (or two that cannot be told apart).</summary>
    public static ClaudeRelease Unknown => default;

    /// <summary>The release as the CLI printed it (<c>2.1.309</c>); empty for <see cref="Unknown"/>.</summary>
    public string Value => _printed ?? string.Empty;

    /// <summary>Whether a release is named.</summary>
    public bool IsKnown => !string.IsNullOrEmpty(_printed);

    /// <summary>
    /// The one release a <c>--version</c> answer names (<c>2.1.309 (Claude Code)</c> is <c>2.1.309</c>), or <see cref="Unknown"/> — when it names none, or two that
    /// cannot be told apart (a wrapper's banner beside the CLI's own line), neither is believed.
    /// </summary>
    public static ClaudeRelease Parse(string versionOutput) =>
        Named().Matches(versionOutput).Select(named => named.Value).Distinct().ToArray() is [var only] ? new(only) : Unknown;

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>A release number — three dotted parts, not part of a longer dotted run.</summary>
    [GeneratedRegex(@"(?<![\d.])\d+\.\d+\.\d+(?![\d.])", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Named();
}
