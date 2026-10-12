namespace CoaiMcp.Runners.Snapshots;

/// <summary>
/// The path rules of a snapshot: what a git-listed path may spell before it is placed under the root, and how a
/// requested absolute path is resolved against the root and the manifest — lexically, by components, refusing every
/// spelling that could reach past either.
/// </summary>
/// <remarks>
/// <para><b>Why lexical.</b> The filesystem is asked NOTHING here. <see cref="Path.GetFullPath(string)"/> would resolve
/// against the current directory and, on Windows, strip a trailing dot or space so that <c>a.txt.</c> opens
/// <c>a.txt</c>; a resolution that asks the disk follows the very links this exists to refuse. The requested path is
/// split into components, <c>.</c> and <c>..</c> are applied by hand, and what is left must start with the root's own
/// components and then name a manifest entry, ordinally. What is served is the manifest's stored path — never the
/// supplied spelling, so an 8.3 short name, a case variant or an alternate data stream can never be what is opened.</para>
/// <para>The forms refused outright, each one a way of naming a place without an ordinary absolute path: a
/// drive-relative <c>C:foo</c> (the drive's current directory), a root-relative <c>\foo</c> (the current drive), a UNC
/// or device path <c>\\?\</c>, <c>\\.\</c>, <c>\\server\</c> (a prefix that switches off normalisation or names another
/// machine), a <c>:</c> after the drive (an alternate data stream), a component ending in <c>.</c> or a space (Windows
/// strips them). A name with <c>:</c> is refused on every platform, so a Unix snapshot cannot serve a file so named —
/// said plainly, and preferred to a rule that differs by platform.</para>
/// </remarks>
internal static class SnapshotPaths
{
    private static readonly char[] Separators = ['/', '\\'];

    private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

    /// <summary>
    /// The names Windows opens as a DEVICE whatever directory they are asked in, and with any extension (<c>nul.txt</c>
    /// is NUL). A component so named cannot be a file of the snapshot on Windows.
    /// </summary>
    private static readonly HashSet<string> Devices = new(
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Why a git-listed relative path (forward slashes) cannot be placed under a root, or empty.</summary>
    /// <remarks>
    /// The third question — would Windows open a DIFFERENT name — is the one the review of S1 added: a tracked
    /// <c>.vibe./config.toml</c> is not <c>.vibe/config.toml</c> to the exclusions, but Windows strips the trailing dot
    /// on the way to the disk, and the copy would have landed under the excluded name. Asked of every component, on
    /// every platform: the index came from somewhere, and the snapshot may be made on Windows.
    /// </remarks>
    public static string Unsafe(string relative)
    {
        if (relative.Length == 0)
        {
            return "an empty path was listed";
        }

        if (relative.StartsWith('/') || IsDriveSpelling(relative))
        {
            return $"{relative} is absolute, so it would leave the snapshot root";
        }

        var parts = relative.Split('/');
        var why = new[] { Leaving(parts), Unnameable(parts), Rewritten(parts) }.FirstOrDefault(static reason => reason.Length > 0) ?? string.Empty;

        return why.Length > 0 ? $"{relative}: {why}" : string.Empty;
    }

    private static string Leaving(string[] parts) =>
        parts.Any(static part => part.Length == 0 || part is "." or "..") ? "the path would leave the snapshot root" : string.Empty;

    private static string Unnameable(string[] parts) =>
        parts.Any(static part => part.IndexOfAny(Invalid) >= 0) ? "the path cannot be named on this filesystem" : string.Empty;

    private static string Rewritten(string[] parts) =>
        parts.Select(WindowsWouldRewrite).FirstOrDefault(static reason => reason.Length > 0) ?? string.Empty;

    /// <summary>Why Windows would open a DIFFERENT name for this component, or empty: a trailing dot or space is stripped, a device name is the device.</summary>
    internal static string WindowsWouldRewrite(string component)
    {
        if (component.EndsWith('.') || component.EndsWith(' '))
        {
            return $"'{component}' ends in a dot or a space, which Windows strips";
        }

        return Devices.Contains(BaseName(component)) ? $"'{component}' is a Windows device name" : string.Empty;
    }

    /// <summary>The part before the first dot, without trailing spaces — what Windows matches a device name against.</summary>
    private static string BaseName(string component)
    {
        var dot = component.IndexOf('.');

        return (dot < 0 ? component : component[..dot]).TrimEnd(' ');
    }

    /// <summary>A requested absolute path, resolved: the manifest-relative path it names, or why it names none.</summary>
    public sealed record Resolved(string Relative, string Refusal)
    {
        public static Resolved Refuse(string why) => new(string.Empty, why);
    }

    /// <summary>Resolves <paramref name="requested"/> against <paramref name="root"/> and what <paramref name="holds"/>.</summary>
    /// <param name="rootRule">How the root's own components compare — the destination's case rule; the manifest part is ordinal.</param>
    public static Resolved Resolve(string root, StringComparison rootRule, string requested, Func<string, bool> holds)
    {
        var form = NotAbsolute(requested);
        if (form.Length > 0)
        {
            return Resolved.Refuse($"{form}; only an absolute path inside the snapshot is served");
        }

        var asked = Lexical(requested);
        if (asked.Refusal.Length > 0)
        {
            return Resolved.Refuse(asked.Refusal);
        }

        var inside = Under(Lexical(root), asked, rootRule);
        if (inside.Length == 0)
        {
            return Resolved.Refuse("the path is outside the snapshot");
        }

        return holds(inside) ? new Resolved(inside, string.Empty) : Resolved.Refuse($"{inside} is not in the snapshot's manifest");
    }

    /// <summary>The form the request takes when it is not a plain absolute path, or empty when it is one.</summary>
    private static string NotAbsolute(string requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return "an empty path";
        }

        if (requested.Contains('\0'))
        {
            return "a path with a NUL character";
        }

        if (requested.Length >= 2 && Separators.Contains(requested[0]) && Separators.Contains(requested[1]))
        {
            return "a UNC or device path (\\\\?\\, \\\\.\\, \\\\server\\)";
        }

        return Path.IsPathFullyQualified(requested) ? string.Empty : RelativeForm(requested);
    }

    private static string RelativeForm(string requested) =>
        IsDriveSpelling(requested) ? "a drive-relative path (C:foo)"
        : Separators.Contains(requested[0]) ? "a root-relative path (\\foo)"
        : "a relative path";

    private static bool IsDriveSpelling(string path) => path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    /// <summary>A path as components after <c>.</c> and <c>..</c> are applied by hand, or the refusal.</summary>
    private sealed record Parts(string Prefix, IReadOnlyList<string> Components, string Refusal);

    private static Parts Lexical(string path)
    {
        var prefix = Path.GetPathRoot(path) ?? string.Empty;
        var stack = new List<string>();
        foreach (var part in path[prefix.Length..].Split(Separators))
        {
            var refusal = Push(stack, part);
            if (refusal.Length > 0)
            {
                return new Parts(prefix, [], refusal);
            }
        }

        return new Parts(prefix, stack, string.Empty);
    }

    private static string Push(List<string> stack, string part)
    {
        switch (part)
        {
            case "" or ".":
                return string.Empty;
            case "..":
                if (stack.Count == 0)
                {
                    return "the path leaves its root";
                }

                stack.RemoveAt(stack.Count - 1);
                return string.Empty;
            default:
                return Odd(part, stack);
        }
    }

    private static string Odd(string part, List<string> stack)
    {
        var rewritten = WindowsWouldRewrite(part);
        if (rewritten.Length > 0)
        {
            return rewritten;
        }

        if (part.Contains(':'))
        {
            return $"'{part}' names an alternate data stream";
        }

        stack.Add(part);

        return string.Empty;
    }

    /// <summary>The manifest-relative path when <paramref name="asked"/> starts with every component of <paramref name="root"/>, else empty.</summary>
    private static string Under(Parts root, Parts asked, StringComparison rule)
    {
        var prefixesMatch = string.Equals(Trim(root.Prefix), Trim(asked.Prefix), StringComparison.OrdinalIgnoreCase);
        var deeper = asked.Components.Count > root.Components.Count;
        var sameStart = root.Components.Select((component, i) => deeper && string.Equals(component, asked.Components[i], rule)).All(same => same);

        return prefixesMatch && deeper && sameStart
            ? string.Join('/', asked.Components.Skip(root.Components.Count))
            : string.Empty;

        static string Trim(string prefix) => prefix.TrimEnd(Separators);
    }
}
