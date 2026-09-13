using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Rounds;

/// <summary>
/// What a document must be before a round is spent on it — the half of the rule that needs no disk.
/// </summary>
/// <remarks>
/// <para>Pure on purpose: the shape of a name, the size ceiling, which extensions are not text and
/// what a snapshot is called are decisions, and a decision that can only be exercised by writing a
/// file is a decision nothing checks cheaply. <see cref="CoaiMcp.Server.DocumentReader"/> is the
/// other half — resolving links, reading bytes, and proving the payload really is text.</para>
/// </remarks>
public static partial class DocumentRules
{
    /// <summary>
    /// The ceiling on a document, in UTF-8 bytes.
    /// </summary>
    /// <remarks>
    /// <para>256 KB is roughly 64 000 tokens, which fits every reviewer this product ships with once
    /// the prompt and the answer are counted. The plan's first draft said 1 MB and codex refused it:
    /// 1 MB is about 250 000 tokens, so the tool would ACCEPT a document that a 128K-context
    /// reviewer then refuses — a failure the caller cannot see coming and did not cause.</para>
    /// <para>It is a bound this product can enforce honestly. Checking against each selected model's
    /// real context window was asked for and is out of scope until model capabilities are data:
    /// a check against a number we are guessing would refuse valid rounds and still miss invalid
    /// ones.</para>
    /// </remarks>
    public const int MaxBytes = 256 * 1024;

    /// <summary>
    /// The bound on a <c>documentName</c>, which is plan 3's role-id bound and for the same reason:
    /// it becomes a path segment and a session key.
    /// </summary>
    public const int MaxNameLength = 48;

    /// <summary>The shape a <c>documentName</c> must have — plan 3's rule, not a second one.</summary>
    /// <remarks>
    /// Anchored <c>\A…\z</c> and never <c>^…$</c>: .NET's <c>$</c> also matches immediately before a
    /// trailing newline, so the <c>^…$</c> form accepts a name carrying a line break into a file
    /// path and a log line. Story 1 of plan 3 found that with its own RED test, and its code round
    /// then found that only one of the two copies had been fixed.
    /// </remarks>
    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9_]*\z")]
    private static partial Regex NameShape { get; }

    /// <summary>
    /// Extensions this product refuses to read as text, BEFORE it reads a byte.
    /// </summary>
    /// <remarks>
    /// Belt and braces beside the strict decoder, and the belt is the one that talks: a refusal that
    /// can name the format tells the calling AI what to convert, and one that only says "not text"
    /// gets retried with the same bytes until the session stalls. (local, the plan round.)
    /// </remarks>
    public static readonly IReadOnlySet<string> NotText = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".doc", ".pdf", ".xlsx", ".xls", ".pptx", ".ppt", ".odt", ".ods", ".odp",
        ".zip", ".gz", ".tar", ".7z", ".rar",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".svgz",
        ".mp3", ".mp4", ".wav", ".avi", ".mov", ".mkv",
        ".exe", ".dll", ".so", ".dylib", ".bin", ".class", ".pyc", ".wasm",
    };

    public static bool IsName(string? name) =>
        name is { Length: > 0 and <= MaxNameLength } && NameShape.IsMatch(name);

    /// <summary>Why this name cannot be one, or null when it can.</summary>
    public static string? NameRefusal(string? name)
    {
        var said = name?.Trim() ?? string.Empty;
        if (said.Length == 0)
        {
            return "documentText needs a documentName: it is what makes a second round about the "
                 + "same document, after you have edited it. A latin name, letters digits and "
                 + "underscores, starting with a letter — 'Spec', 'Q3_policy'.";
        }

        return IsName(said)
            ? null
            : $"documentName '{said}' is not a name this can key a session by. Letters, digits and "
            + $"underscores, starting with a letter, at most {MaxNameLength} characters — it becomes "
            + "a file name and a session key.";
    }

    /// <summary>Why this document is too large, or null when it is not.</summary>
    public static string? SizeRefusal(long bytes) =>
        bytes <= MaxBytes
            ? null
            : $"the document is {bytes:N0} bytes and the limit is {MaxBytes:N0}. That limit is about "
            + "what every reviewer's context can hold together with its prompt and its answer, so a "
            + "larger document is better reviewed in parts — send a section at a time, each with its "
            + "own purpose.";

    /// <summary>Why this extension is not text, or null when nothing here says it is not.</summary>
    public static string? ExtensionRefusal(string path)
    {
        var extension = Path.GetExtension(path);

        return NotText.Contains(extension)
            ? $"'{Path.GetFileName(path)}' is not a text document. Convert it and pass the result as "
            + "documentText with a documentName — this gate reads text, and guessing at a conversion "
            + "inside it would have reviewers reading whatever the guess produced."
            : null;
    }

    /// <summary>
    /// The SNAPSHOT's name: the first 16 hex characters of the SHA-256 of the text.
    /// </summary>
    /// <remarks>
    /// <b>Not the session's identity.</b> The plan's first draft used this as the session key and
    /// three vendors independently found the same consequence: an edit between rounds would change
    /// it, orphaning the previous round unresolved. What it is for is saying WHICH text a round
    /// read, which is what makes "the document changed between rounds" observable instead of silent.
    /// </remarks>
    public static string ArtifactIdOf(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
}
