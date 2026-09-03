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
