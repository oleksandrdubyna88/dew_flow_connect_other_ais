# PLAN — the candidate list can be asked of the API

> Status: **plan only, nothing implemented yet, 2026-09-17 — and GATED on step 0 below: whether a vendor
> key is reachable by the extension at all. If it is not, this plan is BLOCKED and says so; nothing here
> is built on the assumption that one exists.** Scope: `src_vs_code/src/claudeModels.ts`
> (`CLAUDE_CANDIDATES`), `src_vs_code/src/claudeProbe.ts` (the probe that spends the time) and
> `src_vs_code/src/claudeProbeFile.ts` (what is kept between windows).
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_the_models_are_asked_rather_than_listed.md](../research/PLAN_the_models_are_asked_rather_than_listed.md)
> — this is that plan's open tail, named there under *What this does NOT do*,
> [PLAN_panel_probing_state.md](PLAN_panel_probing_state.md) — the boundary is named below.
>
> **Citations verified at `252813d3`**, each against the symbol named beside it.

## Step 0 — the prerequisite, before any code

**Does the extension have a vendor key at all, and where from?** The parent's wording — *"nothing here
has a key to call it with"* — is a statement about the code as it stands, not a promise that one is
reachable. Establish this FIRST: a key read from the environment, from the CLI's own config, or from
`creds`.

- **If a path exists**, name it here and continue to step 1.
- **If none exists**, record that answer in this file, set the status line to BLOCKED, and stop. Every
  line below is dead code until a key can be obtained, and building it first is how a feature nobody can
  reach gets written and maintained.

This is step 0 rather than a question further down because the round that reviewed this plan pointed out
that a reader who starts at *What ships* will build the parser before knowing whether anything can call
it.

## Where this came from

Issue #301 replaced a curated list of Claude models with an ASKED one: the CLI is asked which models it
actually reaches, family by family. The plan recorded, as its open tail:

> **It does not call `/v1/models`.** Nothing here has a key to call it with. When a vendor key is
> configured the API becomes the better candidate source, and that is the open tail.

## The symptom the tail names

The candidate set is four words, hard-coded — `export const CLAUDE_CANDIDATES` at
[claudeModels.ts:80](../src_vs_code/src/claudeModels.ts#L80):

```ts
export const CLAUDE_CANDIDATES: readonly string[] = ['haiku', 'sonnet', 'opus', 'fable'];
```

`probeClaudeModels` ([claudeProbe.ts:52](../src_vs_code/src/claudeProbe.ts#L52)) asks the CLI about
each, and the answers are kept for a week (`PROBE_GOOD_FOR_MS`,
[claudeModels.ts:83](../src_vs_code/src/claudeModels.ts#L83)), invalidated when the CLI version changes
(`stillGood`, [claudeModels.ts:142](../src_vs_code/src/claudeModels.ts#L142)).

That design is right for what it had: a probe cannot enumerate, so it can only ask about names it
already knows. But the consequence is that **a family not in this list is invisible**, however new the
CLI is — and the failure is silent, because the probe succeeds at everything it was asked.

`/v1/models` enumerates. When a vendor API key is configured, the candidate list stops being a guess.

## The two names are not the same name (the contract this needs)

This is the hole the code round found, and it is the reason the change is not one line. `/v1/models`
answers **API identifiers** — versioned, dated, e.g. `claude-opus-4-20250514`. `CLAUDE_CANDIDATES`
carries **CLI family words** — `opus`. `probeClaudeModels` asks the CLI, which takes the second kind.

Feeding API ids straight into an unchanged probe produces one of two silent failures: every probe
refuses, or the list comes back empty and the person sees fewer models than before the change.

So the plan needs, before the source may change:

1. **A canonical candidate identity** — what the rest of the extension holds and compares.
2. **An explicit API-id → CLI-name mapping**, or an API source filtered to CLI-accepted names only.
3. **A stated refusal**: an API id that maps to nothing is DROPPED with a reason, never passed through
   on the hope the CLI understands it. `familyOf` ([claudeModels.ts:92](../src_vs_code/src/claudeModels.ts#L92))
   is the existing seam for this and should be widened rather than duplicated.

## What ships

1. **A key, if there is one, makes the candidate list derived.** When a vendor key is available, the
   candidates come from `/v1/models`, mapped through the contract above, rather than from the constant.
2. **The constant remains the fallback, and stays the whole behaviour when there is no key.** This is
   not a migration; the no-key path must not change at all, because it is the path most installs are on.
3. **The probe is unchanged.** It still asks the CLI what it reaches — the API says what EXISTS, the CLI
   says what this install can USE, and they are different questions. Answering only the first would
   offer models the CLI refuses.
4. **The cache record says which source produced the candidates, AND which account** — see the two
   sections below.

## Every way the API call can fail, and what happens then

A configured key does not mean a reachable endpoint. The rule is one sentence: **no API failure may be
worse than having no key at all.**

| Failure | Behaviour |
|---|---|
| Timeout, or no network | Bounded by an explicit timeout and a cancellation token; fall back to `CLAUDE_CANDIDATES` |
| `401`/`403` — key rejected or revoked | Fall back to the constant, and do NOT cache the failure as an answer |
| `429` | Fall back; this is the probe's startup path and must never block on a retry ladder |
| Malformed or unexpected JSON | The parser answers *nothing*; fall back. It must never produce a candidate named `undefined` |
| An empty model list | Treated as malformed — a vendor with zero models is not an answer, it is a bug |

The probe must never be blocked by this call. Every row above is a named test.

## The key can be rotated, and the cache must notice

`stillGood` today refuses a record whose CLI version differs. That is not enough once the list depends
on an account: rotating from one key to another leaves the same `api` source and the same CLI version,
so a week-old list from the previous account is accepted — offering models the new account cannot use,
or hiding ones it can.

So the record carries a **non-secret fingerprint** of the key and endpoint — a salted hash, never the
key, never a prefix of it — and `stillGood` refuses a mismatch. The bounds at
[claudeProbeFile.ts:25-40](../src_vs_code/src/claudeProbeFile.ts#L25) are unchanged: a fingerprint is
one short field.

## The list gets longer, and the probe gets slower

Four candidates become perhaps forty. `probeClaudeModels` asks the CLI once per candidate, so the probe
can take ten times as long — and the round is right that a person then waits with nothing on screen.

Two things follow, and the first is this plan's own job:

- **The candidate list is BOUNDED before it reaches the probe.** A cap, and a rule for which ones are
  dropped (newest per family first). An unbounded list from a remote source is what turns a startup into
  a stall.
- **If the bounded list still exceeds what the current surface can report on,**
  [PLAN_panel_probing_state.md](PLAN_panel_probing_state.md) becomes a prerequisite rather than a
  neighbour. That is a measurement, not a guess: time the probe with the real list at step 1 and decide
  then. The boundary table below records both orders as legitimate for exactly this reason.

## The boundary with the plan this came from

| Item | Which plan builds it | The other plan's part | Order |
|---|---|---|---|
| Asking the CLI which models it reaches, the week-long cache, the version invalidation | [PLAN_the_models_are_asked_rather_than_listed.md](../research/PLAN_the_models_are_asked_rather_than_listed.md) | this plan leaves all of it in place | shipped first |
| Where the CANDIDATE NAMES come from when a key exists, and the id→name contract | **this plan** | named it as the open tail | after the parent |
| The no-key path | neither changes it | — | frozen |

**Disjoint**: the parent decides what the probe ASKS and how long the answer keeps; this plan decides
what it asks ABOUT. The probe itself is untouched.

## The boundary with the probe's progress reporting

The parent defers progress reporting to another open plan, and that division was legible from one side
only until now.

| Item | Which plan builds it | The other plan's part | Order |
|---|---|---|---|
| How a probe SAYS it is working — the panel's progress state across local-engine and model probes | [PLAN_panel_probing_state.md](PLAN_panel_probing_state.md) | this plan neither adds nor removes a probe surface | **either — unless the measurement at step 1 says otherwise** |
| What the probe asks about, and where those names come from | **this plan** | none | either |
| `ASKING_CLAUDE` and `claudeNote` ([claudeModels.ts:63](../src_vs_code/src/claudeModels.ts#L63)) | shipped with #301 | consumed unchanged by both | done |

**Disjoint**, with one stated dependency: a longer candidate list makes the progress plan's case
stronger, and if the bounded list makes the probe long enough to look hung, the progress plan goes
first. Nothing else couples them.

## What this does NOT do

- **It does not add a key-entry UI.** If a key has to be typed, that is a separate decision with its own
  storage and redaction questions.
- **It does not change progress reporting** — see the boundary table above.
- **It does not extend the cache's bounds.** `MOST_ENTRIES`, `LONGEST_FIELD` and `LONGEST_FILE` were
  argued on the plan round of #301 and hold: a list from an API is still bounded by construction — which
  is now this plan's explicit job rather than an assumption.

## Build order

0. **Step 0 above.** If no key path exists, mark BLOCKED and stop.
1. Time the probe with the real candidate count, so the ordering question against
   `PLAN_panel_probing_state.md` is answered by a measurement.
2. A parser for the `/v1/models` response, pure and tested against a captured sample — no network in a
   test — including the malformed and empty cases.
3. The id→name contract, on `familyOf`, with the drop-with-a-reason rule.
4. The candidate source becomes a function of "key present or not"; `CLAUDE_CANDIDATES` is its fallback,
   and every failure row above falls back to it.
5. The key fingerprint in the record; `stillGood` refuses a mismatch.

## Test plan

- The parser, against a real captured response, a malformed one, and an empty one.
- **Every row of the failure table by name** — timeout, 401, 429, malformed, empty — each falling back
  to the constant.
- The id→name contract: a known id maps, an unknown id is dropped with a reason and never reaches the
  probe.
- **The no-key path is unchanged**: an existing test pinned to the four constants must pass untouched.
- A rotated key invalidates a cached list — seen RED first by asserting `stillGood` before the
  fingerprint is added.
- The whole extension suite.

## Definition of Done

- [ ] Step 0 is answered in writing, with the key path named or the plan marked BLOCKED.
- [ ] With no key, behaviour is byte-identical to today.
- [ ] No API failure leaves the person worse off than having no key — every row of the table is a test.
- [ ] An API id that maps to no CLI name is dropped with a reason, never passed to the probe.
- [ ] A rotated key invalidates the cached list; the record holds a fingerprint, never the key.
- [ ] The candidate list is bounded before it reaches the probe, and the probe was TIMED with it.
- [ ] A model the constant does not name is reachable when a key is present.
- [ ] The boundary tables above are mirrored in
      [PLAN_the_models_are_asked_rather_than_listed.md](../research/PLAN_the_models_are_asked_rather_than_listed.md)
      and [PLAN_panel_probing_state.md](PLAN_panel_probing_state.md).
