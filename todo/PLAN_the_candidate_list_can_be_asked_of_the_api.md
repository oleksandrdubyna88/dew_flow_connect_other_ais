# PLAN — the candidate list can be asked of the API

> Status: **plan only, nothing implemented yet, 2026-09-17.** Scope:
> `src_vs_code/src/claudeModels.ts` (`CLAUDE_CANDIDATES`), `src_vs_code/src/claudeProbe.ts` (the probe
> that spends the time) and `src_vs_code/src/claudeProbeFile.ts` (what is kept between windows).
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_the_models_are_asked_rather_than_listed.md](../research/PLAN_the_models_are_asked_rather_than_listed.md)
> — this is that plan's open tail, named there under *What this does NOT do*,
> [PLAN_panel_probing_state.md](PLAN_panel_probing_state.md) — the boundary is named below.
>
> **Citations verified at `252813d3`**, each against the symbol named beside it.

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

## The boundary with the plan this came from

| Item | Which plan builds it | The other plan's part | Order |
|---|---|---|---|
| Asking the CLI which models it reaches, the week-long cache, the version invalidation | [PLAN_the_models_are_asked_rather_than_listed.md](../research/PLAN_the_models_are_asked_rather_than_listed.md) | this plan leaves all of it in place | shipped first |
| Where the CANDIDATE NAMES come from when a key exists | **this plan** | named it as the open tail | after the parent |
| The no-key path | neither changes it | — | frozen |

**Disjoint**: the parent decides what the probe ASKS and how long the answer keeps; this plan decides
what it asks ABOUT. The probe itself is untouched.

## The boundary with the probe's progress reporting

The parent defers progress reporting to another open plan, and that division was legible from one side
only until now.

| Item | Which plan builds it | The other plan's part | Order |
|---|---|---|---|
| How a probe SAYS it is working — the panel's progress state across local-engine and model probes | [PLAN_panel_probing_state.md](PLAN_panel_probing_state.md) | this plan neither adds nor removes a probe surface | either |
| What the probe asks about, and where those names come from | **this plan** | none | either |
| `ASKING_CLAUDE` and `claudeNote` ([claudeModels.ts:63](../src_vs_code/src/claudeModels.ts#L63)) | shipped with #301 | consumed unchanged by both | done |

**Disjoint**: a longer candidate list makes the progress plan's case stronger — a model probe is seconds
rather than milliseconds — but neither plan needs the other to land first.

## What ships

1. **A key, if there is one, makes the candidate list derived.** When a vendor key is available to the
   extension, the candidates come from `/v1/models` rather than from the constant.
2. **The constant remains the fallback, and stays the whole behaviour when there is no key.** This is
   not a migration; the no-key path must not change at all, because it is the path most installs are on.
3. **The probe is unchanged.** It still asks the CLI what it reaches — the API says what EXISTS, the CLI
   says what this install can USE, and they are different questions. Answering only the first would
   offer models the CLI refuses.
4. **The cache record says which source produced the candidates**, so a key added later invalidates a
   key-less answer the same way a CLI version change already does — `stillGood` gains one more mismatch
   to refuse.

## The open question, to answer before building

**Does the extension have a vendor key at all, and where from?** The tail's own wording — *"nothing here
has a key to call it with"* — is a statement about the code as it stands, not a promise that one is
reachable. Establish this FIRST: a key read from the environment, from the CLI's own config, or from
`creds`. If no path to a key exists, this plan is blocked and should say so rather than invent one.

## What this does NOT do

- **It does not add a key-entry UI.** If a key has to be typed, that is a separate decision with its own
  storage and redaction questions.
- **It does not change progress reporting** — see the boundary table above.
- **It does not extend the cache's bounds.** `MOST_ENTRIES`, `LONGEST_FIELD` and `LONGEST_FILE`
  ([claudeProbeFile.ts:25-40](../src_vs_code/src/claudeProbeFile.ts#L25)) were argued on the plan round
  of #301 and hold: a list from an API is still bounded by construction, and if it is not, that is the
  finding.

## Build order

1. Answer the key question above. If blocked, record the answer in this plan and stop.
2. A parser for the `/v1/models` response, pure and tested against a captured sample — no network in a test.
3. The candidate source becomes a function of "key present or not"; `CLAUDE_CANDIDATES` is its fallback.
4. `stillGood` refuses a record whose source differs from the one now available.

## Test plan

- The parser, against a real captured response and against a malformed one — the boundary must refuse
  rather than produce a candidate named `undefined`.
- **The no-key path is unchanged**: an existing test pinned to the four constants must pass untouched.
- A key appearing invalidates a key-less cache record — seen RED first by asserting `stillGood` before
  the mismatch is added.
- The whole extension suite.

## Definition of Done

- [ ] The key question is answered in writing, with the path named or the plan marked blocked.
- [ ] With no key, behaviour is byte-identical to today.
- [ ] A model the constant does not name is reachable when a key is present.
- [ ] The boundary tables above are mirrored in
      [PLAN_the_models_are_asked_rather_than_listed.md](../research/PLAN_the_models_are_asked_rather_than_listed.md)
      and [PLAN_panel_probing_state.md](PLAN_panel_probing_state.md).
