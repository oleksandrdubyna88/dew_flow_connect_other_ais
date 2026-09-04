# Predictions, written before the runs

Recorded at the moment the campaign was launched, and not edited afterwards. A measurement whose
expectation is written down after the numbers arrive measures nothing.

## The three arms

| # | Prediction | Why I expect it |
|---|---|---|
| P1 | `plain` produces MORE than 4 units more often than `commands` does | nothing tells it to stop at four; the smoke run gave 6 |
| P2 | `commands` matches the ordered shape (2–4 units, flat for Stories, two levels for Epics) in the clear majority of runs | it is the one explicit instruction in the prompt |
| P3 | `reviewsEachUnit` / `commitsEachUnit` are TRUE in every arm, including `plain` | the smoke run already showed the model claiming both unprompted — I expect these two to be saturated and therefore worthless as evidence |
| P4 | `batchesQuestions` is TRUE in `commands` and `epic`, and mostly FALSE in `plain` | the autonomy order says it in as many words; nothing in the task prompt suggests it |
| P5 | `namesFable` is TRUE in `commands` only — never in `plain`, never in `epic` | it is named in exactly one command, and the epic arm does not carry it |
| P6 | the `epic` arm does NOT re-split: no `epic` in a unit kind, and its unit shape is a build breakdown rather than a second generation of epics | this is the whole point of the guard; if it fails, the wording is wrong |
| P7 | the `epic` arm's re-split rate is NOT WORSE than `plain`'s | the interesting comparison for the operator's "no need to check further": if silence is as good as the explicit order, the order is only earning its place through the review/commit half |

## The metric over the corpus

| # | Prediction | Why |
|---|---|---|
| P8 | the only plan in this repository that was ACTUALLY split into epics — `PLAN_connect_other_ais` — is verdicted `Epics` | it is the one true positive available, and if the metric misses it the metric is wrong |
| P9 | the epic plans it produced (`PLAN_epic_01..06`) are verdicted below `Epics` | they are the RESULT of a split; a metric that wants to split them again is a metric that recurses |
| P10 | P9 will have at least one exception | `epic_01` names 16 files across 6 areas on 96 lines, and the breadth axis does not know it is already a piece |
| P11 | verdict correlates with the size of the change that actually implemented the plan (commits, and lines changed) | if it does not, the metric is measuring prose length rather than work |

## What would make me say the feature is wrong

- `commands` no better than `plain` on P1/P2/P4/P5 → the orders are decoration.
- `epic` re-splitting into epics → the guard's wording does not work and needs rewriting.
- P11 flat or inverted → the two-axis verdict is not measuring work and should not be shipped as advice.
