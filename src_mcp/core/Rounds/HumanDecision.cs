namespace CoaiMcp.Core.Rounds;

/// <summary>
/// What a person chose when the gate ran out of rounds and asked them.
/// </summary>
/// <remarks>
/// <para>Three answers, because those are the three things that can actually happen next — and
/// notably none of them is "ship it with the findings open". A gate whose human override is
/// "ignore all this" is a gate with an off switch; these three all keep the findings alive.</para>
/// <para>It was asked with a free-text input box, which is the control for a question an AI wrote
/// in words, and this is not that. <see cref="None"/> is what prose means: still their answer,
/// carried to the AI, but never a decision on its own.</para>
/// <para>It lives in the CORE because the round machine now acts on it: after <c>call_human</c> no
/// further round opens until one of these arrives, so the decision is part of the state machine
/// rather than a label the server puts on an answer file.</para>
/// </remarks>
public enum HumanDecision
{
    None,

    /// <summary>Keep going: another set of rounds, nothing changed first.</summary>
    /// <remarks>For when the person thinks the reviewers are wrong, or wants another opinion.</remarks>
    Continue,

    /// <summary>Stop reviewing and act on what was found, then review again.</summary>
    Fix,

    /// <summary>Stop and talk to the person before doing anything else.</summary>
    Discuss,
}
