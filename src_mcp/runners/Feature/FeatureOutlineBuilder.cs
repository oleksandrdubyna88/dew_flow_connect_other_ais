using CoaiMcp.Core.Context;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Outlining;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Feature;

/// <summary>The bounds one outline build keeps to — the shipped ones by default, smaller in a test.</summary>
/// <param name="OutlineBytes">The outline section's budget, member hunks included (D22).</param>
/// <param name="MaxOutlinedFiles">The most files READ; the rest are named, not read.</param>
/// <param name="ReadCeilingBytes">The most bytes of file content one build reads through <c>cat-file --batch</c>.</param>
public sealed record FeatureOutlineLimits(int OutlineBytes, int CollapseAboveBytes, int MaxOutlinedFiles, long ReadCeilingBytes)
{
    public static readonly FeatureOutlineLimits Shipped = new(
        FeatureBudget.OutlineBytes, FeatureBudget.CollapseAboveBytes, FeatureBudget.MaxOutlinedFiles, FeatureOutlineBuilder.ReadCeilingBytes);
}

/// <summary>
/// Builds the feature reviewer's view of <c>base..head</c> — every changed file named, the readable ones
/// outlined at head, the changed members marked and their hunks attached — from git objects alone.
/// </summary>
/// <remarks>
/// <para><b>Bounded before anything is read</b> (plan round E2): the sizes come first, from ONE
/// <c>git cat-file --batch-check</c>; a binary, an unsupported language, a file over
/// <see cref="OutlineLimits.MaxInputBytes"/> and everything past the file cap or the read ceiling is
/// NAMED with its size and never read (<see cref="ReadPlan"/>). Only then does ONE
/// <c>git cat-file --batch</c> read the chosen blobs — the feature-pack trial measured <c>git show</c>
/// per file 50–70× slower — and they are outlined in-process through <see cref="ISourceOutliner"/>,
/// because a process per file was 93 % of a pack's local wall time.</para>
/// <para><b>Git objects at the head commit, never the working tree</b>: a dirty checkout, another
/// session's uncommitted edit or a checkout on another commit cannot reach the reviewer.</para>
/// <para><b>What the source resolver refuses, the pack refuses too</b> (D15, S2.2b): a credential-shaped
/// file (<see cref="CredentialFiles"/>) is named as withheld and never read, whatever language its
/// extension names; and every file's CONTENT — the text its signatures are outlined from, and each
/// changed line of its hunks — passes <see cref="Redaction.SafeSource"/>, the redaction a served file
/// passes. Both happen here, the one place a file's text enters the pack. The redaction keeps every line
/// break where it was, so the line numbers the outline and the hunks carry are still the file's.</para>
/// <para>Every git process goes through the product's one launcher with <see cref="GitDeadline"/>, whose
/// timeout kills the whole tree. A git that fails or times out is an unexpected infrastructure failure
/// and throws <see cref="ContextException"/> naming the command; the caller decides the round.</para>
/// <para><c>repoPath</c> is the repository's top level — paths are read as git prints them, from
/// there.</para>
/// </remarks>
public sealed class FeatureOutlineBuilder(IProcessLauncher launcher, ISourceOutliner outliner)
{
    /// <summary>The most file content one build reads: 16 MiB.</summary>
    /// <remarks>
    /// The widest range S0.2 measured held 6.3 MB of source at head (the S8 notices, 258 files). At the
    /// measured outline ratio of 4.6–5.3 %, 16 MiB of source is about 800 KB of outline — five times what
    /// <see cref="FeatureBudget.OutlineBytes"/> can show — so a file past this ceiling would have been
    /// dropped by the budget anyway; the ceiling only stops it being read first. The batch's stdout
    /// ceiling is set from what was chosen, so the read itself is never cut short.
    /// </remarks>
    public const long ReadCeilingBytes = 16L * 1024 * 1024;

    /// <summary>How long one git process may take before its tree is killed: 60 s.</summary>
    /// <remarks>
    /// The whole batch read measured 58–95 ms for 105–202 files (S0.2); a minute is three orders of
    /// magnitude of headroom for a cold disk or a large repository, and still a bound.
    /// </remarks>
    public static readonly TimeSpan GitDeadline = TimeSpan.FromSeconds(60);

    /// <summary>What a diff may put on stdout before the rest is dropped — and said to be.</summary>
    private const int DiffCeilingChars = 16 * 1024 * 1024;

    /// <summary>The launcher's own stdout ceiling for every other git answer.</summary>
    private const int AnswerCeilingChars = 8 * 1024 * 1024;

    /// <summary>
    /// The sentence the launcher ends a CUT stream with — the piece it lands in is not whole. A piece that
    /// merely quotes it in a changed line (this builder, the launcher, a test of either) is.
    /// </summary>
    private const string CutMarker = "[coai: output truncated";

    public async Task<FeatureOutline> BuildAsync(
        string repoPath,
        string baseRef,
        string headSha,
        FeatureOutlineLimits? limits = null,
        IReadOnlyList<string>? exclusions = null,
        CancellationToken ct = default)
    {
        var bounds = limits ?? FeatureOutlineLimits.Shipped;
        var head = IsCommit(headSha)
            ? headSha
            : throw new ArgumentException($"'{headSha}' is not a commit id — resolve head before building its outline", nameof(headSha));
        var (against, kind) = await new ContextAssembler(launcher).ComparisonBase(repoPath, baseRef, head, ct, GitDeadline);
        var range = new Range(repoPath, against, head, [.. (exclusions ?? DiffExclusions.Default).Select(e => $":(exclude,glob){e}")]);

        var numstat = await Git(range, ["diff", "--numstat", "-z", "-M", range.Span, "--", ".", .. range.Excludes], ct);
        var counted = NumstatReader.ReadCounted(numstat.StdOut).ToList();
        var objects = await CheckAsync(range, counted, ct);
        var files = ChangedFile.Ordered(counted.Select(c => Changed(c, objects[c.Change.Path])));

        var plan = ReadPlan.For(files, p => (objects[p].Head, objects[p].Askable), outliner.LanguageOf, bounds);
        var blobs = await ReadAsync(range, plan.Read, ct);
        var (marks, marksNote) = await PiecesAsync(range, "-U0", "the `*` marks", ct);
        var (hunks, hunksNote) = await PiecesAsync(range, "-U3", "the member hunks", ct);

        var (outlined, named) = Classify(files, plan, blobs, marks, hunks);
        var composed = OutlineComposer.Compose(outlined, bounds.OutlineBytes, bounds.CollapseAboveBytes);

        return new FeatureOutline(
            against, head, BaseNote(kind), files, composed.Section, composed.Outlined,
            new FeatureOmissions(named, composed.Collapsed, composed.Dropped, composed.CutHunks, [.. new[] { marksNote, hunksNote }.Where(n => n.Length > 0), .. composed.Notes]));
    }

    /// <summary>Every file's fate, in change-size order: outlined, or named with the reason it was not.</summary>
    private (IReadOnlyList<OutlinedFile> Outlined, IReadOnlyList<NotOutlined> Named) Classify(
        IReadOnlyList<ChangedFile> files,
        ReadPlan plan,
        IReadOnlyDictionary<string, BlobText> blobs,
        IReadOnlyDictionary<string, string> marks,
        IReadOnlyDictionary<string, string> hunks)
    {
        var (outlined, named) = (new List<OutlinedFile>(), new List<NotOutlined>());
        foreach (var file in files)
        {
            if (plan.NotRead.TryGetValue(file.Path, out var notRead))
            {
                named.Add(notRead);
                continue;
            }

            var outline = Outline(file.Path, blobs[file.Path], plan.Sizes[file.Path]);
            if (outline.IsOutlined)
            {
                outlined.Add(new OutlinedFile(file, outline, DiffHunks.ChangedSpans(Piece(marks, file.Path)), Safe(DiffHunks.Placed(Piece(hunks, file.Path)))));
            }
            else
            {
                named.Add(new NotOutlined(file.Path, outline.Reason, plan.Sizes[file.Path], Lines(blobs[file.Path].Text)));
            }
        }

        return (outlined, named);
    }

    /// <summary>
    /// The outline of a blob's REDACTED text — or, when its bytes were not UTF-8 or its redaction could not
    /// finish, the statement that it has none.
    /// </summary>
    /// <remarks>
    /// Redacted before it is parsed, so a secret in a default argument or a field initialiser never
    /// reaches a signature. A redaction that gave up answers <see cref="Redaction.Redacted"/> for the whole
    /// text (it fails closed); that file is withheld with its reason rather than outlined as the
    /// placeholder, which would parse as nothing and be misreported as a parse failure.
    /// </remarks>
    private SourceOutline Outline(string path, BlobText blob, long bytes)
    {
        var language = outliner.LanguageOf(path);
        var safe = blob.Aligned ? Redaction.SafeSource(blob.Text) : string.Empty;

        return !blob.Aligned ? new SourceOutline(language, OutlineStatus.ParseFailed, "not valid UTF-8 text; not outlined", bytes, [])
            : safe == Redaction.Redacted && blob.Text != Redaction.Redacted
                ? new SourceOutline(language, OutlineStatus.ParseFailed, "withheld — its content could not be redacted in time; not outlined", bytes, [])
            : outliner.Outline(language, safe);
    }

    /// <summary>Each changed line's text through the redaction, its marker and its place kept.</summary>
    private static IReadOnlyList<DiffLine> Safe(IReadOnlyList<DiffLine> lines) =>
        [.. lines.Select(line => line with { Text = line.Text[..1] + Redaction.SafeSource(line.Text[1..]) })];

    private static string Piece(IReadOnlyDictionary<string, string> pieces, string path) =>
        pieces.TryGetValue(path, out var piece) ? piece : string.Empty;

    private static int Lines(string text) => text.Count(c => c == '\n') + (text.Length > 0 && text[^1] != '\n' ? 1 : 0);

    /// <summary>
    /// Every changed file's object at head and whether it existed at the base — ONE
    /// <c>cat-file --batch-check</c>, two names per file. Added and deleted are read here rather than
    /// from a second diff: absent at head is a deletion, absent at the base an addition.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, Objects>> CheckAsync(Range range, IReadOnlyList<CountedChange> counted, CancellationToken ct)
    {
        var askable = counted.Where(c => ReadPlan.CanAsk(c.Change.Path) && ReadPlan.CanAsk(From(c))).ToList();
        var names = askable.SelectMany(c => new[] { $"{range.Head}:{c.Change.Path}", $"{range.Against}:{From(c)}" }).ToList();
        IReadOnlyList<GitObject> answers = names.Count == 0
            ? []
            : CatFile.ParseCheck((await Git(range, ["cat-file", "--batch-check"], ct, stdin: string.Concat(names.Select(n => n + "\n")))).StdOut, names.Count);

        var objects = counted.ToDictionary(c => c.Change.Path, _ => Objects.Unaskable, StringComparer.Ordinal);
        for (var i = 0; i < askable.Count; i++)
        {
            objects[askable[i].Change.Path] = new Objects(answers[2 * i], answers[(2 * i) + 1].Exists);
        }

        return objects;
    }

    private static string From(CountedChange change) =>
        change.Change.RenamedFrom.Length > 0 ? change.Change.RenamedFrom : change.Change.Path;

    private static ChangedFile Changed(CountedChange counted, Objects objects)
    {
        var change = counted.Change;
        var kind = change.RenamedFrom.Length > 0 ? FileChange.Renamed
            : objects.Askable && !objects.Head.Exists ? FileChange.Deleted
            : objects.Askable && !objects.AtBase ? FileChange.Added
            : FileChange.Modified;

        return new ChangedFile(change.Path, change.RenamedFrom, kind, counted.Added, counted.Deleted, change.IsBinary);
    }

    /// <summary>The chosen blobs' text, by path — ONE <c>cat-file --batch</c>, its stdout ceiling set from what was chosen.</summary>
    private async Task<IReadOnlyDictionary<string, BlobText>> ReadAsync(Range range, IReadOnlyList<(string Path, GitObject Blob)> chosen, CancellationToken ct)
    {
        if (chosen.Count == 0)
        {
            return new Dictionary<string, BlobText>(StringComparer.Ordinal);
        }

        // Characters never outnumber the bytes they were decoded from, so the content fits in as many
        // characters as the chosen sizes add up to (at most the read ceiling); each header is one short
        // line on top.
        var ceiling = (int)Math.Min(int.MaxValue, chosen.Sum(c => c.Blob.Size) + (chosen.Count * 128L) + 1024);
        var result = await Git(range, ["cat-file", "--batch"], ct, stdin: string.Concat(chosen.Select(c => c.Blob.Oid + "\n")), maxChars: ceiling);
        var blobs = CatFile.ParseBatch(result.StdOut, [.. chosen.Select(c => c.Blob)]);

        return chosen.Select((c, i) => (c.Path, Text: blobs[i])).ToDictionary(p => p.Path, p => p.Text, StringComparer.Ordinal);
    }

    /// <summary>
    /// One whole-range diff, split per file by <see cref="DiffSplitter"/> — and, if it was cut at the
    /// ceiling, the sentence saying what that costs. The piece the cut landed in is discarded rather than
    /// read as though it were whole — found by where the launcher's sentence stands, never by the
    /// sentence appearing somewhere inside a piece.
    /// </summary>
    private async Task<(IReadOnlyDictionary<string, string> Pieces, string Note)> PiecesAsync(Range range, string context, string what, CancellationToken ct)
    {
        var result = await Git(
            range,
            ["diff", context, "-M", "--no-color", "--no-ext-diff", "--no-textconv", "--src-prefix=a/", "--dst-prefix=b/", range.Span, "--", ".", .. range.Excludes],
            ct,
            maxChars: DiffCeilingChars);
        var pieces = DiffSplitter.ByFile(result.StdOut)
            .Where(p => !(result.Truncated && EndsAtTheCut(p.Value)))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        return (pieces, result.Truncated
            ? $"The `git diff {context}` of this range passed the {DiffCeilingChars}-character read ceiling, so {what} are missing for the files it did not reach."
            : string.Empty);
    }

    /// <summary>
    /// Whether a piece is the one the cut landed in. The launcher's sentence is the LAST line of a cut
    /// stream, so it can only end the last piece — and no line of a diff begins with <c>[</c>, so a changed
    /// line quoting the sentence (<c>+… [coai: …</c>) is never mistaken for it, whatever it contains.
    /// </summary>
    private static bool EndsAtTheCut(string piece)
    {
        var text = piece.TrimEnd('\n');

        return text[(text.LastIndexOf('\n') + 1)..].StartsWith(CutMarker, StringComparison.Ordinal);
    }

    private async Task<ProcessResult> Git(Range range, IReadOnlyList<string> args, CancellationToken ct, string stdin = "", int maxChars = AnswerCeilingChars)
    {
        var result = await launcher.RunAsync(
            new ProcessRequest("git", args, range.Repo) { StdIn = stdin, Timeout = GitDeadline, MaxOutputChars = maxChars }, ct);
        ct.ThrowIfCancellationRequested();

        return result.TimedOut ? throw new ContextException(string.Join(' ', args.Take(2)), $"did not finish within {GitDeadline.TotalSeconds:0} s; its process tree was killed")
            : result.ExitCode != 0 ? throw new ContextException(string.Join(' ', args.Take(2)), result.StdErr.Trim())
            : result;
    }

    private static string BaseNote(DiffBase kind) => kind switch
    {
        DiffBase.MergeBase => string.Empty,
        DiffBase.ShallowHistory => "the history is shallow, so no common ancestor could be found; compared tip to tip, and the base's own commits may read as deletions — deepen the clone",
        _ => "no common ancestor with head; compared tip to tip, and the base's own commits may read as deletions",
    };

    /// <summary>A hex object id — the only kind of value that reaches a range or a <c>rev:path</c> name here.</summary>
    private static bool IsCommit(string value) => value is { Length: >= 7 and <= 64 } && value.All(Uri.IsHexDigit);

    /// <summary>The range every call reads: the repository, the two commits, the exclusion pathspecs.</summary>
    private sealed record Range(string Repo, string Against, string Head, IReadOnlyList<string> Excludes)
    {
        public string Span => $"{Against}..{Head}";
    }

    /// <summary>A changed file's object at head, and whether it existed at the base.</summary>
    /// <param name="Askable">False when its path cannot be put to <c>cat-file</c> at all — a line break in it.</param>
    private sealed record Objects(GitObject Head, bool AtBase, bool Askable = true)
    {
        public static readonly Objects Unaskable = new(GitObject.Missing, AtBase: true, Askable: false);
    }
}
