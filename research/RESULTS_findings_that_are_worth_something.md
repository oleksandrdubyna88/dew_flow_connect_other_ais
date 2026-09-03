# RESULTS — not how many findings, but how many are worth acting on

> 2026-09-02. Five models reviewed the same commit under the operator's own settings; every finding
> was then judged **by reading the code it names**. 35 findings, judged one at a time.
>
> The counting half is [RESULTS_five_plans_five_models.md](RESULTS_five_plans_five_models.md). This
> is the half that says what the counts were worth, and the two disagree sharply.

## Method, and its one weakness stated first

The subject is commit `2b7d3ab` — the local-model reviewer — chosen because it is the change whose
code this record's author wrote, so every claim can be checked against what the code actually does
rather than against an impression of it.

**That is also the weakness.** Judging findings about your own change invites defending it, and it
did: three findings were first marked wrong and turned out to be right when the file was opened. All
three are named below. Every verdict here was reached by reading the line the finding cites, and the
three reversals are the reason none of them was taken on memory.

Categories: **true and useful** (a real defect or a real cost), **true against a written rule**
(a convention violation with no runtime risk), **duplicate** (the same defect twice from one model),
**wrong** (misreads what the code does).

## The table

| model | findings | true & useful | rule-only | duplicate | **wrong** | precision |
|---|---|---|---|---|---|---|
| Claude Sonnet 5 (native) | 7 | 6 | 1 | 0 | 0 | **100 %** |
| GPT-5.6-Luna | 7 | 6 | 1 | 0 | 0 | **100 %** |
| Gemini 3.7 Flash (Med) | 5 | 4 | 1 | 0 | 0 | **100 %** |
| Gemma4 26B, local | 6 | 4 | 0 | 1 | 1 | 67 % |
| Qwen3.5 35B, local | 10 | 4 | 1 | 1 | 4 | 50 % |

**The ranking inverts.** By raw count across all five subjects the local models led — Qwen 35
findings, Gemma 34, against Sonnet's 28. On the subject where every finding was checked, Qwen
produced the most findings and the fewest correct ones. Counting findings measures how much a model
says; only reading measures how much of it is true.

## The hosted models, in both modes

The five rows above give every model one input shape: the hosted three explored the checkout, the
local two were handed a prompt. That is not a comparison of models, so the hosted three were run
again on the same commit with `COAI_CODE_WORKSPACE=none` — the identical prompt, in an empty
directory, which is what a local reviewer has always had.

Every finding in both modes was then judged the same way — by opening the file it cites.

| model | mode | findings | **true & useful** | rule-only | dup | **wrong** | precision | tokens in |
|---|---|---|---|---|---|---|---|---|
| Gemini 3.7 Flash | with checkout | 5 | 4 | 1 | 0 | 0 | 100 % | 610k |
| Gemini 3.7 Flash | **diff only** | 10 | **8** | 1 | 1 | 0 | 100 % | **266k** |
| GPT-5.6-Luna | with checkout | 7 | 6 | 1 | 0 | 0 | 100 % | 515k |
| GPT-5.6-Luna | **diff only** | 11 | **10** | 1 | 0 | 0 | 100 % | **300k** |
| Claude Sonnet 5 | with checkout | 7 | 6 | 1 | 0 | 0 | 100 % | 1 952k |
| Claude Sonnet 5 | **diff only** | 8 | **7** | 1 | 0 | 0 | 100 % | **579k** |
| Qwen3.5 35B local | diff only, always | 10 | 4 | 1 | 1 | **4** | 50 % | 117k |
| Gemma4 26B local | diff only, always | 6 | 4 | 0 | 1 | **1** | 67 % | 130k |

**Useful findings roughly double, and nothing is lost.** Flash goes from four to eight, Luna from
six to ten, Sonnet from six to seven — while every hosted model stays at 100 % precision and spends
between a half and a third of the tokens. Not one wrong finding appeared in diff-only mode from any
of the three.

**The three defects that only appeared without a checkout**, each verified in the file it cites:

- **`ReadResponse` throws on a valid but non-object response.** `JsonDocument.Parse` succeeds for
  `[]`, `42` or a bare string, and `RootElement.TryGetProperty` then throws
  `InvalidOperationException` rather than returning a classified failure — `LocalAsk.cs`, no
  `ValueKind` check on the root. Found by **flash and luna**.
- **The endpoint is never normalised to `/v1`.** `LocalRuntime.cs:88` takes `baseUrl` exactly as
  typed, while the extension normalises only for the PROBE — so an endpoint that answered discovery
  at the server root 404s at review time. Found by **flash**.
- **One `state.localEngine` is handed to every vendor card.** `panelProvider.ts:188` looks up
  `vendors.find(v => v.runtime === 'local')` and `panelView.ts:236` passes that single value to
  every card, so a second local reviewer on a different engine displays the first one's models.
  Found by **sonnet**.

Plus one that is real but smaller than it reads: **every local invocation leaves its prompt file
behind** (luna, `LocalRuntime.cs:84`). There is no cleanup on any of the round's own paths — true —
but the file lands in the answers directory, which `PruneOldAnswerDirs` sweeps on a later round, so
it is untidy rather than unbounded.

**Taking the repository away made every hosted model find MORE, at a third to a quarter of the
tokens.** Nothing about the prompt changed — the diff and the project's rules are assembled by the
server either way — so the only thing removed was the wandering.

**And the wandering was costing findings, not buying them.** Both defects that Flash missed with a
checkout — the `{}` schema fallback and the randomised seed — it found without one. A reviewer given
a repository spends its attention deciding where to look; a reviewer given a diff reads the diff.

Two findings appeared only in diff-only mode and only from one model each, and both are real:

- **A second local vendor shows the first one's models.** `probeLocalEngine` looks up
  `vendors.find(v => v.runtime === 'local')` and hands that one `state.localEngine` to every card,
  so two local reviewers on different engines display one list (**sonnet**, `panelView.ts:238`).
- **Every local invocation leaves its prompt file behind** — the full prompt, with source and diffs,
  under a GUID name with no cleanup on success, failure or timeout (**luna**, `LocalRuntime.cs:91`).

**Eight real defects, not five.** The five below were found with a checkout; the three named above
appeared only without one. All eight are open.

**What this does not settle.** Three commits' worth of the fair run were still outstanding when it
was stopped, so this is one subject deeply rather than five broadly, and the finding counts above
were judged for correctness only on the with-checkout mode. The direction is large and consistent
across all three models; the size of it is one commit's worth of evidence.

## Five real defects nobody knew about

Every one of these was verified in the file it names, and none was in the four already-known
findings that this change deferred.

| defect | found by | verified |
|---|---|---|
| `staticKey` omits `state.localEngine`, so a reprobe changes the model list and the panel never repaints | **flash alone** | `panelView.ts:1001` — the key lists settings, vendors, codexModels, versions, usageWindow, openSections. Not localEngine. This is the exact defect class `liveRepaint.test.ts` exists for |
| The local seed is `prompt.GetHashCode()`, which .NET randomises per process — so it is not reproducible, and the comment above it claims it is | qwen, luna, **sonnet** | `Program.cs:120`, directly under a comment reading *"the same round asked twice is the same request, which is what makes a local reviewer reproducible at all"* |
| A missing schema file still substitutes `{}`, sending an unconstrained request | **luna alone** | `Program.cs:115-117`. The empty-schema fallback was removed from `LocalAsk` and left in `Program` — the same one-decision-in-two-places pattern as four other defects the same day |
| The final discovery fallback reports `connection refused` whatever actually happened | **luna alone** | `localEngines.ts:266`. Per-candidate reasons are carried at line 236 and then discarded at the last return |
| The endpoint field is hidden when `baseUrl` is empty, so the "Another OpenAI-compatible endpoint" preset — which ships with an empty `baseUrl` — cannot be given one | **gemma alone** | `panelView.ts:255`, `vendor.baseUrl.length === 0 && !local`, against `VENDOR_PRESETS`' entry with `baseUrl: ''` |

**A model running on this machine for free found one of them, and no hosted model did.** That is the
argument for a second reviewer stated as a fact rather than as a principle.

## Where the author was wrong, and the models right

Three findings were judged wrong from memory and were correct when the file was opened:

1. **luna's missing-schema fallback.** This record's author had told the operator hours earlier that
   the `{}` fallback "exits 65 before any request". True of `LocalAsk.RequestBody`; false of
   `Program.cs`, which still substitutes `{}` when the file is absent.
2. **luna's lost discovery reason.** The per-candidate `why` exists — and the last line throws it
   away.
3. **gemma's hidden endpoint field.** Dismissed as "custom vendors do get a baseUrl field". They do
   when they already have one; the preset that exists to be given one starts empty.

Two findings WERE wrong and stayed wrong: the probe race (qwen, gemma) is handled — the answer is
discarded when the endpoint changed mid-flight, `panelProvider.ts:207` — and the stale-model list
does have a recovery control, the ⟳ button.

## The most-agreed finding is the least valuable

Four of the five models raised the same thing: `--ask-local` writes token JSON to stdout while
CLAUDE.md says *"The one sanctioned stdout write is `--help`."* Highest agreement in the set.

It is **true against the written rule and carries no runtime risk**: that mode does not speak the
protocol, and the code says so in a comment beside the line. The conventions pass did exactly its
job — it found a place where the code took an exception the rule never granted. The fix is one
sentence in CLAUDE.md or one line of code, and it would not have prevented any failure.

**This qualifies the campaign's other headline.** Agreement between models is the strongest signal
this product has for *"look here"*, and it is not a signal of severity: the thing four models agreed
on was the cheapest finding of the thirty-five, while three of the five genuinely new defects were
each found by exactly one model.

## What this changes

- **Do not rank models by finding count.** Qwen produced twice Flash's output on this subject and
  the same number of correct findings.
- **A local model earns its place as an ADDITIONAL reviewer, not a cheaper one.** Gemma's unique
  find is real and cost nothing; its precision is 67 % and Qwen's is 50 %, so the findings need a
  reader in a way the hosted models' did not here.
- **Precision was 100 % for all three hosted models** on this subject. That is one commit and 19
  findings between them — a small sample, and the number to check next rather than to rely on.
