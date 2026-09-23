namespace CoaiBugs;

// The vocabulary of Corpus.Keep's answer, in a file of its own: the two words a caller reads back
// from storing a pair are the contract, and Corpus.cs is the storage that produces them. They moved
// here when the store crossed the 800-line ceiling on the change that taught `Words.Stored` to
// recognise a retried comment; a type that callers switch on belongs where it can be read alone.

/// <summary>What storing a pair came to.</summary>
/// <remarks>
/// Two outcomes and not three: a refusal is decided by the alphabet BEFORE anything is stored, so
/// it is never an answer this type can give. <c>AlreadyHeld</c> is a success — the corpus holds the
/// pair, in quarantine or promoted, and the client may stop sending it.
/// </remarks>
public enum Kept
{
    /// <summary>Written to quarantine, waiting for a person.</summary>
    Stored,

    /// <summary>Already here. Nothing was written and nothing is wrong.</summary>
    AlreadyHeld,
}

/// <summary>
/// What became of the words a contributor sent with a pair — which is not the pair's own fate.
/// </summary>
/// <remarks>
/// <para><b>Four outcomes rather than a boolean, because two of them owe a person a different
/// sentence.</b> It was <c>bool CommentLanded</c>, and a code round found both ways that lied: a new
/// pair with no comment answered "landed" because the row was written, and a pair somebody had
/// already promoted was told "this pair already carries a comment" when it carried none. The first
/// was masked by the one caller checking the comment's length itself; the second reached a real
/// person as a false explanation of where their words went. (Code round, codex.)</para>
/// <para>The pair's fate stays <see cref="Kept"/>: a duplicate is still a duplicate whether or not
/// it took the comment, because the CLIENT reads that word to decide whether to keep sending.</para>
/// </remarks>
public enum Words
{
    /// <summary>There were none, and nothing is owed.</summary>
    None,

    /// <summary>
    /// Held with the pair — written with it, attached to one that had none, or the very words it
    /// already carried, offered again by a send whose answer was lost.
    /// </summary>
    Stored,

    /// <summary>The pair already carries somebody else's words, and the first to speak keeps them.</summary>
    AlreadySpokenFor,

    /// <summary>The pair has been promoted out of the queue; there is no waiting row to attach to.</summary>
    TooLate,
}
