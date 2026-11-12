# PLAN — the server deploy says when the host rolled back

> Status: **plan only, nothing implemented yet, 2026-11-12.** Scope: `.github/workflows/deploy-server.yml` (the
> serving check and the job summary) and `deploy/systemd-release.sh` (the rollback's own report); no product code.

## Symptom

On 2026-11-09 two deploys of server 0.15.0 (runs 42680645389 and 42682410067) failed at the host's canary
(`claude: failed`). The host script rolled the box back to 0.14.1 by itself, as designed. The workflow then said
`the deploy did not take effect — https://coai.remsoft.dev still reports what it did before it ran. Nothing was rolled
back, because nothing changed.` That sentence is wrong for a reader: a rollback DID happen, on the host. Whoever reads
the run learns the opposite of what the host did.

## Where it is

- `.github/workflows/deploy-server.yml:264-268` — when the box serves what it served before (`state=unchanged`), the
  serving check prints the sentence above. It cannot tell "the swap never happened" (download or SSH failure before
  any swap) from "the swap happened and the host rolled back".
- `deploy/systemd-release.sh:244-256` — `roll_back_to`. Line 255 prints `rolled back to $previous` even when
  `switch_to` on line 254 failed, so that line alone is not proof of a working rollback either.

## Decided against (consultant advice, 2026-11-12, verified against the lines above)

**Only rewording the message when "deploy failed and before == after".** That state also describes a download or SSH
failure before any swap, so a sentence that says "the host rolled back" there would be a new wrong claim.

## Build order

1. Smallest truthful fix first, in the workflow only: when `state=unchanged`, say "the server reports the same version
   as before this attempt; the workflow did not roll anything back; the host may have restored it — read the deploy
   step's log". This works with every host script, old and new.
2. Host script: print ONE machine-readable result line per run, with a unique prefix, for exactly these outcomes —
   rollback succeeded (to which version, confirmed by the health check after the switch), rollback failed (service
   down), no predecessor (service stopped). Fix line 255 so "rolled back" is printed only when `switch_to` succeeded.
3. Workflow: capture that line even when the SSH step exits non-zero, write it into the job summary, and keep the
   run's failed status. A missing or malformed line means **unknown**, never "no rollback" — an older host script
   without the line keeps the neutral sentence from step 1 (the host script and the workflow ship separately).

## Test plan

- Host script, in its existing test harness: canary fails with a predecessor → `rolled-back` line with that version;
  `switch_to` fails during the rollback → `rollback-failed` line and no "rolled back to" sentence; no predecessor →
  `no-predecessor` line.
- Workflow logic, run by hand on the step's script: each of the three lines, plus no line at all, produces the right
  summary sentence; `before == after` with no line gives the neutral sentence.
- One real failed deploy is not required to prove this; if one happens, its summary is checked and recorded in
  `POST_DEPLOY.md`.

## Definition of Done

- [ ] No run can print "Nothing was rolled back" after the host rolled back.
- [ ] A host rollback is named in the run's summary with the version it went back to, or "unknown" when the host did
      not say.
- [ ] `roll_back_to` never prints "rolled back" after a failed switch.
- [ ] `todo/README.md` row removed and this plan promoted to `research/` with its deviations.
