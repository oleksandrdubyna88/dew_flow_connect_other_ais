namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// The paragraph a REPAIR launch's prompt ends with — one text, appended to whichever prompt the launch
/// it repairs was given.
/// </summary>
/// <remarks>
/// <para>It lived inline in the roster until S3.2, when a second composer needed it: a feature
/// reviewer's follow-up turn builds its own repair from ITS turn's prompt (plan §4.9 — a malformed
/// turn-2 answer repaired against turn 1's prompt would be asked to fix an answer to a question it was
/// never shown), and the reuse rule's second move — extract the shared half — beats a second copy that
/// drifts.</para>
/// <para>The paragraph is written before anybody knows which way the first attempt failed, so it covers
/// both. Its second sentence exists because a refused tool produces NO answer at all, and telling that
/// model its JSON was malformed describes a failure it never had.</para>
/// </remarks>
public static class RepairInstruction
{
    public const string Text =
        "\n\nYOUR PREVIOUS ATTEMPT DID NOT PRODUCE A USABLE ANSWER."
        + " If it returned text that was not the schema's JSON: return ONLY the JSON object — no fences, no prose."
        + " If it returned nothing because a command or a file read was refused: there are no tools here"
        + " and none are needed — answer from the text above.";
}
