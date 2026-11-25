namespace CoaiMcp.Core.Rounds;

/// <summary>Where a plan round's text came from — the three values <see cref="PlanSource.Origin"/> takes.</summary>
public static class PlanOrigin
{
    public const string Commit = "commit";

    public const string Text = "text";

    /// <summary>A pack the server made of a scope of a committed work-plan folder (epoch 3 E3 s3). A release before it reads this value as "no source".</summary>
    public const string Folder = "folder";
}

/// <summary>What a plan round reviewed — what the session file records beside the round (E1.S6 writes it).</summary>
/// <param name="Origin"><see cref="PlanOrigin.Commit"/>, <see cref="PlanOrigin.Text"/> or <see cref="PlanOrigin.Folder"/>.</param>
/// <param name="Commit">The 40-hex commit read (filled by the engine for a text round).</param>
/// <param name="Path">The committed path; empty for a text round; the repository-relative folder of the SCOPE for a folder round.</param>
/// <param name="Blob">The blob's object id; empty for a text round; the APPROVAL ID for a folder round — the SHA-256 over every packed file's blob id and path and every outside dependency's node and title.</param>
/// <param name="Bytes">The blob's size (committed), the UTF-8 byte count of the text (text) or of the pack (folder) — what git holds, never the re-encoded count.</param>
/// <param name="TextId"><see cref="DocumentRules.ArtifactIdOf"/> of the text SENT to the reviewers, the one the end line carries.</param>
public sealed record PlanSource(string Origin, string Commit, string Path, string Blob, long Bytes, string TextId)
{
    /// <summary>
    /// Whether this is a source <c>PlanSources.Stamp</c> could have written — read back from a column or a file, where any member
    /// may be missing, empty or hostile. The rule the extension's <c>reviewedFrom</c> applies too, so <c>--log</c> and the page agree.
    /// </summary>
    /// <remarks>
    /// Every source: a size that is not negative, a text id, and a commit that is a hex id or empty (the engine fills it for both
    /// origins; an unresolved one is empty). A committed source: a repository-relative path (<see cref="RepoPaths.IsRelative"/>)
    /// and a blob that is a hex id. A folder source: the same shape — a repository-relative scope folder and an approval id that is hex of 7 to 64 characters. A pasted source: no path and no blob — that is its valid shape. Any other origin is not one.
    /// </remarks>
    public bool IsWellFormed() => HasSharedShape() && HasOriginShape();

    private bool HasSharedShape() => Bytes >= 0 && TextId is { Length: > 0 } && (Commit is "" || IsHash(Commit));

    private bool HasOriginShape() => Origin switch
    {
        PlanOrigin.Commit or PlanOrigin.Folder => Path is { Length: > 0 } && RepoPaths.IsRelative(Path) && IsHash(Blob),
        PlanOrigin.Text => string.IsNullOrEmpty(Path) && string.IsNullOrEmpty(Blob),
        _ => false,
    };

    private static bool IsHash(string? value) => value is { Length: >= 7 and <= 64 } && value.All(char.IsAsciiHexDigit);
}
