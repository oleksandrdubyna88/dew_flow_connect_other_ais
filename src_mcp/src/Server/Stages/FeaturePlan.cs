using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>The plan a feature review is OF: its identity (the session's fourth key segment) and its text.</summary>
/// <param name="Identity">The plan's repository-relative path, as <see cref="DocumentReader.IdentityOf"/> spells it (D13).</param>
internal sealed record FeaturePlanText(string Identity, string Text);

/// <summary>
/// Reads <c>planPath</c> for <c>review_feature</c>: inside the repository, a file, UTF-8 text — and keyed
/// by its repository-relative PATH, never its file name (D13).
/// </summary>
/// <remarks>
/// <para><b>The identity is the one every other tool resolves.</b> <c>resolve</c>, <c>status</c> and
/// <c>ask_human</c> take the plan back as <c>feature</c> and turn it into a key through the same
/// <see cref="DocumentReader.IdentityOf"/>; a second resolution here would be the relative-path defect
/// the document stage already paid for once (five findings across two vendors, recorded on
/// <c>PanelService.DocumentKeyFor</c>).</para>
/// <para><b>Containment and existence are asked first, in the feature stage's own words.</b> The document
/// reader's refusals for those two tell the caller about <c>documentPath</c> and <c>documentText</c> —
/// arguments this tool does not have. Its other refusals (a directory, not text, too large, not UTF-8)
/// are about the file and read true here, so the content is still read through it.</para>
/// <para><b>A credential-shaped NAME is refused before anything is asked of the disk</b> (D15, as the
/// source resolver and the pack refuse it): the plan is pasted whole into every feature reviewer's
/// context, and <c>planPath: ".env.production"</c> would have handed a third-party model the one file
/// this product promises never to read. The same fixed shapes, <see cref="CredentialFiles"/>, and never
/// the credential words — a plan may be called <c>PLAN_token_rotation.md</c>.</para>
/// </remarks>
internal static class FeaturePlan
{
    public static FeatureInput<FeaturePlanText> Read(string repoPath, string planPath)
    {
        var said = planPath.Trim();

        return said.Length == 0 ? Refused("planPath was not given — pass the plan file, repository-relative (todo/PLAN_x.md)")
            : CredentialFiles.WhichPattern(said) is { Length: > 0 } shape ? Refused(CredentialNamed(said, shape))
            : Located(repoPath, said, Inside(repoPath, said));
    }

    private static string CredentialNamed(string said, string shape) =>
        $"'{said}' looks like a credential file ({shape}) — this product never reads a file of that shape, and a plan is never named like one; "
        + "pass the plan's path inside the repository (todo/PLAN_x.md)";

    /// <summary>The plan where the caller said it is: inside the repository, and a file that exists.</summary>
    private static FeatureInput<FeaturePlanText> Located(string repoPath, string said, string identity) =>
        identity.Length == 0 ? Refused($"'{said}' is not inside '{repoPath}' — a feature review is of a plan in the repository it reviews, and its path is the review's identity")
        : !File.Exists(Path.IsPathRooted(said) ? said : Path.GetFullPath(said, repoPath)) ? Refused($"there is no plan file at '{said}' in '{repoPath}' — planPath is the plan's path inside the repository")
        : Contents(repoPath, said, identity);

    /// <summary>The plan's repository-relative identity, or empty when it resolves outside the repository.</summary>
    private static string Inside(string repoPath, string said) =>
        DocumentId.Of(
            DocumentReader.CanonicalRoot(repoPath, DocumentReader.FollowLink),
            DocumentReader.Canonical(repoPath, said, DocumentReader.FollowLink));

    private static FeatureInput<FeaturePlanText> Contents(string repoPath, string said, string identity) =>
        DocumentReader.Read(repoPath, new DocumentRequest(Path: said), DocumentReader.FollowLink) switch
        {
            DocumentOutcome.Ready { Text: var text } when text.Trim().Length > 0 => new FeatureInput<FeaturePlanText>.Accepted(new FeaturePlanText(identity, text)),
            DocumentOutcome.Ready => Refused($"'{said}' is empty — a feature review is of a plan, and its reviewers read it first"),
            DocumentOutcome.Refused refused => Refused($"planPath: {refused.Sentence}"),
            _ => throw new InvalidOperationException("the union is closed"),
        };

    private static FeatureInput<FeaturePlanText>.Refused Refused(string sentence) => new(sentence);
}
