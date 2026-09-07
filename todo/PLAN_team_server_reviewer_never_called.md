# PLAN — a Team server reviewer you enabled, and the round that never called it

> Status: **in progress — story 1.1 landed 2026-09-07, the other eight are open.** Kept in `todo/`
> until the last one ships; promoted then. What has shipped: `vendorsEnv` carries `remoteVendor`,
> asserted from both sides, with `null` and whitespace-only values treated as absent.
> Scope: `src_vs_code/src/vendors.ts`,
> `serverSettingsFile.ts`, `serverSettingsSync.ts`, `extension.ts`, `models.ts`, `panelView.ts`,
> `panelProvider.ts`; `src_mcp/src/Server/PanelService.cs`, `RoundAudit.cs`, `Program.cs`,
> `src_mcp/core/Rounds/SessionState.cs`.
>
> Related docs: [architecture.md](../research/architecture.md),
> [module_team_server.md](../research/module_team_server.md),
> [module_extension.md](../research/module_extension.md),
> [PLAN_team_server.md](../research/PLAN_team_server.md).

## The symptom

A Team server was added, `claude` was picked from its catalog, the row appeared in *Reviewers* with
both stage boxes ticked — and the round ran without it. Nothing said so. From
`logs/2026-09-07/coai-mcp-11-03-41-57668.log`:

```
[11:03:41 INF] starting: codex,gemini,local,remsoftdev-claude enabled, vault: no COAI_CREDS_KEY configured
[11:06:00 INF] round 1 PlanReview opening: 3 reviewer(s) — codex/PlanCritique, gemini/PlanCritique, local/PlanCritique
```

Four enabled, three asked, and the two lines are eleven seconds apart in the same file. The round
summary then said *"all 3 reviewers answered"*, which is true about what it asked and silent about
what it did not.

## What is actually wrong — four defects, one visible

`providers` answers, today, on the live machine:

```json
{ "provider": "remsoftdev-claude", "enabled": true, "cliFound": false, "auth": "unavailable",
  "note": "'remsoftdev-claude' was not found on this machine — install it with: npm install -g @openai/codex" }
```

The server holds that row as a **codex** vendor with a base URL. `RuntimeResolution.AuthOf`
(`src_mcp/runners/Reviewers/RuntimeResolution.cs:99`) answers `unavailable` for a base-URL vendor
with no vault key, and `PanelService.BuildWork` (`src_mcp/src/Server/PanelService.cs:841`) drops
every provider whose auth is `unavailable`. The drop is correct. Everything that led to it is not.

### A. An older extension host downgrades a runtime it does not know, and writes it out

`vendorsFrom` (`src_vs_code/src/vendors.ts:225`) turns an unrecognised runtime into `codex`, so a row
written by a newer extension "still leaves a row that launches something". That fallback is right for
RENDERING and for LAUNCHING. It is wrong the moment the coerced value is **persisted**:
`ServerSettingsSync.sync` (`src_vs_code/src/serverSettingsSync.ts:39`) writes the parsed list into
`<dataDir>/settings.json`, a file shared by the server and by every VS Code window on this machine.

Observed, on the reporting machine: VS Code user settings hold

```json
{"id":"remsoftdev-claude","runtime":"remote","teamServerId":"remsoftdev","remoteVendor":"claude", … }
```

and `%LOCALAPPDATA%\coai-mcp\settings.json` holds

```json
{"id":"remsoftdev-claude","runtime":"codex","model":"haiku","baseUrl":"https://coai.remsoft.dev", … }
```

written **0.4 seconds apart** (13:03:30.407 and 13:03:30.821). Two hosts, one file. The extension
folders date 0.31.1 to 10:39:04 and 0.31.2 to 12:16:15; one window's host started at 10:25:36, so it
still runs 0.31.0 — whose `RUNTIMES` has no `remote` (verified in its shipped bundle). It receives
`onDidChangeConfiguration` like every other window, re-parses, coerces, and overwrites the file the
correct window had just written.

**Honest limit of the fix below:** a guard added now cannot stop 0.31.0, which has no guard. It stops
the NEXT pair. Today's machine is unstuck by reloading the stale window.

### B. `remoteVendor` is never carried to the server at all

`vendorsEnv` (`src_vs_code/src/vendors.ts:300`) emits `id`, `runtime`, `model`, `baseUrl`,
`executablePath` and the two narrowing flags. Not `remoteVendor`. The server has had the field since
epic 3 — `VendorDto.RemoteVendor` (`src_mcp/src/Server/SettingsJsonContext.cs:22`),
`VendorIdentity.VendorOnServer` (`src_mcp/runners/Reviewers/RuntimeResolution.cs:21`) — and nothing
fills it. `PLAN_team_server.md:401` listed `vendorsEnv` as a site to change and `:712` asked for a
`settingsReach` test; neither happened, and no test names the gap.

So **even with A fixed** the row would reach the server as `remote` with an empty `RemoteVendor`,
`VendorOnServer` would fall back to the row id, and `coai.remsoft.dev` would refuse
`remsoftdev-claude` as a vendor it does not offer. [architecture.md](../research/architecture.md)
§*The one interface neither container owns* predicted this failure in those words.

`teamServerId` is deliberately NOT carried: the server has no field for it and no question it
answers. It stays a panel-side fact.

A `remote` row that reaches the server with an EMPTY `remoteVendor` — a hand-written row, or one
written by a build that predates B — is reported by `providers` with a note saying the row does not
record which vendor its server knows it by. Today that row fails much later, as a vendor the server
"does not offer", which reads exactly like a typo in a name nobody typed. Resolving the missing name
against the catalog at sync time was considered and rejected: it makes writing a settings file depend
on a reachable network service.

The distinction lives on `VendorIdentity` as **`NamesItsServerVendor`**, beside the fallback it is
about — `VendorOnServer` alone cannot serve, because a recorded name that happens to equal the id
returns the same string as a fallback does. Both read one trimmed value, so a name made of spaces is
no name.

The sentence, exactly, so it can be asserted rather than inferred — the plain refusal, then:

> This row does not record which vendor its server knows it by, so its own id was used as the name;
> if it came from a Team server, remove it and add the reviewer again.

It stops there deliberately. It does **not** say the id was never typed: the fallback is legitimate
for a hand-written row somebody called `codex`, nothing at the probe can tell that apart from a
generated `remsoftdev-claude`, and the repository's own `codex` fixture refuted the stronger claim
within a minute of it being written.

### C. A round that excludes an enabled reviewer says nothing

`RoundAudit.Opening` (`src_mcp/src/Server/RoundAudit.cs:25`) prints what was asked.
`ReviewerSummary.Sentence` (`src_mcp/core/Rounds/SessionState.cs:119`) counts asked against answered.
Both are honest about the roster they were handed and neither can see a vendor that never entered it.
The only surface that knows is `providers`, which nobody calls mid-round.

This is the defect that made the other three invisible. `VendorRuntimeSurvivesParsingTests` already
exists because the same class of bug — a runtime lost in parsing, a reviewer silently dropped —
happened to `local`; the lesson was recorded and the reporting was not built.

### D. A remote row's card claims codex's model list

`modelsProvenance` (`src_vs_code/src/models.ts:169`) has arms for `local`, `gemini`, `claude`,
`antigravity`, and falls through to codex. A Team-server row is therefore captioned *"codex · 8
models the Codex CLI has cached for this machine"* — a sentence about a CLI that has nothing to do
with it, under a row whose models come from the server's catalog.

## The fix

### B — `vendorsEnv` carries `remoteVendor`

Emitted only when non-empty, exactly like `plan`/`code`: a codex row stays byte-identical to what it
has always been, and the file a person opens still carries only what differs from the default.

### D — a `remote` arm in `modelsProvenance`

The list comes from the server's catalog, so the sentence must too: how many models this Team server
allows for this vendor, and — when the catalog has not arrived — that the server has not been asked
yet. `allowedModelsFor` (`src_vs_code/src/panelView.ts:322`) already computes the list; the caption
is told the same number rather than deriving it a second way.

### A — the settings file carries who wrote it

`serverSettingsJson` (`src_vs_code/src/serverSettingsFile.ts:16`) adds one key, `COAI_WRITTEN_BY`,
holding the extension version.

It goes in the **file writer**, not in `envBlock`: `envBlock` also builds the block a person pastes
into an MCP client, and a provenance stamp there is noise in something a human reads. The server is
unaffected either way — `PanelSettings.UnknownValues` (`src_mcp/src/Server/PanelSettings.cs:343`)
reports unknown VALUES of known keys, never unknown keys, so a new key raises nothing.

`ServerSettingsSync` gains a reader for the existing file and refuses to write when the stamp there
is a strictly newer version than its own. Absent, unparseable or older — write, which is today's
behaviour and the migration path. On a refusal it says so once, naming the version it found and
offering *Reload Window*; a race that silently reverts a person's configuration becomes a sentence
telling them what to do.

Semver comparison is a pure function with its own tests, and it must SPECIFY what it compares rather
than leave a naive split to decide: build metadata (`+build`) is ignored, a pre-release
(`0.31.2-alpha.1`) is older than the release it precedes, and numeric segments compare numerically so
`0.31.10` is newer than `0.31.9`. `lastWritten` is not updated on a refusal — the next change must
try again, the same reasoning the existing failed-write branch already uses.

**Read, check and write happen under one exclusive lock.** A read-then-write guard is a
time-of-check-to-time-of-use race and the gate was right to refuse it: two hosts can both read a
stamp they are allowed to overwrite, and the one that writes second wins with a payload it decided on
before the other's write existed. The observed collision was 0.4 seconds wide, which is enormous next
to a read and a write. So the sync takes a lock file next to the settings file — an exclusive create
(`wx`), released in a `finally`, with a stale lock older than a few seconds broken so a killed window
cannot wedge every other one — and does the read, the comparison and the write inside it. A lock it
cannot take is not an error: the other holder is about to write, and this host's next configuration
change will sync anyway.

### C — the round says who could not run, and why

One field, three surfaces, because all three already read the same sentence.

`ReviewerSummary` (`src_mcp/core/Rounds/SessionState.cs:114`) gains
`ImmutableArray<string> Excluded = []`, and `Sentence` appends, when it is non-empty:
`; 1 enabled reviewer could not run: remsoftdev-claude (<the server's own note>)`.

That reaches `ReviewAnswer.Reviewers` (`PanelService.cs:1049`), `RoundAudit.Closing`
(`PanelService.cs:576`) and the live round record (`PanelService.cs:513`) with no new plumbing —
each already passes `summary.Sentence`.

`RoundAudit.Opening` additionally logs the excluded list beside the asked one, so the two lines in
the log stop contradicting each other.

The filter predicate in `BuildWork` (`PanelService.cs:841-844`) is extracted to one private
`CanRun(ProviderSettings)`, and a new `internal ExcludedFrom(bool isPlanStage)` returns the enabled
providers that serve the stage and fail it, each with the reason from `AuthFor(p).Note`. One
predicate, two readers; `BuildWork`'s signature does not change, so its tests keep compiling.

**The panel badge** comes from the same author. `Program.cs` (`src_mcp/src/Program.cs:83-87`) gains a
`--providers` startup mode printing exactly what `PanelService.ProvidersAsync`
(`src_mcp/src/Server/PanelService.cs:128`) already returns, and the panel calls it the way it already
calls `--log` (`src_vs_code/src/roundsDbRead.ts:22`). A vendor the server reports `unavailable` is
badged on its card with the server's own note.

The alternative — deciding availability again in TypeScript — is the mistake
`RuntimeResolution` (`src_mcp/runners/Reviewers/RuntimeResolution.cs:86`) exists to have ended: *"three
copies of one decision is what allowed two of them to be right."* The panel displays; it does not
decide.

**A badge has three states, not two.** An MCP client's `env` block outranks the settings file key by
key (`SettingsFile.Layer`), so a standalone `--providers` cannot see environment a scripted or
containerised client passed to the running server, and would then report an availability the live
server does not have. The panel therefore treats a spawn that fails, times out or exits non-zero as
**unknown** — no badge, unchanged card — and never as unavailable. Only an answer the binary actually
gave produces a badge, and its tooltip names what it was read from. A badge that lights up because a
probe failed is a badge that lies; the ⤓ buttons already refuse to guess for the same reason
(`panelProvider.ts:585`).

## What the plan round changed (2026-09-07, session `1dcd1528`)

Three reviewers, twelve findings, `good_enough` at 9 gating against a threshold of 6. Five accepted,
four rejected with reasons, three minor left as they were. The fourth reviewer — `remsoftdev-claude`,
the subject of this plan — did not run, which is the bug.

**Accepted, and already folded in above:** the read-then-write guard was a TOCTOU race and is now a
lock (raised three times, by all three reviewers); the excluded-reviewer sentence needed an assertion
per SURFACE rather than one on the sentence; `--providers` needed an unknown state distinct from
unavailable, because an MCP client's `env` outranks the settings file and a standalone probe cannot
see it; the semver comparison needed a specification; and a `remote` row with no `remoteVendor`
needed a note of its own.

**Rejected, with what the reviewer had wrong:**

- *"B does not make the reviewer runnable — nothing obtains a credential."* A remote vendor does not
  use a vault key. `AuthOf` decides `remote` before the vault-key branch and answers `server token`,
  and `ARemoteVendorParsedFromTheListIsRunnableONCEThisMachineHasSignedIn` has asserted exactly that
  since epic 3. Today's `unavailable` is defect A wearing a credential's clothes.
- *"`ExcludedFrom` ignores the stage."* `ProviderSettings.Serves(bool isPlan)` exists
  (`PanelSettings.cs:50`) and runs first, in `BuildWork` today and in `ExcludedFrom` as specified. The
  test it implies is kept as C6.
- *"0.31.0 corrupts the configuration permanently."* VS Code's own `coai.vendors` was never
  corrupted — only the derived file was, and `sync()` runs at activation, so any correct host rewrites
  it when its window loads. A version check cannot be added to a build that has already shipped.
- *"`--providers` and the badge could disagree."* There is no second code path: the flag prints what
  `ProvidersAsync` returns and the panel renders it. The real form of that concern is the `env` one,
  accepted above.

## Build order

1. **B** — `vendorsEnv` carries `remoteVendor`. Smallest, and the one that unblocks the reviewer.
2. **D** — the `remote` arm in `modelsProvenance`.
3. **A** — `COAI_WRITTEN_BY`, the sync guard, the notification.
4. **C** — `ReviewerSummary.Excluded`, `CanRun`/`ExcludedFrom`, `Opening`, `--providers`, the badge.

B and D are extension-only. C spans both halves and is built last because its C# half is what proves
B worked.

## Test plan

Every item is written RED first and its failure message is checked to name the real symptom.

| # | Test | Where | Asserts |
|---|---|---|---|
| B1 | `settingsReach.test.ts` | extension | a `remote` row's `remoteVendor` survives into `COAI_VENDORS` |
| B2 | `settingsReach.test.ts` | extension | a codex row's JSON gains no `remoteVendor` key at all |
| B3 | `VendorRuntimeSurvivesParsingTests.cs` | mcp | the JSON the extension now writes parses to a `VendorIdentity` whose `VendorOnServer` is `claude`, not `remsoftdev-claude` |
| B4 | `RemoteProbeTests.cs` | mcp | a `remote` row with an empty `remoteVendor` is reported with a note naming that, not as a vendor the server does not offer |
| D1 | `remoteVendorCard.test.ts` | extension | a remote card's caption names the Team server's catalog, never the Codex CLI |
| D2 | `remoteVendorCard.test.ts` | extension | with no catalog yet, it says the server has not been asked |
| A1 | `settingsSync.test.ts` | extension | the written file carries `COAI_WRITTEN_BY` with this extension's version |
| A2 | `settingsSync.test.ts` | extension | a file stamped NEWER is not overwritten, and `lastWritten` is not poisoned |
| A3 | `settingsSync.test.ts` | extension | absent / unparseable / older stamps are overwritten |
| A4 | `settingsSync.test.ts` | extension | the refusal reports once, naming the version found |
| A5 | new pure test | extension | semver compare: `0.31.10` is newer than `0.31.9`, equal is not newer, `+build` is ignored, `-alpha.1` is older than its release |
| A6 | `settingsSync.test.ts` | extension | with the lock already held, the sync writes nothing and poisons nothing |
| A7 | `settingsSync.test.ts` | extension | a lock left behind by a killed window is broken once it is stale, so one crash cannot wedge every window |
| C1 | `LocalReviewerRunsTests.cs` neighbour | mcp | a round with one unavailable enabled vendor reports it in `Sentence`, naming the vendor AND the server's own reason |
| C2 | as above | mcp | with nothing excluded the sentence is byte-identical to today's |
| C3 | new | mcp | `ExcludedFrom` and `BuildWork` agree — nothing is both asked and excluded |
| C4 | new | mcp | `--providers` prints the same JSON as the `providers` tool, on stdout, exit 0 |
| C4b | new, **the live check for this seam** | extension | `serverSettingsJson()`'s own output is written into a temp `COAI_DATA_DIR` and the REAL `coai-mcp` binary is run against it with `--providers`; the Team-server row must resolve under `claude`. This is what B1–B3 cannot do: two suites agreeing about a format each hold a copy of the names, and `.claude/rules/shared/common/testing.md` says in as many words that a contract with two implementations needs ONE check exercising them against each other. Raised twice on story 1.1's code round, by codex and by gemini, and it is why `--providers` earns its keep twice |
| C5 | `panelView.test.ts` | extension | a vendor the server calls unavailable is badged with the server's note |
| C6 | new | mcp | a reviewer ticked for CODE only is not reported as excluded from a PLAN round — the stage filter runs before the availability one |
| C7 | `panelView.test.ts` | extension | a probe that failed renders NO badge — the unknown state is not the unavailable one |
| C8 | new, one per surface | mcp + extension | the same excluded vendor and reason appear in the opening log line, in `ReviewAnswer.Reviewers`, in the closing audit line, in the live round record, and on the card — a sentence that is right in one place and missing in another is what this row exists to catch |

Full suites both sides: `./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe` and
`cd src_vs_code && npm test`.

## Definition of Done

- [ ] B, D, A, C implemented in that order, each with its tests RED before GREEN and both
      observations reported.
- [ ] `remoteVendor` reaches `COAI_VENDORS`; `teamServerId` deliberately does not, and the code says why.
- [ ] The settings file carries `COAI_WRITTEN_BY`; an older host's overwrite is refused and reported.
- [ ] A round that excludes an enabled reviewer names it and the reason, in the log, in the tool
      response, and on the card.
- [ ] The availability decision has exactly ONE author (`RuntimeResolution.AuthOf`); the panel displays it.
- [ ] Both suites green.
- [ ] Verified on the real machine: after reloading the stale window, `providers` reports
      `remsoftdev-claude` as `server token`, and a plan round opens with four reviewers.
- [ ] `research/module_extension.md`, `research/module_server.md` and
      `research/module_team_server.md` updated; the `remoteVendor` seam in
      `research/architecture.md` records that it was broken in shipping and how.
- [ ] This plan promoted to `research/` with `IMPLEMENTED <date>` and its deviations.
- [ ] `todo/README.md` table updated.
