# PLAN — E3 stories: the conventions, the words every consumer's AI reads (`dew_flow_conventions` and the pins)

> Status: **plan only, nothing built — written 2026-11-18 by Opus when the epic started, on branch `docs/auto-mode-e3`
> at `origin/main` `82a4eeac` (E1 merged as #1011 and released as mcp 0.66.0 by #1012; E2 merged as #1018 and released
> as extension 0.89.0 by #1022).** Epic E3 of [PLAN_auto_never_asks_a_person.md](PLAN_auto_never_asks_a_person.md)
> (its `## Epics`, § *E3*; the design is its § 4.7 snippet item, § 8 PR 3 and note N1). A stories document keeps no
> change ledger: a change to this epic is an entry in the parent. Epoch 3 of the parent is open with entry 3.1, so
> **one more accepted change stops the parent**. This document records no change; § 9 lists what it found for the
> parent and says, for each, why it is or is not a change. **Revised 2026-11-18** after its coai plan round (`proceed`;
> four findings accepted: G0–G3) and an Opus judge (M1–M6, m1–m7 and the nits), each verified against the code
> before it was folded.
>
> Scope: four rule files and their records in `dew_flow_conventions`, a short conventions plan that names the change
> there, one conventions release and its promotion, a pin bump in each of the nine consumers, and in this repository
> the pin bump with the regenerated snippet, its versions and its tests. Then the feature review over the three epics
> and the promotion of the parent. Two stories, the parent's intended cut, kept. **No cornerstone** (the parent: rule
> text, reviewed as every rule change is; the ledger stays `Cornerstones: 2 of 2 — E1.S1, E2.S2`).

## 1. Goal, and why the cut holds

When E3 is done, the shared gate text is true under `auto`. An AI that meets `not_reviewed` says in its summary that
the stage was not reviewed and why, resolves, and goes on. An AI that meets `consult_first` asks the consultants and
comes back with the `consultId`. No sentence promises a person that the default never calls, and no sentence promises
more than the released server does. The parent's E3 done-when 1–5 are the bar (§ 11 maps them).

The parent's two stories are kept:

- **S1** is the text: four rule files, their markers, their body records, the conventions plan, and the two-way
  reading. It changes no code. It is one conventions pull request.
- **S2** is everything after S1's merge: the release, the promotion, nine pin bumps, this repository's snippet, the
  extension release that carries it, the feature review and the promotion of the parent.

The cut follows the repository boundary and the release boundary: S1 can be reviewed and merged with nothing else
moving; S2 cannot start until S1's commit is on conventions `main` with a green `ci` run.

## 2. What exists — verified at coai `82a4eeac` and conventions `origin/main` `4ab5207`

coai pins the conventions at `b1746499` (`git submodule status`: `.agents/conventions`, `remotes/origin/release`), and
conventions `origin/release` is that commit. `git diff --stat b174649 4ab5207 -- common/` is empty: the eight commits
since the pin are plan documents only. Every rule line quoted below is the same at the pin and at `4ab5207`.

### 2.1 What the released server says — the limit on the rule text

`coai-mcp` 0.66.0 is tagged `mcp-v0.66.0`; `git diff mcp-v0.66.0 82a4eeac -- src_mcp/src/Tools.cs src_mcp/src/Program.cs
src_mcp/core src_mcp/src/Server` is empty, so `main` says what the release says.

| What | Where (coai `82a4eeac`) |
|---|---|
| The server instructions: the protocol advances on `proceed`, `continue_anyway`, `good_enough` or `not_reviewed`; rule 3, *"`call_human` comes only from a policy asking a person; under the default `auto` an unreviewed round says `not_reviewed`, and an older build's hold is released, saying so."* | `src_mcp/src/Program.cs:2445-2469` |
| `review_plan`'s verdict list includes `not_reviewed` | `src_mcp/src/Tools.cs:103-104` |
| `review_code` refuses until the plan stage advanced (`proceed`, `continue_anyway`, `good_enough` or `not_reviewed`, then `resolve`d) | `Tools.cs:133-134` |
| `review_feature`: a person's second round "never under policy `auto`"; at the cap, *"under `auto` it is `good_enough`, or `not_reviewed` when nobody answered in it"* | `Tools.cs:300-309` |
| `resolve`: *"policy `auto` raises no hold, and `not_reviewed` and `good_enough` are resolved like `proceed`"*; a hold from a build before `auto` is released and one `resolve` — *"with your decisions, or none"* — advances | `Tools.cs:342-353` |
| `ask_human` under `auto`: never waits, writes no card; own question without a `consultId` → `consult_first`; a held gate → `no_answer_yet` at once; four replies listed | `Tools.cs:407-460` |
| The `not_reviewed` instruction: *"No reviewer answered — … Policy `auto` proceeds WITHOUT a review. Call resolve (there is nothing to decide), say in your summary that this stage was not reviewed and why, then proceed."* With security-lane findings standing it asks for a decision on each instead | `src_mcp/src/Server/Rounds/RoundEngine.cs:954-962` |
| The `consult_first` status string and its instruction: *"The person says: call the consultants — ask_consultants — talk it through, and come back to me only if the two of you did not settle it. Then call ask_human again with the consultId."* | `src_mcp/src/Server/ServerJsonContext.cs:355`; `src_mcp/src/Server/AskUnderAuto.cs:13-15` |
| Under `auto` the AI's own question: a verified `consultId` → the person; no consultant can be had → stand-down, the person; otherwise `consult_first` — in every phase. **A production risk is today's decision**, phase exclusion included: in the planning or released phase, or with the question mode off, it reaches the person at once with no consultant | `src_mcp/core/QuestionConsult/AskGate.cs:131-135`, `:152-160`, `:214-217` |
| Feature stage: round 1 with a ground → `revise` (a retry, also under `auto`); at its cap under `auto`, nobody answered → `not_reviewed`, otherwise `good_enough` | `src_mcp/core/Rounds/RoundMachine.cs:493-512` |
| An older server (`mcp-v0.65.4`) advances on `resolve` after any verdict that sets `AdvanceOnResolve` — `proceed`, `continue_anyway`, `good_enough` — and never emits `not_reviewed` or `consult_first` | `git show mcp-v0.65.4:src_mcp/core/Rounds/RoundMachine.cs`, the resolve arm at `:764-777`; its `Tools.cs:133` and `Program.cs:2451` still said "REFUSES until a plan round reached `proceed`" |

### 2.2 The rule files

| File (conventions `4ab5207`) | Marker | Body record | What it says that `auto` makes untrue or incomplete |
|---|---|---|---|
| `common/coai-review-gate.md` | `coai-snippet v5` (`:6`) | `research/rule-bodies.json`, `bodySha256` `d3203daf…` | `:42-43` "REFUSES until a plan round has reached `proceed`"; `:61-62` step 4 names only `revise` and `proceed`; `:84-96` step 6 knows `call_human` and `escalated`, not `not_reviewed`, and reads as if every gate calls a person; nothing names `consult_first` |
| `common/coai-document-gate.md` | `coai-document v3` (`:6`) | `6da343a2…` | `:39-40` "about `call_human` applies here unchanged"; `:59-64` step 4's verdict list; `:86-87` the DoD's verdict line |
| `common/coai-feature-gate.md` | `coai-feature v3` (`:6`) | `cbf1c0de…` | `:50-52` three grounds, one of them a person; `:68-71` the person's request; `:72-74` `call_human` at the cap; `:80-82`; `:93-94` the DoD's summary line |
| `common/coai-question-consultant.md` | `coai-question v1` (`:6`) | `369af5c4…` | `:20-23` the floor; `:53-56` step 6 knows only `ask_in_conversation`; `:60-62` the gate's own question "on a card directly"; `:94-96` the DoD's door line |

Checked and **not** changed, each with the reason:

- `common/coai-consultant.md` — its "person" sentences (`:27`, `:53`, `:76`, `:111-112`, `:118`) are about the AI being
  stuck, not about the gate; no verdict word.
- `common/coai-caller-model.md` — no verdict, no `ask_human`. Its blockquote `:12-17` says the gate rule "is frozen";
  that has been false since 2026-09-15 (§ 2.3). Not E3's: `todo/PLAN_product_manuals_go_home.md` step 4 already owns
  merging the caller and document files back now that the freeze is retired. The same holds for the document gate's
  blockquote `:15-20`, which S1 leaves as it is (§ 4.2).
- `common/plan-change-control.md:60` — "Approval is the first passing plan-round verdict (`proceed`, `good_enough` or
  `continue_anyway`)". True as written (a round nobody answered approved nothing), but it leaves open what opens a
  plan's epoch 1 when every plan round says `not_reviewed`. Outside E3's scope: § 9, P2.
- `tools/smoke-rules.test.mjs:422` and `tools/fixtures/behaviour/held-gate.json` — a fixture of four held-gate
  moments; its expected answers (`ask_human`, `point_to_notice_card` or `ask_human`, `resolve`, `ask_human`) are what
  step 6 must still prescribe after `call_human`. The fixture is not in `BEHAVIOUR_ORDER`
  (`tools/lib/rule-behaviour.mjs:208`), so no CI runs it; S1's review reads the four moments against the new text.
- `tools/fixtures/gate/*/…/coai-review-gate.md` and `tools/selftest.test.mjs:335` — the fixture's own copy at `v5`;
  unaffected.
- `tools/ownership-check.mjs:36`, `:48`, `:59` — product words, `mcp__*__*` names, and `the|its|our` + `gate`,
  `panel`, `extension`. New text uses bare tool names (`ask_consultants`), "this gate" and "the ConnectOtherAIs panel"
  shapes only, and adds no undeclared name.
- Size caps: `tools/rules.test.mjs:522-532` caps the always core, one gpu scope and seven rules by id; none selects a
  coai rule (their `tasks` exclude `gpu`, and none is `always`). The resolver's per-response 32 KiB is read with
  `--only`.

### 2.3 N1, checked — the freeze, and who reads `research/shared-rules-migration-map.json`

N1 left one question open: whether a tool still reads the migration map. **It does — one test, for provenance only.**

- `tools/rules.test.mjs:142` (conventions `4ab5207`, and the same at the pin `b174649`) reads
  `research/shared-rules-migration-map.json` in the test *"the migration evidence for the original 24 is intact"*
  (`:136-151`). It asserts that the 24 entries exist, that each migrated rule is still in the catalog, and that each
  `originalBodySha256` and `sections` is well formed. It **never compares a live body** with it; it asserts the
  opposite — `original.currentBodySha256` must be `undefined` (`:149`). The comment at `:124-134` says why: "comparing
  it forever is what made those 24 rules uneditable".
- What a body is checked against now is `research/rule-bodies.json`, for every rule, in the test *"EVERY rule's live
  body matches research/rule-bodies.json"* (`:153-176`). A deliberate edit records itself with
  `node tools/rule-bodies.mjs --update <id>…` in the same commit (`tools/rule-bodies.mjs:13-26`).
- That change control arrived in conventions `747887d` on 2026-09-15. coai's own record already says so:
  `research/PLAN_consult_on_a_cadence.md`, D10: "NOT because the gate rule is frozen — it is not since 2026-09-15
  (`rule-bodies.json` + `--update`)". The gate body has been edited since (conventions #83, `6d67ca4`, 2026-11-05)
  with its marker left at 5.
- `git grep` over `tools/*.mjs` and `tools/lib/` at `4ab5207` finds no other reader of the map and no tool naming
  `5d6984eb` (the baseline is only inside the JSON). `tools/canonical-markers.test.mjs:33-43` pins each half's marker
  as `v\d+`, "the marker's VERSION is left free".

**So the gate half's marker can move.** What still says it cannot is this repository: `src_vs_code/src/claudeSnippet.ts`
(`frozen: true` on the gate row, `:212`, and the docblocks at `:45-67`, `:115-128`, `:131-141`, `:191-210`, `:220-239`,
`:316`, `:539`), `src_vs_code/src/test/snippetVersion.test.ts` (`:83-104`, the `!half.frozen` filter at `:111`, the
message at `:120-127`, `:603-613`), `src_vs_code/src/test/snippetVersionIsVisible.test.ts:18`,
`src_vs_code/src/test/install.test.ts:108`, `src_vs_code/scripts/prepare-gate.mjs:10-26`,
`research/module_extension.md:4198-4211` and `research/module_server.md:7022-7024`. § 5.3 corrects them.

### 2.4 This repository's half

| Fact | Where (coai `82a4eeac`) |
|---|---|
| `prepare-gate.mjs` reads the six halves from the pinned mount after `rules check`, strips metadata and leading `owns:` comments, requires each body to start with its marker and heading, and writes `src/generated/{gateRule,consultantRule,featureRule,questionRule}.ts` | `src_vs_code/scripts/prepare-gate.mjs:7-104`, `:150-208` |
| The generated files are not committed; `precompile`, `pretypecheck` and `prebundle` regenerate them | `.gitignore:93-100`; `src_vs_code/package.json:1427-1430` |
| `SNIPPET_VERSION = 5`, `SNIPPET_BODY_SHA = '9833b04294fc083a'`, `ARTEFACT_VERSION = 18`, `DOCUMENT_VERSION = 3`, `CALLER_VERSION = 2`, `CONSULTANT_VERSION = 4`, `FEATURE_VERSION = 3`, `QUESTION_VERSION = 1` | `src_vs_code/src/claudeSnippet.ts:40`, `:43`, `:68`, `:129`, `:142`, `:159`, `:175`, `:189` |
| `KNOWN_HALVES` (with `frozen`), `RETIRED_TEXT` (one gate row, `retiredAt: 18`) | `claudeSnippet.ts:211-218`, `:240-242` |
| The hash guard: sha256 of the paste with every half marker removed, first 16 hex; its failure names the new sha, `ARTEFACT_VERSION + 1`, the menu title, and the raisable halves | `src_vs_code/src/test/snippetVersion.test.ts:105-128` |
| The menu title `Copy the CLAUDE.md snippet (v18)`, checked against `ARTEFACT_VERSION` | `src_vs_code/package.json:231-232`; `snippetVersionIsVisible.test.ts` |
| Tests that pin half versions or phrases: feature `v3` (`featureSnippet.test.ts:37-48`), its verdict phrases (`:70-97`, incl. "`call_human` stops the release" and "a\s+reviewer failure, a `blocking` finding, or the person asking for it"); question `v1` (`questionSnippet.test.ts:28-36`) and its phrases (`:38-68`, incl. "The server's phase rule is the floor, not the target", "review gate's own question never goes to the consultants", "without a `consultId`"); the gate's "Do not proceed on your own judgement" and "REFUSES until a plan round has reached" (`snippetIsAdditive.test.ts:39-80`) | as listed |
| The bench reads any `coai-snippet v<n>` number from a checkout and needs no change | `src_bench/CoaiBench/Running/Snippet.cs:19`, `:33-70` |
| The server's reviewer rule budget pins the tier and this repository's base, not the coai rules; the tail canary depends only on the sizes of the four smallest rules outside the tier (2 197–3 460), so a larger gate rule cannot change it | `src_mcp/tests/StageRulesTests.cs:177-207`, `:209-260`; `src_mcp/runners/Context/RuleOrder.cs:161-174` |

### 2.5 The consumers

The estate is listed in one place, conventions `README.md` § *Consumers* (`4ab5207`): `dew_flow_rag_qln`,
`dew_flow_mcp`, `dew_flow_sidecar_rust`, `dew_flow_benchmark`, `dew_flow_creds_for_devs`, `dew_flow_connect_other_ais`
(this repository), `wsl_care`, `dew_flow_vscode_kit`, `dew_flow_amd_r9700_tuning_ai` — **nine**, all on `release`.
The last full cascade (F41, conventions `research/PLAN_F41_container_scoped.md:3`) merged them in this order:
benchmark #216, mcp #82, sidecar_rust #81, vscode_kit #35, wsl_care #199, creds #372, amd_r9700 #37, rag_qln #121,
coai #1004. rag_qln's bump also moved its two code pins, `external/dew_flow_benchmark` and `external/dew_flow_mcp`, to
their `main` tips, after those two bumps merged (`gh pr view 121 -R …/dew_flow_rag_qln`, read 2026-11-18: files
`.agents/conventions`, `external/dew_flow_benchmark`, `external/dew_flow_mcp`). Conventions `README.md` § *Editing
discipline* says why: a rules-pin bump in mcp or benchmark moves the tip rag_qln pins, and a code-pin bump needs its
build run.

Open pull requests on 2026-11-18 (`gh pr list --state open`): conventions 2 (#181, #143);
coai 11 (#1021, #1016, #1003, #1002, #966, #928, #927, #926, #925, #923, #922). The cap is three per repository
(`common/pull-requests.md` rule 7). These numbers are a record of the day; § 5.1 counts again before the promotion.

## 3. Conventions every story follows

- **Branches and worktrees.** coai: this branch, `docs/auto-mode-e3`, carries this document and later S2's coai
  commits (one coai PR for the epic, as E1 and E2). Conventions: a new worktree on a new branch,
  `git -C /home/jinx/git/dew_flow_conventions worktree add <path> -b docs/coai-gate-words-for-auto origin/main` — the
  local conventions checkout is on an old `main` with a dirty `.claude/settings.json`; it is never edited, checked out
  or reset (a `worktree add` from `origin/main` touches none of its files). Each consumer's bump: its own worktree and
  branch (`common/task-lifecycle.md` § 1).
- **Models.** S1: brief by Opus (this document — the words are given verbatim in § 4), the edit by a Sonnet coder (four
  rule files, a JSON record, a plan and a README row: not a small edit), review by Opus. S2's coai change: Sonnet coder,
  Opus review. A pin bump with nothing else is one gitlink line in one file: a small edit the main session may make.
- **The gate, in both repositories.** This document: `review_plan` on `docs/auto-mode-e3` with
  `plan: todo/PLAN_auto_never_asks_a_person.md`, `epic: 3/3`. The conventions branch: `open`, `review_plan` with the
  conventions plan, then `review_code` over its diff before the PR (`common/pull-requests.md` rule 2). S2's coai
  commits: `review_code` over this branch's diff. Every finding resolved; a rejection with a reason. Your own Opus
  review runs beside each, at the same time.
- **RED first.** The rule text has no behaviour of its own, so its RED is the body record (§ 4.6) and, in this
  repository, the snippet tests (§ 5.3): each is seen failing for the right reason before the change that makes it
  pass, then broken once by undoing the line it names.
- **No `src_mcp` change** in E3. **No wider promise** than § 2.1: a sentence the released server does not make true is
  not written, and a sentence about `auto` is always conditional on `auto`, so it stays true beside an older server.
- **Ownership words.** In a conventions rule, never `the gate`, `the panel`, `the extension` (nor `its`/`our` before
  them), no `dew_flow_*` name, no `mcp__coai__*` name without its `owns:` marker in that file.

Commands, conventions worktree:

```bash
npm ci --ignore-scripts
node tools/rules.mjs check --repo .
node tools/rules.mjs explain --repo . --task policy --task docs --file common/coai-review-gate.md --file common/coai-document-gate.md --file common/coai-feature-gate.md --file common/coai-question-consultant.md --file todo/PLAN_coai_gate_words_for_auto.md
node --test tools/rules.test.mjs tools/canonical-markers.test.mjs     # RED, then GREEN (§ 4.6)
node tools/rule-bodies.mjs --update common.coai-review-gate common.coai-document-gate common.coai-feature-gate common.coai-question-consultant
node tools/ownership-check.mjs
node tools/plan-lifecycle.mjs
npm test && npm run check                                             # what ci.yml's selftest runs
```

Commands, coai worktree, from `src_vs_code/`:

```bash
npm ci
npm run compile                                                 # runs prepare:gate; read the EXIT CODE
node scripts/run-tests.mjs out/test/snippetVersion.test.js      # one file, after compile
node ../.agents/conventions/tools/low-priority.mjs npm test     # the whole suite
npm run lint
```

and from the repository root:

```bash
node .agents/conventions/tools/low-priority.mjs dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests --filter-class "*StageRulesTests"
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests --filter-class "*RuleFilesTests"
node .agents/conventions/tools/gate-snippet-check.mjs
node .agents/conventions/tools/pin-check.mjs                    # AFTER the commit: it reads HEAD
node .agents/conventions/tools/plan-lifecycle.mjs               # before every commit that touches todo/
```

## 4. E3.S1 — the rule text · Opus brief, Sonnet edit

**Goal.** The four rule files say what a released server does under every policy, `auto` included, and nothing more.

**Files.** In the conventions worktree: `common/coai-review-gate.md`, `common/coai-document-gate.md`,
`common/coai-feature-gate.md`, `common/coai-question-consultant.md`, `research/rule-bodies.json` (four entries, by the
tool), `todo/PLAN_coai_gate_words_for_auto.md` (new, § 4.7), `todo/README.md` (its row, in the same commit),
`todo/PLAN_product_manuals_go_home.md` (a boundary section, § 4.7).

Every quoted line below is conventions `4ab5207`. Replace each block exactly; keep every line not quoted.

### 4.1 `common/coai-review-gate.md` — `coai-snippet v5` → `v6`

**(a) The marker, `:6`.** `<!-- coai-snippet v5 -->` → `<!-- coai-snippet v6 -->`.

**(b) The order, `:42-43`.** Now:

```markdown
**The order is a contract, and the server enforces it — `review_code` REFUSES until a plan round
has reached `proceed`.**
```

New:

```markdown
**The order is a contract, and the server enforces it — `review_code` REFUSES until a plan round
has reached a verdict that advances the stage (`proceed`, `continue_anyway`, `good_enough` or
`not_reviewed`) and was resolved.**
```

(It keeps "REFUSES until a plan round has reached", which `snippetIsAdditive.test.ts:72` asserts. It is true of an
older server too: three of the four words advance there, § 2.1's last row, and the fourth never comes. A held plan round
the person releases with Proceed also unlocks `review_code` after its `resolve`; step 6 says to resolve then, so the
sentence does not repeat it.)

**(c) Step 4, `:61-62`.** Now:

```markdown
4. Verdict `revise` → fix the accepted findings, run `review_plan` again. Verdict `proceed`
   → implement.
```

New:

```markdown
4. Verdict `revise` → fix the accepted findings, run `review_plan` again. Verdict `proceed`
   → implement. `continue_anyway`, `good_enough` and `not_reviewed` advance the stage too; step 6
   says what each asks of you first.
```

**(d) Step 6, `:84-88`.** Now:

```markdown
6. Verdict `call_human` → surface the open findings to the person and stop.
   **Do not proceed on your own judgement.** Verdict `escalated` → apply the named step and run
   a fresh round.

   **The server will not take another round until a person answers, and this is enforced.** After
```

New:

```markdown
6. Verdict `not_reviewed` → no reviewer answered, so this stage was NOT reviewed. It is never a
   pass. Call `resolve` — with a decision for each finding the reply still carries (the security
   lane's can stand), otherwise with none — say in your summary that the stage was not reviewed and
   why (the reply names what failed), and go on. Verdict `good_enough` or `continue_anyway` → the
   rounds are spent: do what the reply says, and say in your summary which findings you took and
   which you declined. Verdict `escalated` → apply the named step and run a fresh round.

   Verdict `call_human` → surface the open findings to the person and stop.
   **Do not proceed on your own judgement.** It comes only under a policy other than `auto`:
   `human` (and `escalate`, once its ladder is spent) when the rounds run out, and any other value
   when nobody answered or a feature review reached its cap. Under `auto` — the default of every
   server that knows it — this gate asks nobody: a plan, code or document round nobody answered ends
   `not_reviewed`, spent rounds end `good_enough`, and no hold is raised. So `call_human` is the operator's choice: `human` or another
   value they set, including one a repository's workspace settings keep in force — or a server too
   old to know `auto`, which still asks a person by default.

   **When `call_human` does come, the server will not take another round until a person answers,
   and this is enforced.** After
```

The rest of that paragraph (`:88-96`, from "After `call_human`, `review_plan` and `review_code` REFUSE" to "ignores one
that would not.") is kept word for word.

What (e) below rests on, read at coai `82a4eeac`: a legacy hold is released only when no answer of the person's is on
file (`SessionPolicy.cs:123-135`, "The person's answer wins"; `LegacyHold.cs:7`, `Stands` = held and predating
`auto`). The release's sentence is returned by a review entry (`RoundEngine.cs:272`, `:283-285`) and by the
fresh-document check (`PanelService.cs:1163-1165`); `status` returns the rounds with the held round's note
(`LegacyHold.Note`, "released by policy auto: …", `LegacyHold.cs:31`). `ask_human` releases through
`CurrentUnclaimed` (`AskHumanService.cs:104-105`) and then decides on the released session: with `HumanGate` false
the door is the conversation (`AskGateDesk.cs:156-157`), so its reply is the AI's-own-question answer —
`consult_first` without a `consultId` — and does not mention the release. Only a release that could not be written
reaches the card door and says "call again" (`AskUnderAuto.cs:32-37`). The person's-hold sentence names the notice
only when the session records one (`AskUnderAuto.cs:50-53`).

**(e) After `:96`, a new paragraph inside step 6** (before "This is enforced because it was not", `:98`):

```markdown
   Under `auto`, `ask_human` never waits and writes no card. A hold the person owns — one raised
   under a policy that asks a person by a server that knows `auto`, or one from before `auto` that
   the person has already answered — is answered `no_answer_yet` at once, naming the hold's notice
   when one is recorded: tell the person, and stop as above. Any other hold from a build that predates
   `auto` is released by the next call that reads it. The next review call says so (or why it could
   not be released yet: call again once), and `status` shows it in the held round's note; `ask_human`'s
   reply says nothing about a release that went through. One it could not write yet is answered
   `no_answer_yet` with the reason: call again once. If the release still cannot be written, tell the
   person that reason and stop as above. Once it is released, call `resolve` once — with your decisions,
   or none — and the stage advances.
```

**(f) Before `:108` ("Report the verdicts and the reviewer counts"), a new paragraph:**

```markdown
**Your OWN question to `ask_human`** — not one this gate asks — is answered `consult_first` under
`auto` when it brings no `consultId` the server accepts, unless no consultant can be had or it is a
production risk: call `ask_consultants`, settle it with them, and call `ask_human` again with their
`consultId` for what you did not settle and for what the question rule always sends to the person
(an action outside this working copy, a change to the person's machine, a pure preference); then it
reaches the person (`ask_in_conversation`).
When no consultant can be had it reaches the person at once, and its `note` says why. A production
risk keeps its own path: the question rule, `common.coai-question-consultant`, says which.
```

Checked against every case of `AskGate.Autonomously` (`AskGate.cs:152-160`): the card door (no card, answered at once —
(e)); a production risk (today's decision — "its own path", spelled out in § 4.4 (f)); a verified `consultId` (the
person); no consultant can be had (the person, with the stand-down note); otherwise `consult_first`. Nothing in (d)–(f)
says `consult_first` comes in the second, fourth or first case.

Kept as they are, and why: `:21-40` (additive, commands); `:63-82` step 5 — "refused … when … `call_human` awaits a
person" is conditional and true under every policy; `:98-106` (the measured case, "Stop" means stop shipping).

### 4.2 `common/coai-document-gate.md` — `coai-document v3` → `v4`

**(a) The marker, `:6`.** `<!-- coai-document v3 -->` → `<!-- coai-document v4 -->`.

**(b) `:39-40`.** Now:

```markdown
Everything the review-gate rule says about this being ADDITIONAL to your own review, about the
`commands` a round may carry, and about `call_human` applies here unchanged.
```

New:

```markdown
Everything the review-gate rule says about this being ADDITIONAL to your own review, about the
`commands` a round may carry, and about `call_human` and `not_reviewed` applies here unchanged.
```

**(c) Step 4, `:59-64`.** Now:

```markdown
4. Act on the VERDICT before you consider another round, exactly as you would at the other gates:
   `revise` → fix what you accepted and call `review_document` again; `proceed`, `good_enough` or
   `continue_anyway` → the document gate is done, and your summary says what you took and what you
   declined; `call_human` → surface the open findings and stop, with `ask_human` to fetch the
   person; `escalated` → apply the named step and run a fresh round. Running the review again is
   never a way past a verdict that asked for something else.
```

New:

```markdown
4. Act on the VERDICT before you consider another round, exactly as you would at the other gates:
   `revise` → fix what you accepted and call `review_document` again; `proceed`, `good_enough` or
   `continue_anyway` → the document gate is done, and your summary says what you took and what you
   declined; `not_reviewed` → no reviewer answered: `resolve` with the same `document`, and your
   summary says the document was NOT reviewed and why — never that it passed; `call_human` (never
   under policy `auto`) → surface the open findings and stop, with `ask_human` to fetch the person;
   `escalated` → apply the named step and run a fresh round. Running the review again is never a way
   past a verdict that asked for something else.
```

**(d) The DoD, `:86-87`.** Now:

```markdown
- [ ] The verdict was acted on rather than re-run: fixes for `revise`, a person for `call_human`,
      the named step for `escalated`.
```

New:

```markdown
- [ ] The verdict was acted on rather than re-run: fixes for `revise`, a person for `call_human`,
      the named step for `escalated`, and a summary that says the document was not reviewed for
      `not_reviewed`.
```

The blockquote `:15-20` ("it is a separate file because that one is frozen") is **not** touched: it has been false
since 2026-09-15 (§ 2.3), it is just as false in `coai-caller-model.md`, which E3 does not edit, and the conventions plan
that owns both is `todo/PLAN_product_manuals_go_home.md` (its step 4: "Both exist because the gate rule was frozen,
so retiring the freeze may merge them back"). The conventions PR description says so, so a reviewer who meets the
contradiction finds its owner.

### 4.3 `common/coai-feature-gate.md` — `coai-feature v3` → `v4`

**(a) The marker, `:6`.** `<!-- coai-feature v3 -->` → `<!-- coai-feature v4 -->`.

**(b) `:50-52`.** Now:

```markdown
**What the verdicts mean.** One round is the budget. A second runs only on one of three grounds — a
reviewer failure, a `blocking` finding, or the person asking for it — and no request of yours adds a
third.
```

New:

```markdown
**What the verdicts mean.** One round is the budget. A second runs only on one of three grounds — a
reviewer failure, a `blocking` finding, or the person asking for it — and no request of yours adds a
third. Under policy `auto` the person cannot ask for a second round, so only the first two grounds remain.
```

**(c) After `:61` (the end of the `proceed` or `good_enough` bullet), a new bullet:**

```markdown
- `not_reviewed` — under policy `auto`, nobody answered the round that closes the review: a second
  round (after a reviewer failure or a `blocking` finding in the first) or the only round of a budget
  of one. That closing round reviewed nothing; a first round that was answered stays what it was, with
  your decisions on its findings. Resolve with `feature` — a decision for each finding the reply still
  carries (the security lane's), otherwise none — and say in your summary that the closing round was
  NOT reviewed and why, apart from what an earlier round reviewed and found. It does not block the
  release, and it is never a pass.
```

**(c2) The `proceed` or `good_enough` bullet itself, `:57-61` (added after the S1 judge, 2026-11-18).** "On
`good_enough` the findings gate but none is blocking" is not true under `auto` at the cap: `AutoVerdicts.FeatureAtItsCap`
(`AutoVerdicts.cs:40-50`) ends the review `good_enough` whenever a reviewer answered, whatever the second round still
carries (a `blocking` finding included). Without a sentence the file contradicts itself under `auto` — the parent's
done-when 2, "no wider promise". The bullet's last sentences, now:

```markdown
  rewriting merged epics, and reject the rest with reasons. Your summary says what you took and what
  you declined.
```

New:

```markdown
  rewriting merged epics, and reject the rest with reasons — except under policy `auto` at the cap,
  where `good_enough` can carry a `blocking` finding the second round did not clear: land it as a NEW
  pull request like the rest and name it in your summary. Your summary says what you took and what
  you declined.
```

**(d) `:68-71`, the person's request.** Now:

```markdown
- **The person's request** is the third ground, and only theirs: after the first round, ask with
  `mcp__coai__ask_human` and `feature`. Your own `again: true` is refused over the same base until they
  answer; once they say to keep going, call `mcp__coai__review_feature` again with `again: true` — it
  needs no new commit.
```

New (one sentence added at the end):

```markdown
- **The person's request** is the third ground, and only theirs: after the first round, ask with
  `mcp__coai__ask_human` and `feature`. Your own `again: true` is refused over the same base until they
  answer; once they say to keep going, call `mcp__coai__review_feature` again with `again: true` — it
  needs no new commit. Under policy `auto` that call is answered at once that no second round runs on
  request, and the review closes on `mcp__coai__resolve`.
```

**(e) `:72-74`.** Now:

```markdown
- `call_human` stops the release. It is also what a second round that still carries a `blocking`
  finding, or fails again, comes back with. Surface the open findings and call `mcp__coai__ask_human`;
  only the person's answer moves it on.
```

New:

```markdown
- `call_human` stops the release — under any policy but `auto`. It is also what a second round that
  still carries a `blocking` finding, or fails again, comes back with under such a policy; under `auto`
  that round ends `good_enough`, or `not_reviewed` when nobody answered. Surface the open findings and
  call `mcp__coai__ask_human`; only the person's answer moves it on.
```

**(f) `:80-82`.** "… about the `commands` a reply may carry and about `call_human` applies here unchanged." →
"… about the `commands` a reply may carry and about `call_human` and `not_reviewed` applies here unchanged."

**(g) The DoD, `:93-94`.** Now:

```markdown
- [ ] The summary names the verdict and the reviewer count — and, for `skipped`, why the review did not
      run.
```

New:

```markdown
- [ ] The summary names the verdict and the reviewer count — and, for `skipped` or `not_reviewed`, why
      the review did not run or reviewed nothing.
```

`featureSnippet.test.ts` keeps passing on its phrases: "One round is the budget", the three-ground sentence, "`call_human`
stops the release", "tell the person" (the `skipped` bullet, `:54-56`, is unchanged), "NEW pull requests", "`again: true`".

### 4.4 `common/coai-question-consultant.md` — `coai-question v1` → `v2`

**(a) The marker, `:6`.** `<!-- coai-question v1 -->` → `<!-- coai-question v2 -->`.

**(b) `:20-23`, the floor.** Insert one sentence after "That is what `mcp__coai__ask_human` checks." (`:23`), before
"Measured 2026-10-09:":

```markdown
Under policy `auto` the floor rises to this rule's bar: in every phase, your own question without a
`consultId` the server accepts is answered `consult_first`, unless no consultant can be had or it is a
production risk.
```

(The paragraph is reflowed around the insertion, so no line is left short: the whole of `:20-26`, from
"`ask_consultants` first." to "decide.", is re-wrapped at the file's usual width and its words are the old ones.)

**(c) Step 6, `:53-56`.** Now:

```markdown
6. **Then the door, for what still goes to the person.** Call `mcp__coai__ask_human` with the `consultId` the consultants' reply gave you
   — it is accepted once, from this session, for 30 minutes — and with `document` or `feature` when the
   question belongs to one of those reviews. For your own question it answers `ask_in_conversation`:
   ask the person in this conversation, with your own question tool.
```

New (one sentence added at the end):

```markdown
6. **Then the door, for what still goes to the person.** Call `mcp__coai__ask_human` with the `consultId` the consultants' reply gave you
   — it is accepted once, from this session, for 30 minutes — and with `document` or `feature` when the
   question belongs to one of those reviews. For your own question it answers `ask_in_conversation`:
   ask the person in this conversation, with your own question tool. Without an accepted `consultId`,
   under policy `auto`, it answers `consult_first` instead and reaches nobody — when a consultant can
   be had and the question is not a production risk: ask the consultants, as above, and come back with
   the `consultId` for what they did not settle and for the three kinds of step 4, which go to the
   person whatever the consultants said.
```

**(d) `:60-62`.** Now:

```markdown
- **The review gate's own question never goes to the consultants.** A round held by `call_human`, or a
  feature review's request for a second round, is the person's: `mcp__coai__ask_human` puts it on a
  card directly.
```

New:

```markdown
- **The review gate's own question never goes to the consultants.** A round held by `call_human`, or a
  feature review's request for a second round, is the person's: `mcp__coai__ask_human` puts it on a
  card directly. Under policy `auto` it writes no card and waits for nobody: a held round is answered
  `no_answer_yet` at once, and its instruction says what to do; a request for a second feature round
  is answered at once that none runs on request.
```

**(e) The DoD, `:94-96`.** Now:

```markdown
- [ ] `mcp__coai__ask_human` received the `consultId` when the consultants answered; when none could be
      had it was called without one, and only with the `coai` tools absent was the person asked in this
      conversation directly. The review gate's own question and a production risk took their own paths.
```

New:

```markdown
- [ ] `mcp__coai__ask_human` received the `consultId` when the consultants answered; when none could be
      had it was called without one, and only with the `coai` tools absent was the person asked in this
      conversation directly. A `consult_first` reply was followed by `mcp__coai__ask_consultants`, never
      by asking the person. The review gate's own question and a production risk took their own paths.
```

**(f) `:63-65`, a production risk.** Now:

```markdown
- **A production risk keeps its own path.** When a wrong answer could take production down, pass
  `productionRisk: true` with a `riskReason` to `mcp__coai__ask_human`: it asks the consultants itself
  and returns their answers in `consultantAnswers` — show those beside your question.
```

New — the old text holds only while the work is being built (`AskGate.cs:214-217`: no consultant in the planning or
released phase, with the question mode off, or on a held round's card without a risk; `:223-232`: in the building
phase, the consultants first with their answers in the reply, or beside the card; none to be had, the person at once).
Under `auto` the same decision is taken (`:155`), except that a held round's card door writes no card (`:154`,
`AskHumanService.cs:128`):

```markdown
- **A production risk keeps its own path.** When a wrong answer could take production down, pass
  `productionRisk: true` with a `riskReason` to `mcp__coai__ask_human`. While the work is being built it
  asks the consultants itself and returns their answers in `consultantAnswers` — show those beside your
  question. While the plan is being formed, after the release, with the question consultants switched
  off, or when none can be had, it reaches the person at once with no consultant, under every policy.
  On a held round, while the policy is `auto`, it writes no card: the hold is answered `no_answer_yet` at once.
```

**(g) After `:68`, the workflow step.** `:66-68` say a skill's or workflow's step that asks the person still asks. Under
`auto`, `ask_human` answers such a question `consult_first` like any other of the AI's own (`AskGate.cs:159`: it has
no notion of a workflow step). One sentence is added at the end of that bullet:

```markdown
  Under policy `auto`, `mcp__coai__ask_human` answers such a question `consult_first` too, when it brings
  no accepted `consultId`, a consultant can be had and it is not a production risk: consult once for the
  step's batch, then bring the `consultId` and put the step's questions to the person.
```

`questionSnippet.test.ts` keeps passing on its phrases (`:38-68`; "`productionRisk: true` with a `riskReason`" is kept
in (f)). Kept as they are: `:72-83` (no consultant: the server stands down, `AskGate.cs:158`).

### 4.5 The markers (N1)

| Half | Now | After E3 | Why it moves |
|---|---|---|---|
| `coai-snippet` (`coai-review-gate.md`) | v5 | **v6** | its text changes; the parent's § 4.7 names v6; nothing forbids it since 2026-09-15 (§ 2.3) |
| `coai-document` | v3 | **v4** | its text changes |
| `coai-feature` | v3 | **v4** | its text changes |
| `coai-question` | v1 | **v2** | its text changes |
| `coai-caller` | v2 | v2 | unchanged |
| `coai-consultant` | v4 | v4 | unchanged |

In this repository (§ 5.3): `SNIPPET_VERSION` 6, `DOCUMENT_VERSION` 4, `FEATURE_VERSION` 4, `QUESTION_VERSION` 2,
`ARTEFACT_VERSION` 18 → **19** (one raise for one change of the paste), the menu title `(v19)`, `SNIPPET_BODY_SHA` the
value the hash guard prints, and the `frozen` member removed from `KNOWN_HALVES` — it was the one fact that made the
gate half's number unmovable, and it has not been a fact since 2026-09-15. `RETIRED_TEXT`'s row stays: a v6 paste
that carried the retired sentence would still be found by it, and its test (`snippetVersion.test.ts:611-619`) still
holds. `prepare-gate.mjs` needs no code change: its markers are `v\d+` (`:63`, `:80`, `:92`, `:97`).

What a person sees from it: a repository whose pasted copy or mounted rule is still at `coai-snippet v5` (or the other
halves' old numbers) is reported `older` by the extension that carries v19, naming the halves behind — until that
repository's pin bump lands. That is the panel saying the truth during the cascade, not a defect.

### 4.6 Tests and checks — RED first

1. **RED.** After the four edits, before recording them: `node tools/rule-bodies.mjs` (verify mode) exits 1 and names
   every drifted rule — exactly the four ids; a fifth means an unintended edit. (The test *"EVERY rule's live body
   matches research/rule-bodies.json"*, `tools/rules.test.mjs:153-176`, asserts in a loop and stops at the first
   mismatch, so it cannot show the set.)
2. **GREEN.** `node tools/rule-bodies.mjs --update common.coai-review-gate common.coai-document-gate
   common.coai-feature-gate common.coai-question-consultant`; verify mode exits 0 and `node --test tools/rules.test.mjs`
   passes. **Break it:** revert one marker (`v6` → `v5`) without `--update`: verify mode names that id alone; restore.
3. `node --test tools/canonical-markers.test.mjs` — each body still starts with its marker and heading.
4. `node tools/ownership-check.mjs` exit 0; and a grep of the added lines for `\b(the|its|our)\s+(gate|panel|extension)\b`
   finds nothing.
5. `node tools/plan-lifecycle.mjs` clean (the new plan, its README row, the boundary section).
6. `npm test` and `npm run check`, the whole of what `ci.yml`'s `tools · selftest` runs.
7. `node tools/rules.mjs read --repo . --task implement --only common.coai-review-gate` shows the new body between
   BEGIN and END with the recorded hash; the same for the other three.
8. **A dry run of the consumer's build, before the conventions PR merges** — the step that would have caught the
   2026-09 marker displacement upstream. In a throwaway local branch of this coai worktree (never pushed):
   (i) write § 5.3 scenario 1's file, `src_vs_code/src/test/snippetSpeaksAuto.test.ts`, and commit it at the old pin —
   it goes RED as § 5.3 scenario 1 says; (ii) fetch the conventions branch head into the submodule
   (`git -C .agents/conventions fetch <the conventions worktree> docs/coai-gate-words-for-auto`), check it out and
   commit the gitlink (`prepare-gate` runs `rules check`, which refuses a gitlink that differs from the checked-out
   commit); (iii) `npm run compile` and the snippet tests. Expected: `prepare-gate` green; the new file GREEN; red
   only where a test compares a constant with a marker (§ 5.3 scenario 2's list); any red in a phrase or content
   assertion is a defect in S1's text, fixed in conventions before its merge. The file is carried to § 5.3 unchanged
   (cherry-picked onto `docs/auto-mode-e3`); delete the throwaway branch after.
9. **The held-gate moments, by reading** (the Opus review): after the new step 6, moment 1 (`call_human` just returned)
   still prescribes `ask_human`; 2 `point_to_notice_card` or `ask_human`; 3 `resolve`; 4 `ask_human`
   (`tools/fixtures/behaviour/held-gate.json`).

### 4.7 The conventions plan — a new short `todo/` plan

**Choice: a new `todo/PLAN_coai_gate_words_for_auto.md` in `dew_flow_conventions`, in the same PR as the rule edits,
promoted to `research/` when the release and the nine bumps are merged.** Why:

- The change crosses a repository boundary, so it is named in both repositories' plans (`common/planning-docs.md`
  § *Across repositories*).
- No open conventions plan covers these files' words (`todo/` at `4ab5207`: the shared-rules rollout, product
  improvements, prod checks, the scenario harness, the product manuals, action pins, the mutation harness, the
  test-leftovers rollout, F30, the follow-ups, node parity and its E1, the raw-temp baseline). Adding E3 to one of
  them is a scope change to THAT plan and an entry in its ledger.
- A new plan is never written into `research/` (`common/planning-docs.md` § *Creating a plan*); a short single-release
  plan with one PR is the house precedent (`research/PLAN_F41_container_scoped.md`,
  `research/PLAN_agent_models_1_4_0.md`).

Its content, carried over rather than linked (cross-repository citations are paths): the status line; the goal (§ 1
here, in a paragraph); the source, cited as `dew_flow_connect_other_ais · todo/PLAN_auto_never_asks_a_person.md`
§ *E3* and this document; the wording of § 4.1–4.4 as a table of file · lines · what changes; § 4.5's markers; § 4.6's
checks; the two-way reading of § 4.8 in brief; the release, the promotion pre-checks and the nine bumps of § 5.1–5.2;
a Definition of Done that includes § 5.4a's post-deploy run against a bumped consumer. Its ledger opens at its first approval (its plan round's first passing verdict): `## Plan changes` and `> Epoch 1 · approved <date> · by reviewers · state: open` directly under the status block, no entries — the plan round's own accepted findings and the judge's, folded in the commit that records the approval, are not changes (§ 8).

**A boundary on both sides.** `todo/PLAN_product_manuals_go_home.md` has `common/coai-review-gate.md` and
`common/coai-document-gate.md` in its scope and plans to move "tool names, arguments, verdict words" to the product. The
new plan carries, and the older plan gains as a section of its own (a boundary, not a change to its scope), this table:

| Item | Built by | The other plan's part |
|---|---|---|
| The verdict and status words for `auto` (`not_reviewed`, `consult_first`) in the four coai rules | the new plan (E3 of the coai plan) | for `coai-review-gate.md` and `coai-document-gate.md`, the two that plan names: when it runs, moves them with the rest of the protocol. The feature and question rules are outside that plan. |
| Merging `coai-caller-model.md` and `coai-document-gate.md` back (which removes the blockquotes), and their "frozen" blockquotes | `PLAN_product_manuals_go_home.md`, step 4 | none — the new plan leaves both blockquotes as they are |

(The same table, seen from its own side, is a section of the new plan too: `common/planning-docs.md` asks for the table in both documents.)

Order: the new plan first (it edits in place); the manuals plan later moves whatever is there. Disjoint otherwise.

### 4.8 The two-way reading (the parent's done-when 3) — written into the conventions PR

The window: E1 and E2 are released, and a consumer has or has not bumped. Two readers can meet a server that does not
match their text.

**A. Old text (the pin `b174649`) against a server that knows `auto` (coai-mcp ≥ 0.66.0) running `auto`.**

| Old sentence | What the server does | Reading |
|---|---|---|
| gate `:42-43`, "REFUSES until a plan round has reached `proceed`" | a plan round may end `good_enough` or `not_reviewed`; after `resolve`, `review_code` runs | narrower than the server, not opposite: the reply's own instruction ends "then proceed", and the call is accepted. The sentence was already narrower before E1 (`continue_anyway` and `good_enough` advanced then) |
| gate step 4 names `revise` and `proceed`; document step 4 lists no `not_reviewed` | `not_reviewed` arrives with its instruction (`RoundEngine.cs:954-962`), and the server instructions name it (`Program.cs:2452`) | silent, not contradicted: the reply says what to do |
| gate step 6, `call_human` → stop, `ask_human`, the hold | never `call_human` under `auto` | the step never fires |
| gate step 6, "Call `ask_human`. Their answer decides", on a hold the person owns (raised under a policy asking a person by a server that knows `auto`, or a hold from before `auto` the person already answered) | `no_answer_yet` at once, naming the notice when one is recorded | the old text expects a wait; the reply comes at once and says the same thing to do — the person answers on the notice |
| the same step on a hold from before `auto` that nobody answered | released by the first call that reads it; a review call answers with the release's sentence; `ask_human`'s reply is silent about a release that went through, and answers `no_answer_yet` with the reason for one it could not write yet (§ 4.1, what (e) rests on) | the old text says to ask the person; an AI that calls `ask_human` first gets `consult_first` or the conversation door for its question and no word of the release, and learns it at its next review call or `status`. Nothing is passed wrongly: one `resolve` is still needed to advance. Recorded as an E1 residual, § 9 P6 |
| question `:66-68`, a workflow step's questions go to the person | `consult_first` for them too, without an accepted `consultId` while a consultant can be had | the old text does not say to consult first for them; the reply says so, and the questions reach the person after one consultation |
| feature `:68-71`, the person's request as a third ground; question `:60-62`, the request "on a card directly" | under `auto` the request is answered at once: no second round on request, the review closes on `resolve` (`AskUnderAuto.cs:17-19`) | **the one stale promise**: the AI asks, is told at once, and resolves. Nothing waits and nothing passes that should not; the PR names it as the window's residual |
| feature `:72-74`, `call_human` at the cap | `good_enough`, or `not_reviewed` when nobody answered | the old sentence describes another policy; under `auto` it never fires and the reply instructs |
| question `:28-29`, "no question reaches the person before the consultants have had it" | `consult_first` for an own question with no accepted `consultId` | the same bar, now enforced: they agree |
| question `:80-82`, no consultant can be had → call without a `consultId` | the stand-down, `ask_in_conversation` with its note (`AskGate.cs:158`) | true |

**B. New text against a server that predates `auto` (coai-mcp < 0.66.0; an absent key means `human`).**

| New sentence | What the older server does | Reading |
|---|---|---|
| "REFUSES until … `proceed`, `continue_anyway`, `good_enough` or `not_reviewed` … and was resolved" | advances after `proceed`, `continue_anyway`, `good_enough`; never says `not_reviewed` | true; the fourth word never comes |
| step 6, `not_reviewed` → … ; feature and document `not_reviewed` | never emitted; nobody answering is `call_human` there | the step never fires; the `call_human` step after it is still there and true |
| "Under `auto` — the default of every server that knows it — this gate asks nobody … or a server too old to know `auto`, which still asks a person by default" | knows no `auto`; asks a person | true as written: the clause names this server |
| "Under `auto` this call never waits …"; `consult_first`; "Under policy `auto` the floor rises …"; "only the first two grounds remain" | never under `auto` | conditional on `auto`; never fires. The old blocking behaviour is still what `ask_human`'s own reply describes |
| the question rule's production-risk sentence: while the plan is being formed, after the release, with the question consultants off, or when none can be had, `productionRisk` reaches the person at once with no consultant, "under every policy" | at `mcp-v0.65.4` `AskGate.AsksNoConsultant` (`:170-172`) lets such a question through in every phase but building or with the mode off, and `Risk` (`:179-180`) asks the person at once when no consultant could be asked (`git show mcp-v0.65.4:src_mcp/core/QuestionConsult/AskGate.cs`) | true unconditionally: the one new sentence that does not depend on `auto`, and what the older server does |

No sentence of either text tells an AI to do the opposite of what the server it meets will do; the one stale
sentence (A, the person's request) is answered by the reply at once.

### 4.9 Done when

- [ ] The four files carry § 4.1–4.4 word for word, as revised on 2026-11-18 after the S1 judge (§ 8; the blocks above are the revised ones; a reviewer's accepted finding may change a word; it is recorded in
      the conventions PR and, if it widens or narrows a promise, put to the coordinator before it is accepted).
- [ ] The markers are v6, v4, v4, v2; `research/rule-bodies.json` moved in the same commit; § 4.6 steps 1–7 green, each
      RED seen first; step 8 run and its result in the PR; step 9 read.
- [ ] `todo/PLAN_coai_gate_words_for_auto.md` and its README row are in the same commit; the boundary section is in
      `todo/PLAN_product_manuals_go_home.md`.
- [ ] The two-way reading of § 4.8 is in the PR description.
- [ ] Plan round and code round passed in conventions; Opus review done; CodeRabbit read; every thread resolved; CI
      green on the head sha; merged by rebase or squash.

## 5. E3.S2 — the release and the pins · Opus brief, Sonnet coder for the coai change

**Goal.** The rule text of S1 is what every consumer loads, and the snippet this repository hands out is its text,
with numbers that say it moved. Then the whole feature is reviewed and the plan is promoted.

### 5.1 Before the promotion — asked and counted (MANDATORY)

A promotion reddens `pin-check` at once in all nine consumers — on `main` and on every open pull request there, each
of which then needs a rebase onto its repository's bump (conventions `README.md` § *Editing discipline*, "reddened every
open pull request"). So, after S1 merged and before the dispatch:

1. **In-flight consumer releases.** For each of the nine, look for an open release pull request
   (`gh pr list --state open --search "release in:title"`) and a release workflow running, and ask — the consultants
   first, then the owner — whether any consumer is mid-release. The promotion waits until no consumer is mid-release: its release finishes first, because a promotion reddens its `pin-check` while the release still needs green checks (plan round 1, finding 2).
2. **Open PRs per repository**, counted again that day against the cap of three (`common/pull-requests.md` rule 7). A
   bump that would put a repository over three needs the owner's agreement, asked in one sentence that names what is
   queued there. coai is over the cap today (§ 2.5).
3. **What the release carries.** `git log --oneline origin/release..<S1's merge sha>` in conventions. If anything other
   than S1 and plan documents is in it — a tier rule above all (`StageRulesTests.cs:177-207`) — say so and expect
   coai's bump to re-baseline in the same PR.

Then `gh workflow run promote-release.yml --ref main -f "sha=<S1's merge sha, 40 hex>" -f reason="the coai gate's words
for policy auto (coai plan E3)"`, and verify: `git ls-remote https://github.com/oleksandrdubyna88/dew_flow_conventions.git
refs/heads/release` answers that sha. The workflow refuses a sha whose `ci` run is absent, unfinished or red.

### 5.2 The nine pin bumps

One PR per consumer, each `chore(rules): bump the shared rules to release <sha8> — the coai gate's words for policy auto`,
each from its own worktree and branch: `git submodule update --init .agents/conventions`, then
`git -C .agents/conventions fetch origin release && git -C .agents/conventions checkout <sha>` (or
`git submodule update --remote` — watch for `pin-check`'s `LOCAL OVERRIDE`), `git add .agents/conventions`, commit,
`node .agents/conventions/tools/pin-check.mjs` after the commit, push, PR, CI, CodeRabbit read, threads resolved, merge.
A CI check present and green on the head sha before each merge, never "no failure found". Each PR body records,
as conventions `README.md` § *Editing discipline* asks ("validation and rollback recorded"):

```markdown
Bumps `.agents/conventions` <old sha8> → <new sha8> (release; the coai gate's words for policy auto —
coai todo/PLAN_auto_never_asks_a_person.md E3, conventions <PR>).
Validation: rules check <output line>; pin-check <output line>; gate-snippet-check <output line, "(v6)">; CI <run url>.
Rollback: a forward release in conventions (revert there, promote), then bump again — never a pin moved back by hand.
```

Order, the F41 precedent (§ 2.5): `dew_flow_benchmark`, `dew_flow_mcp`, `dew_flow_sidecar_rust`, `dew_flow_vscode_kit`,
`wsl_care`, `dew_flow_creds_for_devs`, `dew_flow_amd_r9700_tuning_ai`; then **`dew_flow_rag_qln` after benchmark and
mcp have merged**, its bump also moving `external/dew_flow_benchmark` and `external/dew_flow_mcp` to their `main` tips —
code pins, so its build runs in its CI and is read before the merge; then this repository (§ 5.3), which needs the
longest PR and may run beside the others.

### 5.3 This repository's bump, with the regenerated snippet

**Files.** `.agents/conventions` (gitlink); `src_vs_code/src/claudeSnippet.ts`; `src_vs_code/package.json` (the
`copyClaudeSnippet` title only); `src_vs_code/scripts/prepare-gate.mjs` (comments only); tests
`src_vs_code/src/test/snippetVersion.test.ts`, `featureSnippet.test.ts`, `questionSnippet.test.ts`,
`snippetVersionIsVisible.test.ts` (comment), `install.test.ts` (comment at `:108`), and a new
`src_vs_code/src/test/snippetSpeaksAuto.test.ts`; documents `research/module_extension.md`, `research/module_server.md`;
this document's and the parent's status lines and `todo/README.md`.

**Scenarios, RED first** (every one asserts on the generated constants — rule text, not a webview page, so
`.agents/PROJECT.md`'s ban on page-source assertions does not apply):

1. **New test file first, at the old pin.** `snippetSpeaksAuto.test.ts`:
   - the gate half (`GATE_RULE`) contains `` `not_reviewed` ``, "NOT reviewed", `` `consult_first` `` and "Under
     `auto`"; it still contains "Do not proceed on your own judgement" and "REFUSES until a plan round has reached";
   - the document half contains `` `not_reviewed` `` and "NOT reviewed";
   - the feature half contains `` `not_reviewed` `` and "only the first two grounds remain";
   - the question half contains `` `consult_first` `` and "the floor rises to this rule's bar";
   - the question half says "While the plan is being formed" in its production-risk bullet, and "consult once for the
     step's batch";
   - **no wider promise**: no half matches `/never asks (you|a person|the person)|nobody is ever asked/i`; the gate
     half names "a server too old to know `auto`" and, whitespace-normalised, "unless no consultant can be had or it
     is a production risk".

   Run it: RED on every new phrase, each failing on the missing phrase (not on a compile error). Green from the start,
   and staying green: "Do not proceed on your own judgement", "REFUSES until a plan round has reached", and the
   no-wider-promise pattern. (Written first in § 4.6 step 8's throwaway branch and cherry-picked here.)
2. **Move the gitlink to the promoted sha**, `npm run compile`, run the snippet tests. Expected RED: every test that
   compares a constant with a marker in the text. At `82a4eeac` that is the hash guard (`snippetVersion.test.ts:105-128`,
   naming the new sha, `ARTEFACT_VERSION` 19 and `(v19)`); in `snippetVersion.test.ts` also `:35-37` and `:39-44`
   (`snippetVersionIn` reads 6 against the constant 5), `:46-53`, `:68-70` and `:72-80` (they build an older or newer
   paste by replacing `coai-snippet v${SNIPPET_VERSION}`, which is no longer in the text, so the status is wrong), and
   `:590-600` (each half's marker against its row's version); `snippetDiscovery.test.ts:39` and `:59` (the same
   replacement); `featureSnippet.test.ts:44` (`coai-feature v3`); `questionSnippet.test.ts:36` (`coai-question v1`).
   The new file is GREEN now. A red outside that class — a phrase or content assertion — is a defect in S1's text and
   goes back to conventions as a fix, not into a test here.
3. **GREEN**, in `claudeSnippet.ts`: `SNIPPET_VERSION = 6`, `SNIPPET_BODY_SHA` = the printed value,
   `ARTEFACT_VERSION = 19`, `DOCUMENT_VERSION = 4`, `FEATURE_VERSION = 4`, `QUESTION_VERSION = 2`; remove `frozen`
   from every `KNOWN_HALVES` row and the `!half.frozen` filter in the hash test (every half is raisable). In
   `package.json`: `"Copy the CLAUDE.md snippet (v19)"`. In the version tests: `featureSnippet.test.ts:37-48` → v4 (and
   its comment's history line), `questionSnippet.test.ts:28-36` → v2.
4. **Break it.** Put the gitlink back at `b1746499` (the old pin) and compile: scenario 1's file goes RED on every new
   phrase (the two kept phrases and the no-wider-promise pattern stay green), and the constant-against-marker tests of
   scenario 2 go RED the other way. Restore.
5. **Words that were true of the freeze**, rewritten as history in the same commit — the freeze ended 2026-09-15
   (conventions `747887d`), the halves stay separate files each with its marker, and merging some back is the
   conventions' manuals plan's step 4: `claudeSnippet.ts` docblocks at `:45-67`, `:115-128`, `:131-141`, `:191-210`,
   `:220-239`, the comments at `:316` and `:539`; `snippetVersion.test.ts:83-104`, the hash test's message
   `:120-127` (no "SNIPPET_VERSION stays …"), `:603-613`; `snippetVersionIsVisible.test.ts:18`;
   `install.test.ts:108`; `prepare-gate.mjs:10-26`; `research/module_extension.md:4198-4211` (the gate half moved to
   v6 with E3; `RETIRED_TEXT` remains for a paste whose text changed and whose marker did not) and
   `research/module_server.md:7022-7024` (the marker was not "frozen"; it moved to v6 on <date>). Dated records in
   promoted plans (`research/PLAN_the_menu_names_the_clipboards_version.md`,
   `research/PLAN_the_gate_rule_says_a_code_round_closes_the_session.md`) are history and stay.
6. **The rest of the suite**: the whole extension suite and `npm run lint`; on the server side `StageRulesTests` and
   `RuleFilesTests` (expected unchanged: the gate rules are not in the tier and the tail cannot grow, § 2.4) and CI's
   full run; `gate-snippet-check.mjs` prints `OK — … (v6)`; `pin-check.mjs` green after the commit.

Commit type: `fix(extension): the CLAUDE.md snippet speaks policy auto — not_reviewed, consult_first, call_human as the
operator's choice (v19; E3 of the auto plan)`, so release-please opens an extension release for it (§ 5.4). The PR is
merged by squash, so **the PR's title is that line** — release-please reads the squashed commit, whose subject is the
title, not the branch's commit messages. Then the
gate's `review_code` over this branch, an Opus review beside it, the PR (over the cap: the owner's agreement, § 5.1),
CI with its checks present and green, merge.

### 5.4 The feature review, then the extension release that carries the snippet

There is no open extension release: 0.89.0 shipped on 2026-11-18 (#1022). The snippet reaches people only with the
next one, and `common.coai-feature-gate` asks for the feature review **before the release**. So:

1. **`review_feature`**, from a checkout of coai `main`, pulled, after § 5.3 merged: `planPath`
   `todo/PLAN_auto_never_asks_a_person.md`; `baseRef` **`e829bba8`** (the plan's merge #1007, the commit before E1's
   #1011); `epics` E1 (#1011, `feat/auto-mode-e1`), E2 (#1018, `feat/auto-mode-e2`), E3 (this branch's PR and the
   conventions PR); `lessons` honest in all three arrays — among the findings: a reviewer of this repository sees E3's
   rule text only as a moved gitlink and the snippet constants, so the conventions PR is named in E3's summary; the
   stale-promise residual of § 4.8 A; § 9's P2, P3 and P6; and that the range `e829bba8..main` also holds work outside this
plan, named so reviewers leave it alone: #1015 and #1019 (the terminal door), #1017 (the agy hook plan's promotion),
#1013 (extension 0.88.0) and #1020 (the server 0.16.3 deploy record). #1012, #1014 and #1022 are this plan's release
bookkeeping (the parent's re-review table keeps #1014 as `fits`). `callerModel` your own id. `resolve` every finding with
   `feature`; accepted fixes land as NEW PRs, never by rewriting a merged epic.
2. **The extension release.** The release-please PR the `fix(extension)` commit opens (0.89.1 by its count), with a
   `## Extension 0.89.1 — <date>` section written into `src_vs_code/CHANGELOG.md` in that PR: the snippet is v19; what
   changed for an AI that reads it (`not_reviewed`; `consult_first`; `call_human` only under a policy that asks a
   person); the correction § 9 P3 decided, in plain words: under Auto **the gate** never asks a person, while **your
   AI's own question** reaches the person after its consultants, or at once when none can be had — and a question it
   marks as a production risk keeps its own path and may reach the person without the consultants first, which
   0.89.0's entry did not say; that a repository's mounted rule or paste at the old numbers reads `older` until its pin
   bump; the server to pair it with (coai-mcp 0.66.0). Merged by squash, the tag pushed alone, the new run's id checked against the ids
   recorded before (`common.task-lifecycle` § 3), and `POST_DEPLOY.md` run against the published version, as for
   0.89.0.

### 5.4a The conventions' post-deploy check

The conventions deliver through gitlinks, so their `POST_DEPLOY.md` names a consumer checkout as the target. After
coai's bump (§ 5.3) merges, from a pulled checkout of coai `main`:

```bash
node .agents/conventions/tools/post-deploy-check.mjs --target <that checkout>
```

Items 1–2 are automatic (`rules check` on the target, and `gate-snippet-check`, which prints `(v6)`); item 3 is the
manual native-agent smoke and is run or recorded as not run, with the reason. The result, its date and the delivered
sha go into the conventions plan; the conventions plan's Definition of Done carries this run.

### 5.5 The promotion of the plan

When § 5.1–5.4 are done: the parent, `PLAN_auto_never_asks_a_person_E1.md`, `…_E1_S1_spec.md`, `…_E2.md`,
`…_E2_S2_spec.md` and this document move to `research/` together, each `IMPLEMENTED <date>` with its deviations — the
parent's carrying § 9's P3 if the coordinator records it there — and the three link cases fixed, by the order of
`common/planning-docs.md` § *The `git mv` goes LAST*; `todo/README.md` and `research/README.md` in the same commit;
`plan-lifecycle.mjs` clean. The rollout is reported per side as committed / built / released / running: conventions
(release sha), nine consumers (PR per repository), coai-mcp 0.66.0 (unchanged by E3), extension 0.89.1. The
conventions plan is promoted in its own repository once its release and nine bumps are merged.

### 5.6 Done when

- [ ] § 5.1's three checks were made and their answers written in the conventions plan before the dispatch; `release`
      points at S1's merge sha.
- [ ] Nine bump PRs merged with their checks present and green; rag_qln's code pins moved after benchmark and mcp.
- [ ] coai's bump: § 5.3 scenarios 1–4 seen RED, GREEN and broken; 5 and 6 done; `snippetVersion.test.ts` green.
- [ ] The conventions' `post-deploy-check.mjs --target` ran against coai `main` after its bump (§ 5.4a), recorded in the
      conventions plan.
- [ ] `review_feature` ran over the three epics before the extension release, every finding resolved.
- [ ] The extension release carrying v19 is published and checked; the plan is promoted with its deviations.

## 6. Order, and what may run in parallel

1. This document: plan round on `docs/auto-mode-e3` (`epic: 3/3`), findings resolved, committed and pushed.
2. S1 in conventions: edit → § 4.6 → plan round and code round → PR → merge. (§ 4.6 step 8 runs in a throwaway local
   branch here, beside it.)
3. § 5.1, then the promotion.
4. § 5.2's eight bumps and § 5.3's coai PR in parallel; rag_qln after benchmark and mcp.
5. § 5.4a: the conventions' post-deploy check against coai `main` once its bump merged; § 5.4: the feature review,
   then the extension release.
6. § 5.5: the promotion of the five coai documents and this one; the conventions plan's promotion in its repository.

Nothing in E3 waits for an `mcp-v*` release, and E3 ships none.

## 7. The parent's references for E3, checked

- § 4.7 "`common/coai-review-gate.md` … step 6 gains *`not_reviewed` → … resolve, proceed* … Version v6": true at
  `4ab5207` (step 6 at `:84-106`); v6 is possible (§ 2.3). S1 also edits `:42-43`, step 4 and one paragraph before
  `:108` of the same file, because without them the file is still untrue under `auto` — a fix that satisfies the plan
  as written (§ 9, P4).
- N1's line numbers: `coai-document-gate.md:60-62`, `coai-feature-gate.md:57,72`, `coai-question-consultant.md:55` —
  correct at `4ab5207`. `claudeSnippet.ts:119-122` has moved: the gate-half-stays-v5 docblock is at `:115-128` now;
  `prepare-gate.mjs:12-15` is at `:10-26`.
- N1's "no `.mjs` under `tools/` names that baseline" — true; the map's reader is a test (§ 2.3).
- E3's "Depends on. E1 released": coai-mcp 0.66.0, verified (§ 2.1). "After E2 by order": extension 0.89.0 released,
  no extension release open.

## 8. Settled here as fixes that satisfy the parent as written (not changes)

- The gate half's marker moves to v6, as § 4.7 says; the obstacle N1 named has not existed since 2026-09-15 (§ 2.3).
- The other three markers move because their texts change (N1: "E3.S1 decides which markers move").
- The `frozen` member and the docblocks and documents that state the freeze are corrected in the coai PR, because the
  plan's own § 4.7 change makes them false.
- The extension release that carries the snippet is the patch release after the feature review (N1: "which extension
  release carries the regenerated snippet").
- The conventions plan is a new `todo/` plan with a boundary section in the manuals plan (§ 4.7), and the same table in the new plan.
- The S1 judge's finding 3 adds one feature-gate sentence (§ 4.3 (c2): `good_enough` at the cap under `auto` can carry a `blocking` finding) because the file otherwise contradicts itself under `auto` — the parent's "no wider promise" / done-when 2 requires it; it satisfies the plan as written, so it is not a change. The judge's other findings (§ 4.1 (d) and (e), § 4.4 nits, the plan's boundary, ledger and line ranges, the PR body) are wording or accuracy fixes of the same kind. The conventions plan's ledger opens at the plan round's approval with no entry.

## 9. Findings for the parent plan

None of these is written into the parent here. Each says whether recording it would be a change; epoch 3 already has
entry 3.1, so one change stops the parent.

- **P1 — N1's premise (not a change).** "the gate half stays v5 because its body is hashed against … `5d6984eb`" has
  been false since conventions `747887d` (2026-09-15); § 2.3 has the evidence. § 4.7 already asks for v6, so building v6
  is the plan as written. The coordinator may correct N1's wording as a repaired reference.
- **P2 — what opens epoch 1 when the plan round says `not_reviewed` (would be a change if taken into E3; recommended
  not).** `common/plan-change-control.md:60` counts `proceed`, `good_enough` and `continue_anyway` as approval, and the
  owner's go-ahead "where no review server runs". A server that runs and in which nobody answered is neither. The text
  is true — an unreviewed round approved nothing — so E3 does not need it. Adding a fifth rule file to E3's scope is a
  change (scope), and the second of epoch 3. Recommended: leave E3's scope; open a conventions todo for the owner's
  decision.
- **P3 — requirement 5 is wider than the released server (a deviation to record; the coordinator decides whether it is
  a change).** Requirement 5 says "the only road to a person is the AI's own question after the consultants". Under
  `auto` a production-risk question in the planning or released phase, or with the question mode off, reaches the
  person at once with no consultant (`AskGate.cs:131-135` → `:214-217`; E1 stories § 9.1 item (3); test
  `AProductionRisk_IsTodaysDecision`). § 4.4's table row says "today's path", which is what was built; its parenthesis
  "(consultants asked beside, answers returned)" holds only in the building phase. The extension's 0.89.0 CHANGELOG
  repeats the wide sentence ("The one road left to a person is your AI's own question after its consultants, or when
  no consultant can be had"). E3's rule text says what the server does ("a production risk keeps its own path").
  Recommended: record it at promotion as a deviation of E1 against requirement 5. If it is instead read as a change to
  requirement 5 or to E1's done-when 5, it is the second change of epoch 3 and stops the plan.
  **Decided 2026-11-18 by the coordinator: a wording erratum, not a change** (consultants `df932f5e…`, 2 of 2 for it).
  § 4.4's `productionRisk: true` row has stood since the plan's first commit (#1007, e829bba8), approved with
  requirement 5 in the same baseline; what was built follows that row, and nothing built or to be built changes. The
  conventions precedent is a wording repair that is not counted (`common/plan-change-control.md`;
  `research/PLAN_plan_change_control_E2.md`, the "every other session sees the stop" repair). It is recorded at the
  promotion as an erratum of requirement 5 and of § 4.4's parenthesis, and the next extension CHANGELOG entry (E3.S2's
  patch release) says plainly that a production-risk question keeps its own path and may reach the person without the
  consultants first. Requirement 5's text is not edited before the promotion.
  *Added 2026-11-18 at the fold of the plan round and the judge.* The erratum also covers the parent's **E3 done-when
  2** ("`ask_human` reaches the person only after the consultants (O3)"), which is wide in the same way; § 11 below
  keeps E3's wording and points here. The reasoning holds on two further facts: E1's stories § 9.1 item (3) built the
  production-risk case as "exactly today's decision for the same input with `Autonomous = false`", which is § 4.4's
  row taken literally, and the epoch-3 re-review sorted #1011 as `fits` (the parent's table, row "E1, the server") — a
  re-review's baseline is not counted (`common/plan-change-control.md`, *The full stop*, step 4). § 4.4's parenthesis
  "(consultants asked beside, answers returned)" was wrong outside the building phase, and the erratum says so. The
  server's own `ask_human` text is equally wide — `Tools.cs:410-411`, "the consultants of a production risk still run,
  and their answers come back before this call returns" — and is server text outside E3: a deviation for the
  promotion, not a fix here. E3's question rule says what the server does (§ 4.4 (f)).
- **P4 — E3.S1 edits more of `coai-review-gate.md` than "step 6" (not a change).** The intro sentence `:42-43`, step 4
  and the `consult_first` paragraph are needed for the file to be true under `auto`; they are within the intended story
  "The wording in the four rule files".
- **P5 — a boundary in conventions (not a change).** The manuals plan there has two of E3's files in its scope; § 4.7's
  table goes into both conventions plans. The parent's § 7 is about coai plans and gains nothing.
- **P6 — `ask_human` is silent about a legacy hold it released (an E1 residual; a deviation, not a change).** The parent's
  E1 done-when 4 and requirement 4 name `ask_human` among the calls that release a hold from before
  `auto`, and it does. But its reply then answers the question on the released session's conversation door and never
  says the hold went (§ 4.1, what (e) rests on). Requirement 4 asks for the release, its log line, the withdrawn notice
  and the note — all built; it does not ask `ask_human` to announce it. E3's rule text says where the release is told
  (the next review call, `status`'s round note). Recorded at the promotion as an E1 deviation; no code here.

## 10. Open questions, each with the assumption this document takes

- **E3-Q1 — which extension release carries the snippet.** *Assumed:* a patch release (0.89.1 by release-please's
  count of a `fix(extension)` commit) cut after the feature review (§ 5.4). Alternative: hold the snippet for the next
  feature release.
- **E3-Q2 — whether the owner's agreement to over-cap PRs covers the nine pin bumps.** *Assumed:* it is asked again at
  § 5.1 (the consultants first, then the owner), as for F41 and agent-models 1.4.0.
- **E3-Q3 — the private `ai_conventions` mirror.** coai's `todo/PLAN_question_consultant.md:311` updated a private
  mirror beside the conventions for the consultant rule. It is not in conventions `README.md` § *Consumers*.
  *Assumed:* not touched by E3; named to the owner in the report.
- **E3-Q4 — the held-gate behaviour smoke.** *Assumed:* not run (it is not in `BEHAVIOUR_ORDER` and costs a model run);
  its four moments are checked by reading (§ 4.6 step 9). The owner may ask for one run.
- **E3-Q5 — what else the release carries.** Conventions #181 (a plan document) and #143 (Dependabot) are open.
  *Assumed:* the release is S1's merge sha with whatever `main` holds then; § 5.1 step 3 names it, and coai's bump
  re-baselines `StageRulesTests` in the same PR if a tier rule moved.
- **E3-Q6 — the conventions plan in the same PR as the rule text.** *Assumed:* yes (the F41 precedent, "one PR, docs
  and code"); its plan round runs first on the branch, its code round over the whole diff.
- **E3-Q7 — the feature review before the extension release.** *Assumed:* yes, as the feature rule says; E1's and E2's
  releases are already out and are reviewed as shipped.

## 11. Definition of Done for E3 — the parent's done-when, mapped

- [ ] **Done-when 1** — `not_reviewed` → summary, `resolve`, proceed; `consult_first` → consult, then `ask_human` with
      the `consultId`: § 4.1 (d)–(f), § 4.2 (c), § 4.3 (c), § 4.4 (b)–(c); coai's § 5.3 scenario 1 holds it.
- [ ] **Done-when 2** — no wider promise: `call_human` only from a policy that asks a person, `human` or another value
      including a workspace's, or a server too old to know `auto`; under `auto` the gate asks nobody; `ask_human` reaches
      the person only with an accepted `consultId` or when none can be had, a production risk on its own path: § 4.1
      (d)–(f), § 4.4; the no-wider-promise assertion of § 5.3 scenario 1. The parent's own done-when 2 is wider about a
      production risk; that is § 9 P3's erratum, and this line, not the parent's, is what E3 is held to.
- [ ] **Done-when 3** — § 4.8, both tables, in the conventions PR.
- [ ] **Done-when 4** — conventions checks green (§ 4.6), the release cut and promoted after § 5.1, nine bumps merged,
      coai's with the regenerated snippet and `snippetVersion.test.ts` green (§ 5.2–5.3).
- [ ] **Done-when 5** — `review_feature` over the three epics (§ 5.4), and the parent and its five documents promoted
      together with their deviations (§ 5.5; DoD items 9 and 10).
- [ ] The parent's DoD item 2, the snippet part: `snippet v6` (§ 4.5).
- [ ] `plan-lifecycle.mjs` clean in both repositories at every commit that touches `todo/` or `research/`.
