namespace CoaiMcp.Core.QuestionConsult;

/// <summary>
/// The numbers the question consultant's phase rule rests on (<c>todo/PLAN_question_consultant.md</c> A7, D7,
/// D14 (a)) — ONE place, read by the autonomy order's text, the gate, the settings default and the test that
/// holds the tool description to it.
/// </summary>
public static class QuestionPolicy
{
    /// <summary>
    /// How many batches of questions after the plan's <c>proceed</c> go to the person before <c>ask_consultants</c>
    /// is required. Two, the operator's number: the first questions after a plan is accepted are the ones that
    /// settle what the plan left open, and those are the person's to answer.
    /// </summary>
    public const int FreeBatches = 2;

    /// <summary>
    /// How young a consultation must be to count as proof on <c>ask_human</c> (D14 (a)): a consultId older than
    /// this is refused as if none were given, so an answer from last week cannot open today's door.
    /// </summary>
    public static readonly TimeSpan ConsultProofAge = TimeSpan.FromMinutes(30);
}

/// <summary>The enforcement, as the gate reads it: nothing, a note, or a refusal (D8).</summary>
public enum QuestionMode
{
    /// <summary>The gate says nothing and counts nothing; replies are what they were.</summary>
    Off,

    /// <summary>The gate says what require would have refused, and lets the question through.</summary>
    Remind,

    /// <summary>The gate refuses a question the phase rule sends to the consultants first.</summary>
    Require,
}

/// <summary>The three words of <c>COAI_QCONSULT_MODE</c>, and the mode each is — the settings reader's own rule for an unknown word.</summary>
public static class QuestionModes
{
    public static QuestionMode Of(string word) => (word ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "off" => QuestionMode.Off,
        "remind" => QuestionMode.Remind,
        _ => QuestionMode.Require,
    };

    public static string Spelled(QuestionMode mode) => mode switch
    {
        QuestionMode.Off => "off",
        QuestionMode.Remind => "remind",
        _ => "require",
    };
}
