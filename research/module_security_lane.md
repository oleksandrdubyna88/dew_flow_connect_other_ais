# module_security_lane — extra security reviewers inside code and feature rounds

The lane is off by default. `COAI_SECURITY_LANE` pairs existing reviewer rows with independently
named `redteam-*` prompts. An ordinary gate must still be configured. A successful lane answer
cannot compensate for ordinary reviewers that failed to answer.

```mermaid
flowchart LR
  settings[Validated pairings] --> roster[SecurityRoster]
  diff[Committed diff] --> signals[SecuritySignals]
  signals --> roster
  pinned[Pinned commit] --> sources[SecuritySources]
  signals --> sources
  sources --> context[SecurityContext: bounded fenced context]
  roster --> context
  context --> scheduler[Existing scheduler and runtime]
  scheduler --> limit[SecurityAnswerLimit: refuse malformed or oversized answers]
  limit --> evidence[SecurityEvidence: reproduction cap]
  evidence --> merge[Ordinary findings first]
  merge --> gate[Role and lane thresholds]
  gate --> history[Session and SQLite history]
```

`shared/security-lane.json` owns signal names and seed prompt metadata. The C# core embeds it;
the extension generates `securityLane.generated.ts` during build preparation. A trigger selects
a run; focus ranks its source. Detectors are bounded lexical heuristics, including removed lines;
a match is not proof of vulnerability. A non-matching preset becomes an incomplete/excluded pairing when any
diff exceeds the character cap or files exceed the count cap. Fully inspected non-matches are
ordinary skips. Positive matches still run with partial context; oversized diff bodies stay withheld.
SQL routing uses database API names and bounded query/statement shapes. Generic `database` and
`migration` prose alone does not select SQL; `DbConnection`, `DbCommand`, `DbContext`, Dapper and
`MigrationBuilder` still do. This remains lexical routing, not parsing or proof of a data flow.
Unknown triggers refuse their prompt; unknown focus is ignored with
a complaint. Invalid root configuration leaves the lane off. Unknown settings fields survive
extension edits so an older server can refuse them.

`SecuritySources` reuses the feature `SourceResolver`: at most 16 files and four changed regions
per file, with a 30-second collection deadline, all read from the pinned commit. `SecurityContext`
accounts for instructions, schema, framing and output reserve using a UTF-8/4 estimate. It records
omissions, applies the existing credential-file guards and redaction, and fences material with a
fresh nonce. Coverage remains partial; local input coverage remains unverified even when reported
token usage looks plausible. Neither the server nor the extension executes reproduction text.
For slices, production/config paths rank ahead of known documentation and test paths, then focus
signals rank within each tier. Both the sixteen-file reader and the context composer call the one
`SecuritySignals.Rank`. The reader collects source only for the pairings `SecurityRoster.Due` says
this round will ask — within the lane's round budget, free of configuration refusals, and on a
reviewer row that can run — so a spent budget or a broken pairing reads nothing. A slice whose
source window does not fit keeps its patch, listed as `(source omitted for budget; patch only)`.
Supporting material is retained when it fits and labelled in the payload/omissions;
the path hint never proves a file safe. The default local slice budget is 24000 tokens, independent
of the model's configured context window. Explicit source start/end labels surround the nonce fence;
all auditor instructions and the JSON schema stay above them.

Each provider/prompt pair has its own invocation, output files, repair and history identity.
Existing concurrency limits still apply. Security work is exempt from ordinary local stand-down;
it neither triggers that stand-down nor consumes its cloud-completion count. The outer deadline
includes configured lane work and engine queues, unless the operator supplied an explicit limit.

`SecurityEvidence` caps a major/blocking security finding at minor unless its reproduction contains
preconditions, steps, expected and actual results, within 8000 characters. This precedes merging.
`SecurityAnswerLimit` refuses answers exceeding 100 findings or 131072 evidence characters in aggregate.
The security schema requires `status: SECURE` with no findings, or `FINDINGS` with findings. Every
finding carries nonempty `trigger`, `mechanism` and `consequence` (8000 characters total; the
schema allows each field a third of that, because a schema cannot express a sum and must never
admit an answer the validator refuses), preserved
in history and per-pair sightings. Missing or inconsistent status/evidence refuses the answer.
The security schema does not offer prose `notes`; the operator's source-only and JSON-hygiene
block is embedded in every redteam prompt and precedes its output instructions.
The operator's evidence threshold and no-hedging block also precedes the output instructions.
Security severity uses `blocking` = CRITICAL, `major` = HIGH, and `minor` = MEDIUM;
the security schema and response validator exclude `nit` (Low/Info). Ordinary schemas keep it.
The local runtime already sends temperature zero and a seed derived from the complete prompt.
These settings do not prove
semantic correctness: a schema-valid finding still needs its claimed execution path checked.
Ordinary ownership wins a duplicate; stronger severity survives, with per-pair sightings retained.
Schema step 18 stores this evidence in `findings.security_evidence` (step 17 is the question
consultant's; the preview build that numbered the evidence 17 is reconciled on open by
`SecurityPreviewFork`). Finding queries use literal
SQL and read columns by name; a missing optional evidence column in an older database yields no
security projection. A damaged stored projection does not prevent reading the round and does not
pass for "no evidence" either: it reads as `SecurityFindingDetails.Unreadable` with the reason,
which the rounds log shows where the evidence would have been. Ordinary findings with no security evidence keep an empty
column and no evidence disclosure in the extension.
Persisted findings written before the lane have neither `alsoSeenBy` nor `capReason`.
Their owning properties normalize the source generator's missing-field defaults to an empty
array/string, so resuming and saving old pending findings or rejections cannot crash the round.
Existing nonempty sightings and cap reasons survive the same serialization path.
Context composition separates instructions, bounded file selection and fencing; a typed budget
carries input/output limits and the excluded-file count. These refactors retain the existing
caps, ordering, omissions and evidence protocol. Schema keys and calibration serializer options
are shared constants. Generated extension catalog data is excluded only from Sonar's duplication
metric; its complete runtime value remains checked against the shared JSON by `securityLane.test.ts`.

The lane has its own round budget and threshold, read under `lane:security`, and it never
extends the ordinary roles' budget: `PanelConfig.For(Stage)` is the ordinary roles alone. A code
round past every ordinary role's budget runs only for the lane. A feature second round uses the
lane's budget only when every blocking finding is the lane's and no ordinary reviewer failed; an
ordinary reviewer's failure or blocking finding is judged on the ordinary budget, so a feature
stage whose roles have one round answers `call_human` rather than buying a round only the lane
could answer. A round only the lane's budget admitted — past every ordinary budget and still within
the lane's own; a round past both was admitted by nobody and keeps the ordinary refusal — whose lane
then has no work (its trigger is
gone, it was switched off, composition refused), is not refused on every call: it runs empty and
completes as a round nobody answered, recorded with `lane:security was not asked: …`, for a
person to decide. In a round with lane work the ordinary decision summary keeps who could not run
(`Excluded`, `NotAsked`) and whether the deadline ended it. A pairing whose own configuration is
broken is `Excluded` with its reason — "configured pairings could not run" — never a skip that
reads as "no pairing was due". Missing, failed and unverified local answers
are named in the reply and persisted round summary; failed/unverified or excluded work also
raises the existing durable notice. This optional lane does not make a clean ordinary round fail
just because the lane did not answer. A clean response is never described as security coverage
when no complete lane answer exists.

## Prompts and settings

The twelve operator-authored presets are Git files under `src_mcp/src/prompts/`, listed with their
conditions in [security_prompt_catalog.md](security_prompt_catalog.md). Authorization, SQL, concurrency,
auth tokens, SSRF, webhooks, files, commands, deserialization, secrets, prompt injection and XSS each have a checkbox per
reviewer and require a matching trigger. Empty preset triggers refuse execution. The additional
`redteam-general.md` remains available as a custom prompt outside the twelve; custom prompts may use
empty triggers to request an unconditional pass. Prompts are embedded by the server build. Edit those files and
commit the changes to change shipped defaults. An optional `<dataDir>/prompts/<id>.md` overrides
the corresponding default; the Settings button explicitly edits this local override. Custom
prompt metadata is registered in the lane's prompt library. An empty or blank override is no
override — a shipped preset keeps its shipped text, as every ordinary role does — while a custom
prompt with no text of its own, or any oversized text, refuses the run.

At the operator's request, all thirteen prompts were shortened to approximately half their word
count on 2026-10-01. The source/evidence and output sections remain, with the declared schema owning
the exact required keys. Qwen did not pass the three-consecutive-answer quality preflight. Gemma
passed the empty-answer feature-slice preflight, but positive-finding fidelity remains open; see
[the consolidated measurement record](RESULTS_security_lane_local_llm_windows.md).

The extension's Security lane section controls pairings, triggers, focus, source mode, token budget,
stages, threshold and rounds. A reviewer can be enabled for security alone. Every known member,
including each pair's `context` (slice|diff), `contextTokens` (1024..200000) and `stages`
(code/feature), is validated where the setting is read; a malformed value turns the lane off and
the tab names the malformed part of `coai.securityLane` in settings JSON. The panel never saves
over a malformed setting. The extension still sends it switched off, with the stored value under
`invalidConfiguration`, so the server reports the refusal — naming the setting as malformed, not
asking for a newer server.
Missing/disabled reviewers, missing prompt ids and presets without triggers carry visible repair
instructions. Source collection only uses the focus tags of selected, triggered slice pairings;
unchecked modules cannot consume their sixteen-file source budget.
Servers older than 0.41.0 display an update warning and cannot activate the lane in the panel. Team runtimes are refused for this lane because
their current catalog does not advertise its dedicated schema.

The settings boundary was measured on Windows against released MCP 0.40.3 (2026-10-01): the key is
held back, an explicitly supplied future key is inert, and the current Release binary accepts it.
Repeat with `npm run test:security-compat` and `COAI_OLD_SERVER` naming that released executable.
This read-only check starts no reviewer and makes no claim about local model quality.

## Bounds and validation

There are at most 32 prompt definitions and 16 pairs, with at most ten rounds. The default is two
rounds and threshold zero; these remain conservative settings, not a claim of calibrated quality.
Scratch cleanup reuses the existing round cleanup. History is retained indefinitely in the operator's
data directory without automatic pruning or a disk quota; its owner handles backups and removal.
Aggregate reproduction caps limit each round's additional payload. See the growth table in
[the design plan](PLAN_a_security_lane_runs_beside_the_gate.md) for the computed worst case.

`SecurityLaneRoundTests` drives the real engine, Git and SQLite with paid reviewers doubled.
`SecurityEvidenceTests`, `SecurityLaneSettingsTests` and `SecurityCoverageTests` cover caps, routing,
validation, deadline and ordinary-failure independence. `securityLane.test.ts` and
`securityLaneMalformed.test.ts` run the page; the eighth leg of `npm run test:seam`
(`scripts/seam-security.mjs`) sends the extension-serialized lane to the built server (positive
control, unknown trigger refused, malformed lane refused). `securityEvidenceLog.test.ts` covers
evidence parsing and its escaped disclosure in the rounds log.
`SecurityEvidenceOnAnOlderDatabaseTests` reads a history without the evidence column, and a damaged
projection, through the read-only queries. `SecurityLaneBudgetTests` pins that the lane's budget
never widens the ordinary roles' (feature budget of one → `call_human`), and `SecurityLaneBoundaryTests`
with `SecurityLaneRoundTests` drive the real engine, roster, Git and SQLite through the empty
lane-only round, blank overrides, broken pairings, patch-only slices and the schema's field limit. The explicit local calibration test
is described in [module_tests.md](module_tests.md); it measures model behavior separately from
these deterministic guarantees.
`SecuritySessionCompatibilityTests` loads pre-lane finding shapes through the real `SessionStore`
and generated JSON context, saves and reloads them, and checks the empty evidence projection.
Its positive control preserves nonempty lane evidence through the same store.
