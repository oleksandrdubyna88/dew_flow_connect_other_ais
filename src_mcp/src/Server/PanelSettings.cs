using CoaiMcp.Core.Context;
using CoaiMcp.Core.Rounds;
using CoaiMcp.ServiceDefaults;

namespace CoaiMcp.Server;

/// <summary>One reviewer's configuration as the server sees it.</summary>
/// <param name="Provider">Its id: what names it in the panel, in the logs, and in the vault entry.</param>
public sealed record ProviderSettings(string Provider)
{
    public bool Enabled { get; init; } = true;

    public string Model { get; init; } = string.Empty;

    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>Which CLI shape drives it — `codex` or `gemini`.</summary>
    public string Runtime { get; init; } = string.Empty;

    /// <summary>An OpenAI-compatible endpoint, for a vendor riding the Codex CLI. Empty = built in.</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// For a <c>remote</c> row: the vendor id the TEAM SERVER knows it by.
    /// </summary>
    /// <remarks>
    /// <b>Not this row's id, and that is the whole point.</b> A row is named
    /// <c>&lt;server&gt;-&lt;vendor&gt;</c> so two Team servers each offering <c>codex</c> do not collide on
    /// one id — the id names the row, its usage history and its vault key. But the SERVER only knows
    /// <c>codex</c>, so sending the row id as <c>--vendor</c> would be refused by every server, and the
    /// health probe would report a vendor the server "does not offer". Empty falls back to the row id,
    /// so a row written by hand still behaves as it reads.
    /// </remarks>
    public string RemoteVendor { get; init; } = string.Empty;

    /// <summary>Whether this vendor reviews plans.</summary>
    public bool Plan { get; init; } = true;

    /// <summary>Whether this vendor reviews code.</summary>
    public bool Code { get; init; } = true;

    /// <summary>
    /// Whether this vendor reviews DOCUMENTS — three states, and the third one is not an absence.
    /// </summary>
    /// <remarks>
    /// <para><b>A named type rather than a <c>bool?</c>, and that is the doctrine rather than taste.</b>
    /// coding-style forbids null in business logic, and this decides ROUTING — as a nullable it was a
    /// routing rule whose most interesting case had no name. <see cref="DocumentReviews.Unspecified"/>
    /// is what a settings file written before documents existed says, and it has to stay tellable
    /// apart from a person who said no: a plain <c>true</c> default would start sending documents to
    /// every vendor somebody had ticked for code alone, and a plain <c>false</c> would switch off a
    /// local document round that works today. Absence arrives as a nullable at the DTO boundary,
    /// where a missing JSON field legitimately is one, and is given its name there. (codex, plan 5's
    /// code round.)</para>
    /// <para>The extension writes an explicit value the moment anybody changes the PLAN box — the
    /// only other switch this state is read from — so <c>Unspecified</c> is a migration reading rather
    /// than a state a person can sit in unawares while the thing it defers to moves under them.</para>
    /// </remarks>
    public DocumentReviews Documents { get; init; } = DocumentReviews.Unspecified;

    /// <summary>Whether this vendor's reviews run somewhere other than this machine.</summary>
    /// <remarks>
    /// The same question <c>PanelService.Remote</c> asked privately, asked of the row instead: the
    /// stage rule below needs it, and a second spelling of "is this a Team server" is how two answers
    /// to one question start.
    /// </remarks>
    public bool IsRemote =>
        string.Equals(Runtime, "remote", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this vendor serves the stage being run.
    /// </summary>
    /// <remarks>
    /// <para>Measured over fourteen judged runs (`research/RESULTS_vendor_overlap_2026-09-06.md`): a
    /// local model was 19 % useful on a plan and 3 % on code, while writing more findings than the
    /// two hosted vendors together. So the useful setting was never "local on or off" — and until
    /// these flags existed it could not be expressed at all. `Enabled` remains the master switch.</para>
    /// <para><b>It took a bool until there were three stages.</b> "Plan switch or code switch" is a
    /// question with two answers, and a document round's answer is neither — it rode the plan switch
    /// because that was the only other thing to ride. The caller knows which stage it is running and
    /// passes it.</para>
    /// <para><b>And an ABSENT document switch answers differently for a Team server.</b> For a vendor
    /// this machine runs itself, absent is the plan tick — the reading plan 4 gave it, nothing leaves
    /// the laptop, and there is no consent to ask for. For a Team server, absent is NO: the tick
    /// would otherwise be granting permission for a company document to cross the network,
    /// retroactively, on every configuration written before documents existed. A tick that means
    /// "this vendor is good at prose" cannot also mean "this file may go to the shared box".
    /// (gemini, Blocking, plan 5's plan round — against the first draft of this very method.)</para>
    /// </remarks>
    public bool Serves(Stage stage) => Enabled && Reviews(stage);

    /// <summary>Which switch this stage reads, with <see cref="Enabled"/> already answered.</summary>
    /// <remarks>
    /// Split out of <see cref="Serves"/> for the complexity ceiling: the master switch and the
    /// three-way choice are two decisions, and together they are one method over the limit. They also
    /// read differently — <c>Enabled &amp;&amp; Reviews(stage)</c> says in one line that the box at the
    /// top of the card outranks the three below it. (CodeRabbit, on the pull request.)
    /// </remarks>
    private bool Reviews(Stage stage) => stage switch
    {
        Stage.PlanReview => Plan,
        Stage.CodeReview => Code,
        Stage.DocumentReview => ReviewsDocuments,
        // Exhaustive on purpose, and throwing rather than guessing: a stage added later that lands
        // here would otherwise take the code switch in silence. The same shape `PanelConfig.BucketFor`
        // already has, for the same reason.
        _ => throw new ArgumentOutOfRangeException(
            nameof(stage), stage, "no vendor switch decides this stage — add one rather than defaulting"),
    };

    /// <summary>The document switch, with <see cref="DocumentReviews.Unspecified"/> read.</summary>
    /// <remarks>
    /// Its own member so that <see cref="Serves"/> stays one decision per line and inside the
    /// complexity ceiling, and so the migration reading can be read on its own.
    /// </remarks>
    private bool ReviewsDocuments => Documents switch
    {
        DocumentReviews.Yes => true,
        DocumentReviews.No => false,
        _ => !IsRemote && Plan,
    };
}

/// <summary>
/// What a vendor was told about reviewing documents — including that it was told nothing.
/// </summary>
/// <remarks>
/// <see cref="Unspecified"/> is the value this enum exists for. It is what every settings file
/// written before documents existed says, and reading it is the consent rule: for a vendor this
/// machine launches it means the plan tick, because nothing leaves the laptop and there is no
/// permission to ask for; for a Team server it means NO, because the alternative is a tick that meant
/// "this vendor is good at prose" granting a company document passage across the network,
/// retroactively, on every configuration that already exists.
/// </remarks>
public enum DocumentReviews
{
    /// <summary>Nobody has said. See the remarks — this is not "no".</summary>
    Unspecified,

    /// <summary>Somebody ticked the box.</summary>
    Yes,

    /// <summary>Somebody unticked it.</summary>
    No,
}

/// <summary>
/// Everything the extension's settings UI will eventually own. Until that loopback exists
/// (epic 05), the environment is the configuration surface — variables, not call sites, so
/// changing behaviour is a config edit and a client restart.
/// </summary>
public sealed record PanelSettings
{
    /// <remarks>
    /// Antigravity rather than Gemini since 2026-09-01: Google retired Code Assist for individual
    /// accounts, and the Gemini CLI now refuses before it reaches a model. The adapter for its
    /// replacement had shipped a day earlier and nothing used it — supporting a vendor and
    /// DEFAULTING to it are different changes, and only the first one had been made.
    /// </remarks>
    public IReadOnlyList<ProviderSettings> Providers { get; init; } =
        [new("codex"), new("antigravity"), new("deepseek") { Enabled = false }];

    public PanelConfig Rounds { get; init; } = new();

    public int GlobalConcurrency { get; init; } = 3;

    public int PerProviderConcurrency { get; init; } = 2;

    /// <summary>
    /// How many reviewers may use ONE local engine at a time. One, and it is not the same knob as
    /// <see cref="PerProviderConcurrency"/>.
    /// </summary>
    /// <remarks>
    /// <para>Measured 2026-09-03 on the machine that reported it: `COAI_MAX_PER_PROVIDER=3` is a
    /// reasonable setting for a hosted vendor — three HTTP calls into somebody else's fleet — and it
    /// put three reviewers on one card. The one that got there first answered in 30.6 s; the other
    /// two were cancelled at 590 s having produced nothing, because all three were sharing the same
    /// GPU and each got a third of it.</para>
    /// <para><see cref="IntVar"/> refuses a value below one, so a configured zero falls back here
    /// rather than making every local reviewer wait for ever.</para>
    /// </remarks>
    public int LocalConcurrency { get; init; } = 1;

    public TimeSpan ReviewerTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a whole ROUND may take, or zero to derive it from the round's own shape.
    /// </summary>
    /// <remarks>
    /// <para>Zero rather than a number, because the honest default is not a constant: a round runs
    /// `vendors × roles` reviewers through the machine's cap, so it takes as many WAVES as that
    /// division needs and each wave can legitimately last a whole reviewer timeout. Twelve reviewers
    /// at a cap of three is four waves — forty minutes of entirely healthy work at the shipped
    /// settings. See <see cref="RoundBudget"/>.</para>
    /// <para>A number here overrides that, and the panel says what lowering it cuts into. Nothing
    /// stops somebody setting five minutes; what would be wrong is SHIPPING five minutes.</para>
    /// </remarks>
    public TimeSpan RoundTimeout { get; init; }

    /// <summary>How long a rate-limited reviewer waits before its one retry.</summary>
    /// <remarks>
    /// Kept as the panel's own setting and as the older way of saying "one retry, at this
    /// interval": setting it, and nothing else, still means exactly that — see
    /// <see cref="RetryLadder"/> for the precedence.
    /// </remarks>
    public TimeSpan RateLimitBackoff { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The waits a rate-limited reviewer climbs, in order.
    /// </summary>
    /// <remarks>
    /// <para>Four steps by default — 5 s, 30 s, 60 s, 120 s — because one interval is the wrong
    /// number for both cases it has to serve: a transient <c>429</c> clears in seconds, and a usage
    /// window does not clear at all within a round.</para>
    /// <para><b>An operator who set the old variable keeps the old behaviour.</b> With
    /// <c>COAI_RETRY_BACKOFF</c> unset and <c>COAI_RATE_LIMIT_BACKOFF_SECONDS</c> set, this is a
    /// ONE-step ladder at that number: a deployment that deliberately chose 45 seconds must not
    /// silently become four retries, and nothing would have said so. Raised twice, by two vendors,
    /// on this change's plan round.</para>
    /// </remarks>
    public IReadOnlyList<TimeSpan> RetryLadder { get; init; } = Runners.Reviewers.RetryLadder.Default;

    /// <summary>
    /// How long an escalation waits for a person before answering "nobody answered yet".
    /// </summary>
    /// <remarks>
    /// Thirty minutes, and the fallback is the family's: the main AI then asks in the chat, the
    /// same shape `remote-ask.md` prescribes on `no_answer_yet`. Waiting forever would hand the
    /// decision to whichever MCP client's own timeout fires first, with nothing said about why.
    /// </remarks>
    public TimeSpan EscalationBudget { get; init; } = TimeSpan.FromMinutes(30);



    /// <summary>
    /// Which prompt each role uses on each round — <c>role -> [round1, round2, ...]</c>, by
    /// catalog id. An empty or unknown entry falls back to the rotation or the universal prompt.
    /// </summary>
    /// <summary>
    /// Deal the PLAN stage's lenses across the vendors instead of giving every vendor the same one.
    /// </summary>
    /// <remarks>
    /// <para>Opt-in, and off by default, because of what it trades. With it off — the shipped
    /// behaviour — every vendor answers the same question and <c>FindingDedup</c> merges what they
    /// agree on, which is the strongest signal this product produces. With it on every lens gets
    /// asked once instead, at half the launches, and that agreement is gone.</para>
    /// <para>Two flags rather than one because the stages are not alike: a plan has three lenses for
    /// one role, a code round has three roles.</para>
    /// </remarks>
    public bool DealPlanLenses { get; init; }

    public bool DealCodeLenses { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> PromptsPerRound { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>
    /// Spend the rounds on DIFFERENT lenses instead of asking the same broad question again.
    /// </summary>
    /// <remarks>
    /// Off by default: rotation changes what a second round means, and a person who has not asked
    /// for it should get the prompt they last read in the panel.
    /// </remarks>

    /// <summary>Where sessions, prompts overrides and round artifacts live.</summary>
    public string DataDir { get; init; } = DefaultDataDir;

    /// <summary>
    /// Where a code round's worktree is made. Empty means <c>{DataDir}/worktrees</c>, which is what a
    /// settings object built in a test gets.
    /// </summary>
    /// <remarks>
    /// A real server reads it from the environment as <see cref="Runners.Worktrees.WorktreeManager.MachineLocalRoot"/>
    /// (D4 of research/PLAN_a_failed_round_can_be_retried.md): the data dir is routinely a network share,
    /// where a linked worktree is broken for every other machine and a pid — which the tree's owner
    /// marker records — means nothing. <c>COAI_ROUND_WORKTREES</c> overrides it, which is how a
    /// scenario test keeps a spawned server out of the machine's own directory.
    /// <para><b>The override must be a LOCAL path.</b> The owner check trusts that every process
    /// touching the root is on this machine; a network share would let a server on another machine
    /// read a pid that means nothing here (code round). The default already is local.</para>
    /// </remarks>
    public string RoundTreeRoot { get; init; } = string.Empty;

    /// <summary>
    /// Where the CALLER's own transcripts live, when they are not Claude Code's.
    /// </summary>
    /// <remarks>
    /// Empty means the default, which is <c>~/.claude/projects</c> because that is what drives this
    /// gate today. The gate itself said the store should not be wired to one vendor's CLI, and it is
    /// right — this is the seam, and it costs one environment variable rather than a protocol.
    /// </remarks>
    public string AgentLogDir { get; init; } = string.Empty;

    /// <summary>
    /// What a LOCAL reviewer is told about thinking: <c>none</c> by default, a level to ask for it,
    /// or <c>engine</c> to say nothing and take the engine's own default.
    /// </summary>
    /// <remarks>
    /// <para><b>Measured 2026-09-02.</b> Gemma4 26B on Ollama answered the planted-defect plan once
    /// in 171 s and, on the identical request, once filled a 64k context with 110 000 characters of
    /// <c>reasoning</c> and returned an empty <c>content</c> after 1056 s. Unbounded thinking that
    /// outruns the context is a review that never arrives, and one in two is not a reviewer.</para>
    /// <para>The escape was found in dew_flow_rag_qln first (<c>AiRuntimeOptions.ReasoningEffort</c>,
    /// 2026-08-11): on Ollama's OpenAI route <c>think:false</c> is ignored and <c>"low"</c> still burns
    /// the budget; only <c>"none"</c> returns <c>finish_reason: stop</c>. Re-verified here.</para>
    /// </remarks>
    public string LocalReasoningEffort { get; init; } = "none";

    /// <summary>
    /// The most tokens a local answer may be. Eight thousand — generous for findings, far below a
    /// model that does not stop.
    /// </summary>
    /// <remarks>
    /// A review answer measured here is one to two thousand tokens. The ceiling exists for the
    /// failure that has no other bound: an uncapped reasoning model generating until the deadline,
    /// which is what made every local reviewer of a round report a timeout while the engine was
    /// healthy and fast for a capped request.
    /// </remarks>
    public int LocalMaxTokens { get; init; } = 8192;

    /// <summary>
    /// Work without interrupting the person until there is no other way.
    /// </summary>
    /// <remarks>
    /// Off by default, like the two below: a gate that starts issuing orders nobody asked for is one
    /// people turn off entirely. What it produces is a COMMAND in the round's reply, not a change to
    /// what the gate decides.
    /// </remarks>
    public bool Autonomous { get; init; }

    /// <summary>Break an accepted plan into epics and stories, and close each one properly.</summary>
    public bool SplitPlan { get; init; }

    /// <summary>
    /// Do the splitting — and the expensive-to-get-wrong stories — with the caller's strongest model.
    /// </summary>
    /// <remarks>
    /// The name is historical: the switch named Fable to every caller until issue #117 made the models
    /// a per-caller choice (<see cref="CommandModels"/>). Renaming the stored key would be a migration of
    /// every settings file for a word nobody sees.
    /// </remarks>
    public bool SplitWithFable { get; init; }

    /// <summary>
    /// The model pairs a person configured, per caller kind — only those; the rest resolve through
    /// <see cref="Core.Commands.CommandModels.For"/>. From <c>COAI_COMMAND_MODELS</c>; issue #117.
    /// </summary>
    public IReadOnlyDictionary<string, Core.Commands.ModelPair> CommandModels { get; init; } =
        new Dictionary<string, Core.Commands.ModelPair>();

    /// <summary>
    /// How often split work is gated — once per epic unless <c>COAI_GATE_PER</c> says <c>task</c>
    /// (issue #131). Never per story.
    /// </summary>
    public Core.Commands.GateScope GatePer { get; init; } = Core.Commands.GateScope.Epic;

    /// <summary>
    /// What a CODE reviewer is launched in: <c>none</c> (the default) or <c>worktree</c>.
    /// </summary>
    /// <remarks>
    /// <para>A hosted CLI is agentic: handed a checkout it explores it, and the measurements put
    /// the cost at roughly 200 000 input tokens for one code round against about 25 000 for a local
    /// reviewer, which receives one composed prompt and has nowhere to go. That is not a fair
    /// comparison of models, it is a comparison of two different questions.</para>
    /// <para><c>none</c> launches the reviewers in an empty directory. The PROMPT does not change —
    /// the diff is assembled from the repository and the written rules are still read from the
    /// worktree, both by this server — so the only thing removed is the ability to go looking for
    /// more. The repair launch has always worked this way, and the plan stage too.</para>
    /// <para><b>It is the default because it was measured.</b> On one commit across three hosted
    /// models, taking the checkout away made every one of them find MORE useful defects — 4→8,
    /// 6→10, 6→7 — at a half to a third of the input tokens, with no wrong finding from any of
    /// them, and three real defects surfaced that NO run with a checkout had reached
    /// (<c>RESULTS_findings_that_are_worth_something.md</c>). <c>worktree</c> remains for a review
    /// that genuinely needs the surrounding code.</para>
    /// </remarks>
    public string CodeWorkspace { get; init; } = "none";

    /// <summary>
    /// Settings whose VALUE this build does not understand, each as a sentence for a person.
    /// </summary>
    /// <remarks>
    /// <para>The panel and the server ship separately and update separately, so a panel newer than
    /// the server writes values the server has never heard of. Falling back is right — refusing to
    /// start over a future policy would be worse — but falling back in SILENCE is what made a
    /// working configuration look broken: the setting was applied, the value was read, and the
    /// behaviour was the old one with nothing anywhere saying why.</para>
    /// <para>Empty is the normal state. A value nobody set is not a mismatch.</para>
    /// </remarks>
    public IReadOnlyList<string> Unrecognised => [.. UnrecognisedSettings.Select(one => one.Sentence)];

    /// <summary>
    /// The same list, each sentence with the KEY it is about.
    /// </summary>
    /// <remarks>
    /// <para>The key is what the notice written for it groups on — the extension keys repeats on
    /// <c>(code, subject)</c>, so two malformed settings must be two rows and one misconfiguration
    /// across ten restarts must be one row with a count. Without a key that means an empty subject
    /// and every unrecognised setting there has ever been collapsing into one row. (Story 2.3.3's
    /// plan round, gemini.)</para>
    /// <para><b>Typed at the source rather than parsed back out of the prose.</b> Every one of the
    /// three places these sentences come from already knows its key where it writes one —
    /// <see cref="WhyBackoff"/> hard-codes <c>COAI_RETRY_BACKOFF</c> and its three siblings do the
    /// same; <c>catalog.Dropped</c> is rows of <c>COAI_ROLES</c>; <c>consultants.Complaints</c> is
    /// <c>COAI_CONSULTANTS</c>. A key recovered FROM a sentence is a key that stops matching the
    /// first time somebody rewords the sentence.</para>
    /// <para><see cref="Unrecognised"/> stays as the projection because it is ON THE WIRE:
    /// <c>ProvidersAnswer.Unrecognised</c> is what <c>--providers</c> prints and the extension
    /// parses, so changing its shape would be changing a contract for an internal convenience.</para>
    /// </remarks>
    public IReadOnlyList<UnrecognisedSetting> UnrecognisedSettings { get; init; } = [];

    /// <summary>
    /// Which vendor and model consult for each CALLER kind (<c>claude</c>, <c>codex</c>, <c>gemini</c>,
    /// <c>other</c>) — see <see cref="ConsultantRouting"/> for the shipped map and its rule.
    /// </summary>
    public IReadOnlyDictionary<string, ConsultantChoice> Consultants { get; init; } = ConsultantRouting.Shipped;

    /// <summary>Turns one consultation may take. Frozen on the record at creation.</summary>
    public int ConsultTurns { get; init; } = 5;

    /// <summary>Consult calls one caller session may make in a day, across all its consultations.</summary>
    public int ConsultCallsPerSession { get; init; } = 10;

    /// <summary>How long an open consultation may sit unasked before it is closed and its handle dropped.</summary>
    public TimeSpan ConsultIdle { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Whether the <c>consult</c> tool answers at all.</summary>
    /// <remarks>
    /// <para>On unless somebody switched it off, and read through <see cref="NotSwitchedOff"/> rather
    /// than <see cref="Flag"/> for the reason that method states: the failure modes are not
    /// symmetric. A consultant wrongly available costs nothing, because nothing calls it until an
    /// agent is stuck; one wrongly unavailable is a refusal in the one moment it was wanted.</para>
    /// <para>The tool still EXISTS when this is off — a tool that vanishes from the list is a caller
    /// that cannot be told why. It refuses by name instead.</para>
    /// </remarks>
    public bool ConsultEnabled { get; init; } = true;

    /// <summary>
    /// Whether <c>COAI_CONSULTANTS</c> could not be read at all — in which case consulting refuses.
    /// </summary>
    /// <remarks>
    /// Fail-closed, like every other consultation check. A setting that does not parse is a person
    /// who MEANT to choose a consultant and did not succeed; running the shipped one instead sends
    /// their working tree to a vendor they did not pick. (CodeRabbit, on the pull request.)
    /// </remarks>
    public bool ConsultantsUnreadable { get; init; }

    public static string DefaultDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "coai-mcp");

    /// <summary>The file that says a directory is a data directory rather than an empty folder.</summary>
    private const string DatabaseFile = "coai.db";

    /// <summary>
    /// What distinguishes THIS installation from another one pointed at the same location.
    /// </summary>
    /// <remarks>
    /// <para>Issue #115. The default directory already differs per platform, so two installations
    /// only collide once somebody deliberately shares one — a NAS that survives a Windows reinstall.
    /// Then Windows and WSL both want to write, and the decision was that each keeps its own rather
    /// than merging: <c>rounds.id</c> and <c>findings.id</c> are AUTOINCREMENT, so two written-to
    /// databases collide on ids and a merge would have to remap every one of them along with the
    /// foreign keys that point at them.</para>
    ///
    /// <para><b>A name you choose, never one derived here.</b> The plan specified deriving it from
    /// the platform, the WSL distribution and the machine name, and that was wrong for a reason the
    /// extension's own <c>dataDir.ts</c> writes down in its docstring: the EXTENSION writes the
    /// Team-server token into this directory and the MCP shim READS it, so the two halves must agree
    /// on the path exactly. Deriving it would mean computing the same string twice, in C# from
    /// <see cref="Environment.MachineName"/> and in TypeScript from <c>os.hostname()</c> — which
    /// differ in case and in whether they carry a domain. The failure that produces is a silent
    /// "not signed in", which is what that docstring exists to prevent.</para>
    ///
    /// <para>So the side is whatever the person put in <c>COAI_DATA_SIDE</c> — which is also what the
    /// operator asked for in the first place: "give them different names". It is VALIDATED rather
    /// than trusted: a side called <c>../shared</c> would escape the very root it is meant to
    /// partition.</para>
    /// </remarks>
    /// <summary>The side this installation was NAMED, or empty when it was not given one.</summary>
    public static string DataSide(Func<string, string?> env) => PathSafeSide(env("COAI_DATA_SIDE"));

    /// <summary>
    /// The side-name grammar, and it is deliberately narrower than any filesystem's.
    /// </summary>
    /// <remarks>
    /// <para>An EXPLICIT allowlist rather than <see cref="Path.GetInvalidFileNameChars"/>, which is
    /// platform-dependent: a colon is refused on Windows and accepted on Linux, so
    /// <c>COAI_DATA_SIDE=a:b</c> would resolve to <c>&lt;root&gt;/a:b</c> under WSL and to
    /// <c>&lt;root&gt;</c> under Windows — the two halves of one installation disagreeing about where
    /// the Team-server token lives, which reads as a silent "not signed in". Four reviewers found
    /// this independently.</para>
    /// <para>The same grammar is spelled in <c>src_vs_code/src/dataDir.ts</c>. It has to be a rule
    /// simple enough to write twice without drifting, which is why it is a short allowlist and not a
    /// list of what to exclude.</para>
    /// </remarks>
    private const string SideGrammar = "lower-case letters, digits, dot, dash and underscore";

    private static bool IsSafeSide(string side) =>
        side.Length > 0
        && side is not ("." or "..")
        && side.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c is '.' or '-' or '_');

    /// <summary>One path segment, or empty when nothing was asked for.</summary>
    private static string PathSafeSide(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim().ToLowerInvariant();

        return IsSafeSide(trimmed) ? trimmed : string.Empty;
    }

    /// <summary>
    /// Where this side's data lives, and anything the person needs told about how that was decided.
    /// </summary>
    /// <remarks>
    /// <para>With no override the answer is <see cref="DefaultDataDir"/>, exactly as it has always
    /// been — nothing moves on its own and nobody has to do anything.</para>
    ///
    /// <para>With one, it is always <c>&lt;dir&gt;/&lt;side&gt;</c>. <b>There is no flat-layout
    /// fallback, and that is the whole correctness of this.</b> The first draft kept using
    /// <c>&lt;dir&gt;</c> when a database was already sitting there, so that an existing overrider
    /// would not find an empty directory. Three reviewers independently found what that does in the
    /// case this feature is FOR: Windows moves its directory to the NAS root, so the database is in
    /// the root; WSL is pointed at the same root, sees it, adopts the flat layout — and both sides
    /// write one SQLite file. The compatibility shim would have produced the exact corruption the
    /// partition prevents.</para>
    ///
    /// <para>So a database in the root is REPORTED and never adopted, and a side directory created
    /// for the first time is reported too — a mistyped NAS path is a perfectly creatable directory,
    /// and the failure it produces is a second history accumulating quietly beside the real one.</para>
    /// </remarks>
    /// <summary>
    /// What a person needs told about how the directory was decided — the half that touches disk.
    /// </summary>
    /// <remarks>
    /// <para>Separate from <see cref="ResolveDataDir"/>, and called at STARTUP rather than while
    /// settings are parsed. Parsing is pure: every one-shot mode — <c>--version</c>, <c>--help</c>,
    /// <c>--providers</c>, <c>--log</c> — builds a <see cref="PanelSettings"/>, and if that stats the
    /// data directory then a configured NAS that is unreachable makes <c>--version</c> hang on a
    /// mount rather than answering. Raised on both code rounds, and the same objection is why these
    /// notes are not merged into <c>Unrecognised</c>, which is about configuration VALUES.</para>
    /// </remarks>
    public static IReadOnlyList<StorageNote> StorageNotes(Func<string, string?> env)
    {
        if (env("COAI_DATA_DIR")?.Trim() is not { Length: > 0 } configured)
        {
            return [];
        }

        var root = Path.GetFullPath(configured);
        var dir = ResolveDataDir(env);
        var notes = new List<StorageNote>(2);

        if (dir != root && File.Exists(Path.Combine(root, DatabaseFile)))
        {
            notes.Add(new StorageNote(StorageNote.LooseDatabase, root,
                $"there is a {DatabaseFile} directly in {root}, from the layout before this "
                + $"directory was shared between sides. It is NOT being used: this side reads and "
                + $"writes {dir}. Move that database and its sessions into a side directory to keep "
                + "its history."));
        }

        if (!Directory.Exists(dir))
        {
            notes.Add(new StorageNote(StorageNote.NewDirectory, dir,
                $"{dir} did not exist and is being created — this side starts with no history. "
                + "If that is a surprise, check COAI_DATA_DIR for a typo before recording into it."));
        }

        return notes;
    }

    /// <summary>
    /// The ONE rule for where a side's data directory is.
    /// </summary>
    /// <remarks>
    /// <para>Public because it was not, and a second implementation grew beside it:
    /// <see cref="SettingsFile.DataDirFrom"/> was a bare <c>COAI_DATA_DIR</c> read with no side and
    /// no trim, so <c>coai.db</c> and <c>sessions/</c> went to <c>&lt;root&gt;/&lt;side&gt;/</c>
    /// while <c>settings.json</c> and <c>logs/</c> stayed in <c>&lt;root&gt;/</c> — two sides meant
    /// to be independent sharing one settings file and overwriting each other in silence.</para>
    /// <para>The fix is a CALL rather than a copy. Copying this logic would commit the same defect a
    /// second time, and the next rule added to one of them would part them again.</para>
    /// <para><b>It answers a TYPE, and this is the only place that mints one.</b>
    /// <see cref="ResolvedDataDir"/> is what the notices writer takes, so that
    /// <see cref="DataRootFor"/> — the directory BEFORE the side, which looks like a data directory
    /// and is not one — cannot be handed to it. The type's constructor is internal to this assembly
    /// and a test counts its minting sites; everything that needs the string unwraps with
    /// <c>.Path</c>. (Story 1.4, answering the reviewer of story 1.3.)</para>
    /// </remarks>
    // Through the type's named factory, because its constructor is PRIVATE: a target-typed `new`
    // would hide a second minting site from every scan, which is what the code round found.
    public static ResolvedDataDir DataDirectoryFor(Func<string, string?> env) =>
        ResolvedDataDir.For(ResolveDataDir(env));

    /// <summary>
    /// The directory BEFORE the side is applied — what an unpartitioned installation would use.
    /// </summary>
    /// <remarks>
    /// <para>Only one caller needs this and it needs it for one reason: an installation that was
    /// partitioned before the settings file knew about sides has its configuration in the ROOT, and
    /// <see cref="SettingsFile.AdoptRootSettings"/> has to be able to name that file. Everything
    /// else asks <see cref="DataDirectoryFor"/> and should keep doing so — a second way to get "the
    /// directory" is how this pair of functions came to disagree in the first place.</para>
    /// <para>It goes through the same resolver with the side removed, rather than re-reading
    /// <c>COAI_DATA_DIR</c>: the trim, the default and the refusal of an unusable side all still
    /// apply, and a caller asking for the root must not thereby escape the refusal.</para>
    /// </remarks>
    public static string DataRootFor(Func<string, string?> env) =>
        ResolveDataDir(name => name == "COAI_DATA_SIDE" ? null : env(name));

    private static string ResolveDataDir(Func<string, string?> env)
    {
        // Whitespace is not a configured directory. `COAI_DATA_DIR=' '` reaching Path.GetFullPath
        // would be the working directory, which is not what anybody meant by setting it.
        if (env("COAI_DATA_DIR")?.Trim() is not { Length: > 0 } configured)
        {
            return DefaultDataDir;
        }

        var root = Path.GetFullPath(configured);
        var asked = (env("COAI_DATA_SIDE") ?? string.Empty).Trim();

        // A side that was ASKED FOR and refused must never fall back to the shared root. That is the
        // finding seven reviewers raised, and it is the whole feature inverted: `COAI_DATA_SIDE=
        // wsl/node1` is a plausible thing to type, it fails the grammar, and falling back would put
        // this installation and every other one on the root's single database — the corruption the
        // partition exists to prevent, reached by a typo and reported nowhere.
        //
        // So it is refused loudly. A configuration that cannot be resolved safely is not a
        // configuration to carry on from: the alternative is picking a directory the operator did
        // not choose and writing somebody else's data into it.
        if (asked.Length > 0 && PathSafeSide(asked).Length == 0)
        {
            throw new InvalidOperationException(
                $"COAI_DATA_SIDE='{asked}' is not a usable directory name. A side may contain "
                + $"{SideGrammar}. It names a folder under COAI_DATA_DIR so that two installations "
                + "sharing one location keep their own database; refusing is deliberate, because "
                + "falling back would put both of them on the same one.");
        }

        // The partition is OPT-IN: no COAI_DATA_SIDE, no subdirectory, and a directory somebody
        // already points at keeps answering exactly where it always did.
        //
        // This is a correction to the plan, made on evidence. The first build partitioned EVERY
        // override, and six of this repository's own scenario tests went red — they set
        // COAI_DATA_DIR and then read files from that exact path, which is precisely what a person
        // with a script, or the bench, or anyone who set the variable last year also does. Silently
        // moving their data one level down is the same class of surprise the plan spent a section
        // refusing.
        //
        // Opting in also says what the operator said: "give them different names, side by side in
        // one folder".
        return DataSide(env) is { Length: > 0 } side ? Path.Combine(root, side) : root;
    }

    public static PanelSettings FromEnvironment(Func<string, string?> env) =>
        // READ once, composed once, then read twice — the round configuration is built from the
        // catalog and the sentences it refused join `Unrecognised`. Calling the parser or the
        // composer again for the second half would let the two halves of one answer describe two
        // different values of the setting. (codex, story B2's second code round.)
        WithCatalog(env, ParseRoles(env(Key.Roles)));

    private static PanelSettings WithCatalog(Func<string, string?> env, RolesSetting roles) =>
        WithCatalog(env, roles, RoleComposition.Compose(roles.Rows));

    private static PanelSettings WithCatalog(
        Func<string, string?> env, RolesSetting roles, RoleCatalog catalog) =>
        WithCatalog(
            env, roles, catalog, ResolveDataDir(env),
            ConsultantRouting.Parse(env("COAI_CONSULTANTS")),
            CommandModelsSetting.Parse(env(CommandModelsSetting.Key)));

    /// <remarks>
    /// The resolution is passed IN rather than computed twice. It was called once for `DataDir` and
    /// once for the notes, which meant two round trips to a NAS on every settings read and, worse,
    /// two answers: a directory created between the two calls made `DataDir` and `Unrecognised`
    /// describe different states. Raised four times on the code round. The consultant routing is
    /// passed in beside it for the same reason: it is parsed once and read twice, for the map and
    /// for its complaints — and so are the split order's models (issue #117).
    /// </remarks>
    private static PanelSettings WithCatalog(
        Func<string, string?> env,
        RolesSetting roles,
        RoleCatalog catalog,
        string dataDir,
        ConsultantsSetting consultants,
        CommandModelsSetting commandModels) => new PanelSettings
        {
            Rounds = Config(env, catalog),
            // The data directory's own notes ride here rather than in a channel of their own: this list
            // is already "things said out loud at startup, because silence made a working configuration
            // look broken", and a database left behind in a shared root is exactly that.
            // Each sentence with the key it is about, because the notice written for it groups on
            // that key. The two collections below know theirs by construction: `Dropped` is rows of
            // COAI_ROLES and `Complaints` is COAI_CONSULTANTS.
            UnrecognisedSettings =
            [
                .. UnknownValues(env, roles),
                .. catalog.Dropped.Select(dropped => new UnrecognisedSetting(Key.Roles, dropped)),
                .. consultants.Complaints.Select(
                    complaint => new UnrecognisedSetting(Key.Consultants, complaint)),
                .. commandModels.Complaints.Select(
                    complaint => new UnrecognisedSetting(CommandModelsSetting.Key, complaint)),
            ],
            CommandModels = commandModels.Map,
            RoundTreeRoot = env("COAI_ROUND_WORKTREES") is { Length: > 0 } roundTrees
                ? roundTrees
                : Runners.Worktrees.WorktreeManager.MachineLocalRoot,
            Consultants = consultants.Map,
            ConsultTurns = IntVar(env, "COAI_CONSULT_TURNS", 5),
            ConsultCallsPerSession = IntVar(env, "COAI_CONSULT_CALLS_PER_SESSION", 10),
            ConsultIdle = TimeSpan.FromMinutes(IntVar(env, "COAI_CONSULT_IDLE_MINUTES", 15)),
            ConsultEnabled = NotSwitchedOff(env, "COAI_CONSULT_ENABLED"),
            ConsultantsUnreadable = consultants.Unreadable,
            GlobalConcurrency = IntVar(env, "COAI_MAX_CONCURRENCY", 3),
            PerProviderConcurrency = IntVar(env, "COAI_MAX_PER_PROVIDER", 2),
            LocalConcurrency = IntVar(env, "COAI_LOCAL_CONCURRENCY", 1),
            ReviewerTimeout = TimeSpan.FromMinutes(IntVar(env, "COAI_REVIEWER_TIMEOUT_MINUTES", 10)),
            // `CountVar` rather than `IntVar`, because ZERO is the meaningful value here: it means
            // "derive it from the round's shape". IntVar refuses anything below one and would have
            // turned a deliberate zero into a default nobody chose — the same disagreement between the
            // two halves that CountVar was written for.
            RoundTimeout = TimeSpan.FromMinutes(CountVar(env, "COAI_ROUND_TIMEOUT_MINUTES", 0)),
            RateLimitBackoff = TimeSpan.FromSeconds(IntVar(env, "COAI_RATE_LIMIT_BACKOFF_SECONDS", 15)),
            RetryLadder = LadderFrom(env),
            // Seconds win when set: minutes are the setting a person configures, seconds are for a
            // short budget a test or a scripted run needs. One knob would have had to lie about one
            // of the two.
            EscalationBudget = env("COAI_ESCALATION_SECONDS") is { Length: > 0 }
            ? TimeSpan.FromSeconds(IntVar(env, "COAI_ESCALATION_SECONDS", 30))
            : TimeSpan.FromMinutes(IntVar(env, "COAI_ESCALATION_MINUTES", 30)),
            // ABSOLUTE, always. A relative one was accepted happily and made every round unrunnable: the
            // server writes its schema file and hands the reviewer that same relative path, and a vendor
            // CLI is launched in a directory of its own — so every reviewer answered "cannot find the
            // path specified" and the round came back `call_human` with nothing reviewed. Everything
            // reported success until the answer was empty, which is the worst shape a configuration
            // mistake can take. Found by this repository's own bench on its first real run.
            //
            // And PARTITIONED PER SIDE when it was overridden, so a location deliberately shared — a NAS
            // that survives a Windows reinstall — does not end up with Windows and WSL writing one
            // SQLite file. See ResolveDataDir; the default is untouched by it.
            DataDir = dataDir,
            AgentLogDir = env("COAI_AGENT_LOG_DIR") is { Length: > 0 } logs ? Path.GetFullPath(logs) : string.Empty,
            LocalMaxTokens = IntVar(env, "COAI_LOCAL_MAX_TOKENS", 8192),
            Autonomous = Flag(env, "COAI_AUTONOMOUS"),
            SplitPlan = Flag(env, "COAI_SPLIT_PLAN"),
            SplitWithFable = Flag(env, "COAI_SPLIT_WITH_FABLE"),
            GatePer = GateScopeOf(env(Key.GatePer) ?? string.Empty),
            LocalReasoningEffort = env("COAI_LOCAL_REASONING_EFFORT") is { Length: > 0 } effort
            ? effort.Trim().ToLowerInvariant()
            : "none",
            CodeWorkspace = WorkspaceOf(env(Key.Workspace)),
            DealPlanLenses = Flag(env, "COAI_DEAL_PLAN") || Flag(env, "COAI_ROTATE_PROMPTS"),
            DealCodeLenses = Flag(env, "COAI_DEAL_CODE") || Flag(env, "COAI_ROTATE_PROMPTS"),
            PromptsPerRound = ParsePromptRounds(env("COAI_PROMPTS_PER_ROUND")),
        }.WithProvidersFrom(env);

    /// <summary>Where a code reviewer runs, or the checkout when this build does not know the name.</summary>
    /// <remarks>
    /// The checkout is the fallback because it is the behaviour that loses nothing: a reviewer given
    /// MORE than it needs still answers the question.
    /// </remarks>
    private static string WorkspaceOf(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "worktree" => "worktree",
        _ => "none",
    };

    /// <summary>What a policy name means, or Human when this build does not know the name.</summary>
    /// <remarks>
    /// Human is the fallback because it is the end of the range that STOPS: a policy this build
    /// cannot honour must never resolve to proceeding over open findings.
    /// </remarks>
    private static StagePolicy PolicyOf(string? value) => value?.ToLowerInvariant() switch
    {
        "continue" => StagePolicy.Continue,
        "escalate" => StagePolicy.Escalate,
        "good_enough" or "goodenough" => StagePolicy.GoodEnough,
        _ => StagePolicy.Human,
    };

    /// <summary>
    /// The settings whose values this build does not understand, as sentences a person can act on.
    /// </summary>
    /// <remarks>
    /// It names the setting, the value, what happened instead, and that updating the server is the
    /// likely cure — because the likely cause is a panel newer than the server, and "unknown value"
    /// alone sends somebody back into the settings file where the answer is not.
    /// </remarks>
    /// <summary>
    /// The ladder this server climbs: the new setting, else the old one as a single step, else the
    /// shipped four.
    /// </summary>
    private static IReadOnlyList<TimeSpan> LadderFrom(Func<string, string?> env)
    {
        if (Runners.Reviewers.RetryLadder.Parse(env(Key.Backoff)) is { Count: > 0 } ladder)
        {
            return ladder;
        }

        // Absent is not the same as unreadable, and neither is the same as "nobody has an opinion":
        // only a value somebody wrote gets to override the default.
        return env("COAI_RATE_LIMIT_BACKOFF_SECONDS") is { Length: > 0 }
            ? [TimeSpan.FromSeconds(IntVar(env, "COAI_RATE_LIMIT_BACKOFF_SECONDS", 15))]
            : Runners.Reviewers.RetryLadder.Default;
    }

    /// <summary>
    /// The environment variables an unrecognised-setting notice can be ABOUT, spelled once each.
    /// </summary>
    /// <remarks>
    /// Each of these was written three times — where the value is read, where the refusal is
    /// decided, and inside the sentence a person reads — and the notice's grouping key made a
    /// fourth. A rename that updated the reads and missed the sentence would group the page under
    /// the new variable while telling the person about the old one, which is worse than either
    /// alone. (codex, on story 2.3.3's code round.)
    /// </remarks>
    private static class Key
    {
        internal const string Backoff = "COAI_RETRY_BACKOFF";

        internal const string Exhausted = "COAI_ON_EXHAUSTED";

        internal const string Workspace = "COAI_CODE_WORKSPACE";

        internal const string Roles = "COAI_ROLES";

        internal const string Consultants = "COAI_CONSULTANTS";

        internal const string GatePer = "COAI_GATE_PER";
    }

    /// <summary>Every setting whose VALUE this build could not use, as one sentence each.</summary>
    /// <remarks>
    /// <para>One clause per key, each its own method, so this one only assembles them: the family's
    /// C# doctrine bounds a method at four decisions and four diagnostics was already past it.
    /// (CodeRabbit, this plan's pull request.)</para>
    /// <para>Each clause answers a LIST — empty when it has nothing to say — rather than a nullable
    /// one filtered out afterwards with <c>OfType</c>, which is the exact shape the family's
    /// no-null rule names. (codex, on story 2.3.3's code round.)</para>
    /// </remarks>
    private static IReadOnlyList<UnrecognisedSetting> UnknownValues(
        Func<string, string?> env, RolesSetting roles) =>
        [.. WhyBackoff(env), .. WhyExhausted(env), .. WhyWorkspace(env), .. WhyGatePer(env), .. WhyRoles(roles)];

    private static IReadOnlyList<UnrecognisedSetting> WhyGatePer(Func<string, string?> env) =>
        env(Key.GatePer) is { Length: > 0 } scope && !AGateScopeWeKnow(scope)
            ? [new UnrecognisedSetting(
                Key.GatePer,
                $"{Key.GatePer} is '{scope}', which this server does not know — split work is gated "
              + "once per epic, as it is by default. The values are 'epic' and 'task'.")]
            : [];

    private static bool AGateScopeWeKnow(string value) =>
        string.Equals(value.Trim(), "epic", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value.Trim(), "task", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>COAI_GATE_PER</c> as a scope: <c>task</c> is one gate for the whole task, anything else is
    /// per epic — an unknown word is said by <see cref="WhyGatePer"/> (issue #131).
    /// </summary>
    private static Core.Commands.GateScope GateScopeOf(string value) =>
        string.Equals(value.Trim(), "task", StringComparison.OrdinalIgnoreCase)
            ? Core.Commands.GateScope.Task
            : Core.Commands.GateScope.Epic;

    private static IReadOnlyList<UnrecognisedSetting> WhyBackoff(Func<string, string?> env) =>
        env(Key.Backoff) is { Length: > 0 } backoff
        && Runners.Reviewers.RetryLadder.Parse(backoff).Count == 0
            ? [new UnrecognisedSetting(
                Key.Backoff,
                $"{Key.Backoff} is '{backoff}', which this server cannot read as a list of "
              + "seconds — it is using the waits it would have used anyway. The form is "
              + "'5,30,60,120', and one number means one retry at that interval.")]
            : [];

    private static IReadOnlyList<UnrecognisedSetting> WhyExhausted(Func<string, string?> env) =>
        env(Key.Exhausted) is { Length: > 0 } policy
        && PolicyOf(policy) == StagePolicy.Human
        && !string.Equals(policy, "human", StringComparison.OrdinalIgnoreCase)
            ? [new UnrecognisedSetting(
                Key.Exhausted,
                $"{Key.Exhausted} is '{policy}', which this server does not know — it is asking a "
              + "person instead. The panel is probably newer than this server: update it in the "
              + "panel's Server section.")]
            : [];

    private static IReadOnlyList<UnrecognisedSetting> WhyWorkspace(Func<string, string?> env) =>
        env(Key.Workspace) is { Length: > 0 } workspace && !AWorkspaceWeKnow(workspace)
            ? [new UnrecognisedSetting(
                Key.Workspace,
                $"{Key.Workspace} is '{workspace}', which this server does not know — code "
              + "reviewers are getting the diff alone, as they do by default. The values are "
              + "'none' and 'worktree'.")]
            : [];

    private static bool AWorkspaceWeKnow(string workspace) =>
        string.Equals(workspace.Trim(), "none", StringComparison.OrdinalIgnoreCase)
        || string.Equals(workspace.Trim(), "worktree", StringComparison.OrdinalIgnoreCase);

    /// <summary>The whole <c>COAI_ROLES</c> value, when this build could not parse it at all.</summary>
    /// <remarks>
    /// Threaded in rather than re-read, unlike the neighbours above: they parse a few characters
    /// twice and nothing can come of it, while this one decides which roles RUN — and one read for
    /// the catalog and another for the complaint could describe two different values of the setting.
    /// (codex, story B2's second code round.)
    /// </remarks>
    private static IReadOnlyList<UnrecognisedSetting> WhyRoles(RolesSetting roles) =>
        roles.CouldNotBeRead
            ? [new UnrecognisedSetting(
                Key.Roles,
                $"{Key.Roles} is not JSON this server can read — {roles.Unreadable} It is running the "
              + "roles it shipped with, and nothing you added is in this round. The form is an "
              + """array of rows: [{"id":"Requirements","name":"...","stage":"result","prompts":[…]}].""")]
            : [];

    /// <summary>
    /// <c>{"Architecture":["architecture","arch-boundaries"],...}</c> — the panel's per-round
    /// choice. Malformed JSON is no choice at all rather than a half-applied one.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParsePromptRounds(string? json)
    {
        if (json is not { Length: > 0 })
        {
            return new Dictionary<string, IReadOnlyList<string>>();
        }

        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize(
                json, SettingsJsonContext.Default.DictionaryStringListString);
            // `{"Architecture": null}` is valid JSON and would put a NULL list in the map, which
            // the round then dereferences. Found by the gate reviewing this very commit: a
            // hand-edited settings file could crash every round with no useful message.
            return parsed?
                       .Where(e => e.Value is not null)
                       .ToDictionary(e => e.Key, e => (IReadOnlyList<string>)e.Value)
                   ?? new Dictionary<string, IReadOnlyList<string>>();
        }
        catch (System.Text.Json.JsonException)
        {
            return new Dictionary<string, IReadOnlyList<string>>();
        }
    }

    /// <summary>
    /// The reviewers, from `COAI_VENDORS` — a JSON array, because a comma-separated list cannot
    /// carry a runtime and a base URL, and a second encoding for those would be a format nobody
    /// could read in a config file. `COAI_PROVIDERS` still works for the simple case.
    /// </summary>
    private PanelSettings WithProvidersFrom(Func<string, string?> env)
    {
        if (env("COAI_VENDORS") is { Length: > 0 } json && ParseVendors(json) is { Count: > 0 } vendors)
        {
            // The env variable still answers for a vendor the list did not place. It predates the
            // panel, it is what a scripted or containerised run has, and dropping it the moment a
            // vendor list appeared is what made an executable path unsettable from either side.
            return this with { Providers = [.. vendors.Select(v => WithExecutable(v, env))] };
        }

        var listed = env("COAI_PROVIDERS");
        var providers = (listed is { Length: > 0 }
                ? listed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : ["codex", "antigravity"])
            .Select(p => new ProviderSettings(p.ToLowerInvariant())
            {
                Model = env($"COAI_MODEL_{p.ToUpperInvariant()}") ?? string.Empty,
                ExecutablePath = env($"COAI_EXE_{p.ToUpperInvariant()}") ?? string.Empty,
            })
            .ToList();
        return this with { Providers = providers };
    }

    /// <summary>Where this vendor's CLI is: the list first, then its own env variable.</summary>
    private static ProviderSettings WithExecutable(ProviderSettings vendor, Func<string, string?> env) =>
        vendor.ExecutablePath.Length > 0
            ? vendor
            : vendor with { ExecutablePath = env(ExecutableVariable(vendor.Provider)) ?? string.Empty };

    /// <summary><c>my-claude</c> → <c>COAI_EXE_MY_CLAUDE</c>, the same derivation the key uses.</summary>
    internal static string ExecutableVariable(string provider) =>
        $"COAI_EXE_{provider.ToUpperInvariant().Replace('-', '_').Replace('.', '_')}";

    /// <summary>Malformed JSON is no configuration at all — the caller falls back rather than
    /// running a review with a vendor list somebody half-wrote.</summary>
    internal static List<ProviderSettings> ParseVendors(string json)
    {
        try
        {
            var vendors = System.Text.Json.JsonSerializer.Deserialize(json, SettingsJsonContext.Default.ListVendorDto);
            return vendors is null
                ? []
                : [.. vendors
                    .Where(v => !string.IsNullOrWhiteSpace(v.Id))
                    .Select(v => new ProviderSettings(v.Id!.Trim().ToLowerInvariant())
                    {
                        Runtime = RuntimeOf(v.Runtime),
                        Model = v.Model?.Trim() ?? string.Empty,
                        BaseUrl = v.BaseUrl?.Trim() ?? string.Empty,
                        // NOT lower-cased, unlike the row's own id: this is the SERVER's spelling
                        // of its vendor, and a catalog that says `DeepSeek` matches `DeepSeek`. The
                        // row id is ours to normalise; this one is not. Caught on the code round.
                        RemoteVendor = v.RemoteVendor?.Trim() ?? string.Empty,
                        ExecutablePath = v.ExecutablePath?.Trim() ?? string.Empty,
                        // Absent is TRUE on both, so a vendor list written by an older extension
                        // keeps reviewing both stages rather than silently reviewing neither.
                        Plan = v.Plan != false,
                        Code = v.Code != false,
                        // Absence is NAMED here rather than carried on as a null: a missing JSON
                        // field is legitimately a nullable at this boundary, and one line past it
                        // the state has to be a value with a name. See `VendorDto.Document`.
                        Documents = DocumentReviewsOf(v.Document),
                    })
                    // One id, one vendor — the extension already refuses a duplicate row, and a
                    // hand-edited settings file is how one reaches the server. The id is the
                    // provider/role key of every reviewer launch, so two rows sharing it would
                    // collide in the round's dictionary before any model ran.
                    .DistinctBy(v => v.Provider)];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <summary>What a caller's <c>document</c> field means, once absence has been given its name.</summary>
    /// <remarks>
    /// The one place the nullable stops. A missing field in JSON is honestly a null; a routing rule
    /// reading a null is what coding-style forbids, so the translation happens here, at the boundary
    /// where absence actually arrives.
    /// </remarks>
    private static DocumentReviews DocumentReviewsOf(bool? said) => said switch
    {
        true => DocumentReviews.Yes,
        false => DocumentReviews.No,
        null => DocumentReviews.Unspecified,
    };

    /// <summary>
    /// Which runtime a configured vendor drives — every one this build knows, not two of them.
    /// </summary>
    /// <remarks>
    /// This used to read "gemini, else codex", which silently ran every <c>claude</c> vendor
    /// through the Codex CLI. It hid behind the id lookup — a vendor CALLED claude was matched by
    /// name before its runtime was consulted — so it only surfaced when someone named one
    /// <c>my-claude</c> and watched codex start. A reviewer that runs the wrong vendor's model is
    /// worse than one that refuses: the panel reports an answer from a model nobody chose.
    /// </remarks>
    private static string RuntimeOf(string? runtime)
    {
        // Unset stays unset, and that is the whole distinction: the id then decides, so a vendor
        // called `gemini` with no runtime field is still a gemini.
        var name = runtime?.Trim().ToLowerInvariant() ?? string.Empty;
        if (name.Length == 0)
        {
            return string.Empty;
        }

        // Membership, not a hand-written list. This WAS a hand-written list — gemini, claude,
        // antigravity, else codex — and `local` never got added to it, so a local vendor became a
        // codex vendor with a base URL, failed the key check that base URLs imply, and was dropped
        // from every round. The panel showed a configured reviewer; the round opened with zero.
        // The extension had the identical defect in its own copy of this set, days earlier.
        //
        // An unknown runtime is still a custom vendor riding the Codex CLI against its own base
        // URL — a deliberate decision kept from the vendor-settings tests, not a fallthrough.
        return Runners.Reviewers.ReviewerRuntimeSelector.RuntimeNames.Contains(name) ? name : "codex";
    }

    /// <summary>
    /// Every role's gate, read widest-first: the role's own keys, its stage's, then the legacy pair.
    /// </summary>
    /// <remarks>
    /// <c>COAI_ROUNDS_ARCHITECTURE</c> / <c>COAI_THRESHOLD_SECURITYRELIABILITY</c> name a role;
    /// <c>COAI_MAX_ROUNDS_CODE</c> / <c>COAI_THRESHOLD_PLAN</c> name a stage; <c>COAI_MAX_ROUNDS</c>
    /// and <c>COAI_GATE_THRESHOLD</c> are the originals and still fill in for everything.
    /// <c>COAI_ENABLED_ARCHITECTURE</c> switches one CODE role off, and only off — see
    /// <see cref="NotSwitchedOff"/>.
    /// </remarks>
    /// <summary>
    /// The roles a person configured, composed onto the shipped ones.
    /// </summary>
    /// <remarks>
    /// <c>COAI_ROLES</c> is a JSON array of rows — id, name, stage, programmingTask, active and a
    /// prompt list — and malformed JSON is NO custom roles rather than a half-applied list, the
    /// reflex <c>COAI_VENDORS</c> and <c>COAI_PROMPTS_PER_ROUND</c> have had since they shipped.
    /// What composition refuses row by row comes back in <see cref="RoleCatalog.Dropped"/> and joins
    /// <see cref="Unrecognised"/>, so a person reads WHY the role they wrote is not running — and a
    /// value this build cannot parse at ALL joins the same list from <see cref="UnknownValues"/>,
    /// because a parse that never reached a row has no row to refuse.
    /// </remarks>
    /// <summary>
    /// What <c>COAI_ROLES</c> turned out to be: the rows, and a reason when there are none because
    /// this build could not read it.
    /// </summary>
    /// <remarks>
    /// <para>A record rather than a nullable list, per doctrine 4 and 5: "not captured" and "empty"
    /// are different facts and must be different states, and an expected failure is a value carrying
    /// its reason rather than a null somebody has to know the meaning of. The nullable list this
    /// replaced needed a comment at each of its two call sites to say which null meant what.
    /// (codex and gemini, story B2's code round.)</para>
    /// <para><see cref="Unreadable"/> empty is the good case — including for an absent key, which is
    /// no rows and no complaint.</para>
    /// </remarks>
    internal sealed record RolesSetting(IReadOnlyList<RoleEntry> Rows, string Unreadable = "")
    {
        public bool CouldNotBeRead => Unreadable.Length > 0;
    }

    /// <summary>The rows a person wrote, or why there are none.</summary>
    internal static RolesSetting ParseRoles(string? json)
    {
        if (json is not { Length: > 0 })
        {
            return new RolesSetting([]);
        }

        try
        {
            return new RolesSetting(
                System.Text.Json.JsonSerializer.Deserialize(json, SettingsJsonContext.Default.ListRoleEntry)
                ?? throw new System.Text.Json.JsonException("it is the JSON value null rather than a list of roles"));
        }
        catch (System.Text.Json.JsonException e)
        {
            // The parser's own message, which carries the line and the character it stopped at. A
            // person is looking at a screenful of JSON for one comma, and where it went wrong is the
            // only part of that sentence they cannot work out for themselves — so the template we
            // add around it must not replace it. (gemini, story B2's code round.)
            return new RolesSetting([], Detail(e));
        }
    }

    /// <summary>The parser's complaint, with the position counted the way an editor counts.</summary>
    /// <remarks>
    /// <c>JsonException</c> numbers lines from zero and already renders them into its message that
    /// way, so the message is rewritten rather than appended to: telling somebody "line 1" about
    /// what their editor calls line 2 is worse than not telling them at all.
    /// </remarks>
    private static string Detail(System.Text.Json.JsonException e) =>
        e.LineNumber is { } line
            ? $"{Sentence(e)} (line {line + 1}, character {(e.BytePositionInLine ?? 0) + 1})"
            : Sentence(e);

    /// <summary>The parser's own words — without its position, and without its advice.</summary>
    /// <remarks>
    /// "Change the reader options" is addressed to whoever wrote the deserializer, and the person
    /// reading this has a settings file and no reader to change. The DIAGNOSIS is worth every word
    /// ("the JSON array contains a trailing comma at the end"); the remedy is ours to give.
    /// </remarks>
    private static string Sentence(System.Text.Json.JsonException e) =>
        e.Message.Split(" LineNumber:")[0].Replace("Change the reader options.", string.Empty).Trim();

    private static Dictionary<string, RoleGate> RoleGates(Func<string, string?> env, RoleCatalog catalog)
    {
        var gates = new Dictionary<string, RoleGate>();
        foreach (var definition in catalog.Roles)
        {
            gates[definition.Id] = GateFor(env, definition);
        }

        return gates;
    }

    /// <summary>
    /// One role's budget: its own keys, then its stage's, then the shipped default.
    /// </summary>
    /// <remarks>
    /// <para>Split out of <see cref="RoleGates"/> so the loop and the three-deep fallback are
    /// separate methods, each inside the complexity the family's C# doctrine allows. (CodeRabbit,
    /// this plan's pull request.)</para>
    /// <para>The SHIPPED plan role carries no switch at all — code review only, by the operator's
    /// ruling — so the boundary refuses to disable it rather than trusting nobody sets the variable.
    /// A plan-stage role a person ADDED is theirs to switch: the ruling was about not turning the one
    /// shipped stage off by accident, and a stage with two roles in it has a second one to keep
    /// running.</para>
    /// </remarks>
    private static RoleGate GateFor(Func<string, string?> env, RoleDefinition definition)
    {
        var isPlan = definition.Stage == RoleStages.Plan;
        var stage = isPlan ? "PLAN" : "CODE";
        var shipped = isPlan ? PanelConfig.PlanDefault : PanelConfig.CodeDefault;
        var key = definition.Id.ToUpperInvariant();

        return new RoleGate(
            IntVar(env, $"COAI_ROUNDS_{key}",
                IntVar(env, $"COAI_MAX_ROUNDS_{stage}",
                    IntVar(env, "COAI_MAX_ROUNDS", shipped.MaxRounds))),
            CountVar(env, $"COAI_THRESHOLD_{key}",
                CountVar(env, $"COAI_THRESHOLD_{stage}",
                    CountVar(env, "COAI_GATE_THRESHOLD", shipped.Threshold))),
            (isPlan && definition.BuiltIn) || NotSwitchedOff(env, $"COAI_ENABLED_{key}"));
    }

    /// <summary>The round configuration: which roles exist, and what each may spend.</summary>
    /// <remarks>
    /// Two questions with one answer, and they are asked in that order — the gates are built for the
    /// roles the catalog holds, so a role a person added gets its own <c>COAI_ROUNDS_&lt;ID&gt;</c> and
    /// <c>COAI_ENABLED_&lt;ID&gt;</c> keys like any other. Three layers of those, widest first: a ROLE's
    /// own keys, then its stage's, then the legacy single pair. Somebody who set a threshold once
    /// must not have their gate change under them, and somebody who set a stage must not have to
    /// repeat it for three roles.
    /// </remarks>
    private static PanelConfig Config(Func<string, string?> env, RoleCatalog catalog) =>
        new(Roles: RoleGates(env, catalog), OnExhausted: PolicyOf(env(Key.Exhausted)))
        {
            Catalog = catalog,
        };

    private static bool Flag(Func<string, string?> env, string name) =>
        env(name) is "1" or "true" or "TRUE" or "True";

    /// <summary>
    /// A switch that is ON unless it says, in so many words, that it is off.
    /// </summary>
    /// <remarks>
    /// <para>The inverse of <see cref="Flag"/>, and deliberately not <c>!Flag(...)</c>: the two
    /// answer different questions. <c>Flag</c> is for a switch whose absence means off, where an
    /// unreadable value should stay off. This one guards a REVIEWER, where the failure modes are not
    /// symmetric — a role wrongly on costs one extra pass, a role wrongly off means a review nobody
    /// performed and nothing on screen saying so.</para>
    /// <para>So only the four spellings of false disable it. Absent, empty, <c>no</c>, <c>0.0</c>,
    /// a typo, a value some shell mangled — every one of them leaves the reviewer working. This is
    /// the parser half of "absent means on"; <see cref="RoleGate.Enabled"/> is the other half.</para>
    /// </remarks>
    private static bool NotSwitchedOff(Func<string, string?> env, string name) =>
        env(name) is not ("0" or "false" or "FALSE" or "False");

    private static int IntVar(Func<string, string?> env, string name, int fallback) =>
        int.TryParse(env(name), out var value) && value > 0 ? value : fallback;

    /// <summary>
    /// A count where ZERO is a legitimate value — a threshold, unlike a round budget.
    /// </summary>
    /// <remarks>
    /// <see cref="IntVar"/> requires a positive number, which is right for rounds and concurrency and
    /// wrong for a threshold: zero means "any gating finding blocks", the panel has always accepted
    /// it and has a test saying so, and the server silently substituted its own default. The two
    /// halves disagreed about a number a person had deliberately set to nothing.
    /// </remarks>
    private static int CountVar(Func<string, string?> env, string name, int fallback) =>
        int.TryParse(env(name), out var value) && value >= 0 ? value : fallback;
}

/// <summary>
/// A configured vendor as the library's resolution needs it — the bridge between this server's
/// settings type and <see cref="Runners.Reviewers.VendorIdentity"/>.
/// </summary>
/// <remarks>
/// It exists so <c>CoaiMcp.Runners</c> need not know what a <see cref="ProviderSettings"/> is:
/// the library answers questions about a vendor, and a vendor is three strings. Everything else on
/// this record — the model, the executable path, whether it is enabled — belongs to a launch or to
/// a probe, and travels as its own argument.
/// </remarks>
public static class ProviderIdentity
{
    public static Runners.Reviewers.VendorIdentity Identity(this ProviderSettings provider) =>
        new(provider.Provider, provider.Runtime, provider.BaseUrl, provider.RemoteVendor);
}
