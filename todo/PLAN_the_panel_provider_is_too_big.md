# PLAN — `panelProvider.ts` is 4 022 lines against a ceiling of 800

> Status: **plan only, nothing implemented yet, 2026-09-18.** Scope: `src_vs_code/src/panelProvider.ts`
> and the modules extracted from it. No behaviour change anywhere in the series — that is the
> constraint, not an aspiration.
>
> Opened by operator ruling on 2026-09-18, after the tail plan measured the ceiling and found it
> losing: **+470 lines above 800 across the eight offenders in twenty-four hours**, against the 3 640
> one deliberate split removed over a week. `panelProvider.ts` is the largest of the eight and grew
> **3 762 → 4 022** inside that day.
>
> **On the parent's model**, and that phrase means something exact here:
> [PLAN_the_command_file_is_too_big.md](../research/PLAN_the_command_file_is_too_big.md) took
> `chatCommand.ts` from 4 183 lines to 543 in fifteen modules with **no behaviour change**, proved by
> `scripts/prove-move.mjs` — 3 388 moved lines verbatim, zero residue. Its most valuable output was a
> FAILURE: a first attempt on a base 28 commits stale would have silently reverted the notifications
> ledger for every call site it moved, with a green suite, a clean typecheck and a `proceed` from the
> gate. Everything below is shaped by that.

## The symptom, measured

```
src_vs_code/src/panelProvider.ts   4 022 lines   5.0× the ceiling
```

One `export class PanelProvider` spans lines **200–3 917** — 3 717 lines of the 4 022. It is not a file
of functions that can be lifted out; it is a single class with roughly **forty fields** and **ninety
methods**, and that is why this needs a plan rather than an afternoon.

## Why the parent's method does not transfer unchanged

`chatCommand.ts` was a file of top-level functions. Moving one was a cut and a paste, and
`prove-move.mjs` could assert every moved line verbatim.

A method carries **`this`**. Extracting `refreshClaudeProbe` means deciding what its state is and who
owns it — which is a design decision on every single extraction, and design decisions are where
behaviour changes hide. So:

- **Each extraction takes its FIELDS with it.** A cluster that keeps reaching back into
  `PanelProvider` for state has not been found yet; find a better seam rather than pass `this`.
- **`prove-move.mjs` still applies to the bodies.** The method body moves verbatim; only its
  signature and its state access change. The prover is run per extraction against the pre-split file,
  and the lines it cannot match are exactly the lines a reviewer must read.
- **The class keeps a field holding the extracted unit**, and its former methods become one-line
  delegations, or disappear if nothing outside the cluster called them.

## The seams, measured rather than guessed

Read off the file on 2026-09-18. Line numbers are where each cluster sits **today** and will move as
earlier extractions land; each story re-reads them rather than trusting this table.

| # | cluster | its state | its methods | ~lines |
|---|---|---|---|---|
| 1 | **The Claude probe** | `claudeProbe`, `claudeProbeRead`, `claudeProbeInFlight`, `askingClaude`, `claudeAskedFor`, `claudeCliVersion`, `claudeProbeFailedAt` | `claudeProbeAnswer` (826), `claudeProbeToShow` (856), `refreshClaudeProbe` (876), `readClaudeProbe` (956), `keepClaudeProbe` (974) | ~180 |
| 2 | **The bugs corpus** | `bugzCache`, `bugzAt`, `review` | `bugz` (2662), `collectBugs` (2801), `watchCollect` (2843), `reviewBugs` (2896), `bugsKeys` (2936), `sendBugs` (2955), `watchSend` (3010), `setBugsKey` (3052), `setBugsServer` (3067) | ~420 |
| 3 | **Team servers** | `catalogs`, `teamCheckedAt` | `teamServers` (2422), `teamServerStates` (2433), `carryTeamLogins` (1935), `addTeamServer` (3103), `reachable` (3141), `serverNamed` (3165), `signInTeamServer` (3169), `signInAndTell` (3193), `signOutTeamServer` (3209), `removeTeamServer` (3239), `refreshCatalog` (3303), `refreshTeamServers` (3350), `reconcileHere` (3388), `addFromTeamServer` (3443) | ~560 |
| 4 | **Vendor CLIs** | `cliStatus`, `cliCheckedAt` | `vendorCliStatus` (1517), `oneCliStatus` (1530), `installedCliVersion` (1548), `runVendor` (1576), `installVendorCli` (1612), `updateVendorCli` (1624), `openCliTerminal` (1628) | ~170 |
| 5 | **Local engines** | `consultEngines`, `consultEngineAt`, `consultEnginesShown`, `consultEnginesInFlight`, `localEngines`, `localProbedEndpoints`, `localCheckedAt`, `windowsSideThisPass` | `consultEnginesAnswer` (1000), `probeConsultEngines` (1025), `probeLocalEngines` (1262), `windowsSideOnce` (1283), `probeLocalEngine` (1289) | ~330 |
| 6 | **Usage and the ledgers** | `usageWindow`, `usageScope`, `chatLedger`, `doorLedger` | `usageLines` (543), `doorLines` (577), `chatLines` (588), `ledgerStamp` (606), `setUsageWindow` (617), `usageTab` (643), `forgetUsage` (1398), `remembered` (1428), `forgottenBefore` (1439), `forgetChatUsage` (1453), `chatForgottenBefore` (1478), `chatLedgers` (1491), `readUsage` (3887) | ~430 |
| 7 | **Vendor add / remove** | — | `addVendor` (3517), `askCustomEndpoint` (3611), `customConsultant` (3646), `saveVendor` (3658), `removeVendor` (3682), `customModel` (2406), `readAgyModels` (3729), `readCodexModels` (3744) | ~330 |
| 8 | **WSL networking** | — | `fixWslNetwork` (2249), `mirroredIsAlreadyWritten` (2285), `setNetworkingMode` (2310) | ~160 |
| 9 | **Prices** | `openRouterPrices`, `liteLlmPrices`, `pricesCheckedAt` | `modelPrice` (417), `refreshPriceTables` (1350), `modelPrices` (1361) | ~100 |
| 10 | **The rounds-log cache** | `roundsLogCache`, `roundsLogAt` | `forgetRoundsLog` (468), `roundsLog` (472), `roundFindings` (500), `roundFindingsMany` (515) | ~80 |
| 11 | **The consult prompt** | `promptWriteFailed` | `readConsultPrompt` (3794), `saveConsultPrompt` (3823), `reportPromptFailure` (3872) | ~95 |

**Roughly 2 850 of the 3 717 class lines**, leaving the view lifecycle (`resolveWebviewView`,
`render`, `enqueue`, `write`, `run`), the settings readers and the editing state as what
`PanelProvider` is actually for.

## Defects read on the way, recorded and left

The series may not change behaviour, so anything found while moving code is written here instead of
fixed in the move. This is the same discipline that produced the parent split's tail plan.

- **`RoundsLogCache.roundsLog` stamps its cache window BEFORE the read, not after.** Found by
  CodeRabbit on the second extraction. If a read takes longer than `AGE_MS` (10 s) the next ordinary
  call treats the result as already stale and starts another server process.
  **The obvious fix is wrong**: stamping after the `await` would let EVERY tick arriving during a slow
  read spawn its own read, which is the stampede the cache exists to prevent — the log page refreshes
  every tick while a round runs. Stamping before suppresses them for the first ten seconds; stamping
  after suppresses none.
  **The right fix is an in-flight guard**, which the sibling class extracted from this same file the
  day before already has: `ClaudeProbeCache.claudeProbeInFlight`, added against the same shape of bug.
  That gives both — no second read while one runs, and a window that starts when the data is fresh.
  Its own change, not a move.

## The control measurement, after three (2026-09-18)

Cut iteratively on the operator's ruling — two or three clusters, then a control slice, then
recalibrate — rather than straight down the list. This is that slice.

| cluster | planned | actual | gap | its public surface | what the class kept |
|---|---|---|---|---|---|
| **1** Claude probe | 180 | **181** | +1 | private, one caller in `render` | nothing |
| **10** rounds log | 80 | **56** | **−24** | PUBLIC, four methods called from `extension.ts` | four delegations |
| **11** consult prompt | 95 | **93** | −2 | private, three callers | nothing |

**The estimates are good, and they are wrong in exactly one way.** Where a cluster is private its
size predicts the saving to within two lines. Where it is PUBLIC the class must keep a delegation per
method, and the saving falls by about thirty per cent. The table above this section records SIZE and
says nothing about SURFACE, which is the half that decides what an extraction is worth.

**So the remaining eight are re-read by surface**, counted from the class's public methods today:

| cluster | public methods it would take | delegation cost |
|---|---|---|
| **6** usage and the ledgers | **eight** — `usageLines`, `doorLines`, `chatLines`, `setUsageWindow`, `consultationsTab`, `usageTab`, `forgetUsage`, `forgetChatUsage` | ~28 lines |
| **9** prices | one — `modelPrice` | ~4 lines |
| **2** bugs | one — `closeConsultation` | ~4 lines |
| **7** vendor add/remove | one — `vendorIds` | ~4 lines |
| **3, 4, 5, 8** | none | none |

Cluster **6** is the one this changes. It is the largest remaining at ~430 lines and it carries eight
of the class's seventeen public methods, so its real saving is nearer 400 — and, more to the point,
it is the cluster where "does the panel still own this?" is a design question rather than a move.
**It moves to last**, behind the four with no surface at all.

**Three more things the three extractions taught, which the plan did not know:**

- **`prove-move.mjs` did not fit a class**, and was fixed rather than worked around — a verification
  tool may not produce a false positive on basic syntax. Two defects: residue reset the walk, and a
  bare `}` or `/**` could start a region. It reported 15 runs for 8 regions; it reports 8 now.
- **A private method that must become public costs one residue line and one run**, each, because
  `unexported()` forgives `export` and nothing else. Expect it and say so in the pull request rather
  than reading it as fragmentation.
- **`sonarExclusions.test.ts` catches every new module**, three times out of three. Add the module to
  `sonar.coverage.exclusions` in the same commit; the ratchet is red until you do, which is the guard
  working.

**Where the file stands: 4 022 → 3 748 with all three in**, and 3 692 once #403 lands beside them.

## Build order, and why it is this order

**One extraction per pull request, each through the gate.** Never two: the point of the parent's
series is that a reviewer can hold one move in their head, and `prove-move.mjs` can say whether it was
a move.

1. **Cluster 1, the Claude probe — first and alone.** The smallest cluster with the cleanest
   boundary: seven fields nothing else touches and five methods that call each other. It is the one
   that proves the METHOD — whether a cluster really can take its state with it — at the lowest cost
   of being wrong.
2. **Cluster 10, the rounds-log cache**, then **9, prices**, then **11, the consult prompt.** Small,
   state-owning, independently readable. Three more confirmations before anything large.
3. **Clusters 4 and 8** — vendor CLIs and WSL networking. Larger, still self-contained.
4. **Cluster 5** — local engines. No public surface; do it once the shape is established.
   **Cluster 6, usage and the ledgers, MOVED TO LAST** by the control measurement above: eight of the
   class's seventeen public methods are its, so it is the one place where "does the panel still own
   this?" is a design question rather than a move.
5. **Clusters 2, 3, 7** — bugs, team servers, vendor add/remove. The biggest and the most entangled
   with the webview's messages; last, deliberately.

**Re-measure the file after each.** If the count is not falling by roughly the cluster's size, an
extraction left a shim behind and the next one should not start.

## Test plan

Per extraction, and none of it is optional:

1. **`prove-move.mjs` against the pre-split file** — `node scripts/prove-move.mjs origin/main
   src/panelProvider.ts src/<theNewModule>.ts`. Every line the new module has that the original did
   not is a line to be justified in the pull request body, one by one.
2. **The collected test NAMES, not the count**, before and after — the parent's own guard, because a
   count survives a test being renamed between files.
3. **`npm test` and `npm run test:host`.** The panel is a webview; the host harness is the only thing
   that can see it actually resolve.
4. **The ratchets**: `notificationSites.test.mjs` (the count only ever falls),
   `importCycles.test.mjs` (`KNOWN` may not grow), `sonarExclusions.test.ts` (transitive `vscode`
   reachability). An extraction that adds a cycle or a notification site is refused by them, which is
   why they exist.
5. **REBASE IMMEDIATELY BEFORE MERGING, and re-run `prove-move.mjs` after the rebase.** This is the
   parent's hardest-won lesson: git resolves delete-versus-modify in favour of the delete, so a
   rebase can silently revert whatever main changed in the moved region, with no conflict and every
   check green.

## Definition of Done

- [ ] `panelProvider.ts` under 800 lines, or a header saying exactly why not and what is left.
- [ ] Every new module under 400, or its own header says why not.
- [ ] Every extraction proved a MOVE by `scripts/prove-move.mjs`, with residue justified line by line.
- [ ] No behaviour change: collected test names identical per tier, measured on the same commit.
- [ ] `importCycles.test.mjs`'s `KNOWN` did not grow.
- [ ] `notificationSites.test.mjs`'s count did not grow.
- [ ] The host harness passes after each extraction, not only at the end.
- [ ] Each extraction was rebased and re-proved immediately before merge.
- [ ] `research/module_extension.md` updated per extraction, not in one lump at the end.
- [ ] The coai gate ran on each pull request.

## What this plan does NOT do

- **It does not change behaviour.** Any defect read on the way is recorded and left where it is, as
  the parent did — that is what produced the tail plan, and it is the right trade again.
- **It does not touch the other seven files over the ceiling.** `panelView.ts` (2 855),
  `chatPage.ts` (2 350), `roundsLog.ts` (2 032), `extension.ts` (1 370), `claudeSessions.ts` (1 188),
  `chatStoreFile.ts` (1 019), `dataCommands.ts` (808). One split at a time.
- **It does not redesign the panel.** The clusters above are the ones already in the file; finding a
  better architecture is a different plan and must not ride along inside a move.
