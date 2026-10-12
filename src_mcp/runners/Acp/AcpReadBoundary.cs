namespace CoaiMcp.Runners.Acp;

/// <summary>
/// The one question the client asks when the agent asks for a file: the text, or the sentence refusing it.
/// </summary>
/// <remarks>
/// <para>An interface because the rule changes under the client without the client changing: today
/// <see cref="FolderReadBoundary"/> serves a folder lexically; the sanitised snapshot's manifest-based resolve
/// (D6 of <c>PLAN_vibe_and_kimi_runtimes.md</c>) replaces it behind the same seam, and the tests of the client
/// stay as they are.</para>
/// <para>The client never inspects the path itself and never reads a file itself — every read the agent makes
/// goes through here, which is what makes "served only inside the working folder" one decision in one place.</para>
/// </remarks>
public interface IAcpReadBoundary
{
    /// <summary>The text at the path the agent asked for, or the refusal.</summary>
    AcpReadAnswer Read(string requestedPath);
}

/// <summary>What a read came back as: the text, or the sentence the agent is told instead.</summary>
/// <param name="Served">True when <see cref="Text"/> is the file.</param>
/// <param name="Text">The file's text, empty when refused.</param>
/// <param name="Refusal">Why not, empty when served — the sentence the agent is shown, so it names no secret.</param>
public sealed record AcpReadAnswer(bool Served, string Text, string Refusal)
{
    public static AcpReadAnswer Of(string text) => new(true, text, string.Empty);

    public static AcpReadAnswer Refused(string why) => new(false, string.Empty, why);
}

/// <summary>
/// Serves only absolute paths lexically inside one root folder, refusing the vendor's excluded names at any
/// depth and every spelling that could resolve somewhere else.
/// </summary>
/// <remarks>
/// <para><b>Lexical first, then the disk.</b> The path is judged as a string BEFORE anything is normalised,
/// because normalisation is where the spellings hide: <c>Path.GetFullPath</c> strips a trailing dot or space from a
/// Windows component and would make <c>secret.txt.</c> read as <c>secret.txt</c>; a drive-relative <c>C:x</c>
/// resolves against a per-drive current directory nobody chose; a UNC or device path (<c>\\server\</c>,
/// <c>\\?\</c>, <c>\\.\</c>) is another namespace; a <c>:</c> beyond the drive letter names an alternate data
/// stream. Each is refused by shape. Then the normalised path must start with the root by components, and no
/// component (or pair, for <c>.agents/skills</c>) may be an excluded name — ordinal, and case-insensitive where
/// the filesystem is.</para>
/// <para><b>Then every step from the root down is checked for a reparse point</b> — a symlink, a junction, any
/// link — and the read is refused if one is on the way. The check and the read are two operations, so a link
/// planted between them is not closed here; the snapshot's manifest (D6) closes it by serving retained bytes, and
/// swaps in behind <see cref="IAcpReadBoundary"/> when it lands.</para>
/// <para><b>8.3 short names are refused by shape on Windows.</b> Every long name has an alias (<c>VIBE~1</c>,
/// <c>AGENTS~1.MD</c>) that the exclusions, comparing long names, would never match — so a component with a
/// <c>~</c> followed by a digit is declined outright (finding 2 of the security review). A legitimate name of that
/// shape is refused too, and the sentence says why.</para>
/// <para><b>What it does not know.</b> A link placed between the check and the read is the snapshot manifest's to
/// close. The root itself is the caller's and is not checked. And a path the filesystem cannot even normalise — too
/// long, a character it refuses — is declined with a sentence rather than thrown, so the agent's request is
/// answered (finding 3).</para>
/// </remarks>
/// <param name="root">The folder served — the sanitised snapshot, or a scratch folder.</param>
/// <param name="excludedNames">The profile's exclusions, as relative fragments.</param>
public sealed class FolderReadBoundary(string root, IReadOnlyList<string> excludedNames) : IAcpReadBoundary
{
    /// <summary>
    /// The most of one file that is served. A review's files are kilobytes; past this a file is a binary or a
    /// log, and reading it would hold it in the shim's memory to send it down a pipe one line at a time.
    /// </summary>
    public const long MaxBytes = 4 * 1024 * 1024;

    private static readonly char[] Separators = ['\\', '/'];

    /// <summary>The platform's rule: Windows and macOS fold case, Linux does not.</summary>
    private static readonly StringComparison Names =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;

    public AcpReadAnswer Read(string requestedPath)
    {
        try
        {
            var refusal = Refusal(_root, requestedPath, excludedNames, Names, OperatingSystem.IsWindows());
            if (refusal.Length > 0)
            {
                return AcpReadAnswer.Refused(refusal);
            }

            var full = Path.GetFullPath(requestedPath);
            var onTheWay = LinkOnTheWay(_root, full[_root.Length..]);

            return onTheWay.Length > 0 ? AcpReadAnswer.Refused(onTheWay) : ReadFile(full);
        }
        catch (Exception e) when (e is PathTooLongException or ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            // The filesystem would not even normalise it: declined, not thrown — the agent is owed an answer.
            return AcpReadAnswer.Refused("a path this boundary cannot judge — too long, or a character the filesystem refuses");
        }
    }

    /// <summary>
    /// Why <paramref name="requested"/> is not served from <paramref name="root"/> — or empty when it is. Pure,
    /// so every spelling is a row in a test on any platform.
    /// </summary>
    /// <param name="root">Normalised, with its trailing separator.</param>
    /// <param name="shortNames">Whether the filesystem keeps 8.3 aliases — Windows — so a <c>NAME~1</c> component is refused.</param>
    public static string Refusal(string root, string requested, IReadOnlyList<string> excluded, StringComparison names, bool shortNames)
    {
        if (Shape(requested) is { Length: > 0 } shape)
        {
            return shape;
        }

        if (shortNames && HasShortName(requested))
        {
            return "an 8.3 short name (NAME~1) is not served — the exclusions compare long names";
        }

        var full = Path.GetFullPath(requested);
        if (!full.StartsWith(root, names) || full.Length == root.Length)
        {
            return "outside the working folder";
        }

        return ExcludedIn(full[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries), excluded, names);
    }

    /// <summary>The refusals decided by the SPELLING alone, before normalisation can tidy it.</summary>
    private static string Shape(string requested)
    {
        if (requested.Length == 0 || requested.Contains('\0'))
        {
            return "an empty path, or one with a NUL in it";
        }

        if (requested.StartsWith(@"\\", StringComparison.Ordinal) || requested.StartsWith("//", StringComparison.Ordinal))
        {
            return "a UNC or device path (\\\\server, \\\\?\\, \\\\.\\) is not inside the working folder";
        }

        if (!Path.IsPathFullyQualified(requested))
        {
            return "not an absolute path — relative, drive-relative (C:name) and root-relative (\\name) spellings are refused";
        }

        return requested.IndexOf(':', 2) >= 0
            ? "a ':' beyond the drive letter names an alternate data stream"
            : Components(requested);
    }

    /// <summary>A component ending in a dot or a space — which Windows strips, so the spelling and the file differ.</summary>
    /// <remarks>The drive (<c>C:</c>) is not a component; <c>.</c> and <c>..</c> are spellings normalisation keeps honest.</remarks>
    private static string Components(string requested) =>
        requested.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Where(component => !(component.Length == 2 && component[1] == ':'))
            .Any(component => component is not ("." or "..") && (component.EndsWith('.') || component.EndsWith(' ')))
            ? "a path component ending in a dot or a space"
            : string.Empty;

    /// <summary>A component with a <c>~</c> followed by a digit — the shape of every Windows 8.3 alias (<c>VIBE~1</c>, <c>AGENTS~1.MD</c>).</summary>
    private static bool HasShortName(string requested) =>
        requested.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Any(component => Enumerable.Range(0, Math.Max(component.Length - 1, 0))
                .Any(i => component[i] == '~' && char.IsAsciiDigit(component[i + 1])));

    /// <summary>The first excluded fragment the relative components contain, as a refusal — or empty.</summary>
    private static string ExcludedIn(string[] components, IReadOnlyList<string> excluded, StringComparison names)
    {
        foreach (var fragment in excluded)
        {
            var parts = fragment.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (Enumerable.Range(0, Math.Max(components.Length - parts.Length + 1, 0))
                .Any(at => parts.Select((part, i) => string.Equals(components[at + i], part, names)).All(same => same)))
            {
                return $"'{fragment}' is not served — a vendor configuration or instruction path";
            }
        }

        return string.Empty;
    }

    /// <summary>A reparse point on any step from the root to the file — the refusal naming it, or empty.</summary>
    private static string LinkOnTheWay(string root, string relative)
    {
        var walked = root;
        foreach (var component in relative.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            walked = Path.Combine(walked, component);
            try
            {
                if ((File.GetAttributes(walked) & FileAttributes.ReparsePoint) != 0)
                {
                    return $"the path resolves through a link at '{walked}'";
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return "no such file in the working folder";
            }
        }

        return string.Empty;
    }

    private static AcpReadAnswer ReadFile(string full)
    {
        try
        {
            var info = new FileInfo(full);
            if ((info.Attributes & FileAttributes.Directory) != 0)
            {
                return AcpReadAnswer.Refused("a directory, not a file");
            }

            return info.Length > MaxBytes
                ? AcpReadAnswer.Refused($"over the {MaxBytes / (1024 * 1024)} MiB ceiling for one read")
                : AcpReadAnswer.Of(File.ReadAllText(full));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The sentence names no path and no system message: the agent is told the read failed, not what is there.
            return AcpReadAnswer.Refused("the file could not be read");
        }
    }
}
