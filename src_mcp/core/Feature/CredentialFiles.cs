namespace CoaiMcp.Core.Feature;

/// <summary>
/// Whether a file NAME is a credential shape the source resolver never serves (plan D15) — decided
/// on the basename alone, before git is asked anything.
/// </summary>
/// <remarks>
/// <para><b>The fixed shapes, and nothing from the credential words.</b> D15 as first written also
/// applied the redaction words of <c>shared/credential-words.json</c> to file names; the feature-pack
/// trial (2026-09-26) ran that over 21 real features and it refused NINE ordinary code files —
/// <c>providers/credentials.ts</c>, <c>Auth.cs</c>, <c>TokenIdentity.cs</c>, <c>tokens.rs</c> — which
/// is exactly the code a reviewer of a credential feature needs to read. The operator narrowed the
/// decision: a name is refused only for these shapes, and the words are applied to the file's
/// CONTENT through <see cref="Notices.Redaction.SafeSource"/>, where a credential would actually
/// be.</para>
/// <para>Case-insensitive, because refusing is the safe direction and <c>SERVER.PEM</c> is the same
/// file. A <c>*</c> at the end is a prefix (<c>id_rsa*</c> covers <c>id_rsa.pub</c>), at the start a
/// suffix (<c>*.pem</c>); the two are the only shapes the table needs.</para>
/// </remarks>
public static class CredentialFiles
{
    /// <summary>The shapes, as the plan spelled them — one table, read by the check and by the tests that derive their examples from it.</summary>
    public static readonly IReadOnlyList<string> Patterns =
        [".env*", "*.pem", "*.key", "*.pfx", "*.p12", "id_rsa*", "id_ed25519*", "id_ecdsa*"];

    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>Whether <paramref name="path"/> names a credential-looking file.</summary>
    public static bool LooksLikeOne(string path) => WhichPattern(path).Length > 0;

    /// <summary>The pattern <paramref name="path"/> matches — for the refusal that names it — or empty.</summary>
    public static string WhichPattern(string path)
    {
        var name = BasenameOf(path);

        return Patterns.FirstOrDefault(pattern => Matches(name, pattern)) ?? string.Empty;
    }

    private static string BasenameOf(string path)
    {
        var at = path.LastIndexOfAny(Separators);

        return at < 0 ? path : path[(at + 1)..];
    }

    private static bool Matches(string name, string pattern) => pattern switch
    {
        _ when pattern.EndsWith('*') => name.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase),
        _ when pattern.StartsWith('*') => name.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase),
        _ => name.Equals(pattern, StringComparison.OrdinalIgnoreCase),
    };
}
