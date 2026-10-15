namespace CoaiMcp.Core.Rounds;

/// <summary>Where a plan round's text came from — the two values <see cref="PlanSource.Origin"/> takes.</summary>
public static class PlanOrigin
{
    public const string Commit = "commit";

    public const string Text = "text";
}

/// <summary>What a plan round reviewed — what the session file records beside the round (E1.S6 writes it).</summary>
/// <param name="Origin"><see cref="PlanOrigin.Commit"/> or <see cref="PlanOrigin.Text"/>.</param>
/// <param name="Commit">The 40-hex commit read (filled by the engine for a text round).</param>
/// <param name="Path">The committed path; empty for a text round.</param>
/// <param name="Blob">The blob's object id; empty for a text round.</param>
/// <param name="Bytes">The blob's size (committed) or the UTF-8 byte count of the text (text) — what git holds, never the re-encoded count.</param>
/// <param name="TextId"><see cref="DocumentRules.ArtifactIdOf"/> of the text SENT to the reviewers, the one the end line carries.</param>
public sealed record PlanSource(string Origin, string Commit, string Path, string Blob, long Bytes, string TextId);
