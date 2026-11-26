using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// The rollout files of a job's <c>CODEX_HOME</c> (epoch 3 E3 s5 t4, id CX-T5): the check after a turn wants the thread of the turn's handle and no other. A job's home lives as long as the job, and a job that failed over from one codex model to another has the rollout of the first model's lost
/// thread in it — a file of another thread, which the check must not read as a child of the session. So a FRESH launch (turn 0 of an attempt: no <c>resume</c>) starts with no <c>sessions</c> folder; a resumed one keeps it, because the thread it continues is there.
/// </summary>
internal static class CodexSessions
{
    /// <summary>Whether the argv is a resumed turn (<c>exec resume …</c>).</summary>
    internal static bool IsResumed(IReadOnlyList<string> arguments) => arguments.Count >= 2 && arguments[0] == "exec" && arguments[1] == "resume";

    /// <summary>
    /// Removes the <c>sessions</c> folder of <paramref name="codexHome"/> when it is a real folder, and answers the sentence that says why the launch must not start when it is something else (a link, a file) or cannot be removed — empty otherwise. A folder that is not there is nothing to remove.
    /// </summary>
    internal static string Clear(string codexHome)
    {
        var sessions = Path.Combine(codexHome, "sessions");
        switch (RealPath.Lstat(sessions).Kind)
        {
            case EntryKind.Missing:
                return string.Empty;
            case EntryKind.Directory:
                try
                {
                    GitScratch.DeleteEvenIfReadOnly(sessions);

                    return string.Empty;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    return $"the sessions folder of the job's CODEX_HOME could not be cleared ({e.GetType().Name}), so the codex coder was not launched";
                }

            default:
                return "the sessions entry of the job's CODEX_HOME is not a real folder, so the codex coder was not launched";
        }
    }
}
