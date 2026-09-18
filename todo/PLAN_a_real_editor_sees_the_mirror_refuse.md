# PLAN — a real editor watches the mirror refuse, and the text survive

> Status: **plan only, nothing implemented yet, 2026-09-18.** Scope:
> `src_vs_code/src/test/host/scenarios.ts` and `src_vs_code/scripts/run-host.mjs`.
>
> **Extracted from two plans on 2026-09-18**, both of which named it as buildable and did not build
> it: [PLAN_the_mirror_says_when_it_stood_down.md](../research/PLAN_the_mirror_says_when_it_stood_down.md)
> and [PLAN_a_deleted_role_stays_deleted.md](../research/PLAN_a_deleted_role_stays_deleted.md).
>
> Related: [module_tests.md](../research/module_tests.md).

## The gap, stated as both plans state it

Three scenarios now run inside a real VS Code and every one of them drives the path where things
WORK:

- a changed setting reaches `settings.json` (defect 1);
- a deletion the mirror carries takes the prompt text with it (defect 2).

**No scenario drives the path where the mirror refuses**, and that path is the one the 2026-09-16
incident is made of: a stand-down, a role that stays on the server, and text that must NOT be
deleted while it does. Every part of it is tested as a value — `mirrorSchedule.test.ts` runs the
three attempts and the supersede against an injected clock, `roleDeletion.test.ts` runs the refusal
and the claim against a map — and what no value can answer is whether the real host, the real
`serverSettingsSync` and the real configuration listener behave that way together.

Both plans give the same reason for stopping: making a write genuinely fail inside a real editor
needs something the scenario has to arrange, and arranging it is the story.

## The two ways to make a real mirror refuse

**A. The stand-down — a stamp from a newer build.** `serverSettingsSync` refuses to overwrite a
`settings.json` whose `writtenBy` names a version newer than its own
([serverSettingsFile.ts](../src_vs_code/src/serverSettingsFile.ts), `writtenBy`; the comparison is
`updateAvailable`). The scenario writes such a file before touching a setting, and everything
downstream follows: `sync()` answers `stood-down`, the schedule reports it, `reportStandDown` fires,
the deletion keeps its text, and the tombstone gains a reason.

This is the one to build. It needs no permissions, no platform-specific behaviour and no waiting —
a stand-down is decided on the first attempt, not after the eight-second ladder.

**B. The unwritable directory.** Rejected, and the reason is worth keeping: making a directory
refuse a write differs on every platform this ships to — an ACL on Windows, a mode bit on Linux,
and a macOS runner that may be neither — and the scenario would then have to sit through three
attempts and two waits to reach the report. It would be the slowest and least portable scenario in
the suite, testing a branch the value tests already cover, for the sake of an errno.

## What the scenario asserts

In order, and each one is a thing only a host can answer:

1. A `settings.json` stamped by a newer build is NOT overwritten when a `coai.*` setting changes —
   the file's bytes are the same afterwards.
2. The notification is RECORDED. Not that a toast appeared — that is VS Code's, not ours — but that
   the ledger has a `settings-stood-down` row, which is the whole point of S1–S5 and the thing the
   incident lacked.
3. A role deletion begun in that state keeps its prompt file, and its tombstone carries a reason.
4. Removing the stamp and changing a setting again lets the mirror through, and THEN the prompt file
   goes.

Four is what makes one to three mean something: a scenario that only watches a refusal passes just
as happily against a build that never writes at all.

## What it needs from the launcher

Nothing new, most likely: `run-host.mjs` already gives the host a data directory of its own through
`COAI_DATA_DIR`, which is what lets the scenario write a stamped `settings.json` before the
extension activates — or between steps, since the extension re-reads it every time.

The one thing to check while building: whether the stamp has to be in place BEFORE activation for
the first `sync()` to see it, and if so, whether the launcher or the scenario writes it. That is a
question the first run answers, not one to decide in advance.

## Test plan

It IS the test. What has to be proved is that it can fail:

| # | Break | What must go red |
|---|---|---|
| 1 | Neutralise the stand-down check in `wouldOverwriteANewerBuild`. | Assertion 1 — the newer build's file was overwritten. |
| 2 | Drop the funnel call from `reportStandDown`. | Assertion 2 — nothing was recorded. |
| 3 | Make `settled` treat `stood-down` as carried. | Assertion 3 — the text was deleted while the server still had the role. |
| 4 | Leave the stamp in place at the last step. | Assertion 4 — proves the scenario is not passing by never getting anywhere. |

Each break planted, the run watched failing, the break removed, the run watched passing — and the
failure message reported, since that is what says the assertion observed the real symptom.

## Definition of Done

- [ ] A scenario in `scenarios.ts` drives the stand-down, all four assertions.
- [ ] All four breaks planted and watched failing, with their messages recorded.
- [ ] `research/module_tests.md` names the flow and what it still does not prove.
- [ ] The scenario adds no more than a few seconds: a stand-down is decided on the first attempt.
- [ ] Promotion per `common/planning-docs.md`, then `plan-lifecycle.mjs`.
- [ ] Through `review_plan` and `review_code`.

## What this will NOT prove

That the SERVER re-reads the file, which is `module_server.md`. And not the unwritable-directory
branch, for the reasons above — that stays a value test, and this plan records the decision so the
next reader does not have to make it again.

**One thing to watch while building.** On this machine, on 2026-09-18, three clipboard scenarios
added by #393 failed reproducibly while the settings ones passed, and the same three failed on a
clean `main` checkout — the machine's clipboard, not the code. A new scenario should be judged
against a control run on `main` before anybody believes it broke something.
