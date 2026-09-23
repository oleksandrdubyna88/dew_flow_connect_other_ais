# PLAN — the extension says when a window died

> Status: **plan only, nothing implemented yet (2026-09-23).** Scope: `src_vs_code` — a run marker
> per extension host, a sweep at activation that records the windows that never finished, and the
> vectors that hold its decision to the server's. Extracted from section *H* of
> [PLAN_every_message_is_written_down.md](PLAN_every_message_is_written_down.md) on the operator's
> ruling of 2026-09-23.
>
> **Through its plan round, 2026-09-23: two reviewers (codex could not answer), 11 findings, 9
> accepted.** They changed how a death is RECORDED (with the dead run's identity, not through the
> funnel's stamp), what the vectors take as input, when the marker may be cleared, and added the
> growth budget. Marked *(round)* below.
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
| The drain on the way out, with a ceiling | `src_vs_code/src/extension.ts:614` (`deactivate`) awaits `flushChatUsage()`, which IS `flushLedgers()` (`src_vs_code/src/chatUsageFile.ts:95`, `src_vs_code/src/jsonlLedger.ts:161`) and answers whether the drain finished | the clear goes AFTER the drain, and only when it finished |
| The data directory | `src_vs_code/src/dataDir.ts:27` (`coaiDataDir`) | where the folder lives |
| The ledger the page reads | `src_vs_code/src/notificationsFile.ts:43` (`notificationsPath`) | where a death is written |
| The funnel — records, then shows | `src_vs_code/src/notify.ts:244` (`notify`), `:265` (`notifyOnce`) | the TOAST only, if question 1 wants one |
| The funnel's stamp — every record carries THIS host's run | `src_vs_code/src/notify.ts:214` (`noticeRecord(notice, RUN, process.pid, at)`) | the reason a death does NOT go through it *(round)* |
| The server's design, measured | `src_mcp/src/Server/RunMarkers.cs`, `RunLife.cs` | the rules to port, not the code |

The rest of section *H* shipped with S5: the two-chain drain to quiescence, its ceiling, and the
write-gap record. **Only the marker is missing.**

## What must be true when this is done

1. **Every extension host writes `extension-runs/{run}.json`** — `{run, pid, host, startedUtc,
   heartbeatUtc}`, the server's field list — written to a temporary file and then replaced. It is
   beaten every 60 s and cleared by its own `deactivate` AFTER the ledgers have drained — and only
   when the drain answered that it FINISHED. A drain that gave up leaves the marker behind on purpose,
   so the next start records a window whose last records nobody confirmed: the server's rule for a
   crash whose record did not land, applied to the extension's ledgers. *(round, gemini)*
2. **Its own folder, never `runs/`.** The server's sweep reads EVERY `*.json` in `runs/`
   (`RunMarkers.Read`), so an extension marker there would be recorded by the server as a *coai-mcp*
   death with a coai-mcp sentence. The extension could also delete a server's marker. Two programs,
   two folders — and `extension-runs/` joins `shared/data-inventory.json` as live state that does
   not move with the data directory.
3. **The next activation records every window whose marker went silent for more than 30 minutes, once
   per activation.** It writes an `unclean-exit` that carries the DEAD run's id and pid, so it joins
   that window's own records — and that is why it is NOT written through `notify`: the funnel stamps
   every record with THIS host's run (`notify.ts:214`), which would file the death under the healthy
   window that found it. A death is appended to the ledger with the dead run's identity, the way the
   server's `UncleanExit.Of` is — but still through the SAME serialiser (`notificationLine`,
   `src_vs_code/src/notifications.ts:291`, which redacts every field) and the same append
   (`appendLine`, `src_vs_code/src/jsonlLedger.ts:113`); only the funnel's stamp is bypassed. It
   is still ADMITTED by the finder's bounds (`BOUNDS.admit`, `notify.ts:212`, keyed on
   `unclean-exit` and the dead run): bounds limit what a run WRITES, and the finder is the writer, so
   a folder full of stale markers cannot flood the ledger — a first occurrence is never suppressed,
   and past the run budget the usual storm record says so. It carries NO `seq`: that field is this
   run's own diagnostic ordinal (`notifications.ts:106`), and on a record about another run it would
   mean nothing. *(CodeRabbit, on the pull request)* *(round, gemini)* Several windows activating at once on
   one share record each death once between them.
4. **A live window is never recorded as dead**, and that includes the case that makes this harder on
   the extension than on the server. A laptop that slept overnight wakes with every window's marker
   eight hours stale, and the first thing a person does is often open a new window, whose activation
   sweeps. So a stale marker from THIS host whose pid is alive is not a death (see *Costs* for what a
   reused pid does). "This host" is the marker's `host` — `os.hostname()`, compared as the server
   compares it, case-insensitively (`RunMarkers.cs:270`). A marker from ANOTHER host is never probed:
   its pid names nothing here, so the heartbeat alone decides, and a stale one is a death. *(CodeRabbit,
   on the pull request)* "Alive" is read from `process.kill(pid, 0)` by its ERROR CODE: `ESRCH` is the
   only answer that means gone; `EPERM` — a live process owned by someone else, which Windows and a
   sandboxed macOS both answer — means alive, and so does any error nobody named, because a missed
   death is the safe direction and a false one is not. A test drives each code. *(round, local)*
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
   unreadable file, a young unreadable file) with the decision each must produce. Liveness is an
   INPUT of each vector (`sameProcess: "alive" | "gone"`), never something the planner works out,
   because the two languages judge it differently — C# by pid and start time, Node by pid alone — and
   a vector that asked them to judge it would have no single right answer. `RunMarkers.Plan` already
   takes liveness as a predicate (`sameProcessAlive`), and the TypeScript planner will take it the
   same way. *(round, gemini)* BOTH suites answer the same file — the `data-side-vectors.json`
   pattern. Two ports that agree only by resemblance
   drift the way the credential redactors nearly did.

## Decisions taken here

- **Port, don't call.** The extension runs without the server (a chat-only window never starts
  `coai-mcp`), so it cannot ask the server to sweep for it. And a one-shot mode for another program's
  markers would couple two release lines that ship on different days. The shared vectors are what
  keep one rule.
- **Same-host liveness is the pid alone** (`process.kill(pid, 0)`). The server pairs the pid with the
  process start time; Node has no portable way to read another process's start time, and on Windows
  it would mean spawning PowerShell at activation. A reused pid therefore reads as alive: that is a
  **missed** death, never a false one, and a missed death is the safe direction. This is a PERMANENT
  limitation of this design, not a gap to close later: such a marker is neither recorded nor retired
  while the process that reused its pid lives, and is recorded as soon as that process ends.
  *(round, local)*

## Decided by the operator — the toast

> **Decided 2026-09-23: (a).** One toast per activation, saying how many windows closed without
> finishing — shown AFTER each death has been recorded with its own identity, never one toast per
> death. What follows is the question as it was put, kept for the reasoning.

1. **Is a death found at activation shown, or only recorded?** Recording is decided (requirement 3:
   each death appended with its own identity); what is open is only the TOAST. The funnel records and then shows
   (`notify.ts:244`). `notifyOnce` (`:265`) shows the first of each `(code, subject)` per run, and
   the subject here is the dead run, so every dead window would be a toast. The options:
   - (a) `notifyOnce` with the subject `unclean-exit`, so one toast per activation says how many;
   - (b) a record-only door, which the funnel's own doc calls a caller's explicit choice and which
     would be its fourth policy.

   **Recommendation: (a)**, because a window that died is news the person may not have noticed —
   one summary toast AFTER the per-death records, never a toast per death. *(round, gemini, on
   how (a) and requirement 3 fit together)*

## Build order

1. `shared/run-marker-vectors.json` and a C# test that `RunMarkers.Plan` answers every vector —
   this pins the server's rule BEFORE a second implementation exists.
2. `src_vs_code/src/runMarkerPlan.ts` — the pure planner — answering the same vectors.
3. `src_vs_code/src/runMarkers.ts` — write, beat, clear, sweep, claim — against a real temporary
   directory.
4. Wiring: `activate` starts it, and `deactivate` clears only when `flushChatUsage()` — which is
   `flushLedgers()` — answered that the drain finished. Deaths are appended with their own identity;
   then ONE `notifyOnce` toast per activation says how many windows closed without finishing
   (the operator's decision, 2026-09-23).
5. `extension-runs/` in `shared/data-inventory.json`; the docs.

## Test plan

Every item RED first, each guard broken to prove it.

1. Both suites answer every vector in `shared/run-marker-vectors.json`, and a MISSING or EMPTY
   file fails each suite rather than passing over nothing. The file is committed in build step 1,
   before either planner exists. *(round, local)*
2. The race: two sweepers over fifty stale markers record fifty deaths, run repeatedly.
3. A sweep records nothing of its own, nothing fresh, nothing from a live pid on this host, and
   never a file in `runs/`.
4. The beat: a beat that finishes after the clear leaves no marker, and neither does one whose
   replace was under way when the clear landed.
5. The wiring in the real editor (`npm run test:host`): a window that starts and stops leaves no
   marker. A host killed with no `deactivate` is recorded by the next start — with its heartbeat
   backdated by the test, as the server's scenario does.
6. `extension-runs/` is in the inventory with `move: false`, held by the inventory's own scan.
7. Liveness by error code: `ESRCH` is gone; `EPERM` and an unnamed error are alive.
8. A drain that gave up leaves the marker; one that finished clears it.
9. A death's ledger record carries the DEAD run's id and pid, never the finder's.

## Growth budget *(round, gemini — `planning-docs.md` requires one before the first write)*

- **What is in `extension-runs/`:** one marker per window open NOW (a dozen at most, ~150 bytes
  each), plus the dead not yet recorded, plus claims held for milliseconds.
- **Retirement, by every activation:** a recorded death's marker is removed under its claim; a
  leftover claim, a temporary and an unreadable file are removed once they are older than the
  30-minute window — the server's rules. A clean exit removes its own marker.
- **The one thing that can linger:** a marker whose pid was reused by a live process (see
  *Decisions*), until that process ends. It is one file per such coincidence.
- **Bound:** tens of files and a few kilobytes on any machine. No rotation is needed, and none
  ships.

## Costs accepted

- **A window asleep past the window, seen from ANOTHER machine on the same share, is a false death**
  — the same residual the server accepted, and for the same reason: a pid means nothing across a
  share.
- **A reused pid on this host is a missed death** (see *Decisions*).
- **A rename still pending when the host is killed** can leave a marker a clean exit meant to remove
  — the server's residual, unchanged.

## The boundary with the parent plan and its siblings

| Item | This plan | The other plan's part |
|---|---|---|
| The run marker on the SERVER side | reads its rules and its `runs/` folder, never writes there | [PLAN_the_server_says_what_it_did.md](../research/PLAN_the_server_says_what_it_did.md) built it, shipped 2026-09-23 |
| The run marker on the EXTENSION side | **builds it** | the parent's *H* promised it and now points here |
| The drain, its ceiling and the write-gap record | clears only after the drain finished | the parent's S5 built them, shipped |
| Reading, counting and showing `unclean-exit` rows | writes the rows | the parent's S1–S5, shipped — no new page, section or count |

The order is the server first, because this plan ports the server's MEASURED rules and pins them
with vectors before a second implementation exists. The two plans touch no file in common except
`shared/data-inventory.json` (one row each) and the vector file this one creates. *(round,
gemini: a boundary has three columns and is written on both sides)*

## Definition of Done

- [ ] Every test above written RED first, with its failure message recorded, and each guard broken.
- [ ] `shared/run-marker-vectors.json` answered by BOTH suites.
- [ ] `extension-runs/` in `shared/data-inventory.json`, `move: false`.
- [x] Question 1 decided by the operator, and the choice recorded here. *(2026-09-23: (a), one
      summary toast per activation.)*
- [ ] `research/module_extension.md` records the marker, and `research/module_tests.md` names the
      scenario.
- [ ] An extension release.
- [ ] Through `review_plan` before code, and `review_code` once for the epic.
- [ ] The parent's section *H* and its Definition of Done point here, and the parent is promoted once
      this and the `coai-mcp` release carrying S8 have both shipped.
