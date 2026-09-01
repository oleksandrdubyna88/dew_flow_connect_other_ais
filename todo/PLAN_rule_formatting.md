# PLAN — does the FORMAT of a written rule change whether a reviewer can apply it?

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp/src/prompts/conventions.md` and,
> if the answer is yes, the shape of the rules in `CLAUDE.md` and `.claude/rules/**`.
>
> Related docs: [RESULTS_conventions_prompt.md](../research/RESULTS_conventions_prompt.md),
> [PLAN_conventions_pass.md](../research/PLAN_conventions_pass.md).

## The observation

The conventions-pass measurement of 2026-09-01 ran eight cells — three prompts × two vendors, plus a
variance control — over one diff with four seeded violations of rules this repository has written
down. Every cell found three of the four. **All eight missed the same one**, and the miss survived a
variance control, so it is not noise.

The three they caught are written as prose sentences:

- *"Use `record` (immutable) or `record class` (mutable) for all data containers"*
- *"Primary constructors for DI"*
- *"No method may exceed a cyclomatic complexity of 4"*

The one they missed is written as a table:

| Situation | Wrong | Correct |
|---|---|---|
| Method returns "nothing" | `return null;` | `return [];` / `return string.Empty;` |

The diff's `Find` method returns `AccountView?` and `return null;` — squarely in that row, and
nothing flagged it.

## The hypothesis, and what would refute it

**H1.** A rule written as a table row is applied less often than the same rule written as a prose
sentence, because the reviewer has to reconstruct the sentence before it can quote one.

**H0 (the null hypothesis worth taking seriously).** The miss has nothing to do with formatting:
`AccountView?` with an explicit null check is idiomatic C# that six models have seen a million times,
and the reviewers passed over it for the same reason a human would. If that is the cause, rewriting
the rule as prose changes nothing.

These make opposite predictions, which is what makes the measurement worth running.

## Build order

1. **Two rule blocks, one diff.** The same 58-line diff and the same prompt. Block P has the no-null
   rule as a prose sentence; block T has it exactly as it is today, a table row. Nothing else differs
   — same file, same position, same neighbouring rules.
2. **Six cells minimum**: two blocks × two vendors, plus the winning block run three times for a
   variance control. The 2026-09-01 control showed this input is stable at ±0 seeded violations, so
   a difference of one is meaningful here in a way it usually is not.
3. **A second table-row rule** as a control against the null rule being special: the coding-style
   rules carry several. If prose beats tables, it should beat them for both.
4. If H1 holds, the cheap fix is a line in `conventions.md` (*"a rule given as a table row is a rule;
   read each row as a sentence"*) and it must be measured too — a prompt line is not a fix until it
   moves the number. If it does not, the expensive fix is rewriting the rules, and the measurement
   says which rows are worth it.

## Test plan

Not a unit test: a measurement, recorded in `research/RESULTS_rule_formatting.md` in the shape the
conventions-prompt record uses. What the harness must do:

- Score by finding TITLE, never by searching the answer's whole JSON. The first version of the last
  scorer credited a cell with catching the null return because the word appeared inside a different
  finding's rule quote.
- Report unquotable findings, so a block that produces more findings by loosening the standard is
  visible as that rather than as an improvement.
- Keep every raw answer.

## Definition of Done

- [ ] Six cells run, scored by title, with a variance control.
- [ ] `research/RESULTS_rule_formatting.md` records the numbers and which hypothesis survived.
- [ ] If H1 holds: the prompt line or the rule rewrite is applied AND re-measured.
- [ ] If H0 holds: it is written down as a refutation, and the no-null rule's table stays.
