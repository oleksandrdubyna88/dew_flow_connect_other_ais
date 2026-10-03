namespace CoaiMcp.Runners.Consultation;

/// <summary>The words a failed consultation turn is classified by — on the record, in the database, in the health file.</summary>
/// <remarks>
/// <c>shared/consult-failure-kinds.json</c> holds the same words with the label a person reads, and the
/// extension draws from that file; a test reads THIS class and every <see cref="ConsultFailure"/> case by
/// reflection and compares both with it, so a kind added on one side fails the build on the other.
/// </remarks>
public static class ConsultFailureKinds
{
    public const string CommandDenied = "command-denied";
    public const string ReadDenied = "read-denied";
    public const string Empty = "empty";
    public const string Quota = "quota";
    public const string RateLimited = "rate-limited";
    public const string VendorRefused = "vendor-refused";
    public const string CliNotFound = "cli-not-found";
    public const string Timeout = "timeout";
    public const string Deadline = "deadline";
    public const string Exit = "exit";
    public const string TreeChanged = "tree-changed";
    public const string ConversationDropped = "conversation-dropped";
    public const string RecordFailed = "record-failed";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// Why a consultation turn produced no advice — one closed case per thing a person would DO differently.
/// </summary>
/// <remarks>
/// <para>Epic 2 of PLAN_the_consultant_works_on_every_vendor.md. Before it a failed turn was one of a
/// handful of sentences built inline in the service and persisted nowhere the panel could read, and an
/// empty answer — which on 2026-10-02 was always an auto-denied shell command — told the caller to "try
/// once more with a sharper problem statement". Every case here carries its own <see cref="What"/> (the
/// fact, naming the vendor) and <see cref="Cure"/> (what to do), so the sentence and the remedy cannot be
/// written apart in two places again.</para>
/// <para>A closed hierarchy (private constructor, nested sealed records), so a switch over it is
/// exhaustive and a fifteenth case cannot arrive without every reader being shown it.</para>
/// <para><see cref="Evidence"/> is where the vendor's own transcript was kept, when it was — filled in by
/// the caller that kept it, since keeping is IO and this is a value.</para>
/// </remarks>
public abstract record ConsultFailure
{
    private ConsultFailure() { }

    /// <summary>The word, from <see cref="ConsultFailureKinds"/>.</summary>
    public abstract string Kind { get; }

    /// <summary>What happened, as a clause about the consultant named <paramref name="vendor"/>.</summary>
    public abstract string What(string vendor);

    /// <summary>What a person can do about it.</summary>
    public abstract string Cure { get; }

    /// <summary>Where the vendor's transcript was kept, or empty when nothing was.</summary>
    public string Evidence { get; init; } = string.Empty;

    /// <summary>
    /// Whether the caller's consult call is handed back for this failure.
    /// </summary>
    /// <remarks>
    /// Every failure that was not the caller's doing — epic 2's plan round widened this from the denied and
    /// empty answers to every kind except two: <c>cancelled</c>, which IS the caller's own choice, and
    /// <c>tree-changed</c>, where the consultant ran and broke the one promise it runs under — handing that
    /// call back would make the cap free for a consultant that writes. The give-back itself is bounded
    /// (<c>ConsultCallCounter.GivesBackPerWindow</c> — named, not cref'd: the counter is the server's, an assembly this one
    /// cannot reference), so a vendor that fails every time cannot become an
    /// unlimited loop.
    /// </remarks>
    public virtual bool GivesTheCallBack => true;

    /// <summary>
    /// A failure that may clear on its own — the vendor's time ran out, it throttled, the turn's deadline
    /// passed, the answer could not be written. Only these are worth RESUMING, and only when the turn's own
    /// launches named the conversation.
    /// </summary>
    /// <remarks>
    /// Epic 2's review (the gate's finding and a reviewer): a CLI that is not installed, a spent quota, a
    /// refusal, an exit on an error of its own and a dropped conversation will all fail the same way on the
    /// next call, and telling the caller "the consultant accepted the turn — call consult again" sent it
    /// straight back into them. <c>record-failed</c> is ours rather than the vendor's, and it is here
    /// because the vendor DID answer: the answer is what was lost, and the conversation still holds it.
    /// </remarks>
    public virtual bool Transient => false;

    /// <summary>
    /// Whether the caller is told not to ask this consultation again: every failure that is neither
    /// resumable nor the caller's own doing.
    /// </summary>
    /// <remarks>
    /// The other side of <see cref="Transient"/>, by the review's decision: a failure no retry cures says so,
    /// and a resumable one must not. <c>tree-changed</c> and <c>cancelled</c> are neither — one is a breach
    /// the person must look at first, the other the caller's own choice.
    /// </remarks>
    public bool DoNotRetry => !Transient && this is not (TreeChanged or Cancelled);

    /// <summary>
    /// Whether this failure's cure is to ASK DIFFERENTLY — a new consult whose problem names the files that hold the
    /// answer — so the caller is told that, never "do not retry" beside a cure that invites exactly that.
    /// </summary>
    /// <remarks>
    /// The whole-branch review, M: a denied shell command and an empty answer carried "name in the problem the files
    /// that hold the answer" as their cure and "do not retry this consultation" one sentence later. The consultation
    /// itself is still not worth asking again (<see cref="DoNotRetry"/> holds); a NEW one, framed for reading, is.
    /// </remarks>
    public virtual bool InvitesAReframedAsk => false;

    /// <summary>A command the consultant reached for was auto-denied, and the continued conversation did not answer either.</summary>
    public sealed record CommandDenied : ConsultFailure
    {
        public override string Kind => ConsultFailureKinds.CommandDenied;

        public override string What(string vendor) =>
            $"the consultant ({vendor}) answered nothing: it reached for a shell command, which headless mode "
            + "denies, and it did not answer even when told the command would not come";

        public override string Cure =>
            "name in the problem the files that hold the answer, so it can read rather than run; or pick a consultant "
            + "on another vendor in ConnectOtherAIs > Consultant";

        public override bool InvitesAReframedAsk => true;
    }

    /// <summary>A read the consultant reached for — outside the checkout, or a URL — was auto-denied.</summary>
    /// <param name="Action">The permission word the vendor reported (<c>read_file</c>, <c>read_url</c>).</param>
    public sealed record ReadDenied(string Action) : ConsultFailure
    {
        public override string Kind => ConsultFailureKinds.ReadDenied;

        public override string What(string vendor) =>
            $"the consultant ({vendor}) answered nothing: it reached for a read this consultation does not allow "
            + $"(the '{Action}' permission was denied)";

        public override string Cure =>
            "a consultant may read only inside this checkout — put what it needs in the repository, or quote it in the problem";
    }

    /// <summary>The process exited cleanly and said nothing, for no reason its own streams give.</summary>
    /// <param name="Said">The CLI's own stderr line, when it printed one.</param>
    public sealed record Empty(string Said) : ConsultFailure
    {
        public override string Kind => ConsultFailureKinds.Empty;

        public override string What(string vendor) =>
            Said.Length > 0
                ? $"the consultant ({vendor}) exited cleanly but answered nothing — it said: {Said}"
                : $"the consultant ({vendor}) exited cleanly but answered nothing";

        public override string Cure =>
            "read its kept transcript for why; if it happens again, pick another consultant in ConnectOtherAIs > Consultant";

        public override bool InvitesAReframedAsk => true;
    }

    /// <summary>The vendor's allowance is SPENT — waiting a minute will not clear it.</summary>
    public sealed record Quota(string Said) : ConsultFailure
    {
        public override string Kind => ConsultFailureKinds.Quota;

        public override string What(string vendor) => $"the consultant ({vendor}) refused the call: its quota is spent ({Said})";

        public override string Cure =>
            "wait for the vendor's quota window to reset, or pick a consultant on another vendor in ConnectOtherAIs > Consultant";
    }

    /// <summary>The vendor is throttling right now — a refusal that clears while you stand there.</summary>
    public sealed record RateLimited(string Said) : ConsultFailure
    {
        public override string Kind => ConsultFailureKinds.RateLimited;

        public override string What(string vendor) => $"the consultant ({vendor}) is rate limited right now ({Said})";

        public override string Cure => "wait a few minutes and consult again";

        public override bool Transient => true;
    }

    /// <summary>The vendor refused the call for a reason a person can fix — a recognised sentence with its own cure.</summary>
    public sealed record VendorRefused(string Said, string Remedy) : ConsultFailure
    {
        public override string Kind => ConsultFailureKinds.VendorRefused;

        public override string What(string vendor) => $"the consultant ({vendor}) refused to run: {Said}";

        public override string Cure => Remedy;
    }

    /// <summary>The consultant's CLI could not be started at all.</summary>
    public sealed record CliNotFound(string Said, string Remedy) : ConsultFailure
    {
        public override string Kind => ConsultFailureKinds.CliNotFound;

        public override string What(string vendor) => $"the consultant ({vendor}) could not be started: {Said}";

        public override string Cure => Remedy;
    }

    /// <summary>The launch ran past its own timeout and was killed.</summary>
    public sealed record Timeout : ConsultFailure
    {
        public override string Kind => ConsultFailureKinds.Timeout;

        public override string What(string vendor) => $"the consultant ({vendor}) did not answer within its time and was stopped";

        public override string Cure =>
            "ask a narrower question, or raise *Reviewer timeout, minutes* in ConnectOtherAIs > Limits "
            + "(COAI_REVIEWER_TIMEOUT_MINUTES), which a consultation turn shares";

        public override bool Transient => true;
    }

    /// <summary>The TURN ran past its own deadline — the backstop beyond the launch's timeout.</summary>
    public sealed record Deadline(TimeSpan Budget) : ConsultFailure
    {
        public override string Kind => ConsultFailureKinds.Deadline;

        public override string What(string vendor) =>
            $"the turn with the consultant ({vendor}) ran past its {Budget.TotalMinutes:0.#}-minute deadline without an answer being recorded";

        public override string Cure =>
            "consult again; if it repeats, check `providers` — something around the launch is not finishing";

        public override bool Transient => true;
    }

    /// <summary>The CLI exited non-zero with nothing this product recognises — or ended in a way that is no exit at all.</summary>
    public sealed record Exit(int Code, string Said) : ConsultFailure
    {
        public override string Kind => ConsultFailureKinds.Exit;

        public override string What(string vendor) =>
            Said.Length > 0
                ? $"the consultant ({vendor}) did not answer: exit {Code}: {Said}"
                : $"the consultant ({vendor}) did not answer: exit {Code}";

        public override string Cure => "check `providers`, and run the CLI once by hand to see what it says";
    }

    /// <summary>The working tree changed while the consultant ran — the read-only promise was broken.</summary>
    /// <param name="Sentence">The filesystem invariant's own sentence, naming what changed.</param>
    public sealed record TreeChanged(string Sentence) : ConsultFailure
    {
        public override string Kind => ConsultFailureKinds.TreeChanged;

        public override string What(string vendor) => Sentence;

        public override string Cure =>
            "look at the change named above before anything else — this product never reverts it, and the file may be yours";

        public override bool GivesTheCallBack => false;
    }

    /// <summary>The vendor no longer holds the conversation a follow-up named.</summary>
    public sealed record ConversationDropped : ConsultFailure
    {
        public override string Kind => ConsultFailureKinds.ConversationDropped;

        public override string What(string vendor) => $"the consultant ({vendor}) no longer holds this conversation — its own store dropped the thread";

        public override string Cure => "start a new consultation";
    }

    /// <summary>The answer — or the failure — could not be written to the record.</summary>
    public sealed record RecordFailed(string Why) : ConsultFailure
    {
        public override string Kind => ConsultFailureKinds.RecordFailed;

        public override string What(string vendor) => $"the turn with the consultant ({vendor}) ran, but the answer could not be recorded: {Why}";

        public override string Cure => "check that the data directory is writable, then consult again";

        public override bool Transient => true;
    }

    /// <summary>The caller withdrew the call.</summary>
    public sealed record Cancelled : ConsultFailure
    {
        public override string Kind => ConsultFailureKinds.Cancelled;

        public override string What(string vendor) => $"the call was cancelled while the consultant ({vendor}) was running";

        public override string Cure => "nothing to cure — the call was withdrawn";

        public override bool GivesTheCallBack => false;
    }
}
