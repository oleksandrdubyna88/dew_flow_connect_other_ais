namespace CoaiMcp.Core.Rounds;

/// <summary>
/// What a person chose when the gate ran out of rounds and asked them.
/// </summary>
/// <remarks>
/// <para>Four answers, because those are the four things that can actually happen next. Three of them keep the
/// findings alive: another set of rounds, act on what was found, stop and talk. The fourth,
/// <see cref="Proceed"/>, ships with the findings open — and it is the one decision that is deliberately NOT
/// offered on every card: only on the gate's own NOTICE, the card that lists the findings being overridden, and
/// only when that card says it offers it.</para>
/// <para>It was asked with a free-text input box, which is the control for a question an AI wrote
/// in words, and this is not that. <see cref="None"/> is what prose means: still their answer,
/// carried to the AI, but never a decision on its own.</para>
/// <para>It lives in the CORE because the round machine now acts on it: after <c>call_human</c> no
/// further round opens until one of these arrives, so the decision is part of the state machine
/// rather than a label the server puts on an answer file.</para>
/// <para><b>Why <see cref="Proceed"/> exists, and where it comes from.</b> The product promised the override on
/// three surfaces and shipped it through the calling AI's own tool argument — which made the gated party the
/// only party that could release the gate. The override is kept (a wrong reviewer, or a round nobody answered,
/// would otherwise be unresolvable without another full panel) and given the provenance the other three have:
/// it is read from the answer file of the hold's own notice, never from an argument. The file is written by the
/// extension; nothing here claims that anything running as the same user cannot write it too.</para>
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

    /// <summary>Proceed despite the findings: the stage advances with them open, recorded as proceeded over by a person.</summary>
    /// <remarks>
    /// Applied by <see cref="RoundMachine.Resolve"/> and nowhere else (the stage advances in one place), and honoured
    /// only from the answer to the hold's own notice. <see cref="RoundMachine.ApplyHumanDecision"/> leaves it alone.
    /// </remarks>
    Proceed,
}
