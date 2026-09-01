# What the prompt measurements actually established (2026-08-31 → 09-01)

> Status: **measurement record.** Three experiments, one of which refuted the reading of the other
> two. Everything here is a number that was observed, with the run that produced it.

## 1. Narrow lenses against the universal prompt (plan stage)

Three plans from three repositories × three plan prompts, Antigravity `gemini-3.7-flash-high`,
one run each.

| plan | universal | assumptions | human path | union |
|---|---|---|---|---|
| payment_instruments | 4 | 4 | 4 | 9 |
| corpus_variants | 6 | 3 | 4 | 12 |
| scoremeter_port | 4 | 4 | 3 | 8 |
| **total** | **14** | **11** | **11** | **29** |

Read alone this looks decisive: the union finds roughly twice what the best single prompt does, and
each lens carries a large unique share (universal 10 of 14, assumptions 7 of 11, human path 9 of 11).

## 2. The control that refuted it

The SAME prompt, the SAME plan (`corpus_variants`), three runs:

| run | findings | overlap with the others |
|---|---|---|
| 1 | 6 | — |
| 2 | 4 | 3 of 6 with run 1 |
| 3 | 5 | 1 of 6 with run 1, **0 of 4** with run 2 |

Two runs of one prompt on one text shared **nothing**. Run-to-run variance alone produces
near-disjoint finding sets, so the "unique share" in §1 is not evidence of a lens effect — it is
what resampling looks like. A peer session reached the same observation independently, from the
other direction: identical plan text in two consecutive rounds produced ten and eight findings with
zero overlapping titles.

**Conclusion:** the lenses are offered because they are useful to AIM a reviewer, not because they
were shown to find more. The honest claim is that this measurement cannot tell them apart from
noise.

## 3. Rotation against the universal prompt (code stage)

Two rounds each over the same real diff, codex + antigravity, three roles per vendor.

| arm | rounds | raw findings | distinct after dedup | wall clock | input tokens |
|---|---|---|---|---|---|
| universal both rounds | 2 | 31 | **25** | 503 s | 1.43 M |
| rotating lenses | 2 | 30 | **17** | 396 s | 1.00 M |

Rotation produced fewer distinct findings for less money. With one trial per arm and the variance
measured in §2, that difference is **inside the noise** and is not evidence either way. It is
recorded because it is what happened, not because it settles anything.

**Product decision:** rotation stays OFF by default. It is an aim, not an improvement.

## 4. What the measurements DID establish

- **A single reviewer run is thin evidence.** Whatever prompt it used.
- **Agreement is the signal worth ranking by.** In the code round that reviewed the prompt-catalog
  commit, the findings both vendors raised independently — the frozen rotation, the shared mutable
  `lastRaw`, the ledger's indented serializer — were all real defects. Every single-vendor finding
  needed judgement; none of the cross-vendor ones did.
- **The panel's value is the panel**, not the wording: more reviewers and more rounds, with the
  `providers` list on every finding, is what turns a noisy signal into a decision.

## 5. What would settle §1 and §3 properly

Five or more runs per arm, scored against a fixed reference set of known defects in a plan written
for the purpose. That is a day of vendor time and it was not spent, because the answer would not
change what the product does: both prompts stay in the catalog either way, and rotation stays a
switch the operator owns.
