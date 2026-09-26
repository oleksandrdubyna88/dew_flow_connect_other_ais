using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using CoaiMcp.Core;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Outlining;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Context;

namespace CoaiMcp.Runners.Feature;

/// <summary>
/// Serves a feature reviewer's source requests from the git objects at ONE pinned commit — and from
/// nothing else (plan §4.9, D4, D15; story S3.1).
/// </summary>
/// <remarks>
/// <para><b>This is a security boundary, not a convenience.</b> What it serves goes to another
/// vendor's model. So: only <c>git show &lt;head&gt;:&lt;path&gt;</c>, through the one launcher and the
/// one guarded road (<see cref="GitHistory.FileAtAsync"/>) — never the working tree, so an edit not
/// yet committed is never seen and a path git will not follow (a symlink) serves nothing; a path that
/// is not repository-relative (<see cref="RepoPaths"/>), a credential-looking name
/// (<see cref="CredentialFiles"/>) and a lock file or build output (<see cref="DiffExclusions"/>)
/// are refused BEFORE any process starts; a binary blob is refused; and every byte served has been
/// through <see cref="Redaction.SafeSource"/> at the one place a file's text enters
/// (<see cref="ReadUncachedAsync"/>), so no slice can skip it.</para>
/// <para><b>One read per file per round.</b> The resolver is built per round, for its head, and is
/// shared by every reviewer of it: a file is read out of git once and outlined once, lazily, whatever
/// is asked of it and by how many. What is NOT shared is the spend — <see cref="SourceSpend"/> is a
/// reviewer's, handed in and handed back on every turn, because the caps are per reviewer and a
/// resolver that kept them would have to know who is asking.</para>
/// <para><b>Every cap is <see cref="SourceBudget"/>'s</b>, and a refusal for a cap names what was
/// dropped: the feature-pack trial's reviewers met "budget spent" without being told which of their
/// requests it was about.</para>
/// </remarks>
public sealed class SourceResolver(GitHistory git, ISourceOutliner outliner, string repoPath, string headSha)
{
    /// <summary>One file as the round holds it: refused with a sentence, or its redacted text, its lines, and its outline on demand.</summary>
    private sealed record CachedFile(string Refusal, string Text, ImmutableArray<string> Lines, Lazy<SourceOutline> Outline)
    {
        public bool IsRefused => Refusal.Length > 0;
    }

    /// <summary>What one request came to: slices, or exactly one refusal.</summary>
    private sealed record Answer(ImmutableArray<ServedSlice> Slices, ImmutableArray<SourceRefusal> Refusals);

    /// <summary>The bytes admitted so far — the reviewer's over every turn, and this turn's.</summary>
    private readonly record struct Ledger(int Reviewer, int Turn);

    private const string GitFailedSentence = "git could not read it just now; ask again next turn";

    private static readonly Lazy<SourceOutline> NoOutline =
        new(() => SourceOutline.UnsupportedLanguage(OutlineLanguage.Unsupported, 0));

    private readonly ConcurrentDictionary<string, Lazy<Task<CachedFile>>> _files = new(StringComparer.Ordinal);

    /// <summary>
    /// One turn's requests, served or refused, in order — and the reviewer's spend after them.
    /// </summary>
    /// <param name="spent">What this reviewer has been served over its earlier turns; <see cref="SourceSpend.None"/> on the first.</param>
    public async Task<ServedTurn> ServeAsync(
        IReadOnlyList<SourceRequest> requests, SourceSpend spent, CancellationToken ct = default)
    {
        var served = ImmutableArray.CreateBuilder<ServedSlice>();
        var refused = ImmutableArray.CreateBuilder<SourceRefusal>();
        var ledger = new Ledger(spent.Bytes, 0);
        foreach (var (request, index) in requests.Select((one, at) => (one, at)))
        {
            var answer = await AnswerAsync(request, index, ct);
            refused.AddRange(answer.Refusals);
            foreach (var slice in answer.Slices)
            {
                ledger = Admit(ledger, slice, served, refused);
            }
        }

        return new ServedTurn(served.ToImmutable(), refused.ToImmutable(), new SourceSpend(ledger.Reviewer));
    }

    private async Task<Answer> AnswerAsync(SourceRequest request, int index, CancellationToken ct)
    {
        var beforeReading = RefusedBeforeReading(request, index);
        if (beforeReading.Length > 0)
        {
            return Refuse(request, beforeReading);
        }

        var file = await ReadAsync(request.File, ct);

        return file.IsRefused ? Refuse(request, file.Refusal) : Choose(request, file);
    }

    /// <summary>What is refused before git is asked anything: the turn's request cap, a head that is no commit, and the name checks.</summary>
    private string RefusedBeforeReading(SourceRequest request, int index) => index switch
    {
        >= SourceBudget.RequestsPerTurn =>
            $"more than {SourceBudget.RequestsPerTurn} requests in one turn; ask again next turn",
        _ when !GitHistory.IsCommitish(headSha) => $"the head this round is pinned to ('{headSha}') is not a commit id",
        _ => RefusedByName(request.File),
    };

    /// <summary>The three name checks, in the order a reviewer should read them — and none of them costs a process.</summary>
    private static string RefusedByName(string file) => file switch
    {
        _ when RepoPaths.WhyNotRelative(file) is { Length: > 0 } why =>
            $"'{file}' {why}; only files in the repository are served",
        _ when CredentialFiles.WhichPattern(file) is { Length: > 0 } pattern => $"looks like a credential file ({pattern})",
        _ when DiffExclusions.WhichExcludes(file) is { Length: > 0 } glob => $"a lock file or build output ({glob}); never served",
        _ => string.Empty,
    };

    // --------------------------------------------------------------------------------------------
    // The read: once per file per round.
    // --------------------------------------------------------------------------------------------

    /// <summary>The file as the round holds it — read out of git once, however many requests name it.</summary>
    private async Task<CachedFile> ReadAsync(string path, CancellationToken ct)
    {
        var entry = _files.GetOrAdd(path, one => new Lazy<Task<CachedFile>>(() => ReadUncachedAsync(one, ct)));
        try
        {
            return await entry.Value;
        }
        finally
        {
            ForgetUnlessRead(path, entry);
        }
    }

    /// <summary>A read git could not finish is not kept: the next turn asks again rather than repeating a timeout's answer for the whole round.</summary>
    private void ForgetUnlessRead(string path, Lazy<Task<CachedFile>> entry)
    {
        var task = entry.Value;
        var kept = task.IsCompletedSuccessfully && task.Result.Refusal != GitFailedSentence;
        if (!kept)
        {
            _files.TryRemove(new KeyValuePair<string, Lazy<Task<CachedFile>>>(path, entry));
        }
    }

    /// <summary>The ONE place a file's text enters: git, the binary check, then redaction — nothing served can skip it.</summary>
    private async Task<CachedFile> ReadUncachedAsync(string path, CancellationToken ct)
    {
        var reading = await CommittedFile.ReadAtAsync(git, repoPath, headSha, path, ct);
        if (reading.Reason.Length > 0)
        {
            return Refused(ReasonFor(reading.Reason));
        }

        if (IsBinary(reading.Text))
        {
            return Refused("binary; not served");
        }

        var text = Redaction.SafeSource(reading.Text.Replace("\r\n", "\n", StringComparison.Ordinal));

        return new CachedFile(
            string.Empty, text, LinesOf(text), new Lazy<SourceOutline>(() => outliner.Outline(OutlineLanguages.Of(path), text)));
    }

    private static CachedFile Refused(string reason) => new(reason, string.Empty, [], NoOutline);

    private string ReasonFor(string reason) => reason switch
    {
        RealMethodReason.FileNotInCommit => $"not in the repository at {headSha}",
        RealMethodReason.CommitUnreachable => $"the commit {headSha} is not in the repository",
        _ => GitFailedSentence,
    };

    /// <summary>git's own test for a binary: a NUL in the first 8000 bytes.</summary>
    private static bool IsBinary(string text) =>
        text.AsSpan(0, Math.Min(text.Length, SourceBudget.BinarySniffChars)).Contains('\0');

    /// <summary>The file's lines, 1-based for the reader: a trailing newline ends the last line rather than starting an empty one.</summary>
    private static ImmutableArray<string> LinesOf(string text)
    {
        var parts = text.Split('\n');

        return text.EndsWith('\n') ? [.. parts[..^1]] : [.. parts];
    }

    // --------------------------------------------------------------------------------------------
    // What is served: a symbol's declaration(s), a span of lines, or the whole file (its head when big).
    // --------------------------------------------------------------------------------------------

    private Answer Choose(SourceRequest request, CachedFile file) => request switch
    {
        { Symbol.Length: > 0 } => SymbolSlices(request, file),
        { StartLine: > 0 } => LineSlice(request, file),
        _ => WholeOrHead(request, file),
    };

    private Answer SymbolSlices(SourceRequest request, CachedFile file)
    {
        var outline = file.Outline.Value;
        if (!outline.IsOutlined)
        {
            return Refuse(request, $"{OutlineExcuse(request.File, outline)}; ask for lines instead");
        }

        var match = SymbolLookup.Find(outline, request.Symbol);

        return match.IsEmpty
            ? Refuse(request, NoSuchSymbol(request, match))
            : new Answer([.. match.Entries.Select(entry => SymbolSlice(request, file, entry, OverloadNote(match)))], []);
    }

    private static string OutlineExcuse(string file, SourceOutline outline) => outline.Status switch
    {
        OutlineStatus.UnsupportedLanguage => $"symbols are not indexed for {Path.GetExtension(file)} files",
        _ => outline.Reason,
    };

    private static string NoSuchSymbol(SourceRequest request, SymbolMatch match) =>
        $"no declaration named '{request.Symbol}' in {request.File}; it declares: {string.Join(", ", match.Names)}"
        + (match.NamesInFile > match.Names.Length ? $" ({match.Names.Length} of {match.NamesInFile} names)" : string.Empty);

    private static string OverloadNote(SymbolMatch match) =>
        match.Total > 1 ? $"{match.Total} overloads, {match.Entries.Length} shown" : string.Empty;

    private ServedSlice SymbolSlice(SourceRequest request, CachedFile file, OutlineEntry entry, string overloads)
    {
        var start = Math.Clamp(entry.StartLine, 1, file.Lines.Length);
        var end = Math.Clamp(entry.EndLine, start, file.Lines.Length);

        return Slice(request, file, entry.Name, start, end, overloads);
    }

    private Answer LineSlice(SourceRequest request, CachedFile file)
    {
        var total = file.Lines.Length;
        if (request.StartLine > total)
        {
            return Refuse(request, $"{request.File} has {total} lines; {request.StartLine}-{request.EndLine} names none of them");
        }

        var wanted = Math.Min(Math.Max(request.EndLine, request.StartLine), total);

        return Slices(Slice(request, file, string.Empty, request.StartLine, wanted, string.Empty));
    }

    private Answer WholeOrHead(SourceRequest request, CachedFile file)
    {
        var total = file.Lines.Length;
        var bytes = Encoding.UTF8.GetByteCount(file.Text);
        if (bytes <= SourceBudget.WholeFileBytes)
        {
            return Slices(new ServedSlice(request.File, string.Empty, 1, total, total, headSha, file.Text, string.Empty, request.Why));
        }

        var head = Math.Min(HeadLines(file.Lines), SourceBudget.MaxLines);
        var note = $"{bytes} bytes, {total} lines; showing lines 1-{head}; ask for lines {head + 1}-{total}";

        return Slices(Slice(request, file, string.Empty, 1, head, note));
    }

    /// <summary>How many leading lines fit in the whole-file ceiling — at least one, so a file whose first line is over it still shows something.</summary>
    private static int HeadLines(ImmutableArray<string> lines)
    {
        var bytes = 0;
        var count = 0;
        foreach (var line in lines)
        {
            bytes += Encoding.UTF8.GetByteCount(line) + 1;
            if (bytes > SourceBudget.WholeFileBytes && count > 0)
            {
                break;
            }

            count++;
        }

        return count;
    }

    /// <summary>A span of lines, cut at the cap — the ONE place the line cap is applied — with the reason for any cut in the note.</summary>
    private ServedSlice Slice(SourceRequest request, CachedFile file, string symbol, int start, int wanted, string note)
    {
        var end = Math.Min(wanted, start + SourceBudget.MaxLines - 1);
        var cut = end < wanted ? $"cut at {SourceBudget.MaxLines} lines; ask for lines {end + 1}-{wanted}" : string.Empty;
        var text = string.Join('\n', file.Lines.Skip(start - 1).Take(end - start + 1));

        return new ServedSlice(request.File, symbol, start, end, file.Lines.Length, headSha, text, Notes(note, cut), request.Why);
    }

    private static string Notes(params string[] notes) => string.Join("; ", notes.Where(note => note.Length > 0));

    // --------------------------------------------------------------------------------------------
    // The caps.
    // --------------------------------------------------------------------------------------------

    /// <summary>The byte caps, applied to one slice: admitted and counted, or refused naming what was dropped and why.</summary>
    private static Ledger Admit(
        Ledger ledger, ServedSlice slice, ImmutableArray<ServedSlice>.Builder served, ImmutableArray<SourceRefusal>.Builder refused)
    {
        var bytes = slice.Bytes;
        if (ledger.Turn + bytes > SourceBudget.TurnBytes || ledger.Reviewer + bytes > SourceBudget.ReviewerBytes)
        {
            refused.Add(new SourceRefusal(slice.File, slice.Symbol, BudgetSpent(slice, bytes)));

            return ledger;
        }

        served.Add(slice);

        return new Ledger(ledger.Reviewer + bytes, ledger.Turn + bytes);
    }

    private static string BudgetSpent(ServedSlice slice, int bytes) =>
        $"budget spent: a turn carries at most {SourceBudget.TurnBytes / 1024} KB and a reviewer "
        + $"{SourceBudget.ReviewerBytes / 1024} KB in all; dropped lines {slice.StartLine}-{slice.EndLine} of {slice.File} ({bytes} bytes)";

    private static Answer Refuse(SourceRequest request, string reason) =>
        new([], [new SourceRefusal(request.File, request.Symbol, reason)]);

    private static Answer Slices(ServedSlice slice) => new([slice], []);
}
