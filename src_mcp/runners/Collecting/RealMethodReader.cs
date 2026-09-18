using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Normalising;

namespace CoaiMcp.Runners.Collecting;

/// <summary>Where one pair's method is: the coordinates the pair already stores.</summary>
/// <param name="HeadSha">The commit the reviewers read; the BEFORE side is the method there, at <paramref name="Line"/>.</param>
/// <param name="FixSha">The commit the collector found the fix in; the AFTER side is the method there, by <paramref name="SymbolName"/>.</param>
/// <param name="File">The finding's path at <paramref name="HeadSha"/>, relative to <paramref name="RepoPath"/>.</param>
/// <param name="SymbolName">The name the collector stored, which is how the after side is found.</param>
public sealed record MethodPlace(
    string RepoPath,
    string HeadSha,
    string FixSha,
    string File,
    int Line,
    string SymbolName);

/// <summary>
/// Reads one pair's method back out of git, un-anonymised, at both of its commits.
/// </summary>
/// <remarks>
/// <para><b>The same locate the collector ran, over the same commits, and nothing stored.</b> A pair
/// on the page is one <see cref="Collector"/> already resolved unambiguously — a candidate that
/// tripped a guard was never written — so this answers as it did, through the same three refusals,
/// in the same order: a NAMELESS function (a lambda) before any comparison, an AMBIGUOUS name (an
/// overload set, or one name in two classes) before the first match is taken, and NO match (renamed
/// or deleted) as an answer rather than a guess. <c>Named</c>'s own docblock says why FIRST is safe
/// only behind <c>CountNamed</c>; the code round refused the approximation twice and this does not
/// re-argue it.</para>
/// <para><b>What it must handle that the collector did not: the repository has CHANGED since.</b> A
/// commit pruned, a file gone, a checkout deleted — 41 % of recorded checkouts no longer exist
/// (measured, story 2.2). Every one of those is an ANSWER naming which, never an exit code and never
/// one word for all of them, because a person acts on each differently.</para>
/// <para><b>Every git failure is a failure, never a fact about the code.</b> <see cref="GitAnswer"/>
/// keeps "the object is absent" apart from "git did not run", and that distinction reaches the page
/// as two different reasons.</para>
/// <para>Nothing here writes: no row, no file, no column. A test asserts the database is byte-identical
/// before and after a read.</para>
/// </remarks>
public sealed class RealMethodReader(GitHistory git, IAstNormalizer normalizer)
{
    /// <summary>The method behind this pair, at both commits, or why either side cannot be shown.</summary>
    public async Task<RealMethod> ReadAsync(long findingId, MethodPlace place, CancellationToken ct = default)
    {
        var language = normalizer.LanguageOf(place.File);
        if (language is SourceLanguage.Unsupported)
        {
            return Refused(findingId, language, place, RealMethodReason.LanguageUnsupported);
        }

        var repository = await git.IsRepositoryAsync(place.RepoPath, ct);
        if (!repository.Ran)
        {
            return Refused(findingId, language, place, RealMethodReason.GitFailed);
        }

        if (!repository.Ok)
        {
            return Refused(findingId, language, place, RealMethodReason.RepoPathMissing);
        }

        return new RealMethod(findingId, language.ToString(), place.SymbolName)
        {
            Before = await BeforeAsync(place, language, ct),
            After = await AfterAsync(place, language, ct),
        };
    }

    /// <summary>An answer that stops both sides for one reason.</summary>
    private static RealMethod Refused(long findingId, SourceLanguage language, MethodPlace place, string reason) =>
        new(findingId, language.ToString(), place.SymbolName, reason)
        {
            Before = MethodSide.Unavailable(reason),
            After = MethodSide.Unavailable(reason),
        };

    /// <summary>The BEFORE side: the function around the finding's line at the commit the reviewers read.</summary>
    private async Task<MethodSide> BeforeAsync(MethodPlace place, SourceLanguage language, CancellationToken ct)
    {
        var read = await CommittedFile.ReadAsync(git, place.RepoPath, place.HeadSha, place.File, ct);
        if (read.Reason.Length > 0)
        {
            return MethodSide.Unavailable(read.Reason);
        }

        // The nameless guard, exactly as the collector applies it: a lambda resolves to a function
        // node with no name, and a function with no name cannot be found again at the fix.
        return normalizer.Locate(language, read.Text, place.Line) is not { } symbol || symbol.Name.Length == 0
            ? MethodSide.Unavailable(RealMethodReason.SymbolNotResolved)
            : Shown(language, read.Text, symbol);
    }

    /// <summary>The AFTER side: the function of the stored name at the commit the fix was found in.</summary>
    /// <remarks>
    /// <para>By NAME, because the line has moved — and by the name the PAIR stores rather than the
    /// one the before side found, so a head commit that has since been pruned still leaves this side
    /// readable.</para>
    /// <para><b>Under the finding's own path, and no rename is followed.</b> Not an omission: measured
    /// on real git while this was built, <c>git log --follow head..fix -- &lt;old name&gt;</c> lists
    /// the rename commit under the OLD name and nothing after it, so the collector's walk reads
    /// <c>fix:&lt;old name&gt;</c>, fails, and records <c>symbol_gone</c> — a pair whose fix renamed
    /// the file is never stored, and a recovery here would be code no stored row reaches. The first
    /// draft had one; <c>AFixThatRenamedTheFile_SaysTheFileIsNotInTheCommit</c> is what showed it
    /// unreachable.</para>
    /// </remarks>
    private async Task<MethodSide> AfterAsync(MethodPlace place, SourceLanguage language, CancellationToken ct)
    {
        var read = await CommittedFile.ReadAsync(git, place.RepoPath, place.FixSha, place.File, ct);

        return read.Reason.Length > 0
            ? MethodSide.Unavailable(read.Reason)
            : ByName(language, read.Text, place.SymbolName);
    }

    /// <summary>The two guards the collector applies before it compares anything, in its order.</summary>
    private MethodSide ByName(SourceLanguage language, string source, string name)
    {
        // An overload set shares a name, so a name is not an identity. Refusing an ambiguous one is
        // the safe direction: showing the wrong overload would put somebody else's method under
        // this defect's heading, with a sha to prove it. (Code round, codex, twice — the collector's.)
        if (normalizer.CountNamed(language, source, name) > 1)
        {
            return MethodSide.Unavailable(RealMethodReason.SymbolAmbiguous);
        }

        return normalizer.LocateNamed(language, source, name) is { } moved
            ? Shown(language, source, moved)
            : MethodSide.Unavailable(RealMethodReason.SymbolGone);
    }

    /// <summary>A side that is shown: the function's text, its class, its kind and its span.</summary>
    private MethodSide Shown(SourceLanguage language, string source, EnclosingSymbol symbol) =>
        new(
            Source: symbol.Source,
            ClassName: normalizer.EnclosingType(language, source, symbol.StartLine),
            Kind: symbol.Kind,
            StartLine: symbol.StartLine,
            EndLine: symbol.EndLine);

}
