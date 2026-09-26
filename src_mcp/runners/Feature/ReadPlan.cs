using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Outlining;

namespace CoaiMcp.Runners.Feature;

/// <summary>
/// Which changed files are READ, decided from their sizes before a byte of content is — and, for every
/// other file, the reason it is named instead (plan §4.7, plan round E2).
/// </summary>
/// <param name="Read">The blobs to read, in change-size order.</param>
/// <param name="NotRead">Every file that will not be read, by path, already in its named form.</param>
/// <param name="Sizes">Each file's size at head, by path — -1 where it has none.</param>
/// <remarks>
/// <para>Pure: it is handed the sizes <c>cat-file --batch-check</c> answered and decides nothing else.
/// The order of the checks is the order of the reasons a reviewer can act on — a deletion is a
/// deletion before it is anything else, and "past the cap" is said only of a file that would otherwise
/// have been read.</para>
/// <para>A file past the cap or the ceiling is skipped, not a stop: files arrive largest change first,
/// not largest file first, so a smaller file behind it may still fit.</para>
/// </remarks>
public sealed record ReadPlan(
    IReadOnlyList<(string Path, GitObject Blob)> Read,
    IReadOnlyDictionary<string, NotOutlined> NotRead,
    IReadOnlyDictionary<string, long> Sizes)
{
    /// <summary>Whether a path can be put to <c>cat-file</c>: its input is one name per LINE, and git strips a carriage return.</summary>
    public static bool CanAsk(string path) => !path.Contains('\n') && !path.Contains('\r');

    /// <param name="headOf">A file's object at head, and whether its path could be asked about at all.</param>
    public static ReadPlan For(
        IReadOnlyList<ChangedFile> files,
        Func<string, (GitObject Head, bool Askable)> headOf,
        Func<string, OutlineLanguage> languageOf,
        FeatureOutlineLimits bounds)
    {
        var read = new List<(string Path, GitObject Blob)>();
        var notRead = new Dictionary<string, NotOutlined>(StringComparer.Ordinal);
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        var readBytes = 0L;
        foreach (var file in files)
        {
            var (head, askable) = headOf(file.Path);
            var reason = Reason(file, head, askable, languageOf(file.Path), (read.Count, readBytes), bounds);
            sizes[file.Path] = head.Size;
            if (reason.Length > 0)
            {
                notRead[file.Path] = new NotOutlined(file.Path, reason, head.Exists ? head.Size : NotOutlined.Unknown);
                continue;
            }

            read.Add((file.Path, head));
            readBytes += head.Size;
        }

        return new ReadPlan(read, notRead, sizes);
    }

    /// <summary>Why a file is not read, or empty when it is.</summary>
    private static string Reason(
        ChangedFile file, GitObject head, bool askable, OutlineLanguage language, (int Files, long Bytes) sofar, FeatureOutlineLimits bounds) =>
        file switch
        {
            // FIRST, before the language is asked: `.env.production.ts` is TypeScript, and the pack goes
            // to third-party models exactly as a served file does (D15 — the fixed shapes, never the words).
            _ when CredentialFiles.WhichPattern(file.Path) is { Length: > 0 } shape =>
                $"withheld — looks like a credential file ({shape}); never read",
            { Change: FileChange.Deleted } => "deleted at head",
            _ when !askable => "a path with a line break cannot be read through git cat-file; not read",
            _ when !head.Exists => "missing at head; not read",
            _ when !head.IsBlob => head.Type == "commit" ? "a submodule at head; not read" : $"not a file at head ({head.Type}); not read",
            { IsBinary: true } => "binary; not read",
            _ when language == OutlineLanguage.Unsupported => "unsupported (language); not read",
            _ when head.Size > OutlineLimits.MaxInputBytes => $"unsupported (too large): over the {OutlineLimits.MaxInputBytes}-byte ceiling; not read",
            _ when sofar.Files >= bounds.MaxOutlinedFiles => $"past the {bounds.MaxOutlinedFiles}-file outline cap; not read",
            _ when sofar.Bytes + head.Size > bounds.ReadCeilingBytes => $"past the {bounds.ReadCeilingBytes}-byte read ceiling of one range; not read",
            _ => string.Empty,
        };
}
