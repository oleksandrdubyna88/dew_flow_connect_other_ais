# module: server — coai-mcp, the protocol holder

> `src_mcp/src` — the host. Identity `connect-other-ais` on the wire, `coai` as the client's
> config key (which is what prefixes the tools: `mcp__coai__review_plan`). Built by hand on the
> `ModelContextProtocol` SDK — the hosted default logs to stdout, and stdout carries JSON-RPC.

## The nine tools

| Tool | Backed by | Refuses when |
|---|---|---|
| `providers` | `PanelService.ProvidersAsync` — CLI probe + vault state | never; it reports |
| `open` | `OpenAsync` — resolve branch, prune worktrees, load-or-create session, stamp the caller | repo/branch unresolvable |
| `review_plan` | `RunStageAsync` with one `PlanCritique` per provider | no session; round awaiting resolve |
| `review_code` | `RunStageAsync` with the four code roles per provider — `Conventions` first | **no plan round reached `proceed`** |
| `review_document` | `ReviewDocumentAsync` → `RunStageAsync` with the document roles | no branch session; no purpose; the document is outside the repo, not text, or too large; every document role off; the review has finished (`newReview`) |
| `resolve` | `ResolveAsync` — reasoned decisions by finding index | bad index; reject without a reason |
| `status` | persisted session + round trail | no session |
| `ask_human` | `Escalations` — a question FILE the extension watches | only an empty question; otherwise it WAITS the budget, then answers `no_answer_yet` telling the model to ask in the chat |
| `consult` | `ConsultationService` — another vendor's model over the LIVE working tree | eleven ways, each a sentence naming its cure — see below |

## The consultant — the ninth tool (2026-09-12)

The first eight tools are the GATE: other vendors judging a plan, a diff and a document. `consult` is the other
direction — the calling AI, stuck, asking one of them a question. Design record:
[PLAN_consultant.md](PLAN_consultant.md); all six stories shipped, 2026-09-12 to 2026-09-13.

**It shares nothing with the round machine, on purpose.** `RoundMachine` refuses a round while a
human gate is set or a round awaits `resolve` — exactly the moments an agent is stuck — and half the
triggers happen before `open` exists. So a consultation is its own entity, keyed by the CALLER
(`CallerIdentity`), referencing a session only when there is one. `open` is unchanged and not required.

**The consultant sees the tree, not the agent's story.** The server runs `git diff HEAD` and
`ls-files --others` itself (`ContextAssembler.CollectWorkingTreeAsync`) and puts the shaped result in
the prompt; the caller hands over a problem statement and the files it suspects, never a diff. An
agent that is stuck has a blind spot by definition, and its own account of the repository is written
through that blind spot. The diff is budgeted at 64 KB (a third of a review's) because turn 1 carries
it and one vendor has no prompt cache; an untracked file over 16 KB, or with a NUL in its first 8 KB,
is NAMED rather than inlined.

**It runs in the LIVE checkout, read-only — and the flags are the VENDOR's promise, so there is an
invariant.** `FilesystemInvariant` fingerprints the tree before the launch and after it: every
tracked, untracked and IGNORED path `git status` lists, each listed file's size and mtime, and
`.git/HEAD`, `.git/config` and every hook. Any difference fails the consultation CLOSED — the advice
is withheld, every path is named with what happened to it, and **nothing is deleted and nothing is
reverted**. A consultation runs for minutes while the person works, so a file that appeared in that
window may be theirs; two reviewers called an automatic delete Blocking on the plan round and they
were right.

Three things are deliberately NOT watched, and each is a measurement rather than a preference.
`.git/index` and the `.git` directory's own mtime: `git status` rewrites the index's stat cache —
including the invariant's own FIRST call — so watching it made every consultation fail closed with
nothing wrong, found the moment the scenario test first ran. And an ignored DIRECTORY's mtime: git
lists such a directory as ONE entry, so its timestamp stood in for everything inside it, and a build,
a language server or an editor writing one temporary file into `bin/` would have failed every
consultation on a machine with a watcher running. **That one was found by the CONSULTANT** — asked,
on this feature's own live check, what the most likely false positive in this invariant was — and
reproduced as a red test before it was believed. A directory's EXISTENCE is still watched; only its
timestamp is gone.

The named residual, which is what that gives up: a file changed deep inside an ignored directory is
not seen. Walking those contents would cost seconds on every call, and an OS-level write audit is out
of scope.

**The alert says the TREE changed, not that the consultant changed it.** Two snapshots cannot name a
writer — the person's own editor, a build watcher or a git command of theirs can move a file in the
same window — and the consultation is withheld either way, because the advice was formed against a
tree that no longer holds. Claiming the consultant did it would send somebody hunting a vendor for
their own keystrokes. The consultant's own second point, on the same live check.

**One consultation per repository at a time** (`RepositoryLock`, a held `FileShare.None` handle, the
`SessionTurn` shape), taken BEFORE the record is read and held to the second snapshot: two
consultations on one tree would each see the other's work as a breach, and two follow-ups that both
read an `open` record before either locked would let the second write its turn over the first one's
answer. Nothing about a consultation is decided outside the lock. Thirty seconds of waiting, then a
named refusal. The startup sweep asks for the same lock with a zero wait before closing an idle
record, so it cannot end a consultation another server is in the middle of.

**A consultation is bound to its repository, its vendor and its model.** A follow-up that names a
different `repoPath` is refused by name — the id would otherwise launch that vendor thread in a
checkout it had never seen while the record went on describing the first one's branch. And a resumed
turn runs on the vendor, model and memory mode FROZEN on the record rather than on what the panel
says now: the record claims they are frozen, and a settings change or a server upgrade between turns
would otherwise hand one vendor's handle to another, or make an open conversation silently stop
carrying its transcript.

**A conversation, resumed through the vendor's own store.** The reply carries a `consultationId`; a
follow-up passes it and the vendor resumes its own thread. The server holds no long-lived child —
only the handle, validated where it is read off the stream and again where it enters an argv
(`ConsultantHandle`, the C# twin of the extension's `codexAdapter` guard). **A turn that dies after
the vendor accepted it becomes `interrupted` rather than failed**: the handle is read off whatever
output was captured, the turn is NOT counted against the cap, and the next call picks the
conversation up — otherwise the caller pays twice for work already done. A vendor that has dropped
the thread says so in its own words and is reported as that, not as a generic exit code.

**Both directions are fenced.** The diff goes to the consultant as material between nonce-marked
lines with the problem LAST; the advice comes back inside
`<consultant_advice … status="advisory_only" nonce=…>` with any copy of the tag inside the text
neutralised, followed by the IMPORTANT note. A convention, not a boundary — the caller runs under its
own permission system — and the plan says so.

**The circuit breakers**: turns per consultation (`COAI_CONSULT_TURNS`, 5), consult calls per caller
session (`COAI_CONSULT_CALLS_PER_SESSION`, 10), an idle close (`COAI_CONSULT_IDLE_MINUTES`, 15) that
drops the vendor's handle so no zombie session is kept, and the per-turn deadline, which is the
reviewer timeout rather than a second number to keep in step. The call counter **never fails open**:
when its file cannot be written the cap is enforced in memory for this server's lifetime and the reply
says so — the opposite of `CallerSessions`' split-order claim, because a repeated instruction is cheap
and a runaway agent on a paid vendor is not.

**And the question phase 2 has to answer is now a NUMBER** (2026-09-13). `consult_missed` on a round
counts the findings it handed back that the caller had already ACCEPTED — agreed with, changed the
code or the plan for, and met again. That is not `re_raised`, which is a reviewer pressing a standing
REJECTION: a disagreement the caller is defending, and the more interesting of the two for reading an
argument. This is the more expensive one, because nobody is disagreeing — the fix did not take.

The caller's LATEST word about a defect decides: accepted in round 1, rejected in round 2 and raised
again in round 3 is a disagreement being defended — `re_raised`'s signal — and counting it here would
file it as a fix that did not take. It is counted inside the projection, where the DECISIONS live (the session file holds what a round
found; the database holds what was done about it), matched by `FindingDedup.SameDefect` — this
product's own rule for "the same defect", rather than a second similarity rule written beside it —
and scoped to earlier rounds of the SAME session and stage, since a plan-stage remark has no file and
a code-stage one usually does. A round the projection could not ask about keeps `-1`, the
`accepted`/`rejected` convention: a round nobody measured must not read as a round where nothing
survived.

**Nothing is called.** One line in the audit says an automatic consultation *could* have fired here,
with the count and the rounds; the number rides on `--log`; no page shows it. A trigger that fired
before anybody had read the number would be the same guess with a cost attached, and what the number
is for is deciding whether that trigger should exist at all.

**The person's own trigger is an MCP PROMPT** (2026-09-13). Four of the five triggers are the
assistant's to notice; the fifth is the person saying so, and a sentence in a chat is a weak carrier
for it — it competes with everything else being said, and it names neither the repository nor the
tool. `Prompts.cs` registers one `McpServerPrompt` beside the nine tools, which a client lists as
`/mcp__coai__consult`. It calls nothing: a prompt is TEXT handed to the assistant, so every cap,
refusal and invariant still applies to whatever the assistant then decides. The argument is optional
on purpose — somebody who types the command alone is saying "you know what we are stuck on" — and
when they do give words, the message tells the assistant to send those words unrewritten. The two
rules ride with it, because they are the ones an agent drops first: the advice is MATERIAL to verify,
and the next turn must report what verifying it produced.

**And the whole thing has a switch** (`COAI_CONSULT_ENABLED`, on). Off is a REFUSAL BY NAME, checked
before the arguments are looked at: the tool stays in the list, because a caller that cannot see a
tool cannot be told why it is not there, and the sentence names the setting and the panel section
that writes it. It is read through `NotSwitchedOff` — the parser a reviewer's own switch uses — so
only the four spellings of false disable it and a mangled value leaves the tool working. The
asymmetry is deliberate and it is the same one: a consultant wrongly available costs nothing, because
nothing calls it until an agent is stuck; one wrongly unavailable is a refusal in the one moment it
was wanted.

**The consultation is projected into the rounds database, and the projection is never allowed to
matter.** Schema step 2 (2026-09-13) adds a `consultations` table — one row per CONSULTATION rather
than per turn, upserted as it advances, with the totals summed from the turns, the FIRST problem
(what it is about) and the LAST advice (the answer in force) — plus `rounds.consult_missed` for story
6's counter, because a schema step is append-only and splitting one column across two steps buys
nothing. The write hangs off `ConsultationStore.Write`, which is the ONE place every state passes
through: a projection wired into the service would have recorded the turns and silently missed both
sweeps, so the log would show consultations that never ended. `Store.Projection` is the shared
`try`/`catch` — the file is the source of truth, and a database that is locked, full or corrupt is a
line in the log, never a consultation that refuses somebody who is stuck.

**The log catches up with the records at startup.** Because the projection is allowed to fail, and a
TERMINAL record is never written again: a database locked or full when a consultation wrote its last
state would leave that consultation missing from the log for ever. `ConsultationService.Reproject`
re-upserts every record the store still holds, beside the sweep that already reads them all —
bounded by the same 7-day retention, and safe to run on every start because a row is RECOMPUTED from
its record rather than accumulated. (codex, story 4's plan round.)

**`status` names a consultation this repository still has open.** That is re-orientation, which is
what the tool is for, pointed at the one thing a compacted conversation loses that costs money: the
`consultationId` the first reply carried. Without it the next call opens a SECOND consultation — the
working tree collected again, a model that has already answered asked from scratch, the caller's own
per-session budget spent twice. Keyed by the REPOSITORY rather than the session (a consultation has
no session) and by the CALLER — `Existing` refuses a follow-up whose caller differs, so another
session's open consultation is an id that would come back as a refusal — compared as a resolved PATH
rather than as text, and absent for one that is over: there is nothing to resume and nothing to say.

**Every turn is one ledger row of `kind: consult`, `stage: Consultation`** — a third kind beside
`review` and `chat`, because the phase-2 question ("should an automatic consultation fire when a
finding survives two rounds?") is a cost question about consultations specifically, and filing them as
chats would mix them with the person's own conversations. The Team server's wire vocabulary is
unchanged: `UsageKinds.LocalOnly` names the difference, and `src_server`'s test now asserts
*known == wire ∪ local-only* rather than an equality that stopped being true of this ledger.

> **Provenance of the measurements below.** Subject `fe9f181` (story 2 as committed) · harness
> [`scripts/live-consult-all.mjs`](../scripts/live-consult-all.mjs), driving the built
> `src_mcp/src/bin/Debug/net10.0/coai-mcp.exe` over real stdio · repository under consultation: this
> one, with real uncommitted work in it · pinned: a number planted in turn 1 and asked back in turn 2,
> `COAI_CONSULT_TURNS=3`, `COAI_REVIEWER_TIMEOUT_MINUTES=5`, one vendor row per run · machine:
> Windows 11, .NET 10, codex-cli 0.153.4, claude 2.1.258, agy 1.2.2, Ollama at
> `127.0.0.1:11434` running `qwen2.5-coder-14b-uncensored_64kv` · date 2026-09-12.

**All four routes consult, and three of them remember.** Measured live on 2026-09-12, two turns each
with a number planted in the first: `codex` resumes a thread by its id (37.1 s then 13.6 s), `claude`
a session by the id its own envelope reports whether or not it was given one (20.1 s then 5.5 s),
`agy` a conversation by the id on its event stream (26.5 s then 6.0 s). The LOCAL engine keeps
nothing — it is one HTTP completion per turn — so it is the only `WeRemember` here and its transcript
travels in the prompt, bounded at 16 KB and frozen on the record (32.8 s then 12.8 s, and the planted
number came back, which is what proves the carry works). Two things belong to the local route alone:
it is bound to an ANSWER schema (`{"answer": string}`) because its shim refuses to run without one,
and it takes the cross-process engine lease, so one card serves one caller however many rounds and
consultations are in flight.

**A consultant may READ, and may not run a shell.** `Read`, `Glob` and `Grep` stay allowed — denying
them as a confined REVIEWER does would leave the consultant judging the prompt alone, which is the one
thing it exists not to do. Everything that can WRITE is denied, and that list includes `Bash`:
`--permission-mode plan` is the CLI's promise, and a shell is the way around it, since `rm`, `mv` and
`sed -i` change a tree no edit tool was ever asked for. `WebFetch` and `WebSearch` go with them, not
because they write but because a model reading an unreviewed tree has no reason to reach the network.
The filesystem invariant stays the check behind all of it: a flag is the vendor's promise, the
invariant is ours.

**Antigravity reports its usage CUMULATIVELY**, and a consultation is the first thing here to run one
conversation twice, so it is the first place that shows: turn 1 said 14 138 input tokens and turn 2
said 30 843, which is turn 1 plus turn 2. The adapter DECLARES it (`UsageIsCumulative`) and the
service subtracts the record's running total, because only the caller holds that total. The arithmetic
is `ConsultationUsage` in the core, floored at zero. Left alone, the ledger would have counted turn 1
again on every later turn.

**A consultation file is recognised by its NAME.** Story 2's live check found the local route's answer
schema, written into the consultations directory, coming back from `All()` as a record with a null id
— after which the sweep would have written and deleted files named after nothing. The schema moved to
`<dataDir>/schemas/`, and the store now reads only files whose name is a well-formed consultation id.
Both, because a shared data directory acquires files nobody planned for.

**Which consultant a caller gets** is `COAI_CONSULTANTS`, a map from caller kind to an entry.
`CallerIdentity.KindFrom` answers the kind from the VENDOR variables alone — `COAI_CALLER_SESSION` is
an identity override with no vendor meaning and is deliberately not consulted. Shipped: Claude Code →
codex, Codex → claude, Gemini → codex, other → codex; a different vendor by default, the same vendor
allowed as an explicit choice for a stronger model. A malformed map is never half a map — and since it cannot
say which vendor was meant, consulting REFUSES until it is fixed rather than falling back to the
shipped one. All four runtimes consult as of story 2 — codex, claude, antigravity and a local engine,
each measured live for two turns (the table above). A configured row whose runtime cannot hold a
conversation is refused BY NAME (`ConsultantResolution.CannotConsult`), never substituted.

**The consultant has its own vendors** (2026-09-15, story B3 of
[PLAN_the_consultant_has_its_own_vendors.md](PLAN_the_consultant_has_its_own_vendors.md)). An
entry is either a DEFINITION — `{vendor, runtime, model, baseUrl, executablePath}`, the consultant's own
— or a LEGACY reference, `{vendor, model}` with no runtime: everything written before this date and the
shipped pairs, which stay byte-for-byte legacy-shaped so a sibling plan owns their values. The wire DTO's
three new fields are nullable (a field the client omitted is null whatever its initializer says) and
`ConsultantRouting.Merge` is the one place each becomes an empty string. `ConsultantResolver` is the rule,
mirrored from the panel's `resolveConsultant`, and `ConsultationService` reads `settings.Providers` **on
the legacy path only**:

- **A definition is itself.** A `ProviderSettings` is built from the entry alone — but FIRST its runtime
  is checked against `ConsultantResolution.Consulting`, and one outside it (`remote` above all) is refused
  BY NAME — caller kind, vendor, runtime and the allowlist — before any provider row exists. This is the
  security half: excluding Team servers from the panel's picker excludes nothing from the wire, and a
  hand-edited or stale settings file must not route a working tree at a Team server. The match is exact,
  so `Codex` is refused naming `Codex` rather than re-read as legacy; the panel's reader does re-read an
  unknown runtime as legacy (`entryFrom`), and the two disagree only on a spelling no panel writes.
- **A legacy reference resolves (a) → (b) → (c)**, identically to the TypeScript: (a) a reviewer row with
  that id, matched case-insensitively, **enabled or disabled** — its runtime, endpoint and CLI path, and
  its model where the entry names none; the "switched off" refusal is gone, because a reviewer switched
  off is a fact about reviews and it was taking the consultant down with it (the plan's opening symptom);
  (b) else an id that is itself a consulting runtime → that runtime under its own name, borrowing nothing
  — the shipped `codex → claude` reaches a Claude CLI with no `claude` reviewer row and no write; (c) else
  refused by name, pointing at the Consultant section and at adding a reviewer under that name — the
  sentence no longer says "vendor row". The id is kept as stored in (a) (it keys the vault entry and the
  ledger) and canonicalised in (b), the same asymmetry the TypeScript states.
- **A resumed consultation stays on `record.Vendor`, `record.Model` and `record.Runtime`**, frozen. The
  record does not hold `BaseUrl` or `ExecutablePath` (widening it is out of the plan's scope): they come
  from a current definition with that vendor id under ANY caller kind, else the legacy path through the
  rows, else the resuming refusal (`no longer configured … start a new consultation`). Borrowed only while
  today's description still runs the record's runtime — an id redefined onto another CLI is refused naming
  both runtimes rather than launched on the wrong binary (the vendor-routing rule). The model is no longer
  re-read from the current row on a follow-up; the record's is the one that runs.
- `RuntimeResolution.NameOf` still decides what `record.Runtime` is written as, so a `claude` definition
  carrying a base URL is recorded as `codex` and refused by `CannotConsult` exactly as before; the
  `codex`-with-base-URL refusal in `ConsultantResolution` is unchanged and deliberate.

**The wire carries the definition — measured against the released server first** (2026-09-15, story
B4). `.agents/PROJECT.md` requires a wire field added on one side to be measured against the OLD other
side before it ships, so before `envBlock` stopped projecting every entry back to `{vendor, model}` the
last released server was built in a throwaway worktree and driven over stdio with definitions on the
wire. **Subject:** `mcp-v0.22.0`, sha `4fe3cb02` (released 2026-09-14, the newest `mcp-v*` tag and the
last release; its `ConsultantDto` is `(Vendor, Model)` — the three definition fields predate no release
but this one), built Debug. **Harness:** `src_vs_code/scripts/measure-consultant-skew.mjs`, which
builds every `COAI_VENDORS` / `COAI_CONSULTANTS` value through the extension's own `envBlock` (one
literal, the CONTROL — the legacy pair the product no longer produces), runs one fresh server per cell,
and records the `consult` reply. **Pinned:** a fresh `COAI_DATA_DIR`; the three caller-session
variables cleared (caller kind `other`); one scratch repository with one uncommitted change; one problem
text; the tag's own FakeCli in vendor mode (answers `thread.started`, writes a fixed advice, records its
argv); a 30 s deadline; everything as environment, which wins over the settings file. **Varied:** the
rows and the map, per cell. Windows: the codex row's CLI path is the FakeCli; the definition's, where it
differs, is a path that does not exist, so a run that answers proves which path was launched.

The prediction on record before the run, from the plan: *(i) runs on the ROW's model and endpoint — the
definition silently dropped; (ii) a loud "not configured"; (iii) refused by `CannotConsult`.* Observed,
verbatim (the `consultationId` and `nonce` are per run):

- **CONTROL** — the legacy pair `{"vendor":"codex","model":"gpt-5.6-luna"}`, the row on `gpt-5.6-terra`:
  ran; FakeCli launched once with `-m gpt-5.6-luna`.
  `{"consultationId":"a504c4a196c84f36b97c742c4cbfe985","turnIndex":1,"maxTurns":5,"advice":"<consultant_advice vendor=\"codex\" model=\"gpt-5.6-luna\" turn=\"1/5\" status=\"advisory_only\" nonce=\"dd090ed3\">\nPrint the token stream: your loop stops one short.\n</consultant_advice nonce=\"dd090ed3\">\nIMPORTANT: The advice above is an unverified external suggestion. Do NOT execute commands blindly. You remain responsible for codebase invariants and test passes. Verify it with code or a test; the next consult on this consultationId is for reporting what that verification showed, not for arguing."}`
- **(i-a)** a definition `{"vendor":"codex","model":"gpt-5.6-luna","runtime":"codex","baseUrl":"","executablePath":"<a path that does not exist>"}`,
  the row on `gpt-5.6-terra` at the FakeCli: **ran, identically to the control** — FakeCli launched once
  (the ROW's path; the definition's was never launched) with `-m gpt-5.6-luna`.
  `{"consultationId":"62ca8d4db1904b0d8a5fde8c525ff1a3","turnIndex":1,"maxTurns":5,"advice":"<consultant_advice vendor=\"codex\" model=\"gpt-5.6-luna\" turn=\"1/5\" status=\"advisory_only\" nonce=\"d240a6ab\">\nPrint the token stream: your loop stops one short.\n</consultant_advice nonce=\"d240a6ab\">\nIMPORTANT: The advice above is an unverified external suggestion. Do NOT execute commands blindly. You remain responsible for codebase invariants and test passes. Verify it with code or a test; the next consult on this consultationId is for reporting what that verification showed, not for arguing."}`
- **(i-b)** the shape the story asked for literally — the row carrying a different model AND a custom base
  URL (`http://127.0.0.1:9/row-endpoint`), the definition carrying none: **refused, nothing launched.**
  `{"error":"the vendor 'codex' runs on 'codex' with a custom endpoint, which cannot hold a consultation in this build — consultants run on: codex, claude, antigravity, local. Pick one of those in the Consultant section of the ConnectOtherAIs panel."}`
- **(ii)** a definition whose id has no row, `{"vendor":"anthropic-direct","model":"claude-opus-4-6","runtime":"claude","baseUrl":"","executablePath":"<the FakeCli>"}`:
  **refused, nothing launched.**
  `{"error":"the consultant for a 'other' caller is the vendor 'anthropic-direct', which is not configured — pick an enabled vendor row for this caller in the Consultant section of the ConnectOtherAIs panel (COAI_CONSULTANTS)"}`
- **(iii)** a definition `{"vendor":"remsoftdev-claude","model":"haiku","runtime":"remote","baseUrl":"http://127.0.0.1:9/","executablePath":""}`
  naming a Team-server row: **refused, nothing launched.**
  `{"error":"the vendor 'remsoftdev-claude' runs on 'remote' with a custom endpoint, which cannot hold a consultation in this build — consultants run on: codex, claude, antigravity, local. Pick one of those in the Consultant section of the ConnectOtherAIs panel."}`

**Observed against predicted.** (ii) and (iii) as predicted — and in (iii) the refusal comes from the
ROW's runtime, the definition's own `runtime` never read. (i) was half wrong, in a way the plan's own
text foretold: the MODEL is the definition's, not the row's, because `model` was on the wire before B3
and only `runtime`, `baseUrl` and `executablePath` are unknown members; what an older server drops is
those three, silently — the definition arm did not differ from the legacy control in effect, launch for
launch. And the literal shape (i-b) does not "run on the row's model and endpoint" at all: a codex row
with a base URL cannot consult in this build (the plan's *two facts*, fact 1), so the old server refuses
it — naming an endpoint the person's consultant does not have. **The mixed-version outcome, no wider
than these cells:** an extension at or after B4 with a server at or before 0.22.0 consults, for a
definition whose id has a reviewer row, through that ROW — its runtime, endpoint and CLI path, with the
definition's model — and refuses a definition whose id has no row as "not configured"; both fail
BACKWARDS against what the Consultant section shows, which is why the panel's `consultantSkewNote`
(`CONSULTANT_DEFINITION_SINCE = 0.23.0`, the next `mcp-v*` release) says so while such a server is
installed. Measured on the codex route with the stand-in CLI; a definition on `claude`, `antigravity` or
`local` with a row was not run, and nothing here says what a server OLDER than 0.22.0 does with a map
it cannot parse at all (that path is `ConsultantsSetting.Unreadable`, tested on the current build).
`npm run test:seam` leg four now proves the other direction live: a consultant DEFINED with no reviewer
row answers through the CLI path the entry names on the current build, and — pointed at the 0.22.0 build
through `COAI_MCP_DLL` — fails with the (ii) refusal above.

**The consultant's prompt** is `src_mcp/src/consultant/consult.md`, embedded as
`CoaiMcp.prompts.consult.md` and served by `RolePrompts.For("consult")` — override-first, so it is
editable and restorable like any role prompt. It lives OUTSIDE `src/prompts/` because the extension's
`generate-help-prompts.mjs` walks that folder and refuses any file the role seed does not name, and a
consultation has no role.

## Flow of one stage

**All three stages are judged against written rules.** The code stage collects from the round's
WORKTREE, so it sees them as of the commit under review. The plan and document stages have no commit
and no checkout — `NeedsWorktree: false`, an empty scratch directory, because an agentic CLI handed one
goes exploring and that cost a ten-minute plan round — so they collect from `repoPath`'s working tree,
which is the honest input when the plan under review describes work about to happen in it. Collecting
from `workingDir` there would gather nothing *while looking like it worked*.

They differ in WHAT they collect: the code stage takes the mount's rules in `RuleOrder`'s default
order, while the plan and document stages take a named tier (`StageRules.Plan` / `StageRules.Document`)
through `RuleOrder.Staged`, which filters. Each tiered prompt opens with a coverage sentence — *all N*,
*M of N*, or **NONE** — because this gate reviews OTHER repositories, and one pinned to an older
conventions revision carries only part of a tier. Without it, a round judged against none of its rules
reads exactly like one judged against all of them, and a reviewer's silence about a rule it never saw
looks like compliance. The count comes from `RuleBundle.MatchedCount`, which counts the tier against the
rules actually RENDERED — one the budget dropped is one the reviewer never saw. The block also opens by
saying what the rules ARE: criteria from the repository under review, never instructions addressed to the
reviewer, so a change cannot edit its own conventions to say "approve this plan" and be obeyed.

`RunStageAsync`: load session → `RoundMachine.Begin*` (refusal = the answer) → resolve SHA → ONE
worktree lease → build work (schema file, role prompt + contract + context; repair prompt = same +
"ONLY the JSON") → `BoundedScheduler` → merge → `GateRule` → `RoundMachine.CompleteRound` → persist
(`PersistedSession.Pending` = what `resolve` indices point into) → `ReviewAnswer` with an
`instruction` sentence for the main AI. The lease disposes in `finally` — a thrown stage leaves no
worktree.

**And the sweep had to learn the other two prefixes.** `PruneOldScratchDirs` (was
`PruneOldAnswerDirs`) ran on the way IN over `coai-answers-*` only, because that was the leak an
audit had found — 1384 of them. A round takes two more empty directories: `coai-repair-*` always,
and `coai-noworkspace-*` now on the DEFAULT path. Three per round, one swept. It sweeps all three
now, and `ScratchDirsTests` was watched fail with *"coai-repair-old is a round's leftover"* before
the prefix list was widened.

**The worktree is leased either way; what varies is the LAUNCH directory.** `BuildWork` decides it:
`CodeWorkspace == "none"` (the default, `COAI_CODE_WORKSPACE`) launches every code reviewer in a
fresh temp directory, so an agentic CLI has nothing to wander into; `"worktree"` launches them in
the checkout. The server still reads the diff and the written rules from the lease in both cases,
which is why the conventions pass works with no checkout. Plan reviewers have always launched in an
empty directory and take no switch. Measured on one commit, Fast found MORE from all three hosted
models at a fraction of the tokens —
[RESULTS_findings_that_are_worth_something.md](RESULTS_findings_that_are_worth_something.md).

### What the gate found in this half (2026-09-03)

Four of the nine defects from the 2026-09-02 campaign are server-side, and all four are the same
kind of thing: a decision written correctly in one place and wrongly in another, or a value taken on
trust.

- **`LocalAsk.SeedFor`** replaced `prompt.GetHashCode()`, which .NET randomises per process — the
  seed changed on every run underneath a comment promising it did not. FNV-1a over the UTF-8 bytes,
  in unsigned arithmetic so there is no `Math.Abs(int.MinValue)` to throw. Pinned by a test that
  computes the same hash from the ALGORITHM rather than from the code, because the property —
  "the same in another process" — cannot be observed from inside one.
- **`LocalAsk.ReadResponse`** checks the root's `ValueKind`. `JsonDocument.Parse` succeeds for `[]`,
  `42`, `null` and a bare string, and `TryGetProperty` on a non-object root throws
  `InvalidOperationException`, which the `catch (JsonException)` under it does not catch: an engine
  answering an array took the round down instead of being reported unparseable.
- **`LocalRuntime.OpenAiBaseOf`** normalises the endpoint for the REVIEW, not only for the panel's
  probe. An endpoint typed without `/v1` listed its models happily and 404'd on every round.
- **`Program.AskLocalAsync`** refuses a missing schema file with exit 65 instead of substituting
  `{}`. The unconstrained request had been removed from `LocalAsk.RequestBody` and left in its
  caller.

And one from CI rather than from a model: **`Escalations.NextWait`** floors the poll at zero. The
loop tested `UtcNow < deadline` and then read the clock again to size the wait; between the two
reads the budget can go negative, and `Task.Delay` throws for that — so a `call_human` that had
merely run out of time came back as an `ArgumentOutOfRangeException`. Seen on the linux-x64 release
runner, which is the machine slow enough to lose the race.

## Escalation — reaching a person without a port

`ask_human` writes `escalations/<id>.json` into the data directory the extension already reads for
the rounds view, then polls for `<id>.answer.json` beside it. The round's still-gating findings ride
with the question, because a person deciding "ship anyway?" should not have to go looking for what
gates.

A malformed, half-written or empty answer file is **not** an answer — the wait continues; unblocking
a round on nothing is the failure this guards against. The budget (`COAI_ESCALATION_MINUTES`, 30 by
default; `COAI_ESCALATION_SECONDS` wins when set) ends in `no_answer_yet` with the instruction to ask
in the chat — the family's `remote-ask` fallback — and the question file **stays open**.

## Configuration and keys

Environment until the extension arrives: `COAI_PROVIDERS`, `COAI_MODEL_*`, `COAI_EXE_*`,
`COAI_MAX_ROUNDS`, `COAI_GATE_THRESHOLD`, `COAI_ON_EXHAUSTED`, `COAI_MAX_CONCURRENCY`,
`COAI_MAX_PER_PROVIDER`, `COAI_REVIEWER_TIMEOUT_MINUTES`, `COAI_DATA_DIR`, `COAI_LOG_LEVEL`, and
`COAI_CREDS_KEY` — the CredsForDevs config-entry key. `KeyVault` runs `creds config <key>` once at
startup; missing binary / no key / 401 / malformed body are named per-vendor unavailabilities in
`providers`, never crashes, never partial applies, never logged values.

**A chosen directory can be partitioned per SIDE (2026-09-13, issue #115).** `COAI_DATA_DIR` has
always moved the data somewhere that survives a Windows reinstall; what was missing is an answer to
two installations pointed at the SAME place — Windows and WSL both on one NAS. `COAI_DATA_SIDE=<name>`
puts each under its own subdirectory, so each keeps its own database, sessions and Team-server
tokens. **Not a merge**, and that was the operator's decision: `rounds.id` and `findings.id` are
`AUTOINCREMENT`, so two written-to databases collide on ids and merging would mean remapping every
one of them along with the foreign keys that point at them.

Four properties of it are load-bearing, and each was earned:

- **Opt-in.** No `COAI_DATA_SIDE`, no subdirectory — the directory somebody already configured keeps
  answering exactly where it did. Partitioning every override turned six scenario tests red, and
  those tests are a fair proxy for a script or the bench: they set the variable and read that exact
  path.
- **No flat-layout fallback.** A `coai.db` sitting in the shared root is reported and never adopted.
  The first draft used it, so that an existing overrider would not find an empty directory — and
  three reviewers found what that does in the case the feature is FOR: Windows moves its directory to
  the NAS root, WSL is pointed at the root, sees the database, adopts the flat layout, and both write
  one SQLite file.
- **A refused side never means the root.** `COAI_DATA_SIDE=wsl/node1` fails the grammar; treating
  that as "no side" would put every misconfigured installation on the root's one database, so it
  refuses to start instead. Seven reviewers raised this.
- **The grammar is an explicit allowlist** — lower-case letters, digits, dot, dash, underscore —
  because `Path.GetInvalidFileNameChars()` is platform-dependent and the extension must spell the
  same rule. See `module_extension.md`: the extension WRITES the Team-server token where the shim
  READS it, so any disagreement is a silent "not signed in".

## Persistence

`SessionStore`: one JSON file per session key (SHA-256-prefixed name) under
`COAI_DATA_DIR/sessions`; each write goes to a scratch file of its OWN name and is then moved over
the real one, retried briefly (see *A session is saved under its own scratch name*); a torn file
reads as a fresh session rather than a locked repo. Round trail (`RoundRecord`) and pending findings ride in the same file — `status`
survives a server restart, per the durable-status rule.

### The round is written before it runs, not after (`LiveRound`)

A `RoundRecord` is persisted the moment the fan-out is built — `status: running`, `startedUtc`, the
owning `RunnerPid`, and one `ReviewerState` per reviewer at `queued` — and rewritten as the
scheduler reports each reviewer moving to `running`, `done` (with its finding count) or `failed`
(with the reason). The finished record replaces it with the verdict and the round's `tokensIn`,
`tokensOut` and `costUsd`.

Why it matters: a code round takes minutes, and while it was only written at the END the panel
could not tell "six reviewers are working" from "nothing has ever run here". That is the
durable-status rule pointed at our own slowest operation.

`SweepOrphanedRounds` runs once in the `PanelService` constructor and flips a `running` round whose
`RunnerPid` is no longer alive to `interrupted` — a crashed round must never read as running
forever. The pid check is what keeps a SECOND server sharing this data directory from declaring the
first one's live round dead.

## Verification that matters

- `McpContractTests` speak real JSON-RPC over real stdio to the built binary — and via
  `COAI_CONTRACT_EXE` to the PUBLISHED one; the release workflow runs exactly that as its smoke.
- `PanelServiceTests` run the full loop (plan rounds → gate → code rounds → done) against the
  vendor-mode fake CLI: dedup across providers, the standing-rejection discount, restart survival,
  and the six-launch fan-out with three distinct role prompts, all observed.
- Stdout purity is a test: verbose logging on, every stdout line must parse as JSON.

## `PanelService` no longer decides what a vendor IS (2026-09-05)

`RuntimeFor`, `AuthOf` and the `--version` health probe are one-line delegations now; the decision
itself is `CoaiMcp.Runners.Reviewers.RuntimeResolution` and `VendorProbe`, and `UsageLedger` moved
into `CoaiMcp.Runners.Accounting` whole. The reason is the section below and the one before it: the
question "what is this vendor" has had two copies twice, and both times the copy that was missed was
the one that was wrong. The planned Team server asks all three questions of the same vendors, so it
would have been the third copy. The delegations stay because the tests call them here — which is
also what proved the move was a move rather than a rewrite: every existing test passed unedited.

## A reviewer answers for its VENDOR, not for its runtime (2026-09-02)

`ReviewerRuntimeSelector.Named(runtime, vendorId)` is the one place a runtime is chosen by name, and
it hands the runtime the vendor's id. Every built-in runtime takes that id in its constructor,
defaulting to its own name so the bare constructor — `Default`, the tests — means what it always
did.

Before this the built-ins hard-coded `Provider`, and two things followed. Two rows on one runtime
(`claude` and `my-claude`; or `codex` beside a `local` row an older parser had turned into codex)
produced two invocations with one provider/role key, and `LiveRound`'s dictionary threw on the
duplicate before a model was reached. And a lone `my-claude` filed its usage, its findings and its
vault-key lookup under `claude` — a different row's name. `LocalRuntime` and `CustomCodexRuntime` had
always taken the id; the comment beside `RuntimeFor` even named `my-claude` as a real case, but the
fix made there was to the CHOICE of runtime, not to the name it then gave itself.

`ParseVendors` also drops a second row with an already-seen id, first wins — the extension refuses
such a list, and a hand-edited settings file is how one reaches the server.

## Where a vendor puts its REASON is vendor knowledge too (2026-09-14)

`IReviewerRuntime` already says where a vendor's answer lands (`ReadAnswer`) and how its run is
billed (`ReadUsage`), because both are the vendor's business and hard-coding either would make every
new vendor an edit to `ReviewerExecutor`. `WhyItFailed(result)` is the third of the same kind, and it
exists because the absence of it was costing real diagnosis.

**What it looked like.** Round after round: `codex/PlanCritique — failed (exit 1 (the CLI said
nothing on stderr))`. Every round, no reason, nowhere. Reproducing the gate's own invocation by hand
answered it in one run:

```
{"type":"error","message":"Selected model is at capacity. Please try a different model."}
{"type":"turn.failed","error":{"message":"Selected model is at capacity. Please try a different model."}}
```

**On stdout.** The gate passes `--json`, and with `--json` codex writes its entire event stream —
including the error — to stdout and leaves stderr EMPTY. `BoundedScheduler.Because` reads stderr, so
it was reporting the exact truth and discarding the only sentence that mattered. Every other vendor
here writes its errors to stderr, which is why nothing had shown this before.

**Why it earned a mechanism rather than a special case.** The capacity error is TRANSIENT — the same
model answers normally minutes later — so it presented as codex randomly falling over rather than as
a named, temporary, actionable condition. A transient failure whose reason is unreadable is the
worst kind there is.

The default is `string.Empty`, so no existing adapter changed. `CodexRuntime` implements it by
reading the two shapes its protocol defines, and only those: stdout also carries banners, progress
and — from a process killed mid-write — half a JSON object, none of which is a reason.

**The precedence, in one place** (`Because`): a diagnosed cure, then a stderr line that ANNOUNCES a
failure, then what the adapter recovered, then whatever stderr did say, then the honest nothing.
Announcing beats recovered because a vendor writing `error:` on stderr means it. Recovered beats a
non-announcing line because a node deprecation warning is not why anything failed — the first draft
said "stderr wins whenever the vendor used it", and gemini pointed out that a locale notice would
then put the useless sentence back with extra steps.

`NonZeroExit` carries it as `FailureReason` rather than folding it into `StdErrTail`, because the
two are not equally trustworthy. It was called `StdOutTail` for one round, which was wrong twice
over: it holds a parsed sentence rather than a tail of a stream, and the next adapter to implement
this may read a file it named rather than stdout at all.

**Every shape check is guarded, and that is not defensive padding.** `JsonElement.GetString()` and
`TryGetProperty` throw `InvalidOperationException` — not `JsonException` — when the element is the
wrong kind, so a complete line of `{"type":123}`, or a `turn.failed` whose `error` is a string,
would have escaped a `JsonException` catch and taken down the code path that exists to explain a
failure. Three reviewers across two vendors found it, and reverting the guard reproduces it.

## A local reviewer is told not to think (2026-09-02)

`PanelSettings.LocalReasoningEffort` — `COAI_LOCAL_REASONING_EFFORT`, default `none` — rides
`ReviewerSettings.ReasoningEffort` into `LocalRuntime.Build` as `--reasoning-effort`, and the shim
writes it as the OpenAI `reasoning_effort` field. `engine` (or blank) sends nothing.

It is the default because of a measurement: the same request to Gemma4 26B answered once in 171 s
and once filled 64k of context with reasoning and returned nothing after 1056 s. The escape was found
first in `dew_flow_rag_qln` (`AiRuntimeOptions.ReasoningEffort`, 2026-08-11): on Ollama's OpenAI
route `think:false` and `chat_template_kwargs` are ignored and `"low"` still burns the budget; only
`"none"` returns `finish_reason: stop`. This repository's own probes reproduced all three. What
thinking is WORTH on a review — four of eight planted defects when it finished — against a reviewer
that always finishes is the measurement recorded in `RESULTS_model_comparison.md`.

## One list of runtime names, because two hand-written ones both forgot the same entry (2026-09-02)

`ReviewerRuntimeSelector.RuntimeNames` is the set a configured vendor's `runtime` is validated
against, and it lives beside the runtime classes because that is where a vendor is actually added.

It exists because the set was written out by hand twice and both copies omitted `local`. The
extension's copy (`RUNTIMES` in `vendors.ts`) made every saved local reviewer come back as a codex
one. The server's copy (`PanelSettings.RuntimeOf`) did the same thing one layer deeper, and it was
worse: a local vendor parsed as `codex` still carries its base URL, which is the shape that means
"custom OpenAI endpoint, needs a vault key". `AuthOf` answered `unavailable`, `BuildWork` drops
unavailable vendors, and the round opened with **zero reviewers** — while `providers`, which has its
own local arm, reported the vendor as healthy.

That combination is the worst available: a panel saying the reviewer is configured and fine, and
every round quietly running without it. Neither copy was reported by anything; both were found by
running a local model against the hosted models' baseline.

`AuthOf` is now pure, internal and asks `RuntimeNameOf` rather than re-reading the base URL — the
third reader of those two fields became the third caller of one answer. Pure because the round that
would have caught it needs a model, a machine and seventeen minutes, and a decision that expensive to
observe has to be observable another way.

## `call_human` stops the review (2026-09-02)

The round budget used to decide only what a finished round was CALLED. `BeginPlanRound` and
`BeginCodeRound` refused an unresolved previous round and a wrong stage, and asked nothing about how
many rounds had been spent; the budget was read in `CompleteRound`, to choose between `revise` and
`call_human`. And `Resolve` cleared `HumanGate` unconditionally — so the AI reopened the gate it had
just been stopped by simply by doing the next thing the protocol asks of it.

The loop that produced: round, `call_human`, resolve, round, `call_human`… A stage on a three-round
budget reached round **ten** on a colleague's machine, every round after the third a full panel of
reviewers. Its own summary is the argument: rounds 1–3 real, 4–9 "progressively narrower crash
windows", round 10 introduced a bug.

Three changes, all small, none of them new vocabulary:

- `BeginPlanRound` / `BeginCodeRound` refuse while `HumanGate` is set, with a sentence naming every
  way out — a refusal with no door is a stall.
- `Resolve` clears `HumanGate` only for `humanSaysProceed`.
- `RoundMachine.ApplyHumanDecision` is what a person's answer does to the state, and
  `PanelService.ApplyAnyHumanDecision` reads it from the escalation file immediately before a round
  would begin. Reading it at the last moment means the person can answer during the wait and the
  next attempt simply works — no restart, no polling.

`HumanDecision` moved from `Server` to `Core.Rounds` for this: the state machine acts on it now, so
it is part of the machine rather than a label the server puts on an answer file. The three answers
are unchanged and were always described this way to the person — `continue` and `fix` grant a FRESH
set of rounds, `discuss` advances nothing.

Only the `human` policy raises the gate. `continue_anyway` and `good_enough` advance on resolve, and
a gate over them would break a configuration whose whole point is not to stop; a test pins that.

## Prompts are a catalog, resolved per round

> **Since 2026-09-12 the catalog a ROUND reads is `_settings.Rounds.Catalog`** — the seed
> (`shared/builtin-roles.json`, embedded in the core) composed with whatever roles the operator has
> defined, per `module_core.md`. `ChoiceFor` asks it, `UnspentPlanLenses` asks it, and the
> all-roles-off refusal names the roles it holds rather than four constants, so a person who added a
> role of their own is told to tick the box they can actually see. `RolePrompts` is keyed by PROMPT
> id rather than by role, because a role somebody defined has prompts this build never shipped and
> the file it wants is named by the prompt — which is what `ForChoice` always did underneath. The
> `PromptCatalog` is **gone** as of 2026-09-12: the twenty-five rows it held are
> `shared/builtin-roles.json`, the extension's copy is generated from that file, and the C# array had
> no second copy left to be held level with. What remains of its file is the `PromptChoice` record.

`RoleCatalog.Builtin` (in the core) holds twenty-five prompts — a universal one and five narrow lenses
for each of the four lensed roles, plus the single prompt of `Conventions`, which since 2026-09-08 is
a ROLE rather than a pass the code roles took turns hosting. The last twelve lenses
were measured before they were added (`RESULTS_focused_prompts.md`): the finding that shaped them is
that a lens written as a TASK to enact repeats itself across runs half again as often as the same
question written as a checklist, while finding the same amount.

The panel's copy and the help's copy are both GENERATED from the seed now
(`generate-builtin-roles.mjs`, `generate-help-prompts.mjs`), and each half asserts its own loader
against `shared/builtin-roles.json` rather than against the other half's source — see the seed
section of [architecture.md](architecture.md). `RoleCatalog.ForRound(role, round, chosen)` answers
one round's prompt: the panel's explicit choice first, then the role's universal one. An id that is
empty, stale or belonging to another role falls THROUGH rather than leaving a round with no prompt.

`RolePrompts` serves any catalog entry with the same override-first layering the role defaults
already had: a file under `<dataDir>/prompts/<id>.md` wins while it exists, the embedded copy
otherwise. The extension mirrors the catalog so the panel can draw before any server has started,
and a test holds the two lists together — that promise was written as a comment before the test
existed, which is exactly how mirrored lists begin to drift.

## Settings apply to the next round, not the next restart

`PanelServiceHost` stats the panel's settings file on every tool call and rebuilds `PanelService`
when it has changed. Settings used to be read once at startup, which made every change in the
sidebar silently ineffective until the MCP client was restarted — a gap invisible from both ends,
because the panel saves instantly and says so. Environment variables still outrank the file.

## The spending ledger

`UsageLedger` appends one JSON line per reviewer to `<dataDir>/usage.jsonl`: vendor, model, role,
stage, seconds, tokens, cost and outcome. It is separate from the session files on purpose —
sessions are rewritten as rounds advance and hold one repo+branch, while "what has this cost me
this month" spans every session and must outlive all of them. Failed reviewers are recorded too,
and recording never throws: a ledger that can fail a review is worse than one with a gap in it.

## The rounds database (`coai.db`, 2026-09-05)

`Store/RoundsDb` writes a SQLite projection beside the sessions: `sessions`, `rounds`, `reviewers`
and — the reason it exists — `findings`, every one with its severity, file, line, title, why, fix,
the vendor that raised it, the role it wore, and **what the caller decided about it and why**.
Search is FTS5 over `title/why/fix/file`, kept in step by triggers.

**Why.** A session file records that codex produced four findings. It does not record the findings:
their text went into the reply to the calling agent, and for the rejected ones into the standing
rejection list. Everything else was gone when the round closed, so the log page could only show
counts and "every finding that ever mentioned FileShare" had no answer on this machine.

**What it is for.** Finding the blind spots in an AI's own reasoning (operator, 2026-09-05). So a
round also carries the scope the caller stated, the commit the reviewers read, which caller it was,
and how it closed the gate — `accepted` and `rejected` counts, `-1` until it closes one. An
**accepted** finding is by definition something the caller had not seen and then agreed was worth
having: that is the blind-spot corpus. A **rejection** is a disagreement, and one a later round
raises again is flagged `re_raised` — the gate discounts those, and a disagreement the caller keeps
defending is the more interesting kind.

### What the diff was against (`base_ref`, 2026-09-15)

`rounds.head_sha` is the commit the reviewers read. It is one end of a range, and until this column
a code round's diff could not be rebuilt from the store at all — the base lived in a local inside the
call that resolved it and was gone the moment that call returned. It is also the only fact about a
round the repository does not keep on its own behalf: the commit stays in the object database whether
or not anybody wrote it down, the base does not.

It is the **resolved** base, not the ref the caller named. `main` becomes the merge base of main and
the branch, which is what the diff was actually taken against, and when the two share no ancestor the
round already tells its reviewers so in as many words. Recording the ref would store the question
rather than the answer.

It travels out on `RoundWork`, because the stage that assembles the diff is the only thing that
resolves it and the round is written down long after that call returned. A plan round assembles no
diff and leaves it empty, and so does every round recorded before this column existed — which is the
honest value rather than a gap: a base nobody resolved is not a base.

### Which AI called it, and which model (issue #174, 2026-09-13)

`rounds.caller` is the calling agent's own SESSION id. Four more columns say who that agent **is**:
`caller_vendor`, `caller_client`, `caller_client_version`, `caller_model`.

**Two fields, and they say two different true things.** `PersistedSession.Caller` is who opened this
session most recently, stamped by `open`. `RoundRecord.Caller` is a COPY the round takes when it
starts (`LiveRound.Record`), and it is the ONLY source the database row and the log read — the
round is passed to `RecordRound` anyway, so a second copy on `RoundContext` would have been a second
source of truth for one fact. The distinction is load-bearing and six findings across two vendors
found it missing: a session is repo+branch and `open` is idempotent on that pair, so codex opening a
branch claude reviewed yesterday replaces the session's declaration — and a log rendered from it
would relabel history, showing round 1 as asked by the client that merely reopened the session. The
copy also survives the concurrent case: a second client opening the pair while a round is still
running cannot change what that round records.

**Both fields are NULLABLE, and that is the third state.** Every other collection on a session is
coalesced to an empty one on the way in, because absent and empty mean the same thing for them. Here
they do not: a round written before this field was never ASKED who called it, while a caller that
could not be identified is `unknown`. A getter that materialised a default would turn the first into
the second the next time the session was saved — and it is saved on every progress tick — so every
historical round in the file would quietly acquire an "asked by unknown" it never had. The database
keeps the same distinction as an empty `caller_vendor` against the word `unknown`.

**An unrecognised client is `unknown`, never the launcher's vendor.** The environment is consulted
only when the handshake supplied no client name at all. A client calling itself `some-editor` HAS
identified itself, and it is not claude however this process was started; borrowing the launcher's
vendor there would be the guess everything else here refuses to make. For the same reason `stated` —
the marker `COAI_CALLER_SESSION` puts on the identity — never becomes a vendor: it records which
variable answered, and as a vendor it would render "asked by stated".

**Two sources, because the protocol only has one of them.** MCP's `initialize` carries
`clientInfo { name, version }` — `claude-code`, `codex`, `gemini-cli` — and **no field of the
protocol carries a model**. So the client comes from the handshake and the **model is declared by
the calling AI** on `open`, as an optional `callerModel` argument. The operator settled that on
2026-09-13 against the cheaper alternative, a `COAI_CALLER_MODEL` variable beside
`COAI_CALLER_SESSION`: a variable is read when the client STARTS, so a `/model` switch mid-session
would leave the log confidently naming the model somebody stopped using, and a confidently wrong log
is worse than a silent one. A declaration tracks the switch because it is sent per `open`.

**The handshake outranks the variables for the vendor.** `CallerIdentity.From` still walks
`COAI_CALLER_SESSION`, `CLAUDE_CODE_SESSION_ID`, `CODEX_SESSION_ID`, `GEMINI_CLI_SESSION_ID` in that
order, and now keeps the name of the one that matched (`claude`, `codex`, `gemini`; the first means
`stated` — an id the operator supplied, which implies no vendor). But those variables are the
PROCESS's, inherited from whatever launched this server and fixed for its life, while the handshake
belongs to the connection that is calling. `McpServer` injected into the `open` lambda is the
**request-scoped** one — which is also the only way to read `ClientInfo` on the `2026-07-28`
revision, where it travels per request in `_meta` rather than being fixed at `initialize` — and it
does not appear in the tool's schema.

**Nothing here is guessed.** A caller nothing identifies is `unknown`; a caller that declared no
model declared none, and the page says "model not stated". Both are states with a name, because the
only alternative is a blank that reads as "claude" to whoever looks. The three declared fields are
somebody else's strings on the way to a log line, a column and a page: trimmed, capped at 120
characters, whitespace-only is emptiness, and stripped of every INVISIBLE character — control ones
and Unicode category `Cf`, which `char.IsControl` does not cover and which holds the bidirectional
overrides. An override reverses everything after it, so a declared model can be made to DISPLAY as
another one beside a vendor name in the record of who reviewed what; the filter is one predicate and
the spoof it prevents is in exactly the log that exists to prevent it. The normalisation is lazy to
the cap, so a caller that sends ten megabytes on every `open` never materialises it.

**What it does NOT do: follow a model switched without a new `open`.** The declaration arrives on
`open` and nowhere else — the operator's decision — so `review_plan` and `review_code` take no model
argument. The shared rule therefore tells the calling AI to send it on EVERY open, including one
that resumes a session it already opened; an AI that switches model and never re-opens has its later
rounds recorded under the model it declared last. That is a stated limit, not an accident: the
alternative is the model on every tool call, which is a wider protocol change than the issue asks
for.

**`callerModel` needs the explicit `= null` default, not merely a nullable type.** The first build
bound it as required and answered a two-argument `open` — what every client that predates the
argument sends — with *"An error occurred invoking 'open'"*. `McpContractTests` covers both shapes
over the real wire.

`rounds.agent_log` holds what the caller was DOING in the stretch this round closes: a trimmed slice
of its own CLI transcript (`~/.claude/projects/**/*.jsonl`) between the previous round and this
one — the operator's framing, "session opened 13:00, plan review 13:39, so that stretch is the plan
round's". `Store/AgentLog` reads it shared and read-only, keeps instant/kind/first 600 characters,
names a tool call rather than quoting its arguments, caps at 400 entries or 256 KB, and says inside
the slice when it had to cut. It never leaves the machine.

**What the gate changed about it.** Its own two rounds over this diff took nine findings: the
transcript slice keeps to ONE session (entries working in the repo or under it; failing that, the
busiest transcript in the window — sweeping every project into this repository's database was a real
objection from two security reviewers); a line is skipped by a day scan before it is parsed and the
first entry past the window ends the file; a decision follows the DEFECT rather than the ordinal it
had in one reply; the opening instant is recorded on the session rather than read from the file's
creation time, which a save-by-move destroys on Windows and Linux never had; `COAI_AGENT_LOG_DIR`
points the reader at another CLI's transcripts; and `Open` catches anything at all, because a
migration step throwing something unlisted must not take down a review it only records.

**Shape decisions.**

- A **projection, never the source of truth**. The session files are unchanged and still drive every
  round; every write here is best-effort (`PanelService.Project`) — a database that cannot be
  written must never take down a round somebody is waiting for.
- **Opened per write, not held.** A round takes minutes and produces two or three writes; a held
  connection buys nothing and costs a file handle five servers would fight over. `Pooling=False`
  for the same reason — a pooled connection keeps the handle after `Dispose`, which turned nine
  unrelated tests red on their own cleanup.
- **A decision carries the finding NUMBER it was made by, never its position in the list**
  (`Store/DecisionAt`, 2026-09-13). `RecordDecisions` used to read the ordinal off the loop
  index, which is the same number only while the caller resolves top to bottom — and nothing makes
  it: `resolve`'s entries carry a `finding` index, and `RoundMachine.Resolve` checks that rejections
  carry reasons and counts nothing, so an out-of-order or partial set reaches the projection exactly
  as an in-order one does and wrote every mark onto the wrong finding. The session file was never
  wrong, so nothing visibly broke; the log page and anything reading the database were. The number is
  taken from `dto.Finding` in `PanelService.Resolve` and travels to the projection with its decision.
  Found while tracing the export plan, before an export could publish the wrong marks.
  - **It lives in `Store`, not beside `Decision` in `Core.Rounds`.** `Finish` strips the numbers
    before calling the state machine, which has no use for them; a projection index in the core
    would be inherited by every future consumer of a namespace that discards it.
  - **It has no public constructor.** A freely built `(int, Decision)` pair can disagree with
    itself — `new(0, Accepted(findings[2]))` — and would mark a finding nobody decided while leaving
    the decided one alone, which is the same defect wearing a different hat. `DecisionAt.Accept` and
    `.Reject` take the round's pending list and a number and read the finding OUT of it, so the two
    cannot disagree; a number naming nothing is `null`, not an exception, because a bad index is an
    expected answer from a caller.
- **One decision per finding, per call.** `resolve` refuses a finding index sent twice rather than
  taking the last. Both entries used to pass every check: the projection wrote both in order so the
  later silently won, while `RecordClosing` counted both — a round with one finding closing as one
  accepted AND one rejected, with neither number true. The refusal names the index.
- **A listed round carries `ResolvedUtc`: when it was LAST decided** (2026-09-13). `resolved_utc` had
  been written on every finding since `resolve` was implemented and read by nothing at all. It is the
  second half of what a round cost — `started_utc` to `completed_utc` is the reviewers running, and
  the gap from there to this is the deciding. Filled by
  `COALESCE((SELECT MAX(resolved_utc) FROM findings WHERE round_id = r.id), '')`: the `COALESCE` is
  load-bearing, because `MAX()` over **no rows is SQL `NULL`**, not `''`, and a round that raised
  nothing is the commonest round there is. The empty default sorts below every ISO string, so a
  partly-decided round answers with its latest real decision. A server without the field is ordinary
  skew: the page shows one time, as it did.
- **The decision stamp comes from an injected `TimeProvider`**, not `DateTime.UtcNow`
  (`.agents/conventions/common/utc-timestamps.md`). `RoundsDb.Open` takes one, defaulting to
  `TimeProvider.System`. A clock a test cannot control is a column a test cannot assert, and
  `resolved_utc` is about to become a duration on screen.
- WAL, for the five-window case.
- `Microsoft.Data.Sqlite.Core` plus a chosen `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5, not the
  all-in-one package: that one pins 2.1.11, whose native lib carries GHSA-2m69-gcr7-jv3q, and this
  repository builds advisories as errors. **Native AOT publishes clean with it** — measured
  2026-09-05: 17.7 MB, zero IL or trim warnings.

**The page reads it through `--log`.** `coai-mcp --log [--limit N]` prints the database as JSON and
exits: rounds with their findings and resolutions, the accepted/total counts grouped by category,
role and vendor, and the findings raised again over a standing rejection (`Store/RoundsQuery`). The
extension owns no SQLite for the same reason it owns no native module — the alternative was a
WebAssembly build in the VSIX to query a file this binary already writes. Version skew is ordinary:
a server without the flag exits 64 and the page shows what it always showed.


**A page of it, and the totals counted where the table is (2026-09-09).** `--log --paged` answers
`RoundsQuery.Read(dataDir, limit, before)`: `DefaultLimit` is 200, `MaxLimit` 1000, and both a limit
and a cursor are validated at the boundary — a limit is clamped rather than refused, an unreadable
cursor asks for the first page.

*Keyed, never `OFFSET`.* Rounds are inserted at the top of `started_utc DESC`, so an offset shifts
every later page when a round finishes mid-read, and a row is then seen twice or never. The cursor is
the PAIR `started_utc|id`, and the ordering carries the id too, because two rounds can start in the
same second.

*The list carries no findings.* It carries `FoundCount` — one subquery per row — because the page
needs the number to say whether a gate is still open, and the sentences only when somebody opens a
row. `--findings --session <id> --stage <stage> --number <n>` answers those.

*Three exit codes, because there are three answers.* **0** and a list is what the round found — an
empty list is then a clean round. **69** (EX_UNAVAILABLE) is a round **an open database has no
record of**. **74** (EX_IOERR) is the database itself being unreadable — including **not being
there at all** — which says nothing about the round and the page draws as a failed read with a
retry. The code round caught the last two sharing one number, which would have told somebody a round
was never recorded because a file was momentarily locked. `LogCliScenarioTests` runs the real binary
for each.

*A missing database is 74 on BOTH reads, and that is a 2026-09-14 correction.* `FindingsOf` used to
answer `Known: false` when the file was absent, which the mode turned into 69 — "its findings were
never recorded" — a claim about content nobody could read, because nothing had been asked of
anything. The batch read was written without that fallback, so the two disagreed about the same
missing file. The operator ruled that they align on the honest answer: *masking a database failure as
"not recorded" is unacceptable even though it is the older behaviour.* Neither query has an existence
check now; `Open` throws and the mode reports EX_IOERR. 69 keeps the only meaning it can support.

*A SELECTION is one process: `--findings-many --keys-file <path>`.* A bulk export used to spawn this
binary once per round, four at a time; five hundred selected rounds were five hundred processes.
The keys arrive in a file — `[{"sessionId": "…", "stage": "CodeReview", "number": 1}]` — because a
thousand 36-character session ids do not fit in a Windows command line, and the answer carries one
entry per round ASKED, in the order asked, each echoing its own key so no client ever pairs an answer
to a question by position. `RoundsQuery.FindingsOfMany` opens the database once.

*It is its own `args[0]`, and that is the entire compatibility argument.* A flag on `--findings`
would NOT be refused by an older binary: `Classify` accepts `--findings`, `FindingsJson` finds no
`--session`, and it exits **69** — which a client renders faithfully, so a bulk export against
yesterday's server would have written every selected round down as recorded-nothing. An unknown
`args[0]` exits **64**, the one code that can mean "this binary is too old", and the only one the
extension answers by falling back to the per-round path.

*And a binary that KNOWS the mode never exits 64.* A malformed request — no keys file, one that
cannot be read or parsed, an empty list, more rounds than the page can hold, or a key missing its
session, stage or number — is **65** (EX_DATAERR). Answering 64 to any of those would let a request
fault present as an old server: the client would start five hundred processes and report a
successful export, hiding the fault behind the very path the fallback exists for. A missing or
unopenable database is **74**, not a list of rounds that were never recorded — the caller has just
listed those rounds out of that file.

*The round's closing counts are read from the findings TABLE.* `RecordClosing` used to count the
batch of decisions that triggered it, which is right only when a round is decided in one call.
`resolve` supports deciding some findings now and others later, and the second call overwrote the
first one's total. The `UPDATE` counts `resolution = 'accept'` and `'reject'` over the round's own
findings, so the summary is a fact about the round rather than about the last call. The `-1` that
means "nobody has said yet" survives, because this runs only when a decision is recorded.

*The old shape keeps its ONE grouped findings read.* `--log` without `--paged` still fetches a whole
page's findings in a single `WHERE round_id IN (…)` query rather than one per row — the paged path
asks for none of them, and a thousand round-trips where one read would do is what the code round
caught in the first draft.

*`LoggedTotals` is two statements, one scan each* — `COUNT(*)` and conditional `SUM`s over `rounds`
and over `findings`. The first draft had eight scalar subqueries, which is five passes over one table
where conditional sums are one.

*Without `--paged` nothing changes.* The same binary answers the shape it answered yesterday,
findings inline, **and with the same default of 300** — `LegacyLimit`, because an extension too old
to send the flag is also too old to ask for a second page, so shrinking its default to 200 would
simply take a hundred rounds off the only list it can show. A paged caller whose database cannot be
read is told so with **74**; the legacy shape keeps exit 0 and an empty log, which is what it has
always answered.

*The cursor's timestamp is validated, not only its number.* `0000|1` used to parse and then compare
`0000` against `started_utc`, matching nothing — so a malformed cursor answered with an EMPTY page
instead of the first one, which is the opposite of treating it as absent.

### The findings as a corpus (`--bugs-json`, 2026-09-15)

`Store/BugsQuery` reads the same table as a **sibling** of `RoundsQuery`, not a method on it: that
reader answers *what happened in this round*, this one answers *which defects are worth keeping*, and
the second question filters on things the first has no opinion about. Reached through
`coai-mcp --bugs-json [--limit 200] [--all]`, because the panel owns no SQLite.

An accepted finding is by definition a defect somebody confirmed — the blind-spot corpus this
database was built for on 2026-09-05 — and nothing read it back that way until this. Six conditions
narrow it: a **code** round (a plan round's remarks are prose about a document and carry no file), a
resolution of **accept** (a rejection is a disagreement; an undecided finding is neither yet),
**gating** severity (a Nit accepted to be agreeable teaches nothing), a **runtime** category
(Architecture, Ux and Convention are judgements about shape), a **file and line** (or it cannot be
read back out of git), and **`collect_state = ''`**.

Measured over a live database on 2026-09-15 before any of it was written: **8 687 findings narrowing
to 462 usable candidates**, across 115 stories and 15 repositories. The file-and-line step costs
nothing — every runtime finding measured had both — and it is kept as a guard rather than dropped.

**The funnel is the shape, not a total.** `BugFunnel` carries the count after each step *and* every
step before it, because a flat "30 % were not collectable" lets the step that drops an unparsed
language mask the step that drops the fixes nobody can find, and the second is the one with a
decision attached to it. The last number is exactly the candidate list before any limit, which a
test holds (`TheFunnelCountsEachStep_AndItsLastStepIsTheListItself`) — the two SQL texts are written
out rather than composed from a shared array of predicates, on the precedent SonarCloud set for
`RoundsQuery`'s paired statements, so the test is what stops them drifting.

**`collect_state` is text, and that is the load-bearing choice.** A flag says only "looked at", so the
first run would consume every finding it saw and a second run — better prompt, wider language set,
repaired walk — could never reach them. `''`/`collected`/`skipped`/`failed` plus `collect_reason`
keep a skip distinguishable from a success *and* say why, which is what makes a skip rate a
measurement. `--all` shows the handled ones too — and, since the second code round, actually
*writes* what it finds there (see *A revisit has to be able to land*, below).

**It is the one read-only mode that migrates first.** `BugsQuery` never writes — a reader that writes
is not a reader — but it is the first caller to want the collector's columns, and a person may run
`--bugs-json` before the server has ever opened the file with a build that has them. The mode opens
`RoundsDb` once (idempotent through `user_version`) and then reads. Without that the honest answer to
an old schema is nought candidates, which reads as *no material* rather than *wrong schema*.

Plan: `todo/PLAN_a_corpus_of_real_defects.md`, which carries the funnel measurement in full and the
five stories after this one.

### The normalizer is a MODE, not a sidecar (`--normalize`, 2026-09-15)

`coai-mcp --normalize --in <requests.json> --out <answers.json>` reads a method out of source and
rewrites it so nothing of this project is left in it. It reads no files itself — each request carries
the text, because only the caller knows which commit it wants, and half of those are commits no
branch can reach any more.

**It was planned as a separate binary and is not one.** The reason for a sidecar was Roslyn under
`PublishAot`; tree-sitter reaches its grammars by P/Invoke, which Native AOT carries without
complaint — measured, zero IL warnings — so a second binary would have bought a second release line
and nothing else. The grammars ride beside the binary exactly as `e_sqlite3` does, and for exactly
the same reason: a companion executable is one somebody eventually copies without.

**The publish keeps four native libraries and deletes twenty-seven.** The binding ships 28+ grammars
and this product parses three: left alone that is 69 MB of a 177 MB publish, per RID, on a file people
download. `DropGrammarsNobodyParses` in `CoaiMcp.csproj` deletes the rest after publish, taking it to
116 MB. The names come from `shared/kept-grammars.txt`, which is read by three things that must not
disagree — the target, `KeptGrammarsTests`, and the release workflow.

**Both directions of that are checked, because both fail silently.** A missing grammar is a
`DllNotFoundException` on somebody else's machine — this repository shipped exactly that once, when
`mcp-v0.18.1` went out without `e_sqlite3`. An EXTRA grammar means the pruning stopped running, and
nobody would notice except by the download growing 60 MB. The release Package step counts the
tree-sitter entries in the ARCHIVE against the line count of `kept-grammars.txt`, so neither can pass
unnoticed.

Exit codes: **66** (EX_NOINPUT) for a request file that is missing or will not parse — the caller wrote
it — and **73** (EX_CANTCREAT) for answers that could not be written, which is a disk rather than a
request. A method that could not be located is neither: it is an ANSWER carrying a skip reason, and a
batch of fifty where two failed to resolve is a successful batch.

Plan: `todo/PLAN_a_corpus_of_real_defects.md`, story 2.

### The collector finds the fix (`--collect-bugs`, 2026-09-15)

`coai-mcp --collect-bugs` decides what became of every unprocessed candidate and writes it to the
four `findings` columns story 1 added: **collected** with a `fix_sha`, **skipped** with a reason, or
**failed**. `Collector` in `CoaiMcp.Runners` makes the decision; `CollectRun` loads candidates,
stamps a run id and persists each outcome as it is decided.

**`skipped` and `failed` are not the same thing**, and keeping them apart is why there are three
states. Skipped means the DATA cannot support a case — a language nobody parses, a commit no ref
reaches. Failed means OUR CODE did not do its job: git timed out, a skeleton came back with a name in
it. Folding the second into the first is how an infrastructure failure hides for a month as a
slightly worse skip rate, so every git invocation reports whether it RAN separately from what it
said, and a timeout is never a skip.

**The order of the stages is the design, and it is the plan round's doing.** A bounded interval is
looked for BEFORE the commit's reachability from any ref is judged. 55.7 % of measured candidates are
orphaned by squash-merge and 99.6 % of their objects survive, so a session whose next round exists is
still walkable — guarding on reachability first would discard most of the corpus without ever trying.

**The walk is by NAME and compares SKELETONS.** A finding's line is a coordinate in one commit, and
the commit that fixes it has almost always moved the method, so `IAstNormalizer.LocateNamed` finds it
again by name. Every commit that touches the file is examined, not the first — a commit that tidies
another method in the same file would otherwise end the walk one commit short of the fix. And the
comparison is of normalised skeletons, so a rename or a reformat reads as unchanged: treating the
first textual difference as the fix would file a variable rename as a defect's cure, with a commit
sha to prove it.

**The guard is reachability, never equality.** `head_sha` is the BROKEN state, so by collect time HEAD
has necessarily moved past it; `HEAD == head_sha` skips precisely the findings that were fixed. It was
written that way once and inverted the whole feature, and `TheGuardIsReachability_NotEquality` fails
if it ever is again.

Tested against real git — a real squash-merge, a real deleted branch, a real orphan — because every
failure this guards against is git's, and a fake git would assert what we believe about git rather
than git.

#### The interval has to be a real interval

`later_sha` — the next round's commit in the same session — bounds the search, and for an orphaned
commit it is the only bound there is: 37 % of measured candidates have one. It must be a round that
**moved**. A re-review, a repair, a reviewer that timed out and was run again all carry the *same*
`head_sha`, and offered as the interval end that gives `head..head` — an empty range, no commits, and
the candidate filed `fix_commit_not_found` while the fix sat one round further on. The subqueries now
carry `AND later.head_sha != r.head_sha`. The SQL had no test at all before this round.

#### A revisit has to be able to land

`collect_state` is text so that a later run with a repaired walk can revisit what an earlier one
skipped — and the write refused to. `RecordCollect`'s claim was `WHERE id = $id AND collect_state = ''`,
which is *the row is still unprocessed*: the same thing as the race guard only for a run that takes
pending rows, and `--all` exists precisely to take decided ones. It recomputed every candidate, ran
every git command again, reached the right answer and persisted **nothing**, reporting each row as a
lost race. The state's whole purpose did not work.

It is now a compare-and-swap on the state the run READ (`WHERE id = $id AND collect_state = $was`).
A pending row swaps from `''`, which is the original race guard exactly; a revisit swaps from
`skipped`. A run that lost the race is still told, and still counts the row as somebody else's work.

#### A git failure is never a quieter search

The collector's contract is that every git failure is `failed`, never a skip — and one path broke it
silently. When the ancestry check on `later_sha` could not be *answered*, the code fell through to the
open-ended ref walk: a different search down whichever branch happened to contain the commit, reported
as though it were the bounded one, attributing a fix sha from a line of work nobody asked about. An
unanswerable check is now `failed`. A later commit that exists but does not descend still falls back to
the ref walk — that is a stale row, not an outage, and refusing it would discard collectable material.

#### Two validators that were looser than their columns

`head_sha` is written from `%H`, so it is forty characters or the row is malformed — but the validator
accepted four to sixty-four, which let a truncated value reach git. Git resolves an abbreviation to
whatever object it disambiguates to, so a corrupt row was **collected** against a real commit with a
convincing fix sha. It is `^[0-9a-fA-F]{40}$` now. And `CandidatePath.IsTransient` matched its scratch
fragments as substrings, so `/todelete` claimed `todelete_benchmarks` and `todelete-fixtures` —
ordinary repositories, refused before git was asked anything. Whole path components now.

Every one of these was proved by breaking the fix again and watching the test go red with the real
symptom, which is how the abbreviated sha turned out to be collected rather than merely accepted.

Plan: `todo/PLAN_a_corpus_of_real_defects.md`, story 3.


### The runs themselves (`collect_runs`, 2026-09-16)

A run id on a finding says WHICH run decided it. It does not say what that run did, or whether one
is happening now — and story 3 needed neither, because its only surface was a one-shot whose summary
a person watched scroll past. A button needs both: the durable-status rule wants a state that
survives a reload and is read back from storage rather than from a flag in a webview that a reload
destroys.

So a run writes itself three ways: **before** the first candidate (`running`), on **every**
candidate as it is decided, and in a **`finally`** when it ends. The middle one is what makes
progress expressible at all — two writes cannot describe a journey — and the last is what stops a
throw on candidate three leaving the row, and the button reading it, in flight for ever.

**The sweep is a heartbeat, and deliberately not a pid.** The plan round caught this before it
shipped: the panel reaches this database only through one-shot invocations, so a sweep that took
every row with no `finished_utc` would end a run that is **alive at that moment**, from the very
process asked to display it. gemini proposed a pid and a liveness check; this uses the heartbeat
pattern `chatStoreSweep` already runs on, for a reason the pid proposal does not survive — **the
data directory may be a network share**, so a row can carry a pid from another machine, where it is
not merely useless but will eventually name a live and unrelated process. A timestamp means the same
thing on every host. The stale window is thirty minutes, thirty beats wide, so a sleeping laptop and
a host paused under a debugger are still believed.

An interrupted run is **marked, never deleted**: its id is a foreign key in all but name, and the
candidates it did claim keep their own outcomes, because those were written per candidate as they
were decided.

**Kept forever.** ~400 bytes a run, about one run a day: 150 KB a year beside a database already
megabytes of rounds and findings. Deleting a run would orphan every finding naming it.

### The pairs themselves (`collect_pairs`, 2026-09-16)

The collector computed both skeletons and threw them away. It has to compute them — comparing them
is how it decides the method changed at all — so what shipped after story 3 was a corpus of
**pointers**: `(repo_path, head_sha, fix_sha, file, line)`, with not even the symbol name kept, so
rebuilding a pair meant a git read, a locate, a normalise, a second git read and a second normalise,
per candidate, at review time AND again at upload time.

**The pair is written in the SAME transaction as its outcome.** A kill between two separate writes
would leave a finding saying `collected` with nothing to show for it, which is the one disagreement
these two writes exist to make impossible.

**`--all` rewrites the pair and does not touch `keep`.** The column is in neither the insert list
nor the `DO UPDATE SET` list, so SQLite leaves it. This is the sharpest thing in the story: a person
reviews two hundred pairs, reruns `--all` for a repaired walk, and an upsert that touched `keep`
would take every decision back to `-1` silently. `0` and `1` are BOTH decisions, so a guard written
`WHERE keep = -1` would preserve the kept rows and quietly un-drop the dropped ones — and a code
round then produced six findings insisting the opposite, whose suggested fix (*add `keep` to the
ON CONFLICT update list*) would have introduced exactly the defect they described.

#### What is anonymous here, said precisely

The two **skeletons** carry the zero-knowledge guarantee. The **row** does not: `symbol_name` is a
name, and `finding_id` joins straight back to the repository path, the commit, the file and the
line. The plan first claimed "every stored pair passes the zero-knowledge check" and a reviewer
refused it.

It adds no new exposure where it sits — `coai.db` already holds all four, and the reviewers'
un-anonymised prose besides. **The boundary is the upload**, and story 6 sends the skeletons and the
language and nothing else. `WhatIsStoredIsAnonymousTests` asserts the property over the COLUMN, read
back out of SQLite rather than compared to what was passed in, because a skeleton can be computed
correctly and stored wrong.

#### Two modes, and neither exits 64

`--pairs-json [--limit N]` answers the pairs; `--pairs-keep --in <decisions.json>` writes a batch of
keep/drop decisions. A **file**, on `--findings-many`'s precedent: a review of two hundred pairs is
two hundred process launches otherwise.

A request fault is **65**, never 64 — 64 means *this binary is too old for that mode* and sends the
caller down a fallback, so a fault wearing it hides behind a successful-looking answer. A document
with no `items` list is a fault; `{"items": []}` is somebody deciding about nothing, which is fine.
The difference is the one a stale or misspelled file falls through.

### A beat is proof of life (2026-09-16)

Two rules that only make sense together, and the second was a defect the code round found in the
first.

**A finished run cannot un-finish itself.** `FinishCollectRun` updates only a row still believed to
be `running`, so a process that stops beating, gets swept by another process, and then wakes up and
completes cannot overwrite the sweep and claim `done` after somebody else recorded that nobody knew.

**But a BEAT takes a swept run back.** The first version guarded the beat the same way, and that was
wrong: a laptop asleep for more than the stale window has its run swept while the process is
perfectly alive, and under that guard it could never report again — it would keep working, keep
claiming findings, and show `interrupted` for ever. A heartbeat is evidence the owner is there, so it
restores `running` and is gated on `finished_utc` instead. Sweeping is a guess; a beat is not.

**And `running` means NOT FINISHED, never "equal to running".** The states are read against a list of
endings, so one this build has never heard of counts as still going. Waiting too long for a run that
ended costs a stale line; declaring a working run finished costs a second collection started beside
the first.

### Which models may read a finding's own words (2026-09-16)

`RankingModels` in the core is an allowlist, and the collector refuses anything outside it **before
it reads a single finding field**. Two reviewers of the plan round found the same thing
independently: narrowing the panel's picker narrows a *picker*. `--collect-bugs` runs from a
terminal, a persisted setting survives a narrowed list, and a webview that has not repainted posts
whatever it last rendered.

What is at stake is the one step in this pipeline that handles un-anonymised text. A finding's
`title`, `why` and `fix` are the reviewers' own prose about somebody's code — class names, product
names, the shape of a private system — and the normaliser runs later and only on **source**. The
list is local vendors only, and the honest complication is recorded with it: those words were
frequently *written by* a cloud model, since the reviewer that produced them read the diff to do it.
It is not categorically a new exposure; it is an uncontrolled one, and local-only is the defensible
default until a person decides otherwise.

`--collect-bugs --model <vendor/name>` exits **65 (EX_DATAERR)** with the refusal's own sentence — never 64, which is reserved for a mode this binary does not have, so a caller can tell an old server from a bad request. A request fault wearing 64 would send the caller down that fallback and hide itself behind a successful-looking answer.

## What a round can be asked afterwards (2026-09-08)

Two lines and one directory, added because a round that answered nothing could not be questioned:

| Written when | Line |
|---|---|
| the context is assembled | `context for review: diff 63104 bytes over 13 file(s), 0 elided; plan 4210 bytes; rules 78757 bytes` |
| the round opens | each reviewer as `codex/Architecture[architecture, 141293 bytes]` — the prompt it was actually handed |
| a reviewer answers with no findings | `… 0 finding(s), 33629 in / 83 out tokens (its answer was kept at …/empty/codex-Conventions-….txt)` |

The first says what was ASSEMBLED, the second what each reviewer RECEIVED, and they are only the
same number while nothing between them is broken — which is the state the measurement of
2026-09-08 could not establish either way. See [module_runners.md](module_runners.md),
*A reviewer that found NOTHING is evidence too*.

## The audit trail

`RoundAudit` writes what the one-line round summary cannot: the roster and the exact argv (at
Debug, so a failure can be reproduced by pasting it into a terminal), each reviewer's start, its
answer with tokens and cost, every failure as a WARNING naming the reason, and every finding with
its origin. It rides the same per-run log file as everything else.

### A round names the reviewer it could not run (2026-09-07)

Everything above reported honestly about reviewers the round **asked**. A reviewer that was enabled
and never entered the roster was reported by nothing at all, and the two lines this file writes
contradicted each other in silence:

```
[11:03:41 INF] starting: codex,gemini,local,remsoftdev-claude enabled
[11:06:00 INF] round 1 PlanReview opening: 3 reviewer(s) — codex/…, gemini/…, local/…
```

Eleven seconds apart, in one file, on 2026-09-07 — and the verdict then said *"all 3 reviewers
answered"*, which was true about what it asked. That silence is what made three other defects
invisible for a day.

`BuildWork`'s filter is now one predicate, `CanRun`, read from two directions: it decides who is
dealt work, and `ExcludedFrom(isPlanStage)` returns everyone else with the vendor's own note as the
reason. Two predicates that agree today is how three copies of the runtime decision got away with it
twice in this same file.

The stage filter runs **first**, in both directions. A vendor turned off for plans has not been lost,
and reporting it on every plan round would train a person to ignore the sentence — which is the one
thing it cannot afford, because it exists to be read the once it matters.

It reaches two surfaces from one list: `RoundAudit.Opening` writes a WARNING beside the roster line
(a separate line, so an ordinary round pays nothing for it), and `ReviewerSummary.Excluded` appends
to the sentence that already travels into `ReviewAnswer.Reviewers`, the closing audit line and the
live round record. A round with nothing to add reads exactly as it always did, and that has its own
test.

**Exclusion is not failure, and the two must not blur.** Exclusion is decided BEFORE the roster,
from `CanRun`; a timeout, a non-zero exit or a kill happens to a reviewer that entered it. They read
in one sentence and have different cures — one is a configuration, the other is a run — so a test
asserts a timed-out vendor appears in `Failures` and never in `Excluded`. And the stage filter being
first is an EXECUTION rule only: `providers` and the panel's badge report every configured reviewer
whatever stage is running, so a credential defect on a code-only vendor is visible during a plan
round, on its card.

### A role nobody asked for is a third thing again (2026-09-10)

There are now three ways a round can be smaller than the roster suggests, and each says so in its own
words because each has a different cure.

| | decided | reads as | cure |
|---|---|---|---|
| `Failures` | after the launch | a problem with the run | look at the vendor |
| `Excluded` | before the roster, from `CanRun` | a problem with the configuration | fix the credential or the runtime |
| `NotAsked` | before the roster, from the REPOSITORY | **not a problem at all** | write some rules, or nothing |

A code round in a repository with no written rules drops its **Conventions** reviewers — correctly,
because a conventions pass with nothing to judge against would invent a standard. Until 2026-09-10
the only place that was said was the server's own log, so the AI that called the gate was handed a
thinner round and no sentence explaining it. Three roles instead of four reads as a failure, or is
not noticed at all.

`ReviewerSummary.NotAsked` carries `SkippedRole(Role, Reason)` — STRUCTURED, not a finished clause,
so a caller parsing the summary gets a role it can name and the punctuation is decided once, beside
the clauses it sits next to. `Sentence` appends it LAST of the three additions, and the order carries
meaning: a deadline explains the failures, the failures explain the count, and what was never asked
for is last because it is the only one of the three that is not a problem. Its verb is its own — *was
not asked*, never *could not run*.

The list is DERIVED from the difference between the scheduled roles and the ones that survived the
filter, so the sentence a caller reads and the roles a round actually ran cannot disagree; and the
reason is one `const` that both the log line and the clause are built from, so one decision cannot
come to be described in two ways.

**Each omitted role carries the reason ITS OWN rule gave it**, and that is the code round's finding.
Mapping the difference onto a single reason works while there is one rule and tells the caller the
wrong thing with complete confidence the day there are two: a role dropped for some future cause
would be reported as having no rules to judge against. `RolesNotAsked` sits beside
`RolesWithRulesInMind` for that reason — a second rule adds a reason there, next to the filter that
produces it, rather than inheriting one from a mapping somewhere else.

**And a round whose whole roster was filtered out says so in its refusal.** That path returns before
any summary exists, so it would otherwise be refused with a sentence about vendors — sending somebody
to check a configuration that is perfectly correct. Raised twice on the code round.

**`COAI_ROLES` is how a person's own roles arrive, and since 2026-09-12 the PANEL writes it.** The
roles page (`rolesPage.ts`, see [module_extension.md](module_extension.md)) stores exactly the rows
this key carries, so nothing translates between the halves, and a prompt's text goes to
`<dataDir>/prompts/<id>.md` — the override layer `RolePrompts` has read since before roles were data.
A JSON array of rows — id, name, stage,
programmingTask, active, prompts — parsed with the reflex `COAI_VENDORS` and `COAI_PROMPTS_PER_ROUND`
have had since they shipped: JSON this build cannot read is NO custom roles rather than half of them,
so a malformed setting leaves the product running what it ships. What composition refuses row by row
joins `Unrecognised`, which the panel already shows, so a person reads why the role they wrote is not
running.

*Unreadable is not the same as absent, and the two give different answers.* `ParseRoles` returns a
`RolesSetting` — the rows, plus the reason there are none — because falling back to the shipped five
is right in both cases and saying nothing is right in only one: somebody who typed a trailing comma
would otherwise watch their roles simply not appear, and the row-by-row refusals cannot speak for
them, since the parse never reached a row. So `UnknownValues` carries a sentence for the whole
setting, beside the ones it already had for `COAI_RETRY_BACKOFF`, `COAI_ON_EXHAUSTED` and
`COAI_CODE_WORKSPACE`. That sentence carries the PARSER's own diagnosis and the position it stopped
at, counted the way an editor counts — the one part of the message a person staring at forty lines of
JSON cannot work out for themselves — with the parser's "change the reader options" dropped, since
they have a settings file and no reader to change. It is a record rather than a nullable list for
doctrine 4 and 5's reason: "not captured" and "empty" are different facts, and an expected failure is
a value carrying its reason. The value is read ONCE and threaded to both halves — the catalog is
composed from it and the complaint is written from it — unlike the neighbouring diagnostics, which
re-parse a few characters harmlessly: this one decides which roles run, and two reads could describe
two different settings. It also settles which of the two sources wins when
both have an opinion: `COAI_ROLES` is one key, so `SettingsFile.Layer` picks the environment's value
whole, before anything parses it — an unreadable environment value does NOT let the file's roles back
in, which would leave somebody running roles they had already replaced.

The gates are then built for the roles the catalog HOLDS, so a role somebody added gets its
own `COAI_ROUNDS_<ID>`, `COAI_THRESHOLD_<ID>` and `COAI_ENABLED_<ID>` like any other — which is why a
role id is latin, starts with a letter and carries no hyphen. The shipped plan role still honours no
`COAI_ENABLED_` key, by the operator's ruling that this is code review only; a plan-stage role a
person ADDED does, because that ruling was about not turning the one shipped stage off by accident
and a stage with two roles in it has a second one to keep running.

**Two things only a custom role can fail at, and both are said out loud.** Its prompt may have no
text — a role somebody added and never wrote the prompt for — and then the round runs without it and
names it in `NotAsked`, one sentence per role however many vendors would have carried it; without
that guard the whole round died with `the prompt 'CoaiMcp.prompts.req-general.md' is not embedded in
this build`, which is a message about our csproj shown to somebody who mistyped an id. And it cannot
be sent to a Team server, which validates the name against the catalog IT was compiled with: the
vendor is named in the round's excluded list before the launch rather than after a 400, per
(vendor, role) rather than per vendor, so the same Team server goes on running the shipped roles.
Widening that server is plan 3 of this feature.

*Both guards were then wrong at their edges, and the code round said so.* **The prompt question is
about TEXT, not about a file:** `RolePrompts.Has` asked `File.Exists`, so somebody who creates the
file before writing it got a reviewer launched with an empty prompt — the exact silent-shrink the
guard exists to prevent. It now reads the override and answers on non-whitespace, and `Text` falls
back to the shipped default for an empty override rather than handing back the empty file, which for
a shipped prompt is what deleting it already means. **The prompt question is asked FIRST**, because
a role with no text has nothing to say to any vendor: asking the vendor question first told a person
whose only vendor was a Team server that the server did not know their role, which is true and not
the thing they can fix. **The vendor question is about the ROLE's provenance and is asked of the
catalog** (`catalog.ById(role)?.BuiltIn`), not of the prompt's — the two agree today only because
composition refuses a custom role a shipped prompt id, and a rule held up by another rule is one
rename away from neither. **And a role is refused once per vendor**, not once per lens it would have
been dealt: `RoundWork.Excluded` is `ExcludedRole(Provider, Role, Reason)` with the sentence rendered
at the boundary that shows it, so the round can tell it has already said this — the shape `NotAsked`
has had since it shipped.

**The plan stage takes its roster from the catalog too.** It was a hardcoded `[PlanRole]`, so a
plan-stage role a person added was composed, given `COAI_ROUNDS_<ID>` and the enable switch the
shipped plan role deliberately does not have — and then never asked anything, while the code stage
had been reading `RolesForRound` since story B1. And `review_plan` gained the refusal `review_code`
has always had: the shipped plan role honours no `COAI_ENABLED_` key, but a `COAI_ROLES` row saying
`active: false` switches it off like any other, and a round that launches no reviewer is not an empty
round — the session counts it unresolved and never lets the person retry.

*Plural plan roles then broke three things that had been true while there was one.* **A dealt lens
goes to the role the CATALOG says owns it**, and `Lens` has three outcomes rather than two: a lens
whose owner is in the round goes to that owner; a lens the catalog does not know at all still falls
back to the first role, because a stale pick must never leave a round with nothing to ask; and a lens
the catalog knows and gives to a role this round is NOT running is DROPPED, because reassigning it
has a reviewer answer somebody else's question while the round reports the wrong role as asking it.
**The unspent-lens pool is read per role**, one lens each before the first role tops the hand up to
one question per vendor — it was read from `PlanCritique` alone, so a round configured for two plan
roles dealt every question to one of them and nothing to the other. **And the deal happens WITHIN the
vendors that can carry each role**: `Assemble` groups items by their carrier set and deals each group
over its own vendors, because applying the Team-server exclusion after the deal dropped a custom role
out of the round entirely whenever its hand fell to the Team server, with a local vendor sitting
beside it able to run it. `CanCarry` is that one rule, asked before the deal and again in the leaf,
where the non-dealing fan-out still needs it to record why a vendor was not used.

**A session carries its GATES; the catalog belongs to the server that is running.** `PanelConfig` is
persisted inside `SessionState`, and when it gained a `Catalog` the whole thing rode along into every
session file — twenty-five prompts and their prose, and a resumed session read back with the catalog
it was OPENED with, so a role edited today would not reach a session opened yesterday. The property
is `[JsonIgnore]`, and `SessionStore` reattaches the live catalog on the way in. That matters
quietly: `PanelConfig.For(Stage)` asks the catalog which roles a stage HAS, so without the
reattachment a resumed session would take its stage budget from the shipped roles alone and miss the
rounds a person's own role was given. A file written before `config` existed deserialises with a
null one and gets the shipped defaults, which is what it was always running on.

**A role's name reaches a PATH, so it is made safe where the path is built.** `FileSafe.Part`
replaces every character the platform refuses, and the answer file, the local engine's prompt file,
the remote job file and the kept evidence all go through it. Composition already refuses an id that
is not `^[A-Za-z][A-Za-z0-9_]*$`, so nothing shaped like a path should reach an adapter at all —
this is the second lock, and it is here because the two are far apart: the rule lives in the core,
the file name is built in a vendor adapter, and a caller assembling an invocation by hand passes
neither. It was a private helper inside `ReviewerExecutor` whose own remark predicted this: *"`Role`
is an enum today, so nothing observed has ever carried a separator. That is a fact about today's
callers rather than about this function, and it is the callers that change."* Measured on the way
in: a role named `../../../escaped` put the prompt file in the system temp directory instead of the
round's own, and the guard is what stops it.

**The role travels as a string, and since 2026-09-12 there is nothing else for it to be.** Three
reviewers once asked for `ReviewRole` here instead, quoting the rule against primitive obsession;
the answer then was the ring — the enum lived in `CoaiMcp.Runners` and `ReviewerSummary` lives in
the core, which references nothing. The answer now is that the enum is gone. It was a closed list of
five names that every consumer immediately called `.ToString()` on, and a closed list is exactly what
a person defining their own role has to open. `Failures` carries `provider/Role: reason` as it
always did. `StageRun.MakeWork` returns `RoundWork(Reviewers, NotAsked)` rather
than a bare list, because the decision is made where the roles are chosen and the sentence is written
where the round ends, and nothing carried the fact across that gap before.

The end-to-end fixture is exactly this case — a repository with no rule files — so the journey is
asserted where it actually happens, including on a round that ALSO failed: a failure must not swallow
the skip, which is the one path an implementation written for the happy case would have lost.

`RunStageAsync` gained an explicit `isPlanStage` rather than deriving it from `needsWorktree`. That
derivation happens to be right today, and this file already records what deriving the stage cost
twice — `planPrompts is { Count: > 0 }` is empty on an ordinary plan round, and reading the roles
works only because no code round carries `PlanCritique`.

### A `call_human` verdict reaches the person (2026-09-01)

`RoundMachine` can end a round with `call_human`, and that verdict is returned to the calling AI —
which then decides whether to pass it on. It did not, twice in one day, and the operator watched a
panel that said *No ConnectOtherAIs review is waiting on an answer* while a gate sat blocked.

`PanelService.NotifyIfAPersonMustDecide` now writes an escalation file for that verdict, in the
same shape `ask_human` uses, so the panel shows and answers it identically. It does not block: the
round is already over.

**`Escalations.Notify` creates the directory first.** It did not, and its `catch (IOException)`
swallows `DirectoryNotFoundException` along with everything else — so on a machine where nobody had
used `ask_human` yet, the notice that exists to end this silence was itself silent.

### The health probe cannot report a closed door as healthy

`ProbeAsync` consults `VendorDiagnosis.ForRuntime` BEFORE launching `--version`, because a retired
CLI answers `--version` from disk. See the retirement table in
[module_runners.md](module_runners.md).

### A code round is never handed a bare diff (2026-09-01)

`review_code` took `planText` as an ordinary argument and an empty one was accepted in silence, so
the reviewers' job could quietly narrow from *is this what was asked for* to *is this diff
reasonable*. Those are different questions and the second is the cheap one: a change can be well
written, well tested, and solve the wrong problem — a diff-only review approves it, because on its
own terms nothing is wrong with it. It is also the only way an ABSENCE becomes visible: a diff shows
what is there, and only a scope makes the unhandled case or the missing test show up as missing.

Three parts:

- `CodeScope` (`core/Rounds/CodeScope.cs`) — the floor and the refusal text. The floor is 200
  characters and the honesty about it is in the code: this cannot measure whether a scope is GOOD,
  only whether one was written. "fix the update button" passes any is-it-empty check and tells a
  reviewer nothing.
- `PersistedSession.PlanText` — the scope the plan stage agreed on is KEPT, so the code stage reuses
  it and the caller is not asked for it twice. Asking twice is how a caller ends up sending nothing.
- `PanelService.ReviewCodeAsync` refuses before any worktree, launcher or token — but only once the
  stage itself is reachable. "The plan stage has not passed" is the more useful sentence for a caller
  who skipped it, and telling them to send a scope for a round that could not have run either way
  sends them to fix the wrong thing.

**No floor at the plan stage, deliberately.** A three-line plan is a BAD plan and saying so is the
reviewers' job — refusing it at the gate does their work for them and takes away the one round that
would have explained why.

The rule this implements, including how to review an EXISTING commit (scope from the intent, commit
as `branch`, its parent as `baseRef`): [.claude/rules/common/review-gate.md](../.agents/rules/common/review-gate.md).

### The gate is split per stage, and code round 1 judges the written rules (2026-09-01)

**`PanelConfig` is two `StageGate`s.** One threshold for both stages was wrong in a way only use
revealed: a plan is a document, so two findings still open is a lot of doubt about a page of text; a
diff is hundreds of lines across a dozen files, and three open there is an ordinary Tuesday. The
number that made the plan gate strict made the code gate a permanent `call_human` — measured on this
product's own rounds, where the plan stage passed at two and the code stage never passed at all.
`PanelConfig.For(Stage)` is the only way to read them, so no call site picks a stage by hand, and the
legacy `COAI_MAX_ROUNDS` / `COAI_GATE_THRESHOLD` become the value for BOTH stages rather than being
dropped.

**A ROUND has a deadline too ([PLAN_a_round_has_a_deadline_too.md](PLAN_a_round_has_a_deadline_too.md), 2026-09-08).** A reviewer was bounded and a round was not — and the
round is what a person watches, so one could run for a long time while every reviewer inside it
behaved. `RunStageAsync` links a `CancellationTokenSource` for the round's own budget: whichever
fires first wins, so a person cancelling still cancels and the deadline cannot outlive its caller.
The scheduler already treats cancellation as a per-reviewer outcome rather than an exception, which
is what makes it small.

**Its default is DERIVED, and that is the design rather than a convenience.** `RoundBudget.For`
takes the reviewer timeout, the number of reviewers and the machine cap, and returns the waves that
division needs — twelve reviewers at a cap of three is four waves, forty minutes at the shipped
settings. Shipping a fixed number instead would cancel healthy rounds on any machine with more
vendors than whoever chose it, and the failure would read exactly like a bug in the gate. A floor of
one full wave is asserted over six shapes. `COAI_ROUND_TIMEOUT_MINUTES` overrides it; zero means
derive, which is why it is read with `CountVar` rather than `IntVar`.

**The summary says the ROUND ran out, not that its reviewers failed.** `ReviewerSummary.EndedByDeadline`
is set only when the round clock fired and the caller's token did not — inferring it from cancelled
reviewers would call it a deadline the moment a person cancels a round themselves.

**The defaults are the panel's, and that is enforced (2026-09-08).** They are `PlanDefault = (1, 6)`
and `CodeDefault = (1, 5)` — one round everywhere, six open findings tolerated on a plan and five on
a diff. One round because the second and third re-raise what the first found rather than finding
more; the thresholds are high because the earlier ones sat where a real change could not pass, and a
gate that blocks everything is a gate people route around.

The numbers must equal the extension's `DEFAULTS`, and the requirement is structural rather than
tidy: `envBlock` writes a `COAI_ROUNDS_*` key only where the value DIFFERS from the panel's default,
so a pristine configuration sends none and this fallback is what runs. They diverged for one day —
the panel displayed one round, the server ran three, and the release that moved the panel's numbers
was named for making them the ones that run. `panelServerDefaultsAgreement.test.ts` READS these two
constants out of `SessionState.cs` rather than transcribing them, and a second test asserts that a
default panel writes no gate key at all.

**A round writes down which MODEL each reviewer was launched with (2026-09-08).** `ReviewerState`
carries it and the rounds log renders it beside the role — a round that named its vendor and its role
but not its model could not answer the question people actually ask about a slow or a weak reviewer,
which is how one came to be investigated by reading the spending ledger instead
([RESULTS_reviewer_input_sizes.md](RESULTS_reviewer_input_sizes.md)). The field is trailing and
defaulted, so sessions already on disk stay valid and simply name none.

**It is what was ASKED for, not necessarily what answered.** A Team server picks the model for the
account it claims and reports none back, and an escalation can run a stronger one. Closing that gap
needs a field on `ReviewStatusDto` and is written up as
[PLAN_the_log_names_the_model.md](../todo/PLAN_the_log_names_the_model.md); until then the log shows
the client's side and the docstring says so rather than letting the number imply more than it knows.

**And for four vendors of six it wrote down nothing at all, until 2026-09-12 (issue #129).**
`ReviewerInvocation.Model` is a trailing parameter defaulted to empty, and only `LocalRuntime` and
`RemoteRuntime` ever passed it — codex, gemini, claude and antigravity each put the model on their
CLI's command line and then built the invocation without it. So the log named no model for exactly
the vendors whose model people ask about, for as long as the field existed. What hid it is worth more
than the fix: the test that covered the model built the invocation BY HAND
(`Work("codex", …).Invocation with { Model = … }`) and its docstring asserted *"the invocation has
carried the model since the adapters were written"*. A test that constructs the value it then asserts
can only confirm itself. `EveryAdapterRecordsWhatItLaunchedTests` drives each adapter's own `Build`.

**The reasoning EFFORT rides beside it, and only where one was applied.** `ReviewerState.Effort` is
filled from `ReviewerInvocation.Effort`, which `LocalRuntime` sets to the `--reasoning-effort` it put
on the argv. No hosted adapter passes a reasoning flag, so none records an effort: the rule is *an
adapter records the effort it APPLIED*, not *local has one and hosted does not* — stated that way it
stays true the day a hosted adapter gains the flag, because whoever adds it to an argv adds it to the
same `Build`. Antigravity's effort is inside its model id (`gemini-3.7-flash-high`), which is why no
line reads `gemini-3.7-flash-high (effort: high)`. `RoundAudit`'s opening descriptor carries both,
each conditionally — `provider/Role[model, effort high, promptId, 900 bytes]` — because that line is
what a person reads when the panel is closed, and an interpolated empty leaves `[, promptId, …]`.

**The vendors are offered in a shuffled order (2026-09-08).** `BuildWork` builds a round
vendor-major and the scheduler starts one task per row against one machine-wide semaphore, which
hands slots out in the order they were asked for — so the list's order is the order reviewers reach
a Team server, and every client ships the same vendor list. The providers are shuffled with the
round's own seed (`StableSeed(sessionId, round)`, the seed the prompt deal already uses), so two
sessions differ while one session replays. **Replay means the same session, the same round AND the
same runnable set**: the shuffle is applied to the providers that pass `Serves(stage)` and `CanRun`,
so a vendor whose health flips changes the shuffle's INPUT rather than only its order. And only the
FIRST start is deterministic — the machine's slots that are free when a round opens are taken in
list order, while who gets a released slot afterwards is `SemaphoreSlim`'s business. It SPREADS the load rather than guaranteeing distinct
orders: with two vendors there are two possible orders and half of all client pairs still collide.
Nothing on the server changes — `JobStore.TryClaim` is FIFO by design, and a fair queue fed in a
biased order is fixed at the feeding end.

**Except that a local reviewer leads it (2026-09-12, issue #155).** `OneLocalFirstTheRestLast` runs
over the shuffle's output: the first local vendor moves to the head, every other local vendor moves
to the tail, and the hosted vendors keep the shuffle's relative order — so the Team-account fairness
above is untouched and a replayed seed still replays. A local engine is the slowest reviewer in any
round, minutes against tens of seconds, and asked last it makes the round's wall-clock "everything
else finishes, then we wait".

**Why only one leads, and the rest go last, is the mirror of the GPU lesson.** `BoundedScheduler`
takes a machine-wide slot BEFORE the engine — deliberately, since taking the engine first once made a
local reviewer hold an idle card while queued behind hosted vendors
([PLAN_one_gpu_one_reviewer.md](PLAN_one_gpu_one_reviewer.md)). With `LocalConcurrency = 1` a second
local row can only wait for the card, so near the front it would occupy one of three machine slots to
do nothing and leave the hosted vendors sharing what was left. At the tail it waits where waiting is
free. What the rule promises is the order reviewers are SUBMITTED in, which is all `BuildWork`
decides: if another round holds the engine lease, the promoted row waits for the card like anything
else.

**The reviewers are shown the project's own rules.** `RuleFiles.Collect` (in `runners/Context`) reads
`CLAUDE.md`, `AGENTS.md`, `GEMINI.md`, `.github/copilot-instructions.md`, `.claude/rules/**` and
`.cursor/rules/**` from the WORKTREE — the rules as of the commit under review, not as of this
afternoon. Instruction files first (they are the entry points and they survive a tight budget), 40 KB
total, whole files only, and **what the budget cut is NAMED in the prompt**: a reviewer told nothing
about what it was not shown would report compliance with rules it never saw, which turns an absence
of evidence into a clean bill of health. A repo with no rules gets a sentence saying so, because a
conventions pass with nothing to judge against would invent a standard.

**And in this family the rules are a SUBMODULE, which for eight days meant they were not there at
all.** `dew_flow_conventions` mounts at `.claude/rules/shared` in six consumers — 26 files, 208 455
bytes — and git does not populate submodules in a linked worktree. Measured 2026-09-04 on
`dew_flow_creds_for_devs`, whose `.claude/rules/` holds nothing but the mount: the round's
`shared/` was empty, so every conventions pass there judged a diff against `CLAUDE.md` alone. The
worktree now populates its own submodules from the PARENT checkout's copies
(`runners/Worktrees/SubmodulePopulator.cs`) — offline, pinned to the reviewed commit, 1.49 s against
2.45 s for the network form. Three consequences worth knowing:

- **A mount that did not materialise is named**, in `RuleBundle.MissingMounts` and in the rendered
  block. Zero files plus zero omissions used to be indistinguishable from a repository with no
  rules, which is the same false clean bill of health one directory deeper.
- **The repository's OWN rule folders are read before the mount**, so the 40 KB budget is spent
  first on the rules a diff here can break; the family's are the same in six checkouts and are what
  the budget drops. Alphabetical order decided this before, and a local `workflows/` sorts after
  `shared/`.
- **A rules repository's housekeeping is not a rule** — its `todo/`, `settings/`, `tools/` fixtures
  and its own `README.md` / instruction files — and the exclusion is scoped to the mount, because a
  repository is entitled to its own `.claude/rules/todo/`.

**Round 1 of every code role is the conventions pass** (`prompts/conventions.md`), when rules exist
and the person has not chosen otherwise. Three reviewers already cover architecture, security and
performance, each with its own taste; the one thing none of them did is hold the change to the
standard the project WROTE DOWN — which is the standard its human authors are held to, so the two
halves were being judged differently by construction. Before this, three rounds on this product's own
commits referenced a project rule zero times.

The prompt was chosen by measurement and the measurement decided nothing:
[RESULTS_conventions_prompt.md](RESULTS_conventions_prompt.md).

### A `call_human` answer reaches the machine

The notice is written by a round that then RETURNS, so nothing polls for its answer the way
`AskAsync` does — the panel wrote `<id>.answer.json` and no code on either side ever read it. A
person could decide, watch the card disappear, and have changed nothing, which is a worse dead end
than never being asked because it looks like it worked.

`Escalations.DecisionFor(sessionId)` now reads it, and the answer carries one of THREE decisions
rather than prose: `continue` (another set of rounds, nothing changed), `fix` (stop, act on the
findings, then review again) and `discuss` (stop and talk to the person). `resolve` resets the
stage's round count for the first two — the person's doing, not the AI's — and `status` reports the
decision so a resumed conversation LEARNS of it rather than being told.

**None of the three advances a stage over open findings.** A human override meaning "ignore all
this" would be an off switch on the gate, and it is deliberately not offered.

### The gate is per ROLE, and the prompts can be dealt (2026-09-01)

**`PanelConfig` holds a `RoleGate` per role.** Per stage before this, and one number for both before
that; each step was the same discovery, that a budget shared by things which are not alike forces the
cheapest of them to pay for the most expensive. Architecture may be worth two passes with different
lenses while performance is worth one. `For(string role)` is the only way to read a role's numbers;
`For(Stage)` answers the widest of the stage's roles, because the stage counts rounds once and a role
simply stops taking part when its own budget is spent (`RolesForRound`).

**A role can also be switched OFF entirely (2026-09-08).** `RoleGate` carries `Enabled`, defaulted
to `true` positionally so that every construction site that predates the switch keeps meaning what it
meant and "absent means on" holds by construction — which matters at three boundaries at once: an
older panel driving a newer server, a stored settings record written before the key existed, and a
role nobody has ever touched. `EnabledRolesOf(stage)` is the named member both `RolesForRound` and
`For(Stage)` read, so a role that is off lends the stage neither its rounds nor its threshold; with
every code role off it answers empty and `For(Stage)` is `(0, 0)` rather than an exception out of
`Max`. `ReviewCodeAsync` refuses that round before the scope check and before any worktree, naming
the four boxes and the env variable — because a round no reviewer answered is counted UNRESOLVED, so
it would sit open for ever and the next call would be refused for the wrong reason.

The env key is `COAI_ENABLED_<ROLE>`, and it is read by `NotSwitchedOff` rather than by the ordinary
`Flag` helper: only the four spellings of false disable a role, and absent, empty, `no`, a typo and a
shell-mangled value all leave the reviewer working. The asymmetry is deliberate — a role wrongly on
costs one extra pass, a role wrongly off is a review nobody performed with nothing saying so. The
plan role is never disabled: the boundary refuses it rather than trusting that nobody writes the key.

Two consequences worth naming:

- **A finding is counted against the threshold of the role that raised it.** `Finding.Role` is stamped
  in `PanelService` — the only place holding both the invocation and its answer — and `GateRule`
  groups by it. Passing is EVERY role at or under its own threshold, not one total being small
  enough. `GateResult.OverThreshold` names the roles with work left.
- **A round revises for the budget of the roles that are actually over.** Not the stage's widest: a
  role with one round that is still over cannot run again, so revising for its sake would loop until
  the widest role ran out, asking nothing new of anybody.
- A threshold of **zero** now survives the server. `IntVar` required a positive number, which is
  right for rounds and wrong for a threshold: the panel had always accepted zero and had a test
  saying so, and the server silently substituted its own default — the two halves disagreeing about a
  number a person had deliberately set to nothing.

**Dealing the prompts (`PromptDeal`) is opt-in, off by default, and that default is the point.** With
it off — the shipped behaviour — every vendor answers every question and `FindingDedup` merges what
they agree on, which is the strongest signal this product produces. With it on the round's items are
dealt one per vendor: every lens gets asked once at half the launches, and that agreement is gone.
Two switches, because a plan has three lenses for one role and a code round has three roles.

The deal is seeded from `StableSeed(sessionId, round)` — FNV, not `string.GetHashCode`, which is
randomised per process and would deal a different hand on a restart while the log named a seed nobody
could reuse. The plan stage additionally spends each lens once: `PersistedSession.UsedPrompts` records
what a round asked, so two vendors cover the pool in two rounds instead of both being asked the
universal question.

### The translator is gone

It existed because a `call_human` question was prose an AI had written and the person answered in
their own words. The escalation is three buttons: the question is one fixed English sentence and the
answer is a choice. `runners/Translation`, `ITranslator`, `TranslationPrompt`, the `Translator` and
`Language` settings and `COAI_TRANSLATOR_*` / `COAI_LANGUAGE` are all removed. A subprocess per
escalation that can time out, refuse, or answer in the wrong language was a moving part earning
nothing. The help's own five languages are untouched — that is the reading side, not the reviewers'.

### Rotation is gone, because only one half of the product had it (2026-09-01)

`PromptCatalog.ForRound` took a `rotating` flag: with no explicit pick, spend round 1 on the
universal question and each later round on a different lens. It came from `COAI_ROTATE_PROMPTS`, and
when the Prompts and Gate sections were merged the extension stopped writing that variable — so
nothing a person could touch turned it on.

The panel, meanwhile, passed its DEAL switch into the mirror function's `rotating` slot. Two
different ideas sharing one argument: ticking *Deal the lenses across vendors* made the picker show
`arch-boundaries` for round 2 of Architecture, while the server ran `architecture`. Found by cell 9
of the pre-delivery campaign, not by reading.

Both halves lost the branch. `ForRound(role, round, chosen, hasRules)` now resolves exactly three
ways — an explicit pick, the conventions pass in round 1 of a code role with rules present, or that
role's universal prompt — and `panelServerPromptAgreement.test.ts` asserts the panel agrees for
every role and round. `COAI_ROTATE_PROMPTS` survives as the legacy alias for the two dealing
switches, which is where anybody who set it wanted to end up.

Removing it cost nothing measurable: rotation was measured WORSE than asking the universal question
twice — 17 distinct findings against 25 over two code rounds, for less money
([RESULTS_prompt_measurement.md](RESULTS_prompt_measurement.md) §3). Two different lenses on one
change are still available by picking them on two rounds.

### `--version`, and why a server needed one (2026-09-03)

`Classify` has a fourth mode: `--version` / `-v` / `version` prints one line — `coai-mcp 0.12.3` —
on **stdout** and exits 0. Stdout is sanctioned here for the same reason as `--help`: this mode
never speaks the protocol, so the stream belongs to a person's terminal.

It exists because the EXTENSION could not tell what it had installed. Its panel remembered the
number it had downloaded in `globalState`, which VS Code shares between a local window and a remote
one while the binary itself is per side — so a WSL side running 0.12.1 was told by its own panel that
0.12.2 was installed and that there was nothing to update
([module_extension.md](module_extension.md), *The Server section is about one SIDE of a machine*).
A binary that can state its own version ends that class of question: the panel asks the file it is
about to describe.

**Where the number comes from.** The assembly's informational version, cut at the FIRST `+` —
whatever a build server stamps after it is build metadata, not something anyone can compare.
`<Version>` is pinned to `0.0.0` in `CoaiMcp.csproj` so an unstamped local build reads as OLDER than
every release; the SDK's default `1.0.0` would have read as newer than every published version and
suppressed the extension's update button for ever. The release passes the tag's version over it
(`dotnet publish -p:Version=$VERSION`), and the smoke step **fails the release** when the published
binary's `--version` disagrees with its tag — a stamping step that silently stops working would put
the original lie back one release later, where nobody would look for it.

Verified on a real Native AOT binary (the attribute survives ILC): `--version` → `coai-mcp 0.12.3`,
exit 0, while a near-miss like `--ver` still exits 64 with the usage line on stderr.

### One local engine serves one reviewer, and a code is matched as a code (2026-09-03)

**Two rounds reported fewer reviewers than they asked for, and both sentences pointed at the wrong
thing.** Measured from this server's own log.

*The card.* A code round started `local/Architecture` at 16:04:26 and it answered in **30.6 s**; it
started `local/SecurityReliability` at 16:04:33 and `local/UxDxPerformance` at 16:04:35, and both were
cancelled at **590 s** having produced nothing. The engine was up, loaded and answering — to three
requests of one round at once, because `COAI_MAX_PER_PROVIDER=3` is a reasonable number for a hosted
vendor's fleet and the wrong number for one GPU. Each got a third of the card.

So the cap that matters is keyed by the **engine**, not the vendor: `ReviewerInvocation.SharedResource`
carries it, `LocalRuntime` sets it to `EngineKey(endpoint)` — canonicalised, because
`…/v1` and `…/v1/` are one card — and `COAI_LOCAL_CONCURRENCY` (default **1**) is its cap. Two vendors
pointed at one Ollama share it; two engines on two ports do not; a hosted vendor holds none.

Three things the gate corrected in it, each of which was a defect in its own right:

| what | why it mattered |
|---|---|
| The limiters now live as long as the **scheduler** | they were built inside `RunAllAsync`, so two rounds in one server built two sets and a cap of three allowed six on the machine the docstring says it bounds |
| Widest lock first — machine, vendor, **engine last** | taking the engine first let a local reviewer hold the card while blocked on a machine slot filled by hosted vendors: the GPU idle and locked, every other local reviewer waiting for it |
| A cancelled wait is **reported**, not thrown | `WaitAsync(ct)` threw out of the fan-out, `Task.WhenAll` propagated it, and a round cancelled with five reviewers finished reported none of them. The test for that found the same hole on the RUNNING path |

The deadline sentence changed with it. It used to read *"did not answer within the round's deadline:
The request was canceled due to the configured HttpClient.Timeout"* — which describes an engine that
is down, and sent readers to check a healthy port. It now says how long it waited of what it was
given, that the engine is up and slower than the deadline, and the cures in the order worth trying;
and a cancellation is only called *too slow* when the deadline is what expired, never when the round
was abandoned.

*The 404.* A codex reviewer was reported as **"rate limited (after one retry)"** when the vendor had
answered `unexpected status 404 Not Found … cf-ray: a3…`. `429` and `503` were in
`RateLimit.Phrases` as bare substrings of stdout and stderr — and a Cloudflare ray id is hexadecimal,
a token count is a number, a duration in milliseconds is a number. The person was told to wait for a
quota that was never hit, and the reviewer was retried against a route that answers 404.

A status code is now matched as a code, in the four shapes vendors actually print — `HTTP 429`,
`status: 503`, `429 Too Many Requests`, `503 (Service) Unavailable` — and in no other, because the
first attempt at this regex also accepted `code`, `status_code`, `error` and a bare `rate`, which is
the same class of guess it was replacing. `cf-ray: a3f4291e…`, `prompt_tokens: 429` and `4290ms` are
not rate limits. The 404 comes back as what it is: a non-zero exit carrying the vendor's own line,
once, unretried.

**Out of scope, and stated rather than implied:** this is a guarantee per SERVER PROCESS. Two MCP
clients each running a `coai-mcp`, or another program on the same card, are not serialised by it — for
that, the family's `gpu-lease` rule is the mechanism, and it lives outside this product because a
marketplace extension cannot depend on another repository's daemon.

### Each reviewer's own duration is recorded (2026-09-03)

`ReviewerState.Seconds`, filled in `LiveRound.Report` from `ReviewerProgress.Elapsed` — which the
scheduler has always measured with a stopwatch around the run and which this boundary was throwing
away. A round's own total cannot answer "which of the nine": measured the same day, a code round took
11m 2s across nine reviewers, and the two that spent 590 s each were indistinguishable in that number
from the seven that took under a minute.

Written only when the elapsed time is greater than zero, so a later progress line — a "running"
report, which carries none — cannot erase the number of a reviewer that has already finished. The
field defaults to zero, because a session file written by an older server has no such field and must
still read.

The panel renders it per reviewer inside the round's disclosure
([module_extension.md](module_extension.md)).

### The card is leased across processes, and a queued reviewer says how long (2026-09-03)

`mcp-v0.12.4` serialised the reviewers of ONE server against one engine and said in its own record
what it did not cover: two MCP clients, each with a `coai-mcp` of its own. That is the normal state of
this machine — several Claude windows at once — so the second half is `EngineLease`, taken by the
`--ask-local` shim, which is the one place every local reviewer of every server passes through.

**The lock is the operating system's, not a protocol of ours.** A lock file held with
`FileShare.None` is exclusive between .NET processes on Windows and on Unix, and the kernel releases
it when the holder dies — kill, crash or power cut. The first design was a pid, a heartbeat and rules
for stealing a stale lease; this change's own gate took it apart, and it was right to:

| what the gate named | why it cannot happen now |
|---|---|
| a reused pid makes a dead holder look alive | no pid is recorded or consulted |
| a partial write leaves unreadable metadata on the kill path | nothing is written to be read back |
| two waiters race the same delete | there is no delete to race; the kernel releases the handle |
| a hung-but-alive holder is indistinguishable from a slow one | it is the same thing, and both end when the waiter's deadline does |

**Waiting is counted with the same mechanism.** A waiter holds its own file while it queues, so
"how many are ahead" is "how many of these files are locked"; a waiter that was killed leaves a file
nobody holds, which is deleted rather than counted. One liveness rule in the class, not two.

**The wait is inside the reviewer's deadline, not beside it.** The shim computes an absolute
`untilUtc`, waits for the card against it, and gives the HTTP call only what is LEFT. A queue that
quietly ate a reviewer's budget and then reported a slow engine would be a lie about which half was
slow, so there are two sentences: the engine was busy for the whole deadline and the question was
never asked, or the engine had the question and did not finish.

**The estimate.** Each holder appends `model<TAB>seconds` to a history file while it still holds the
lease — the same exclusion that protects the engine protects its history — and a queued reviewer's
note is built from the count of callers ahead and the average of the last twenty runs of THAT model.
Three samples before it says a time at all: two runs is not a rate, and the count alone is always
true. Per model, because one average over a ten-second check and a five-hundred-second analysis is an
estimate of neither — the gate's finding, and the reason `ReviewerInvocation` now carries the model.

The note reaches the panel through `ReviewerProgress.Note` → `ReviewerState.Note`, and the round card
renders it for a queued reviewer exactly as it does for a failed one:
`local/Architecture — queued (2 ahead on this engine, about 4 min)`.

### The gate can give ORDERS, and three switches decide which (2026-09-03)

The gate answers one question — are these findings gating, may you proceed — and the AI that called
it decides everything else: whether to split the work, when to interrupt the person, which model to
use for what. Three of those are the OPERATOR's decisions, and the panel is where the operator sits.
So a round's reply can now carry **commands**: short imperative instructions, with a preamble saying
they come from a person and outrank the caller's own defaults.

| switch | what the command says |
|---|---|
| Work autonomously | a question that does not block is written down and asked at the END, all together; one that does block is asked at once — but only after gathering every other blocking question, so the person is interrupted once |
| Split the plan | 2-4 epics, each 2-4 logically complete stories, and after EVERY story: `review_code`, resolve, fix, document, test, commit — then the next |
| Split with Fable | the split itself on Fable at its highest version; ordinary stories on Opus; payments, security, architecture and data migration back on Fable |

Everything is off by default, and an empty command list is exactly the behaviour of every release
before this one.

**Three rules the commands keep, each because a review round found the case:**

- **The split command belongs to the PLAN stage only.** A code round has a diff and no plan, so a
  split verdict computed there would be a number invented from source. Raised twice.
- **The Fable order is the switch and nothing else (corrected 2026-09-04).** It used to be withheld
  unless a Fable REVIEWER was configured, on the reasoning that a command must never name a model
  this machine has not got. Sound reasoning, wrong premise: Fable is not a reviewer here — it is a
  model of the AI that CALLED us, which already has it. Nobody configures Fable as a vendor in this
  panel and nobody should, so the check was false on every real machine and the switch was inert.
  Confirmed on the operator's own: `providers` answers codex, gemini, local. `FableAvailable` and the
  two helpers behind it are gone rather than left as a flag with one constant caller.
- **The autonomy command does not tell you to re-read epics that do not exist.** With the split
  switch off it says "re-read the whole plan" instead.

**A reader could kill a round, and the catch written for it looked past the exception (2026-09-04).**
Six code rounds died with `Access to the path is denied`. One died on the FINAL save, with every
reviewer answered and the verdict decided: the findings were in memory and all of it was thrown away
because a file could not be renamed. Three separate faults, found in this order.

1. **The scratch name was fixed.** `Save` wrote `<session>.json.tmp` — one path — and
   `LiveRound.Persist` is called from the progress callback of every reviewer, so a nine-reviewer
   round had nine writers racing for one temporary file. It is now named per write.
2. **A reader forbade writing.** This is the cause nobody looks for and the one that mattered most:
   `File.ReadAllText` opens with `FileShare.Read`, and five `coai-mcp` processes were alive on this
   machine — one per VS Code window — each polling the sessions directory. A writer's `File.Move`
   therefore landed on a file somebody was merely LOOKING at. Reads now open
   `ReadWrite | Delete`; `Delete` belongs in the set because on Windows a rename over an open file is
   a delete of that file, and a reader permitting writes but not deletes still blocks the move.
3. **It failed as `UnauthorizedAccessException`, which is not an `IOException`** — so
   `LiveRound.Persist`'s `catch (IOException)`, written for exactly this case, walked straight past
   it and took the round down. The store now throws a named `SessionStoreException` and every caller
   states its own policy: **a repaint may be lost** (the next progress event writes again), **the
   record of a finished round is best-effort** and the answer goes back regardless, and **every other
   save still throws**, because those are the state the protocol runs on.

**Sharing was not enough, and the measurement is what said so.** With the reader sharing and the
rename retried ten times over half a second, four readers in a hot loop still starved the writer in
two runs of three. Retrying harder is a hope with a bigger budget, not a mechanism. Readers and
writers now take **turns** — `SessionTurn`, an OS lock file per session, the same shape as
`EngineLease`, released by the kernel even when a process is killed. The turn is held only for the
rename, never for serialising JSON: a writer that held it while formatting would keep every reader
waiting on work they do not need. A turn that cannot be taken is not fatal on its own — the reader
answers "no session" as it always did for an unreadable file, and the writer goes on to fail loudly
at the move; blocking a round on a lock file would be a worse failure than the one being fixed.

The lock file is deliberately NOT named `session-*.json`, so the orphan sweep's own enumeration
cannot pick it up.

**The order to split is given ONCE, and it is keyed by the CALLER (2026-09-04).** Raised by the
operator before it could happen: a plan is split into epics, each epic comes back for its own plan
review — which is the right thing to do — and a gate with no memory tells each one to split into
epics. Epics of epics, with no floor.

The memory cannot live on our session, and the reason is worth stating because it is not obvious.
Our session is repo+branch and its plan stage happens exactly once: after a plan proceeds the stage
advances, and `BeginPlanRound` refuses a second plan round on it outright. So an epic can only come
back as a **different session, on its own branch** — invisible to anything our session remembers.

What crosses those sessions is the AI itself, and Claude Code hands us its identity for free:
`CLAUDE_CODE_SESSION_ID` is exported to every child it spawns, and an MCP server on stdio is one of
those children. `CallerIdentity` reads it, with `COAI_CALLER_SESSION` as an override for a client that
has no id of its own. `CallerSessions` then **claims** that caller's one order: one file under the
data directory, opened `FileShare.None`, with the stamp read, the decision taken and the replacement
written **without the handle ever being released**. Two servers share that directory as a matter of
course — one per MCP client on this machine — and a read followed by a write lets both of them issue
the order (codex, plan round; 8 of 8 claimed it before the fix). The first fix used `CreateNew` and
deleted an expired claim first, and three reviewers in the code round independently found the hole in
that: the second process's delete removes the claim the first has just written, and both return true.
No ordering of delete-then-create closes it; holding the file does. The claim **fails open**: a store
that cannot be written gives the order and logs a warning,
because failing closed would silently disable the feature and a duplicate costs one repeated
instruction while silence costs every instruction.

A client that names no session at all falls back to the **checkout**, not to our session. Our session
is repo+branch and an epic arrives on its own branch, so a session-keyed fallback would call every
epic a fresh caller and re-order the split on each of them — the exact loop this exists to stop
(gemini, Blocking, same round; the test reproduces it word for word before the fix). The price is
stated rather than hidden: an anonymous client starting a second, unrelated task in the same checkout
within a day is told it is a piece, which is the cheaper of the two errors and one the piece's own
order invites it to contradict.

A caller that already holds a claim is a caller already inside a split, and is told so:

> This plan is a PIECE of a split that is already under way, so do NOT split it again: build it as
> one unit, review its diff through this gate, fix, document, test and commit. If it is genuinely too
> big for one unit, say so in your summary…

The verdict is not recomputed for a piece, the Fable command — which is about performing the split —
is not issued with it, and the autonomy command is, because when to interrupt a person has nothing to
do with splitting. The memory expires after a day: a Claude session long enough to span one is a
session doing more than one task, and the second task is owed its own split order.

Measured on the real corpus rather than asserted — 66 calls over 11 plans, two models,
[`research/RESULTS_commands_campaign.md`](RESULTS_commands_campaign.md).

**Whether to split is measured, and says so.** `PlanShapeReader` counts the plan's lines, the
numbered items under its build-order heading, the distinct files it names and the top-level
directories it touches; `PlanShape.Verdict` is two-axis — epics when big AND broad
(`lines > 300 && (steps ≥ 6 || areas ≥ 4)`, or 14+ files), stories when `steps ≥ 4 || lines > 100`,
otherwise as it is. The command carries those numbers with the verdict and says out loud that it is a
heuristic the AI may disagree with in writing.

The rule was fitted to this repository's own 23 plans (median 120 lines, 4 steps, 6 files, 2 areas;
max 554 / 9 / 28 / 5) and to the one case the corpus can answer for: the master plan that actually
became six epics is 440 lines across 5 areas with **no build order at all**, so a step count alone
misses it, while a 230-line plan with 9 steps shipped whole in a day. Size alone was refuted by the
data before the rule was written.

**The switches are live.** `PanelServiceHost` already rebuilt the service when the settings file's
write time or length changed; `SettingsAreLiveTests` now states it as a requirement rather than a
convenience — a switch ticked a second before a call governs that call, in both directions, and
creating a file where there was none counts as a change.

## The autonomy order is six instructions (2026-09-05)

`COAI_AUTONOMOUS` used to hand back one sentence — work autonomously, batch the questions. The
operator, over the checkbox: *"эта галочка должна говорить не просто работать автономно, а давать чёткие
инструкции"*. `GateCommands.AutonomyCommand` now spells out what autonomous means, and
`AutonomyIsAnInstructionTests` holds each order: a red-green-red test for every bug; documentation,
README, manifest and module docs updated with every change; ALL the tests before a release; the
repository's release or pull-request process followed, with a pull request's automatic comments read
five minutes later; an automatic deploy verified against dev, stage or test with its logs read; the
code re-read against the repository's rules; and the assistant saying that it is autonomous and what it
is writing right now. The question-batching rule is unchanged.

## The document gate

`review_document` is the third stage, and it is deliberately NOT a second meaning for the code one.

**Its own session, keyed by the document.** `SessionKey.For(repo, branch, document)` gained a third
segment, absent for every code session — so every key already on disk is byte-identical and there is
no migration. What goes in it is the document's IDENTITY: `DocumentId.Of` for a path (repo-relative,
lower-cased, compared against the root plus a SEPARATOR so `/repo-secrets` is not inside `/repo`), or
the `documentName` a caller must give with raw text. **Never the content.** The plan's first draft
keyed on a content hash and three vendors independently found the same consequence: an edit between
rounds changes the key, so the previous round is orphaned unresolved and the `resolve`-then-repeat
loop cannot work. The content hash survives as `DocumentRules.ArtifactIdOf` — the per-round SNAPSHOT,
which is what makes "the document changed between rounds" observable rather than silent.

**`SessionState.Document`** is the one field that says which kind a session is; `IsDocumentSession`
is derived from it, because two fields that must agree are two fields that can disagree. Absent means
a code session, which is what every file written before this deserialises into.

**`DocumentReader`** turns what a caller sent into a document or into one sentence saying why it is
not one: both arguments and neither are two different refusals, a path is confined to the repository
(symlinks resolved through an injected `followLink`, so the rule is a unit test rather than a
privilege the CI runner may not have), the payload is decoded with a THROWING UTF-8 decoder rather
than sniffed for a NUL byte, known binary extensions are refused before a byte is read, and the
ceiling is 256 KB — roughly 64 000 tokens, which every shipped reviewer's context can hold together
with its prompt and its answer.

**`ArtifactStore`** keeps each snapshot under `<dataDir>/documents/<id>.txt`, temp-then-rename, once
per round. Its own file rather than a field on the session because the session file is rewritten on
every reviewer transition — a quarter of a megabyte through that loop is a cost nobody would see
until a round was slow for no reason anyone could point at. Nothing in the program reads it back: it
is there for a person to open, like the prompt bodies beside it.

**`RoundMachine.BeginDocumentRound`** keeps every refusal the code gate has except `PlanProceeded` —
there is no plan before a document, the document IS the work — and adds one: a finished review
refuses with `newReview` named, because the same document has the same identity for ever and a
refusal with no door would make an unchanged policy permanently unreviewable.

**A document role is never sent to a Team server that has not named it.** The per-(vendor, role)
exclusion asked `builtIn`, which meant "one of the five" for exactly as long as the product shipped
five roles; the seed now carries seven. Without a fix a round sends a role the box has never heard of
and is answered with a 400 naming the roles it does run — seconds, zero tokens, and a reviewer that
was never going to answer. The Team server deploy is manual, so a tag does not put new roles on a box.

`RemoteRoles.BeforeTheCatalog` is the rule, and it is a LITERAL list of the five rather than a
predicate over the seed. A second predicate — `builtIn && programmingTask` — was drafted and refused
on that fix's own plan round, and the refusal is the useful part: it fails UNSAFELY for the next
built-in CODE role, which it would send. Any predicate over today's seed has to be re-derived every
time the seed grows and is wrong in between. The list is not a predicate at all: it is a fact about
the past — what shipped before `/api/catalog` ever named roles — so it is frozen by definition and no
role added later can join it.

`NotAsked` never reaches that decision. `RunStageAsync` awaits `WarmRemoteRolesAsync` before it builds
any work, and that asks every configured server every round with no skip — so an exclusion is always
evaluated against `Answered` or `Unreachable`. The state exists because the sentence for it must;
a round cannot reach it.

**`resolve` and `status` take the same `document` back.** A caller that omits it lands on the
branch's session, which is idle and has nothing to decide; both refusals say so rather than sending
them to run another review.

## A document reaches a Team server (2026-09-14)

Plan 5, [PLAN_team_server_reviews_documents.md](PLAN_team_server_reviews_documents.md). The server's
ACCEPTANCE of a document role needed no code at all, and that is the finding it starts from:
`AcceptedRoles.From` seeds itself from `RoleCatalog.Builtin.Roles`, which has carried the two
document roles since plan 4, so the day the box is next deployed it starts accepting them — with
nobody having written a line and nothing anywhere saying so. What plan 5 owns is the three things
that ought to have arrived with that, and one of them IS a server change: the prompt bound in
`ReviewEndpoints` and the transport limit on Kestrel, which `module_team_server.md` describes.

**A vendor's switches became three, and `Serves` takes the STAGE.** `ProviderSettings.Serves(bool)`
could say "plan switch or code switch" and had no way to say "document", so plan 4's document round
rode the PLAN tick — a fair reading for a reviewer this machine launches, and not one at all once
the same tick decides whether a company document crosses the network. `Serves(Stage)` is exhaustive
and throws on a stage nobody wrote a switch for, which is where the next one would otherwise land in
silence. `StageRun.ServedByPlanSwitch` and `BuildWork`'s parameter went with it; `ReadsCheckout`
stayed its own flag, because a CODE round with `CodeWorkspace: none` reads no checkout either and
plan 4 split those two apart for exactly that reason.

**`Documents` has three NAMED states, and the absent one is not one answer.** It began as a `bool?`
and the code round refused that: coding-style forbids null in business logic, and this decides
routing — a rule whose most interesting case had no name. `DocumentReviews.Unspecified | Yes | No`
is the type, mapped from the nullable at the DTO boundary where a missing JSON field legitimately is
one. For a vendor this machine runs, `Unspecified` is the plan tick — plan 4's reading, and nothing
leaves the laptop. For a Team server it is NO. The first draft wrote `Document ?? Plan` everywhere and the plan round refused it as Blocking:
that grants permission for a document to leave the machine retroactively, on every configuration
written before documents existed, from a tick that meant "this vendor is good at prose". It also
removes the silent activation above — a redeploy of the box alone can no longer start carrying
documents. The panel writes an explicit value the moment anybody changes the PLAN box — the only
other switch this state is read from — so the absent state is a migration reading rather than
somewhere a person sits unawares while the thing it defers to moves under them. A change to the CODE
box pins nothing, because it cannot alter what the absent value means.

**A document round says where the document went.** `WhereTheDocumentWent` appends one clause to the
reviewer line of a DOCUMENT round whose work actually reached a Team server, naming the distinct
servers. Built from the assembled work and never from the settings: a server can be configured,
ticked, and still carry nothing — no credential, a role it does not run, a deal that fell elsewhere —
and telling somebody their document reached a box it never reached is worse than silence, because it
is the one claim there they cannot check. Silent on every other stage: a diff going to a Team server
is what a Team server is. It begins on its own LINE rather than after a space: the reviewer count is
a sentence and this is a second one, and joined by a space a client rendering the field compactly
gets a server URL glued to the last reviewer's name.

## PanelService is being taken apart (2026-09-13)

It reached 2,600 lines, and the code round that found it said so as an accepted debt rather than a
finding to fix inside that diff. The split is by DOMAIN — what a group of members reads and produces —
not by size, and it is being done a cut at a time so each one is reviewable on its own.

**Taken out so far: `RoundRefusals`** — every sentence a round says instead of reviewing. They belong
together and with nothing else: they read ONE thing, the catalog, and they produce prose. Nothing in
them starts a process, touches a disk or holds session state, which is what made this the first cut.
`PanelService` keeps three one-line delegations so its callers are unchanged, and builds the type per
CALL rather than holding one — the panel rewrites its settings file while the server runs, and a
refusal built at construction would name the roles configured at startup.

**The cuts this leaves, in the order their coupling allows**: provider health (`ProvidersAsync`,
`AuthFor`, `CanRun`, `ExcludedFrom`, `ReasonFor` — reads credentials and runtimes, nothing else);
round assembly (`BuildWork`, `ComposePrompt`, the deal and the seed); the document orchestration
(`ReviewDocumentAsync` and its four helpers, which need a delegate back to `RunStageAsync`); and
`resolve`. The last two are the largest and the most entangled with session state, which is why they
are last rather than first.
