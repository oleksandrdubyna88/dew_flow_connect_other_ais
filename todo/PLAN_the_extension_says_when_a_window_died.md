# PLAN — the extension says when a window died

> Status: **plan only, nothing implemented yet (2026-09-23).** Scope: `src_vs_code` — a run marker
> per extension host, a sweep at activation that records the windows that never finished, and the
> vectors that hold its decision to the server's. Extracted from section *H* of
> [PLAN_every_message_is_written_down.md](PLAN_every_message_is_written_down.md) on the operator's
> ruling of 2026-09-23.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [module_server.md](../research/module_server.md) (*A run that never finished is recorded*),
> [PLAN_the_server_says_what_it_did.md](../research/PLAN_the_server_says_what_it_did.md).

## The symptom

A VS Code window whose extension host is killed — a crash, the host running out of memory, a
force-quit, a power cut — runs no `deactivate`, and `notifications.jsonl` simply stops. Nothing says
the window died, so the page cannot tell "nothing happened after 14:02" from "the window that was
recording died at 14:02".

Section *H* of the parent plan promised that **both** halves write a run-start marker. The server's
half shipped on 2026-09-23 (epic 3 of `PLAN_the_server_says_what_it_did.md`, PR #471). The
extension's did not ship: on 2026-09-23 nothing in `src_vs_code/src` outside the tests produces an
`unclean-exit`. This was checked by searching for the code and for a marker, and it is the reason the
parent cannot be promoted.

## What exists, verified 2026-09-23

| What | Where | Use here |
|---|---|---|
| This host's run id — 12 hex characters, minted once per host | `src_vs_code/src/notify.ts:48` (`RUN`) | the marker's name and its `run` |
| The drain on the way out, with a ceiling | `src_vs_code/src/extension.ts:614` (`deactivate`), `src_vs_code/src/jsonlLedger.ts:161` (`flushLedgers`) | the clear goes AFTER the drain |
| The data directory | `src_vs_code/src/dataDir.ts:27` (`coaiDataDir`) | where the folder lives |
| The ledger the page reads | `src_vs_code/src/notificationsFile.ts:43` (`notificationsPath`) | where a death is written |
| The funnel — records, then shows | `src_vs_code/src/notify.ts:244` (`notify`), `:265` (`notifyOnce`) | the door a death goes through (question 1) |
| The server's design, measured | `src_mcp/src/Server/RunMarkers.cs`, `RunLife.cs` | the rules to port, not the code |

The rest of section *H* shipped with S5: the two-chain drain to quiescence, its ceiling, and the
write-gap record. **Only the marker is missing.**

## What must be true when this is done

1. **Every extension host writes `extension-runs/{run}.json`** — `{run, pid, host, startedUtc,
   heartbeatUtc}`, the server's field list — written to a temporary file and then replaced. It is
   beaten every 60 s and cleared by its own `deactivate` AFTER the ledgers have drained.
2. **Its own folder, never `runs/`.** The server's sweep reads EVERY `*.json` in `runs/`
   (`RunMarkers.Read`), so an extension marker there would be recorded by the server as a *coai-mcp*
   death with a coai-mcp sentence. The extension could also delete a server's marker. Two programs,
   two folders — and `extension-runs/` joins `shared/data-inventory.json` as live state that does
   not move with the data directory.
3. **The next activation records every window whose marker went silent for more than 30 minutes, once
   per activation.** It writes an `unclean-exit` that carries the DEAD run's id and pid, so it joins
   that window's own records. Several windows activating at once on one share record each death once
   between them.
4. **A live window is never recorded as dead**, and that includes the case that makes this harder on
   the extension than on the server. A laptop that slept overnight wakes with every window's marker
   eight hours stale, and the first thing a person does is often open a new window, whose activation
   sweeps. So a stale marker from THIS host whose pid is alive is not a death (see *Costs* for what a
   reused pid does).
5. **The server's measured lessons are kept, not rediscovered:**
   - a claim is an exclusive create (`FileMode.CreateNew` / `fs.open(…, 'wx')`), never a rename —
     two renames of one file both succeeded 975 times in 1000;
   - the marker is removed BEFORE the claim is released and re-read UNDER the claim;
   - a beat asks whether its owner has cleared both before and after its replace;
   - the sweep honours a stop between deaths;
   - a beat or a sweep that throws is said, and the loop lives on.
6. **The decision is written once in each language and held together by vectors.**
   `shared/run-marker-vectors.json` lists situations (fresh, stale, at the window, own marker,
   same-host alive, same-host gone, a young claim, an abandoned claim, a leftover claim, an old
   unreadable file, a young unreadable file) with the decision each must produce. BOTH suites answer
   the same file — the `data-side-vectors.json` pattern. Two ports that agree only by resemblance
   drift the way the credential redactors nearly did.

## Decisions taken here

- **Port, don't call.** The extension runs without the server (a chat-only window never starts
  `coai-mcp`), so it cannot ask the server to sweep for it. And a one-shot mode for another program's
  markers would couple two release lines that ship on different days. The shared vectors are what
  keep one rule.
- **Same-host liveness is the pid alone** (`process.kill(pid, 0)`). The server pairs the pid with the
  process start time; Node has no portable way to read another process's start time, and on Windows
  it would mean spawning PowerShell at activation. A reused pid therefore reads as alive: that is a
  **missed** death, never a false one, and a missed death is the safe direction.

## Open question — for the operator

1. **Is a death found at activation shown, or only recorded?** The funnel records and then shows
   (`notify.ts:244`). `notifyOnce` (`:265`) shows the first of each `(code, subject)` per run, and
   the subject here is the dead run, so every dead window would be a toast. The options:
   - (a) `notifyOnce` with the subject `unclean-exit`, so one toast per activation says how many;
   - (b) a record-only door, which the funnel's own doc calls a caller's explicit choice and which
     would be its fourth policy.

   **Recommendation: (a)**, because a window that died is news the person may not have noticed.

## Build order

1. `shared/run-marker-vectors.json` and a C# test that `RunMarkers.Plan` answers every vector —
   this pins the server's rule BEFORE a second implementation exists.
2. `src_vs_code/src/runMarkerPlan.ts` — the pure planner — answering the same vectors.
3. `src_vs_code/src/runMarkers.ts` — write, beat, clear, sweep, claim — against a real temporary
   directory.
4. Wiring: `activate` starts it, and `deactivate` clears after `flushChatUsage()`. It gets the funnel
   door question 1 decides.
5. `extension-runs/` in `shared/data-inventory.json`; the docs.

## Test plan

Every item RED first, each guard broken to prove it.

1. Both suites answer every vector in `shared/run-marker-vectors.json`, and a case is added to each
   vector file's own test so that an empty file fails.
2. The race: two sweepers over fifty stale markers record fifty deaths, run repeatedly.
3. A sweep records nothing of its own, nothing fresh, nothing from a live pid on this host, and
   never a file in `runs/`.
4. The beat: a beat that finishes after the clear leaves no marker, and neither does one whose
   replace was under way when the clear landed.
5. The wiring in the real editor (`npm run test:host`): a window that starts and stops leaves no
   marker. A host killed with no `deactivate` is recorded by the next start — with its heartbeat
   backdated by the test, as the server's scenario does.
6. `extension-runs/` is in the inventory with `move: false`, held by the inventory's own scan.

## Costs accepted

- **A window asleep past the window, seen from ANOTHER machine on the same share, is a false death**
  — the same residual the server accepted, and for the same reason: a pid means nothing across a
  share.
- **A reused pid on this host is a missed death** (see *Decisions*).
- **A rename still pending when the host is killed** can leave a marker a clean exit meant to remove
  — the server's residual, unchanged.

## The boundary with the parent plan and its siblings

| Item | Built by |
|---|---|
| The run marker on the SERVER side | [PLAN_the_server_says_what_it_did.md](../research/PLAN_the_server_says_what_it_did.md), shipped 2026-09-23 |
| The run marker on the EXTENSION side | **this plan** |
| The drain, its ceiling and the write-gap record | the parent's S5, shipped |
| Reading, counting and showing `unclean-exit` rows | the parent's S1–S5, shipped — no new page, section or count |

## Definition of Done

- [ ] Every test above written RED first, with its failure message recorded, and each guard broken.
- [ ] `shared/run-marker-vectors.json` answered by BOTH suites.
- [ ] `extension-runs/` in `shared/data-inventory.json`, `move: false`.
- [ ] Question 1 decided by the operator, and the choice recorded here.
- [ ] `research/module_extension.md` records the marker, and `research/module_tests.md` names the
      scenario.
- [ ] An extension release.
- [ ] Through `review_plan` before code, and `review_code` once for the epic.
- [ ] The parent's section *H* and its Definition of Done point here, and the parent is promoted once
      this and the `coai-mcp` release carrying S8 have both shipped.
