using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>Who a round is recorded against — the calling AI's own session, else the checkout.</summary>
internal static class RoundCaller
{
    /// <summary>
    /// The calling AI's own session when it exports one — Claude Code does, to every child it spawns, so nothing has to
    /// be passed or remembered by the model. A client that identifies itself in no way at all falls back to the
    /// CHECKOUT, not to our session: our session is repo+branch, and an epic arrives on its own branch, while the
    /// checkout follows a caller across its branches.
    /// </summary>
    internal static string For(PersistedSession session) =>
        CallerIdentity.Current().Id is { Length: > 0 } id ? id : $"repo:{session.State.RepoPath}";
}
