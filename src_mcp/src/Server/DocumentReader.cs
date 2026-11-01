using System.Text;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Server;

/// <summary>What the caller said the document is: exactly one of a path or some text.</summary>
/// <param name="Name">
/// Required with <paramref name="Text"/> and refused with <paramref name="Path"/> — a path already
/// names the document, and a second name would be a second identity for one thing.
/// </param>
public sealed record DocumentRequest(string? Path = null, string? Text = null, string? Name = null);

/// <summary>A document ready to be reviewed, or the sentence saying why it is not.</summary>
public abstract record DocumentOutcome
{
    /// <param name="Id">The session's third key segment: the document's identity, never its content.</param>
    /// <param name="Name">What a person reads in the rounds log.</param>
    /// <param name="ArtifactId">This SNAPSHOT — which text this round read.</param>
    public sealed record Ready(string Id, string Name, string Text, string ArtifactId) : DocumentOutcome;

    public sealed record Refused(string Sentence) : DocumentOutcome;

    private DocumentOutcome() { }
}

/// <summary>
/// Turns what a caller sent into a document, or into one sentence saying why it is not one.
/// </summary>
/// <remarks>
/// <para><b>A path is confined to the repository, and that is a security rule rather than a
/// tidiness one.</b> This reads a file and ships its contents to three other vendors' APIs; without
/// confinement an AI that has been prompt-injected, or has simply misread an instruction, turns the
/// review gate into an exfiltration primitive with a friendly name. Two reviewers refused the plan's
/// first draft over it independently.</para>
/// <para>What that costs is said plainly in the refusal: a document outside any checkout cannot be
/// passed as a path. It can be passed as <c>documentText</c>, which is what a caller does anyway
/// once it has converted a <c>.docx</c>.</para>
/// </remarks>
public static class DocumentReader
{
    /// <summary>
    /// A decoder that THROWS rather than substituting. <c>Encoding.UTF8</c> replaces invalid bytes
    /// with U+FFFD, which is how a binary file becomes a document full of question marks that three
    /// reviewers then read in earnest.
    /// </summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private const string OneOrTheOther =
        "review_document takes the document as documentPath (a file inside this repository) or as "
      + "documentText with a documentName";

    /// <param name="read">
    /// How one path entry is looked at (<see cref="RealPath.Lstat"/> by default). Injected because the rule it exists
    /// for — a link INSIDE the repository pointing outside it, which a prefix check misses — is a decision worth a unit
    /// test rather than a privilege the CI runner may not have.
    /// </param>
    public static DocumentOutcome Read(string repoPath, DocumentRequest request, EntryReader? read = null)
    {
        var path = request.Path?.Trim() ?? string.Empty;
        var text = request.Text ?? string.Empty;

        if (path.Length > 0 && text.Trim().Length > 0)
        {
            return new DocumentOutcome.Refused($"{OneOrTheOther} — you sent both, and only one of them can be the document.");
        }

        return path.Length > 0
            ? FromPath(repoPath, path, request.Name, read)
            : FromText(text, request.Name);
    }

    /// <summary>
    /// The real path of <paramref name="path"/>, with EVERY component's links followed — <see cref="RealPath"/>, root-down —
    /// or the sentence saying why it has none.
    /// </summary>
    /// <remarks>
    /// <para><b>Resolving only the last component is an escape, and so is resolving only the LINK.</b> With <c>repo/docs</c>
    /// a symlink to <c>/outside</c>, <c>repo/docs/secret.md</c> has an ordinary final component, and the read follows the
    /// parent link (found by three reviewers on this change's first code round). The second form, found 2026-11-01: this
    /// walk took <c>ResolveLinkTarget(...).FullName</c> for a link — the target's own directories were never resolved and a
    /// <c>..</c> in its text was folded as a string — so <c>root/alias</c> → <c>root/l2/secret.md</c>, with
    /// <c>root/l2</c> a link outside, read the outside file and sent it to three vendors. The walk is now
    /// <see cref="RealPath.Resolve"/>: a stack of pending segments, every link's RAW text pushed in front of the rest, a
    /// budget of <see cref="RealPath.MaxLinkExpansions"/> expansions, and a failure — never a guess — for a loop, an
    /// over-budget chain, a <c>..</c> after a missing name or a NUL.</para>
    /// <para>Containment is decided by the caller on this result and on <see cref="CanonicalRoot"/>, both resolved the same way.</para>
    /// </remarks>
    /// <param name="repoRoot">
    /// What a RELATIVE path is resolved against — never the process working directory, which for an MCP server is wherever
    /// its client happened to launch it. It is joined as spelled, not folded: a <c>..</c> in the path is the disk's to apply.
    /// </param>
    /// <param name="read">How one entry is looked at; the real file system when null.</param>
    public static RealPathResult Canonical(string repoRoot, string path, EntryReader? read = null) =>
        RealPath.Resolve(path, repoRoot, read);

    /// <summary>
    /// The session identity a caller's word for a document means — the same one <c>Read</c> derives.
    /// </summary>
    /// <remarks>
    /// <b>`resolve` and `status` have to reach the session `review_document` created</b>, and they
    /// were reconstructing the identity by a different route: rooted paths only, no link resolution,
    /// and a different case rule. A caller that passed a relative path — which is what a person
    /// types — could then never resolve their own round. Five findings on one code round said so;
    /// one resolution, called from all three places, is the answer.
    /// </remarks>
    public static string IdentityOf(string repoPath, string said, EntryReader? read = null)
    {
        var trimmed = said.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        // A name given to raw text is its own identity and is not a path; it can never contain a
        // separator, so anything that does is a path and anything that does not is tried as a name
        // first and falls back to a path.
        // A path that cannot be resolved (a loop, an over-long one) is not a path of this repository: it is tried as a name.
        var root = CanonicalRoot(repoPath, read);
        var resolved = Canonical(repoPath, trimmed, read);
        var asPath = root.Ok && resolved.Ok ? DocumentId.Of(root.Path, resolved.Path) : string.Empty;

        return asPath.Length > 0 ? asPath : trimmed;
    }

    /// <summary>The repository root, resolved the same way the document inside it is.</summary>
    /// <remarks>
    /// <para><b>Containment compares two paths, and it was only ever resolving one of them.</b> The
    /// document was walked link by link; the root it was compared against was whatever the caller
    /// said. Where the two spellings agree — which is every Linux and Windows checkout anyone here
    /// had tried — the bug is invisible. On macOS it is the default: a repository under
    /// <c>/var/folders/…</c> resolves to <c>/private/var/folders/…</c> because <c>/var</c> is a
    /// link, so every document inside a perfectly ordinary repository was refused as being outside
    /// it, with a message naming two paths that differ by a prefix the person never typed.</para>
    /// <para>It is resolved rather than trusted for the same reason the document is: a root reached
    /// through a link and a root spelled directly are one directory, and a containment check that
    /// says otherwise is wrong in the direction that refuses honest work.</para>
    /// </remarks>
    public static RealPathResult CanonicalRoot(string repoPath, EntryReader? read = null) =>
        Canonical(repoPath, repoPath, read);

    /// <summary>
    /// A caller's path, made absolute against the REPOSITORY rather than the process — joined, never folded.
    /// </summary>
    /// <remarks>
    /// <c>Path.GetFullPath(path)</c> resolves a relative path against the current working directory,
    /// which for this server is wherever the MCP client happened to launch it — never the repository
    /// the session was opened for. A caller passing <c>docs/spec.md</c>, which is the ordinary thing
    /// to pass, therefore named a file nobody meant. And <c>GetFullPath</c> folds a <c>..</c> as a string:
    /// <c>alias/../x</c> with <c>alias</c> a link is not <c>x</c>, so the spelling is kept for the resolver.
    /// </remarks>
    private static string Absolute(string repoPath, string path) =>
        Path.IsPathRooted(path) ? path : Path.Join(repoPath, path);

    private static DocumentOutcome FromText(string text, string? name)
    {
        if (text.Trim().Length == 0)
        {
            return new DocumentOutcome.Refused($"{OneOrTheOther} — you sent neither.");
        }

        if (DocumentRules.NameRefusal(name) is { } why)
        {
            return new DocumentOutcome.Refused(why);
        }

        return DocumentRules.SizeRefusal(Encoding.UTF8.GetByteCount(text)) is { } tooBig
            ? new DocumentOutcome.Refused(tooBig)
            : new DocumentOutcome.Ready(name!.Trim(), name.Trim(), text, DocumentRules.ArtifactIdOf(text));
    }

    private static DocumentOutcome FromPath(string repoPath, string path, string? name, EntryReader? read)
    {
        if (name is not null && name.Trim().Length > 0)
        {
            return new DocumentOutcome.Refused(
                "documentName belongs with documentText: a documentPath already names the document, "
              + "and two names for one thing would be two sessions for it.");
        }

        // Made absolute against the REPOSITORY before anything looks at the disk, so every later
        // step — existence, extension, containment, the read itself — is about the same file.
        var absolute = Absolute(repoPath, path);
        if (OnDisk(absolute) is DocumentOutcome.Refused missing)
        {
            return missing;
        }

        var resolved = Canonical(repoPath, absolute, read);
        var root = CanonicalRoot(repoPath, read);
        if (!resolved.Ok || !root.Ok)
        {
            return new DocumentOutcome.Refused(Unresolvable(path, resolved.Ok ? root : resolved));
        }

        var id = DocumentId.Of(root.Path, resolved.Path);

        return id.Length == 0
            ? new DocumentOutcome.Refused(Outside(path, absolute, resolved.Path, repoPath))
            : Contents(resolved.Path, id);
    }

    /// <summary>Three things that are not a readable file, each said as itself.</summary>
    private static DocumentOutcome? OnDisk(string path)
    {
        if (Directory.Exists(path))
        {
            return new DocumentOutcome.Refused($"'{RealPath.Shown(path)}' is a directory — name the document itself.");
        }

        if (!File.Exists(path))
        {
            return new DocumentOutcome.Refused(
                $"there is no file at '{RealPath.Shown(path)}'. documentPath is a path on THIS machine, inside the "
              + "repository you opened the session for.");
        }

        return DocumentRules.ExtensionRefusal(path) is { } notText
            ? new DocumentOutcome.Refused(notText)
            : null;
    }

    /// <summary>A path that could not be resolved is refused, and the sentence says it is the resolution that failed — never "no such file".</summary>
    private static string Unresolvable(string said, RealPathResult failed) =>
        $"'{RealPath.Shown(said)}' cannot be resolved safely, so it is not read: {failed.Why}. First check the path and the links on it: a link "
      + "that loops, that leads through more than 40 links, or that goes where you did not mean is the usual cause. A document reviewed here is read "
      + "and sent to other vendors' models, so a path this gate cannot follow to its end is refused. Only if the file is the right one, pass its text as "
      + "documentText with a documentName.";

    private static string Outside(string said, string absolute, string resolved, string repoPath)
    {
        // The resolved path is named separately ONLY when it differs, because the case it exists
        // for is the confusing one: a link inside the repository whose target is not, where saying
        // only what the caller typed would look like a refusal of a path that is plainly inside.
        var target = string.Equals(absolute, resolved, DocumentId.Comparison)
            ? string.Empty
            : $" It resolves to '{RealPath.Shown(resolved)}'.";

        return $"'{RealPath.Shown(said)}' is not inside '{repoPath}'.{target} A document reviewed here is read and "
             + "sent to other vendors' models, so this gate reads only what is inside the repository "
             + "you opened the session for. Anything else: convert or copy it, and pass it as "
             + "documentText with a documentName.";
    }

    /// <summary>
    /// The text of the file the resolver named. Everything that touches the disk — the size too — is inside the one try: the path was
    /// judged a moment ago, and the file it names may be gone (a dangling link whose target is inside the repository, a file removed
    /// since), which is "no file", never an unhandled exception.
    /// </summary>
    private static DocumentOutcome Contents(string resolved, string id)
    {
        try
        {
            if (!File.Exists(resolved))
            {
                return new DocumentOutcome.Refused(
                    $"there is no file at '{RealPath.Shown(resolved)}' — the path resolves there (through a link, or as written) and nothing is at it. "
                  + "documentPath is a path on THIS machine, inside the repository you opened the session for.");
            }

            if (DocumentRules.SizeRefusal(new FileInfo(resolved).Length) is { } tooBig)
            {
                return new DocumentOutcome.Refused(tooBig);
            }

            var text = StrictUtf8.GetString(File.ReadAllBytes(resolved));

            return new DocumentOutcome.Ready(
                id, RoundSubject.FileNameOf(resolved), text, DocumentRules.ArtifactIdOf(text));
        }
        catch (DecoderFallbackException)
        {
            return new DocumentOutcome.Refused(
                $"'{Path.GetFileName(resolved)}' is not UTF-8 text — its bytes do not decode. Convert "
              + "it and pass the result as documentText with a documentName; reviewing a bad decode "
              + "means three reviewers reading whatever the decoder guessed.");
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new DocumentOutcome.Refused($"there is no file at '{RealPath.Shown(resolved)}' — it was removed while it was being read.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new DocumentOutcome.Refused($"'{Path.GetFileName(resolved)}' could not be read: {e.Message}");
        }
    }
}
