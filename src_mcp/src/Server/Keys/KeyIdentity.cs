using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Notices;

namespace CoaiMcp.Server.Keys;

/// <summary>The sources a key can come from. A source name is not a secret; <c>file:</c> names a path the person chose.</summary>
internal static class KeySources
{
    public const string Local = "local";
    public const string Environment = "env";
    public const string FilePrefix = "file:";

    public static string File(string fullPath) => FilePrefix + fullPath;
}

/// <summary>
/// The id of a key: <c>k-</c> and the first 12 hex characters of SHA-256 over a fixed prefix line and the token (KEY_POOL_SPEC.md section 3).
/// </summary>
/// <remarks>
/// The prefix line makes the id differ from a plain hash of the token: the Team server and the hub store plain hashes of their own tokens, and an id here must not be a handle for those.
/// 48 bits of a hash of a long random token say nothing about the token. The same token gives the same id, which is how a second add is found.
/// </remarks>
internal static partial class KeyIds
{
    private const string HashPrefix = "coai-claude-key-v1\n";

    [GeneratedRegex("^k-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    public static string Of(string token) =>
        "k-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(HashPrefix + token)))[..12];

    /// <summary>Whether <paramref name="id"/> is exactly the shape of an id — checked before any path is built from one, so a caller cannot name a file outside the store.</summary>
    public static bool IsValid(string id) => Shape().IsMatch(id);
}

/// <summary>
/// The value a key text must have to enter the pool (KEY_POOL_SPEC.md section 4): the prefix the CLI itself tests, the character set, 40 to 512 characters.
/// </summary>
/// <remarks>
/// A refusal names the rule that failed and the LENGTH — never a character of the input, and never the input. No vendor call is made: a wrong key is found by its first launch.
/// </remarks>
internal static class KeyShape
{
    internal const string Prefix = "sk-ant-oat";
    internal const string ApiPrefix = "sk-ant-api";
    internal const int MinLength = 40;
    internal const int MaxLength = 512;

    /// <summary>The sentence that says why <paramref name="token"/> is not a setup-token, or empty when it is shaped like one.</summary>
    public static string Refusal(string token)
    {
        if (token.StartsWith(ApiPrefix, StringComparison.Ordinal))
        {
            return "that is an API key; the pool takes `claude setup-token` tokens";
        }

        if (!token.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return $"the text does not have the shape of a `claude setup-token` token: it must start with the prefix the claude CLI tests (length {token.Length})";
        }

        if (!token.All(Allowed))
        {
            return $"the text holds a character outside A-Z a-z 0-9 _ - (length {token.Length})";
        }

        return token.Length is >= MinLength and <= MaxLength
            ? string.Empty
            : $"the text must be {MinLength} to {MaxLength} characters long (length {token.Length})";
    }

    private static bool Allowed(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-';
}

/// <summary>The rules for a label (KEY_POOL_SPEC.md section 3): 1 to 40 characters of <c>A-Z a-z 0-9 space . _ @ + -</c>, unique without case, and never a secret.</summary>
internal static class KeyLabels
{
    internal const int MaxLength = 40;

    /// <summary>Why <paramref name="label"/> cannot be used — or empty. The sentence never repeats the label: a token on a command line was already one place too many.</summary>
    public static string Refusal(string label, IEnumerable<string> usedByOthers)
    {
        if (label.Length is 0 or > MaxLength)
        {
            return $"a label is 1 to {MaxLength} characters (it had {label.Length})";
        }

        if (!label.All(Allowed))
        {
            return "a label holds only A-Z a-z 0-9 space . _ @ + -";
        }

        if (Redaction.FirstSecretClass(label).Length > 0)
        {
            return "that label reads as a secret — a token was on a command line: revoke it at the vendor, then add the key again with a plain label";
        }

        return usedByOthers.Any(other => other.Equals(label, StringComparison.OrdinalIgnoreCase)) ? "another key already has that label (labels are unique without case)" : string.Empty;
    }

    private static bool Allowed(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or ' ' or '.' or '_' or '@' or '+' or '-';
}
