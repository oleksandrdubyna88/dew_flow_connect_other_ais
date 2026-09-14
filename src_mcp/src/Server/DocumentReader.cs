using System.Text;
using CoaiMcp.Core.Rounds;

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

    /// <param name="followLink">
    /// Resolves a symlink to its target. Injected because the rule it exists for — a link INSIDE the
    /// repository pointing outside it, which a prefix check misses — is a decision worth a unit test
    /// rather than a privilege the CI runner may not have. The same seam
    /// <see cref="RoundSubject.From"/> takes for the same reason.
    /// </param>
    public static DocumentOutcome Read(string repoPath, DocumentRequest request, Func<string, string> followLink)
    {
        var path = request.Path?.Trim() ?? string.Empty;
        var text = request.Text ?? string.Empty;

        if (path.Length > 0 && text.Trim().Length > 0)
        {
            return new DocumentOutcome.Refused($"{OneOrTheOther} — you sent both, and only one of them can be the document.");
        }

        return path.Length > 0
            ? FromPath(repoPath, path, request.Name, followLink)
            : FromText(text, request.Name);
    }

    /// <summary>
    /// The real resolver, for ONE path entry — file or directory.
    /// </summary>
    /// <remarks>
    /// <c>returnFinalTarget</c> walks a chain of links to its end, which is why one call is enough
    /// per entry. What it does NOT do is look at the entry's parents, which is the hole
    /// <see cref="Canonical"/> exists to close.
    /// </remarks>
    public static string FollowLink(string path) =>
        File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName
        ?? Directory.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName
        ?? path;

    /// <summary>
    /// The real path of <paramref name="path"/>, with EVERY component's links followed.
    /// </summary>
    /// <remarks>
    /// <para><b>Resolving only the last component is an escape.</b> With <c>repo/docs</c> a symlink
    /// to <c>/outside</c>, the path <c>repo/docs/secret.md</c> has a perfectly ordinary final
    /// component: the resolver returns it unchanged, the syntactic containment check passes, and the
    /// read follows the parent link and hands <c>/outside/secret.md</c> to three vendors. Three
    /// reviewers found it independently on this change's own code round.</para>
    /// <para>So the walk is from the root DOWN, re-resolving after every step: a link found halfway
    /// moves the whole remaining walk, which is exactly what the attack relies on.</para>
    /// </remarks>
    /// <param name="repoRoot">
    /// What a RELATIVE path is resolved against. It was the process working directory — which for an
    /// MCP server is wherever its client happened to launch it — and both callers happened to pass an
    /// absolute path already. gemini's point is the one worth taking: a guarantee that holds because
    /// of what two callers do today is a guarantee the third caller breaks.
    /// </param>
    public static string Canonical(string repoRoot, string path, Func<string, string> followLink)
    {
        var full = Absolute(repoRoot, path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var rest = full[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        var at = root;
        foreach (var step in rest)
        {
            // Re-resolved at every step, not once at the end: the previous step may have moved this
            // one somewhere else entirely.
            at = Path.GetFullPath(followLink(Path.Combine(at, step)));
        }

        return at.Length == 0 ? full : at;
    }

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
    public static string IdentityOf(string repoPath, string said, Func<string, string> followLink)
    {
        var trimmed = said.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        // A name given to raw text is its own identity and is not a path; it can never contain a
        // separator, so anything that does is a path and anything that does not is tried as a name
        // first and falls back to a path.
        var asPath = DocumentId.Of(
            CanonicalRoot(repoPath, followLink), Canonical(repoPath, trimmed, followLink));

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
    public static string CanonicalRoot(string repoPath, Func<string, string> followLink) =>
        Canonical(repoPath, repoPath, followLink);

    /// <summary>
    /// A caller's path, made absolute against the REPOSITORY rather than the process.
    /// </summary>
    /// <remarks>
    /// <c>Path.GetFullPath(path)</c> resolves a relative path against the current working directory,
    /// which for this server is wherever the MCP client happened to launch it — never the repository
    /// the session was opened for. A caller passing <c>docs/spec.md</c>, which is the ordinary thing
    /// to pass, therefore named a file nobody meant.
    /// </remarks>
    private static string Absolute(string repoPath, string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(path, repoPath);

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

    private static DocumentOutcome FromPath(string repoPath, string path, string? name, Func<string, string> followLink)
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

        var resolved = Canonical(repoPath, absolute, followLink);
        var id = DocumentId.Of(CanonicalRoot(repoPath, followLink), resolved);

        return id.Length == 0
            ? new DocumentOutcome.Refused(Outside(path, resolved, repoPath))
            : Contents(resolved, id);
    }

    /// <summary>Three things that are not a readable file, each said as itself.</summary>
    private static DocumentOutcome? OnDisk(string path)
    {
        if (Directory.Exists(path))
        {
            return new DocumentOutcome.Refused($"'{path}' is a directory — name the document itself.");
        }

        if (!File.Exists(path))
        {
            return new DocumentOutcome.Refused(
                $"there is no file at '{path}'. documentPath is a path on THIS machine, inside the "
              + "repository you opened the session for.");
        }

        return DocumentRules.ExtensionRefusal(path) is { } notText
            ? new DocumentOutcome.Refused(notText)
            : null;
    }

    private static string Outside(string said, string resolved, string repoPath)
    {
        // The resolved path is named separately ONLY when it differs, because the case it exists
        // for is the confusing one: a link inside the repository whose target is not, where saying
        // only what the caller typed would look like a refusal of a path that is plainly inside.
        var target = string.Equals(Path.GetFullPath(said), resolved, DocumentId.Comparison)
            ? string.Empty
            : $" It resolves to '{resolved}'.";

        return $"'{said}' is not inside '{repoPath}'.{target} A document reviewed here is read and "
             + "sent to other vendors' models, so this gate reads only what is inside the repository "
             + "you opened the session for. Anything else: convert or copy it, and pass it as "
             + "documentText with a documentName.";
    }

    private static DocumentOutcome Contents(string resolved, string id)
    {
        if (DocumentRules.SizeRefusal(new FileInfo(resolved).Length) is { } tooBig)
        {
            return new DocumentOutcome.Refused(tooBig);
        }

        try
        {
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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new DocumentOutcome.Refused($"'{Path.GetFileName(resolved)}' could not be read: {e.Message}");
        }
    }
}
