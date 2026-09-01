# RESULTS — the conventions prompt, three variants and a variance control

> Measured 2026-09-01. Eight cells: three prompt variants × two vendors, plus the shipped variant
> run three times on identical input. Raw answers and the scorer are throwaway; this file is the
> record.

## What was being decided

Round 1 of the code stage now judges the diff against the rules the project wrote down
([PLAN_conventions_pass.md](PLAN_conventions_pass.md), promoted). The question was which
prompt to ship for it:

| Variant | The hypothesis |
|---|---|
| **A** — rules verbatim, judge freely | the rules pasted in, "report violations" |
| **B** — rules as a checklist | build a numbered list of checkable statements first, then walk it |
| **C** — verbatim + an explicit prohibition | A, plus "a convention you believe in that this project has not written down is NOT a finding" |

## The input, and one correction to it

A 58-line C# diff with four seeded violations of rules this repository has actually written down,
plus what I intended as a decoy: `Active` returning `List<AccountView>` rather than
`IReadOnlyList<T>`, which I labelled "a defensible criticism the project never wrote down".

**The first run refuted that.** The shared conventions do write it down —
*"Public contracts expose `IReadOnlyList<T> { get; init; }` … never `List<T> { get; set; }`"*. So the
decoy was a fifth real violation, every "taste" finding about it was correct, and the metric I had
built the measurement around was wrong.

It was replaced with one that is checkable rather than judged: does a finding's quoted rule actually
appear in the block the reviewer was given? A distinctive five-word window, matched against the
normalised rules text. That cannot be argued with, and it is the failure mode that matters — a
conventions pass inventing a standard is worse than no pass at all.

## Results

| cell | findings | seeded found (of 4) | quoted a rule that exists | could not be quoted | tokens in / out |
|---|---|---|---|---|---|
| A · antigravity | 8 | 3 | 8 | **0** | 44.3k / 17.8k |
| A · codex | 7 | 3 | 7 | **0** | — |
| B · antigravity | 6 | 3 | 6 | **0** | 25.6k / 10.3k |
| B · codex | 8 | 3 | 8 | **0** | — |
| C · antigravity | 6 | 3 | 6 | **0** | 25.7k / 10.9k |
| C · antigravity, run 2 | 6 | 3 | 6 | **0** | 25.7k / 12.9k |
| C · antigravity, run 3 | 6 | 3 | 6 | **0** | 25.7k / 13.3k |
| C · codex | 9 | 3 | 9 | **0** | — |

Codex reports no token usage on this path, so its cells have no numbers rather than zeros.

### 1. Nothing was invented. Fifty-six findings, every one quotable.

Across all eight cells, **not one finding cited a rule that was not in the block.** The prohibition
in variant C was written to prevent exactly that, and the measurement says it prevented nothing,
because nothing needed preventing: given the rules, both vendors stayed inside them.

That is the result worth keeping. The value is in putting the rules in the prompt AT ALL — before
this, three rounds of review on this product's own commits referenced a project rule *zero* times,
because no rule was ever in the prompt.

### 2. The three variants are indistinguishable.

Same three seeded violations, every cell. Finding counts spread 6–9, and they spread as widely
between VENDORS on one variant as between variants on one vendor. The checklist step in B bought
nothing measurable; the prohibition in C bought nothing measurable.

The variance control makes that a statement rather than a guess: the same prompt, the same input,
three times, produced **6 findings each time, the same three seeded violations each time**, with
output tokens 10.9k / 12.9k / 13.3k. Stable — so a difference of one finding between variants is
inside the noise, and a difference of three is not much outside it.

### 3. All eight missed the same violation — and it is the one written as a TABLE.

Every cell found: the mutable `class` DTO, the primitive `string email`, the `List<T>` contract, the
cyclomatic complexity, the missing primary constructor, the non-static helper. Codex additionally
found the knowledge-base DoD (*"code change has no required knowledge-base update"*), which nothing
prompted it to look for.

**Not one of the eight flagged `Find` returning `AccountView?` with `return null;`** — against
CLAUDE.md §5, *No Null in Business Logic*.

I nearly recorded this as "variant C found 4/4", because my scorer searched each answer's whole JSON
for the word "null" and found it inside a *different* finding's rule quote. Reading the finding
showed it was about complexity. The same mistake as the decoy, one metric later: a check that is
easy to satisfy measures the check.

**The hypothesis, untested:** every rule they caught is written as a prose sentence; the one they
missed is a row in a table (`Method returns "nothing" | return null; | return [];`). A rule may be
harder for a reviewer to apply when it is written as a table cell than when it is written as a
sentence — which would be a finding about the RULES, not the prompt. It is the next measurement and
it has not been run, so it is not a conclusion.

## What shipped, and why

**Variant C**, and not because it measured better — it did not.

- All three are equal on everything this input could measure.
- C's extra clause costs ~400 bytes of prompt and guards a failure mode this input could not test:
  there was no plausible-but-unwritten convention available to smuggle in, because every "extra"
  finding turned out to quote a real rule. A guard that costs nothing against a risk the experiment
  could not create is worth keeping, and saying so is more honest than claiming it won.
- B's checklist step is the one to revisit if a much larger rules block starts producing misses:
  splitting the rules first is the only variant whose cost scales with rule COUNT rather than length.

The shipped text is `src_mcp/src/prompts/conventions.md`, printed verbatim in the extension's help
under *Prompts in full* and held byte-for-byte against it by a test.
