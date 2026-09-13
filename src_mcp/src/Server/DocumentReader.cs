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

    /// <summary>The real resolver: one hop is enough, because a link to a link resolves to the end.</summary>
    public static string FollowLink(string path) =>
        File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;

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

        if (OnDisk(path) is DocumentOutcome.Refused missing)
        {
            return missing;
        }

        var resolved = followLink(Path.GetFullPath(path));
        var id = DocumentId.Of(repoPath, resolved);

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
        var target = string.Equals(Path.GetFullPath(said), resolved, StringComparison.OrdinalIgnoreCase)
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
