namespace CoaiMcp.Core.Normalising;

/// <summary>A language the normalizer can read, or the honest statement that it cannot.</summary>
/// <remarks>
/// <para>Three, deliberately: they are 93 % of the measured corpus (`.ts` 234, `.cs` 189, `.mjs` 38,
/// `.cjs` 1 of 462 candidates) and every one of them is a grammar the binding already ships. The
/// rest — yml, py, md, sh — are <see cref="Unsupported"/>, which the collector records as
/// <c>language_unsupported</c> rather than as a failure.</para>
/// <para><see cref="Unsupported"/> is a VALUE and not a null for the reason the skip vocabulary
/// exists at all: "this file is in a language we do not read" is a measurement, and a measurement
/// nobody can count is not one.</para>
/// </remarks>
public enum SourceLanguage
{
    Unsupported,
    CSharp,
    TypeScript,
    JavaScript,
}

/// <summary>The function a line fell inside — what the collector reads back out of git.</summary>
/// <param name="Kind">The grammar's own word for it, e.g. <c>method_declaration</c>.</param>
/// <param name="StartLine">1-based and inclusive, as a finding's line is.</param>
/// <param name="EndLine">1-based and inclusive.</param>
/// <param name="Source">The function's own text, which is what gets normalised.</param>
/// <param name="Name">
/// What the function is called, or empty when the grammar gives it no name (a lambda).
/// </param>
/// <remarks>
/// <para><b>The name is why a fix can be found at all.</b> A finding's line is a coordinate in ONE
/// commit, and the commit that fixes it has almost always moved the method — an import added above
/// it, a comment removed, another method grown. Looking at the fix commit by the original line finds
/// an unrelated symbol or nothing at all. The collector locates the method at `head_sha` by line,
/// takes its NAME, and finds it again at every later commit by that. (Plan round, gemini.)</para>
/// </remarks>
public sealed record EnclosingSymbol(
    string Kind, int StartLine, int EndLine, string Source, string Name = "");

/// <summary>
/// Reads a method out of a file, and rewrites it so nothing of this project is left in it.
/// </summary>
/// <remarks>
/// <para><b>This interface is the whole reason the dependency is containable.</b> It lives in the
/// pure core, which knows nothing of IO and nothing of tree-sitter; the implementation and every
/// P/Invoke live in one project behind it. That was the third condition of the operator's approval
/// of the binding on 2026-09-15, and it is checked by an architecture test rather than by habit.</para>
/// <para>It is also what keeps the rejected alternative cheap. If the binding ever has to go, a
/// parser per language — Roslyn for C#, the TypeScript compiler API for the rest — is a second
/// implementation of this interface rather than a second design.</para>
/// <para>Nothing here does IO. The caller reads the file out of git, because only the caller knows
/// which commit it wants; this only ever sees text.</para>
/// </remarks>
public interface IAstNormalizer
{
    /// <summary>Which language a path is in, by its extension alone.</summary>
    /// <remarks>
    /// By extension because that is all the collector has: the finding names a path inside a commit,
    /// and the file may no longer exist in the working tree at all.
    /// </remarks>
    SourceLanguage LanguageOf(string path);

    /// <summary>
    /// The smallest function containing <paramref name="line"/>, or nothing when there is none.
    /// </summary>
    /// <param name="line">1-based, as a finding's line is.</param>
    /// <returns>
    /// <c>null</c> when the line is inside no function — a field, a using block, a blank line at the
    /// top of a file. The collector records that as <c>symbol_not_resolved</c>. It is a real and
    /// expected answer: a reviewer's line is whatever the model wrote, and nothing ever resolved it
    /// back to source until this.
    /// </returns>
    EnclosingSymbol? Locate(SourceLanguage language, string source, int line);

    /// <summary>The function of this NAME, wherever it has moved to.</summary>
    /// <remarks>
    /// The other half of <see cref="Locate"/>, and the one that makes a walk possible: the collector
    /// finds a method at the commit the reviewers read, by line, and then has to find the same method
    /// in every later commit — where the line has moved. Nothing but the name survives that.
    /// <para>Returns nothing when the name is not there any more, which is a real answer: a method
    /// that was renamed or deleted between the defect and the fix cannot be compared with itself.</para>
    /// </remarks>
    EnclosingSymbol? LocateNamed(SourceLanguage language, string source, string name);

    /// <summary>
    /// Rewrites a method so that nothing of this project is left in it.
    /// </summary>
    /// <remarks>
    /// <para>Kept: control flow, synchronisation primitives, await points, and the runtime's own
    /// names — the shape of the failure. Removed: every identifier we chose, every string, every
    /// number, every comment. What survives is what <see cref="RuntimeVocabulary"/> allows, plus
    /// placeholders numbered in order of first appearance.</para>
    /// <para><b>Deterministic.</b> The same method normalises to the same text every time, and two
    /// methods that differ only in their names normalise to the SAME text — which is the whole
    /// reason this exists: a corpus keyed on shape cannot be keyed on anything a rename would
    /// change.</para>
    /// <para>Whether the result is safe to send is not decided here. <see cref="Skeleton.Leaks"/>
    /// answers that, and it is run twice: once by the collector, which can also compare against the
    /// original, and once by the server, which never sees one.</para>
    /// </remarks>
    string Normalise(SourceLanguage language, string source);

    /// <summary>The language's own reserved words, which are not ours to rename or to leak.</summary>
    /// <remarks>
    /// It comes from the normalizer because the normalizer has the grammar: tree-sitter names every
    /// keyword as a node kind of its own, so this is read off the parser rather than typed out. A
    /// hand-written list here would be a second copy of the grammar, worse and going stale.
    /// </remarks>
    IReadOnlySet<string> KeywordsOf(SourceLanguage language);
}
