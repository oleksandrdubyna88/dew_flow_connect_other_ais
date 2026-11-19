# PLAN — E1 stories: the read-only accounts list, end to end

> Status: **plan only, nothing implemented yet, 2026-11-19.** The stories of epic E1 of
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md)
> (its `## Epics` § *E1*, its §§ 1–4, its § *Security*, its § *Test plan* and its DoD items for E1). Written by Opus when
> the epic started, on `feat/terminal-accounts-e1` at `8494bb0d` (latest `main` merged; extension 0.89.1). A stories
> document keeps no change ledger: a change to this epic is an entry in the parent (epoch 1 is open with ONE change,
> 1.1, so the next accepted change is a full stop). **The two Fable specs are written** — E1.S1 (the security row of
> `subagent-models.md`, outside the quota) and E1.S2 (the cornerstone; the parent's ledger line `Cornerstones: 1 of 1 —
> E1.S2` does not change) — **and gate G2 judged them on 2026-11-19**
> ([PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_G2.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_G2.md)).
> **Its verdict is applied in this document:** K5 is the parent's entry 1.1; the damaged store (K6) and U10 (K3) are
> fitted to the parent's text; the specs' X tables, the gate's eleven contradictions and its security findings are in
> § 4, § 5, § 6, § 7 and § 10. No code is written yet.
>
> Scope: `src_vs_code` only, plus `SECURITY.md`, `research/` and the help. Five stories, as the parent cuts them.

## 1. Goal, before any solution

The owner has many VS Code terminals called `Creds: <host> / <session>`, one Linux user each, and nothing in VS Code
says which Claude Code, Codex or Antigravity account each host is signed in to, or how much of its limit is left
(parent § *The goal*). Step 0 measured how a VM states both with no secret (parent § *What step 0 and W1–W5 measured*;
[../research/RESULTS_accounts_and_usage_on_a_vm.md](../research/RESULTS_accounts_and_usage_on_a_vm.md)).

When E1 is done (the parent's E1 *Goal* and DoD items 2–9):

1. An icon beside *Ask other AI* in the terminal panel's title bar opens a list of the accounts on the hosts of the open
   `Creds:` terminals, grouped by account, each group naming its hosts, the usage as "left" in the agent-models mod's
   words, a missing value a dash and never a zero, and every host with its own sentence.
2. A round runs by itself, 30 s after the start and then 10 minutes after the end of each round, one host at a time, one
   ssh call for each distinct host that is not refused — two in the one case of the parent's finding 3 — and usage once
   for each account in a round.
3. A host with no answer is allowed by the round itself while `coai.terminalAccountsAllowNewHosts` is on (the default),
   stored with its marker, and named in a notice at most once a day. A "no" always wins: every write of the store
   re-reads and merges, and nothing — a round, a reload, a second terminal, a second window, a full store — removes one.
4. The accounts script is fixed, its hash is in its spec and in a test, and it writes and installs nothing on a VM.
5. Nothing of a reading is persisted. The session reader behaves as before, but for the words of its question and of
   its hosts list.

The silent failures this epic must not ship:

- **A refused host contacted** — by a round, a stale second window, a full store, a damaged store, or a timer that kept
  running after *Stop the accounts round*. Nothing would look wrong: one more login in a VM's ssh log.
- **A secret or the person's activity in the reply** — a credential file printed, the *"What's contributing to your
  usage?"* section of `/usage`, a Codex transcript line. A leak gives no error (parent cornerstone area (a)).
- **A wrong number that looks right** — a percent that is really "used", a missing window drawn as `0%`, a reset of last
  year, two accounts merged into one group because their numbers were equal (areas (b) and (d)).
- **A runaway** — two rounds at once, a usage read repeated on every host of one account, a usage command that makes a
  model call six times an hour, a log line every 10 minutes for a host that is down.
- **A command built from a VM's text** — the row for a host without `jq` showing anything but one of the five fixed
  strings.

## 2. What exists — verified at `8494bb0d`, 2026-11-19

All paths are under `src_vs_code/` unless they start with `research/`, `todo/`, `.agents/`, `.github/`, `src_bugs/` or are
a file of the repository root. Every name below was opened at this commit. The parent's own list (§ *What the code does
today*, verified on `a057c971`) was re-read line by line: since `a057c971` only comments in nine test files changed
(`bfde9b6f`), and every reference of that list still holds. This table repeats what the stories need and adds what the
parent did not name.

| Fact | Where |
|---|---|
| The terminal name rule: host class, session class, the whole pattern, `credsSession` | `src/credsSession.ts:31`, `:32`, `:36`, `:65` |
| The window's terminals, one mapper (`{ key, name, exited }`) | `src/terminalHost.ts:22` `liveTerminals` |
| The open listener of re-attach; the close listener of the session road | `src/terminalHost.ts:82-83` `bindReattach`; `src/remoteSessionHost.ts:220` |
| The pinned options; the argv builder (throws a fixed word); the one executable; the limits | `src/remoteSsh.ts:28-33` `SSH_OPTIONS` (comment `:22`); `:44-50` `sshArgv` (the host test `:45`); `:59-67` `sshExecutable`; `:75` `REMOTE_LIMITS` (15 s, 8 MiB) |
| The run: never rejects; `operands[0] === session`; the options object to `capture`; the refusal sentences | `src/remoteSsh.ts:113-129` `runRemote`, `:131-148` `ranRemote`, `:151-157` `argvOf`, `:144-145`; sentences `:127`, `:160` (both name "the Claude session"), `:166-170` `END_SENTENCES` (the capped one divides by 1 048 576, `:168`), `:175-176`, `:186` |
| The ONE log line, four parameters, written in one place; the default log seam is the one `console.` of the three E2 modules | `src/remoteSsh.ts:234-238` `logLine`, `:202-204` `logRead`; `:92-100` `defaultSeams` (private), `:96` |
| The child: `capture` with `CaptureOptions` `{ cwd, maxStdoutBytes, killTree }` | `src/versionProbe.ts:112` `capture`; `:78-85` `CaptureOptions`; `:72` `CaptureEnd` (`capped`) |
| The session script: one function, `</dev/null 2>/dev/null`, `COAI-END`; its doc gives lines, bytes and sha256 and says no test holds the hash | `src/remoteScript.ts:137` `REMOTE_SCRIPT`, `:428`, `:429`, `:126-128`; `:30` `REMOTE_TOOLS`; `:91-95` `callOf`; `:117-123` `shQuoted` |
| The frame: last `COAI-BEGIN 1`, first `COAI-END` after it — a PRIVATE function tied to the session parser's `Unreadable` faults | `src/remoteFrame.ts:121-130` `framedLines`, `:98` `fail`, `:47` `Unreadable`, `:106` `readRemote` |
| The consent rows, the cap, the pure functions; a list with ONE bad row reads as EMPTY | `src/remoteRead.ts:29-33` `HostAnswer`, `:38` `HOSTS_MAX`, `:120-125` `isRow` (extra keys are ignored), `:127` `rowsOf`, `:130-137` `consentOf` (a "no" wins over another row of the host), `:142-148` `withAnswer` (at the cap the OLDEST row goes, a "no" included), `:154` `readsKey`, `:163` `answersOf`, `:166` `withoutHost` |
| Text from a VM made plain (control and format characters) | `src/remoteRead.ts:67` `plainOf`; `src/remoteSsh.ts:216` `withoutControls` |
| The road's store seam writes a WHOLE list; the person's answer is read-modify-write after an `await` | `src/remoteRead.ts:323-325` `RoadSeams.store`; `:356-364` `asked`, `:366-370` `consented`; `src/remoteSessionHost.ts:75-78` `AnswerStore`, `:136-141` `changed` |
| The question, the hosts list, the "nothing to change" notice, the store key, the change signal, the hosts command | `src/remoteSessionHost.ts:35-45` `askHost` (code `read-remote-session`), `:104-105` `hostChoice`, `:107-114` `saidNoHost` (*"The first read … asks once"*), `:124-133` `askChange`, `:23` `TERMINAL_HOSTS_KEY`, `:158` `remoteSessionsOf(state, run, changed)`, `:163-166`, `:221` `coai.terminalHosts` |
| The test door of the road writes WHOLE lists, and host scenarios use it to set and restore the store | `src/remoteSessionHost.ts:231-247`; `src/test/host/terminalHostsScenario.ts:59`, `:67`, `:87`; `src/test/host/terminalDoorsScenario.ts:63`, `:73`, `:75`, `:78`, `:96`, `:112` |
| Activation: the road is bound once; the test doors are built behind `ExtensionMode.Test`, a further door in a spread of its own | `src/extension.ts:161`, `:163`, `:935-937`; `CoaiExports` `:104-122`; guard `src/test/remoteExportGuard.test.ts:16-27` |
| The extension activates at start-up | `package.json:1480` (`onStartupFinished`) |
| The manifest: the *Ask other AI* command and its title-bar row; the overflow items; the hosts command and its row; 68 settings | `package.json:273-278`, `:1214-1218` (`navigation@90`), `:1219-1233` (`coai@1…3`), `:299-302`, `:1284-1288` (`remote@1`); `contributes.configuration.properties` holds 68 `coai.*` keys |
| A setting a workspace must not decide: the user-layer door, the safety list (its test holds it exact), the scan that refuses any other read | `src/modelKeys.ts:37` `SAFETY_KEYS`, `:52` `USER_LAYER_KEYS`, `:78` `userLayerReader`, `:107-109`; `src/sideConfig.ts:47-49` `userLayer`; `src/test/modelKeys.test.ts:67-68`; `src/test/modelKeysAreReadOnce.test.ts:31-34`, `:41` (`DOOR`), `:56-67` |
| Which layer tried to set a key: a PRIVATE finder, and the public walk covers the MODEL keys only | `src/modelKeys.ts:130-136` `ignoredIn`, `:119-121` `ignoredWorkspaceValues`, `:124-127` `REPOSITORY_LAYERS` |
| An `application`-scoped key today; the Kimi key is `machine` | `package.json:418-421` (`coai.teamServers`), `:669-672` |
| Never in an exported config file | `src/configTransfer.ts:30-55` `NEVER_TRANSFERRED` |
| A setting in `CoaiSettings` must reach the server's env block, unless it is listed as not for the server | `src/settingsShape.ts:249` `DEFAULTS`, `:317` `OVERLAID_SETTINGS`, `:419` `settingsFrom`; `src/test/settingsReach.test.ts:102` `NOT_FOR_THE_SERVER`, `:104-116` |
| A QuickPick instance with item buttons, a title button, separators and rows rebuilt without closing | `src/conversationPickerCommand.ts:33-47`, `:99` (`QuickPickItemKind.Separator`), `:109` (item buttons), `:197`, `:258`, `:374`, `:523`, `:530-547` |
| Clipboard with a status-bar word, one copier for every caller | `src/copyText.ts:203` `textCopier`, `:22-27` `CopyPorts` |
| A one-at-a-time latch | `src/oneAtATime.ts:32` `oneAtATime` |
| A timer helper exists — but it is a PRIVATE `setInterval`, not a timer counted from the end of a round | `src/consultantHealthPanel.ts:214-218` `everyMs` |
| The gauge words: absent is absent; an age rounded down — a PRIVATE function | `src/gaugeWords.ts:13`; `:89-99` `AGO_UNITS`, `agoWords` |
| The mod's word rules and their cases | `.agents/conventions/mods/agent-models/hooks/limits.tsx:36-40` `percentLeft`, `:88-91` `minutesLeft`, `:93-95` `resetText` (no day form: 3 790 minutes is `63h 10m`), `:111-117`, `:123` `limitsLine`; cases `.agents/conventions/mods/agent-models/tests/limits.test.tsx:12-22` (P1–P3), `:70-80` (L1–L3), `tests/reset.test.tsx:48-50` (A4) |
| Every message goes through one door; a notice with a button that does not hold the caller | `src/notify.ts:247` `notify`, `:318` `notifyThen`, `:351` `notifyAndAsk`; classes `src/notifications.ts:33-40` |
| The notice census: generated, 185 sites; the population is a constant raised on purpose with a dated reason | `notification-sites.json:7`; `scripts/count-notifications.mjs:20-21`; `src/test/notificationSites.test.mjs:47`, `:290` (`PLACES_THIS_SPEAKS = 185`), `:292-299` |
| The help census: every command's title (or an alias) and every setting's `SETTING_ALIAS` phrase in the ENGLISH help; every article in five languages | `src/test/helpCoverage.test.ts:425-449` (`ALIAS`, `SELF_EVIDENT`), `:451-540` (`SETTING_ALIAS`, 68 entries), `:542-563`, `:580`, `:610-620`, `:622`; the remote paragraph's test `:84-101` |
| The help: the chat article and its remote paragraph (*"The first time for each host you are asked once…"*, *"They only read: they change nothing on that machine…"*); the four translations | `src/helpContent.ts:287`; `src/helpRu.ts:51`, `src/helpUk.ts:115`, `src/helpDe.ts:51`, `src/helpEs.ts:51`; the help page is built from `HELP_ARTICLES` (`src/helpPage.ts:49`) |
| **The door census counts EVERY `view == terminal` title-bar row as a way into a chat** | `src/test/chatWiring.test.ts:482-493` `doorCommands`, `:562-582`, `:599-605` |
| The terminal title-bar rows held by tests | `src/test/terminalDoorWiring.test.ts:59-65` (`navigation@90`), `:230-242` |
| The log scan: every `src/remote*.ts`, a banned-word list, companions that pin `logLine` and the one `console.` | `src/test/remoteLogScan.test.ts:16` `BANNED`, `:43-50` `logLeaks`, `:71`, `:73-79`, `:81-93` |
| The host modules excluded from coverage, held exact | `.github/workflows/sonarcloud.yml:268`; `src/test/sonarExclusions.test.ts:82` |
| Lint: `complexity` 4, 50 lines a function, 800 lines a file; suppressions only shrink | `eslint.config.mjs:113-115`; `.github/workflows/ci.yml:193` |
| CI: unit tests on ubuntu; the real-editor job is `continue-on-error` | `.github/workflows/ci.yml:526`, `:576-578`; `:596-599`, `:628-630` |
| The script test runs the REAL text by `sh` and `bash --posix` in the run folder; a missing `sh` on CI's Linux is a failure | `src/test/remoteScriptRuns.test.ts:48-60` (`SHELLS`), `:76-80`, `:89-94` |
| An owned temporary folder for a test | `src/test/ownedTemp.ts:18` `ownedTemp` |
| Host scenarios are registered in one list | `src/test/host/scenarios.ts:234` `SCENARIOS` |
| The invisible-character scan over every source file | `src_bugs/tests/NoSourceFileCarriesAnInvisibleCharacterTests.cs` |
| The docs of the session reader | `research/module_extension.md:13700` (E2), `:13909` (E3.S3, the last section; the file ends at `:13935`); `research/module_tests.md:315-317`; `research/architecture.md:624`, `:1654`; `SECURITY.md:126-147` (*"No ssh child is started before a "yes""* at `:130`) |
| The terminal-door plan's consent sentence — the other side of this plan's boundary, still silent about this plan | `research/PLAN_the_chat_opens_from_a_terminal.md:310` |
| `POST_DEPLOY.md` holds twelve items, its cap | `POST_DEPLOY.md:397-408`, `:468` |
| What `jq` this machine has | none: `command -v jq` finds nothing here; `ci.yml` installs none (whether `ubuntu-latest` has one is **UNVERIFIED**) |

## 3. Conventions every story follows

- **Worktree.** The worktree `coai-accounts-e1` of branch `feat/terminal-accounts-e1`. Never the main checkout.
- **Models.** Spec: **Fable** for S1 (security row) and S2 (cornerstone), each in its own document; **Opus** for S3, S4 and
  S5 (this document). An **Opus review of both Fable specs** before S3 is coded (gate G2, § 6; done 2026-11-19). Code
  and tests: Sonnet.
  Review: Opus. Fixes: Sonnet. Fable refused on a limit → Opus, and the summary says so.
- **RED-GREEN-RED.** Each scenario names its assertion and its break-it: the production line to break, and the failure
  message the test must then show. Every new test is seen RED first (before its code, or a compile error for a new
  module), then GREEN, then RED again with the guarded line broken, then GREEN. The summary reports the observations.
- **Test names.** This epic's ids are `A<story>-T<n>` and `A<story>-H<n>` ("A" for accounts), so they never meet the
  terminal-door plan's `S2-T10` and its kin in one file.
- **Every test file** starts with `import '../../scripts/inRunFolder.cjs';`; a temporary folder comes from `ownedTemp`
  (`src/test/ownedTemp.ts:18`), never `mkdtemp`.
- **Pure first, with seams.** Unit tests cannot load a module that imports `vscode`. Every decision AND every orchestration
  a test must see lives in a pure module that takes its effects as parameters. **One** new host module is allowed in this
  epic: `src/remoteAccountsHost.ts` (S3 creates it, S4 adds the list to it), with its row in `sonar.coverage.exclusions`
  (`sonarcloud.yml:268`) in the commit that creates it (`sonarExclusions.test.ts:82` holds the list exact). Other host
  code goes into files that exist (`remoteSessionHost.ts`, `extension.ts`).
- **The `remote` prefix is a control.** Every new module that touches a reply, a run or a log is named `src/remote*.ts`,
  so the log scan (`remoteLogScan.test.ts:71`) covers it from its first commit. No `console.*`, `notify(`, `logged(` or
  `seams.log(` argument of such a file may name a word of `BANNED` (`:16`: `stdout`, `output`, `lines`, `rows`, `names`,
  `reason`, `.message`, `asText(` …) — so a notice that lists hosts passes a variable called `hosts`, never `rows`.
- **Reuse first.** Called, never re-typed: `credsSession`, `liveTerminals`, `consentOf` (only through S1's `admitHost`
  and the session road: S3 and S4 never call it), `SSH_OPTIONS` and the one argv builder, `capture`, `shQuoted`,
  `oneAtATime`, `textCopier`, `notify`/`notifyThen`/`notifyAndAsk`, `userLayerReader`, `agoWords` (exported, S4), the
  framing function (extracted, S2), `plainValue` (S2's one road for VM text; S4's `forList` calls it) and
  `withoutControls` (S2 exports the one rule that takes control and format characters out; S3 makes the two older
  copies call it). The mod's three word rules are
  COPIED once into TypeScript (the mod is a `.tsx` of another runtime that this extension cannot import) and held to the
  mod's own cases by a table (A4-T1).
- **Lint.** A new function keeps `complexity` ≤ 4 and ≤ 50 lines; a file stays under 800 lines; no suppression count
  grows (CI step *Lint suppressions only shrink*, `ci.yml:193`). Split, do not suppress.
- **The census gates, each in the commit that moves it.** A new `notify`/`notifyThen`/`notifyAndAsk` site:
  `node scripts/count-notifications.mjs --write`, commit `notification-sites.json`, and raise `PLACES_THIS_SPEAKS`
  (`notificationSites.test.mjs:290`) with a dated comment naming the codes. A new command: its title (or an `ALIAS`) in the
  English help (`helpCoverage.test.ts:542`). A new setting: a `SETTING_ALIAS` entry whose phrase is in the English help
  (`:557`). A new article: in all five languages (`:610`). So **S1 and S4 each add the help text their own census needs**
  (five languages); S5 writes the rest.
- **No webview page changes in E1.** If one becomes necessary, it is tested by RUNNING the page (`bundledPage.test.ts`);
  a new behavioural assertion over page source text is refused (`.agents/PROJECT.md:99`). Source scans over TypeScript HOST
  code, where no seam reaches, stay legitimate; each has a companion that proves it still finds what it scans for.
- **No literal bidi or invisible character in any source file** (`src_bugs/tests/NoSourceFileCarriesAnInvisibleCharacterTests.cs`).
  A test that needs one builds it with `String.fromCodePoint(…)`.
- **A host scenario that changes the editor's state puts it back** in a `finally`, also when an assertion fails.
- **Nothing in E1 runs on a real VM.** The real hosts are E2's (R1–R12, W6–W8). In `ExtensionMode.Test` the round
  never arms itself and its default call is a stub (S3), so no host scenario can start a real `ssh`.

Commands (from `src_vs_code/`):

```bash
npm ci                                                                                 # once
npm run compile                                                                        # read the exit code: tsc emits even when red
node ../.agents/conventions/tools/low-priority.mjs node scripts/run-tests.mjs out/test/<name>.test.js   # one file, after compile
node ../.agents/conventions/tools/low-priority.mjs npm test                            # the whole suite (compile + run-tests)
npm run lint                                                                           # eslint src
node scripts/count-notifications.mjs --write                                           # after any new notice site
npm run test:host                                                                      # CANNOT start on this machine (no libnss3) — § 8
```

From the worktree root: `node .agents/conventions/tools/plan-lifecycle.mjs` before every commit that touches `todo/`.

## 4. Shared contracts — as the two Fable specs and gate G2 fixed them (2026-11-19)

This section was written before the specs, so that S3 and S4 could be briefed. **It now holds what the specs decided**:
the X tables of the S1 spec (X1–X16) and of the S2 spec (X1–X15), and the verdict of gate G2. The specs are the source
for C1 and C2; where a spec and this section differ, that is a defect of this section. The names below are the
contract.

- **C1 — who may be contacted (S1 owns; Fable; the S1 spec).** NEW pure `src/remoteHosts.ts` (the store; S1 X1) and
  host `src/remoteSessionHost.ts`. `src/remoteRead.ts` keeps the session road and its store seam.
  - The row gains a marker: `HostAnswer = { host; answer: 'yes' | 'no'; askedUtc; how?: 'auto' }` (no `how` = a
    person's). A marker this build does not know makes the list `damaged`.
  - A write is a CHANGE for one host, never a list: `HostChange` = `person` | `forget` | `auto`, and two kinds that
    only S1 sends, `repair` and `mend` (S1 X2). **S3 and S4 send `person`, `forget` and `auto` only.** ONE commit
    function writes the store: `sessions.commitHost(change): Promise<Committed>`, where `Committed` = `written` |
    `kept` | `full` | `damaged` (S1 X3). No function of the product takes a list to write: `withAnswer`, `withoutHost`,
    `answersOf`, `AnswerStore.write` and `RemoteSessions.answers` are gone, and `RoadSeams.store` is `{ consent(host),
    answer(host, answer) }` (S1 X5). The change signal of today fires after every commit that wrote.
  - **One function decides contact: `consentOf(value, host, allowNew)`**, which answers `yes`, `no`, `ask` or
    `damaged` (S1 X4). It is called in `remoteHosts.ts` and in `readsKey`, and nowhere else (A1-T2). **S3 and S4 never
    call it** (G2, C-2; S1 X14).
  - **The round's door**, called just before EVERY accounts call: `sessions.admitHost(host): Promise<Admission>`. It
    runs the refusal guard, reads the store and `coai.terminalAccountsAllowNewHosts` (user layer) afresh, writes the
    automatic "yes" when the rule says so, and answers
    `Admission = { kind: 'read'; how: 'person' | 'auto'; allowedNow: boolean } | { kind: 'refused' } | { kind:
    'not-asked' } | { kind: 'full' } | { kind: 'damaged' }` — `allowedNow` true only for the call that wrote the
    automatic row. It never asks a person; it has no ask seam.
  - **A refusal is guarded by the window that gave it**: before every call, after every commit, and once in 60 s. A
    *Forget* given in ANOTHER window is undone by that window, with the notice `remote-host-refusal-restored`. A *Read*
    in any window holds, and so does a *Forget* in the window that gave the "no" (K5 — **the parent's ledger entry
    1.1**; S1 X9).
  - **A damaged list** contacts nobody and is never written over, but for a person's own answer for a host in the
    change question that exists. It has no notice and no question of its own (K6; fitted to the parent's text at G2).
  - For the list (S4): `sessions.hostStandings(): readonly HostStanding[]` (each host's row as the list shows it:
    `answer`, `how`, `askedUtc`; over a damaged list the rows that can be read), `sessions.changeHost(host)` (the three
    choices of *CoAI: hosts read over ssh*, as one host), `sessions.askHost(host)` (the shared question for a host with
    no row, setting off), `sessions.allowsNewHosts()` and `sessions.ignoredLayer(): 'workspace' | 'folder' |
    undefined` (the bound forms; the pure one is `ignoredLayerOf(inspect, key)` in `modelKeys.ts`, S1 X7), and
    `dayOf(askedUtc)` from `remoteHosts.ts` (S1 X12). The words for a host's row over a damaged list are S1's
    (`HOSTS_SAID`, W11).
  - **The commit primitive.** Pure `commit(state, key, change, merge)`, with `Merge<C, R> = (stored: unknown, change:
    C) => { next: { value: unknown } | undefined; result: R }`. S3 writes the state row (C5) through the BOUND
    `sessions.commitKey(key, change, merge)`, never through the pure one: the bound one runs the refusal guard after
    its write (S1 X6).
  - The census: S1 adds ONE notice site (`remote-host-refusal-restored`), so 186 after S1 (S1 X13).
- **C2 — the script, the mode and the typed result (S2 owns; Fable; the S2 spec).** FOUR new pure modules (S2 X1):
  `src/remoteFraming.ts` (the shared frame finder), `src/remoteAccountsScript.ts`, `src/remoteAccountsUsage.ts` (the
  value rules) and `src/remoteAccountsReply.ts`. S2 also touches `.github/workflows/ci.yml` (one step: `jq` for its
  tests) and `src/notifications.ts` (one exported name) (S2 X10).
  - `ACCOUNTS_SCRIPT: string` (the text sent, whole), `AccountsMode = 'claude-usage' | 'no-claude-usage'`,
    `ACCOUNTS_MODES`, `isAccountsMode(word): word is AccountsMode`, `ManagerWord = 'apt-get' | 'dnf' | 'yum' | 'apk' |
    'none'`, `MANAGER_WORDS`.
  - `readAccounts(run: { code: number; stdout: string }, mode: AccountsMode, nowMs: number): AccountsRead` — never
    throws, never holds an error's message, and reads no clock: the reset text has no year, so the caller hands in
    the time (S2 X2). The typed result is the S2 spec's § A2.7. What S3 and S4 lean on:
    `AccountsRead = { kind: 'unreadable'; why } | { kind: 'read'; claude: ClaudeSide; codex: CodexSide; agy: AgySide;
    jqMissing: ManagerWord | undefined }` — `jqMissing` whenever the VM said that `jq` is missing (S2 X6);
    `ClaudeSide` = `absent` | `signed-out` | `unreadable` (`why`: `no-jq` | `auth` | `timeout` | `credential-shape`) |
    `found` with `version: string | null`, `email` (`''` = the account is not known), `org: string | null`, `plan:
    string | null` and `usage` = `read` (`windows`, `unmatched`) | `skipped` | `failed` (`why`: `timeout` | `error` |
    `format` | `model-call`) (S2 X3);
    a Claude window is `{ name: '5h' | 'week' | { model: string }; usedPercent: number | null; resetsAtMs: number | null }`;
    `CodexSide` = `absent` | `unreadable` (`no-jq` | `credential-shape`) | `found` with `signInFile`, `plan: string |
    null` and `usage` = `read` (`windows: { minutes; usedPercent | null; resetsAtMs | null }[]`, `asOf: { ms; from:
    'record' | 'file' } | null`, `statMissing: boolean`) | `failed` (`why`: `no-sessions` | `none` | `error`) (S2 X4,
    X15);
    `AgySide` = `absent` | `unreadable` (`credential-shape`) | `found` with `version: string | null`, `signInFile` and
    `usage: { state: 'no-source' }` (S2 X5).
    A number that is not there is `null`, never 0.
  - **VM text is made plain in ONE function, `plainValue`** (S2 X7): control and format characters out, every `$(`
    written `$ (`, 200 characters at most. Every text value of the typed result has passed it. Its first step is
    exported by itself, `withoutControls`: the one home of that rule in `src/` (S2 X12).
  - Exported for S3 and S4 (S2 X11, X14): `ACCOUNTS_PROTOCOL`, `ACCOUNTS_SECONDS`, `ACCOUNTS_READ_SECONDS`,
    `ACCOUNTS_CEILING_SECONDS`, `ACCOUNTS_LINES_MAX`, `ACCOUNTS_LINE_BYTES_MAX`, `ACCOUNTS_REPLY_BYTES_MAX`,
    `ACCOUNTS_UNREADABLE_SAID`.
  - The framing function `framed(stdout, sentinels)` in `remoteFraming.ts`, shared with `remoteFrame.ts`, with a
    result that names no parser's fault.
- **C3 — the runner (S3 owns).** In `src/remoteSsh.ts`: `accountsArgv(host, mode)`, `ACCOUNTS_LIMITS` (30 s, 64 KiB),
  `runAccounts(host, mode, limits?, seams?): Promise<RemoteRun>` (it always sends `ACCOUNTS_SCRIPT`), and
  `defaultRemoteSeams()` exported. `runRemote`, `sshArgv` and their sentences keep their contract and their tests.
  The runner's two numbers are held to S2's: `ACCOUNTS_LIMITS.maxStdoutBytes >= ACCOUNTS_REPLY_BYTES_MAX` (S2 X9) and
  `ACCOUNTS_LIMITS.ms === ACCOUNTS_CEILING_SECONDS * 1000` (S2 X14). S3 also makes the two older copies of the
  character rule call `withoutControls` (S2 X12).
- **C4 — the round and its memory (S3 owns).** NEW pure `src/remoteAccountsRound.ts`: `accountsHosts(live)`,
  `roundOrder(hosts, round)`, `HostMemory`, `Memory` (immutable, by host), `modeFor`, `needsSecondCall`, `UsagePolicy`
  (`every-round` | `hourly` | `on-press` | `off`; S2 X8) and `USAGE_POLICY`, `runRound(seams, memory, round, trigger)`,
  `claudeAccountKey(found, host)`, the timer decisions, `refreshAnswer`, and `logGate`. S4 reads `Memory` only through
  the `HostMemory` fields named in S3's brief. One of them is `admission`: where the round keeps an admission that was
  not `read` (G2, C-4).
- **C5 — the state row (S3 owns).** `coai.terminalAccountsState` = `{ toldUtc?, lastAutoNoticeUtc?, stoppedUtc? }`. **A
  value that cannot be read is STOPPED, not empty** (G2, S-2): `stoppedOf(value)` is true for a damaged row. It is
  written only through `sessions.commitKey`, with the merge `stateMerge` — a `Merge` over the stored value as
  `unknown` (S1 X15; G2, C-3) — and a FIELD change: `{ stop: utc } | { start: true } | { told: utc } | { autoNoticed:
  utc }`.
- **C6 — the words and the rows (S4 owns).** NEW pure `src/usageWords.ts` (the mod's rules and the window names) and
  `src/remoteAccountsRows.ts` (`accountsRows(view, now)`, `forList(text, max)` — the ONE road by which VM text reaches
  a row, and it CALLS `plainValue` (S2 X7) — `INSTALL_JQ` the five fixed strings, `noticesDue`). The runner's failure
  sentence is VM text as well and passes `forList` (S2 X13).
- **C7 — the test door (S3 owns, S4 widens).** `TerminalAccountsTestDoor`, present only in `ExtensionMode.Test`, in a
  spread of its own in `extension.ts`: `replaceCall(call | undefined)`, `runRoundNow(trigger)`, `armed()`,
  `memoryHosts()`; S4 adds `lastItems()`. **In test mode the round never arms itself, and the default call is a stub
  that never spawns `ssh` until `replaceCall` gives one** (G2, § 4; S1 X16). The road's own test door (S1) has
  `commit`, `admit`, `standings`, `stored` and `restore`; `answers` is gone (S1 X10).

## 5. Stories

Build order and its gates: § 6.

### E1.S1 — The shared store: a "no" that always wins, the automatic "yes", the setting

`model: fable` — **spec: Fable, [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_S1_spec.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_S1_spec.md)**
(security judgement, area (c), the security row of `subagent-models.md`, outside the quota). Coder: sonnet · review:
opus (of the spec first, gate G2; then of the code). Depends on nothing in this epic; S3 and S4 depend on it.

**Why Fable.** It is authorisation: who is contacted with the person's keys, and what keeps a refusal. The parent's
finding F1 says the consent no longer comes first; what is left of a gate is the refusal, and this story makes it hold.

- **Scope (as the spec fixed it).** NEW pure `src/remoteHosts.ts` (the row's marker, the merge rule, the cap rule, the
  change type, the keeper with the round's door and the refusal guard, `consentOf` with the setting, the words);
  `src/remoteRead.ts` (the road's store seam); `src/remoteSessionHost.ts` (the keeper bound, the guard's timer, the
  bound `admitHost`, the question's title and detail, the hosts list's rows and *Forget*'s sentence, `saidNoHost`'s
  words, `hostStandings`, `changeHost`, `askHost`, the test door); `src/modelKeys.ts` (the key joins the user-layer
  door; `ignoredLayerOf`); `src/sideConfig.ts` (`allowsNewHosts`, `ignoredLayerFor`); `src/extension.ts` (one argument
  of `testDoorOf`); `package.json` (the setting: boolean, default `true`, `"scope": "application"`, a description
  that says what it does, that it is on by default and what off restores); `src/configTransfer.ts`
  (`NEVER_TRANSFERRED`); NOT `src/settingsShape.ts` (§ 10, K9: decided by the spec); the help sentence the setting's
  census needs, in five languages (`SETTING_ALIAS`); the tests; the host scenarios that write the store
  (`terminalHostsScenario.ts`, `terminalDoorsScenario.ts`).
- **Not in scope.** The round, the timer, the stop, the state row's fields (S3); the list and the notices (S4); the help
  paragraph rewritten whole, `SECURITY.md` (S5).
- **What the spec had to decide, beyond the parent's text — and what it decided** (each found in the code, § 10):
  1. **The refusal repair against a person's later press in another window** (K5). § 3 point 3 of the parent writes a
     missing "no" again; point 2 says a person's *Read* or *Forget* changes a "no". A *Forget* in window B leaves no row,
     which window A cannot tell from a lost "no". The spec decides how a person's change survives the other window's
     repair — and whether that needs a tombstone, which changes the row's shape. **Decided (spec § S1.5): no
     tombstone.** The window that gave the "no" writes it again and says so; a *Read* in any window holds, and so does
     a *Forget* in that window. It is the parent's ledger entry 1.1.
  2. **A damaged store** (K6). `rowsOf` reads a list with one bad row as empty (`remoteRead.ts:127`), which is `ask` today.
     Under the automatic "yes" an empty reading is "allow everyone", and a commit over it would write one row and lose
     every "no" the damaged value held. The spec decides what `admitHost` answers (the contract has `damaged`), what the
     commit does over a damaged value, and what the person is told. **Decided (spec § S1.3):** `damaged`; nobody is
     contacted; nothing writes over the value but a person's own answer for a host in the change question that
     exists. Fitted at G2: no notice and no question of its own.
  3. **A person's answer at a full store** (K7). The parent says what an AUTOMATIC row does at the cap. Today a person's
     answer at the cap drops the oldest row, a "no" included — and the test `remoteRead.test.ts:170-184` asserts exactly
     that. The spec decides the person's case; that test is REPLACED, not kept beside a new one. **Decided (spec
     § S1.4):** a "no" is never dropped; a person's answer may push out the oldest person's "yes"; a hundred refusals
     answer `full`.
  4. **The list the setting joins** (K9). `SAFETY_KEYS` is documented as "the overrides that switch a server refusal off"
     (`modelKeys.ts:28-36`, its test name at `modelKeys.test.ts:67`); the parent puts the key there. Whether it joins that
     list or a sibling list of `USER_LAYER_KEYS`, and whether it belongs in `CoaiSettings` at all
     (`settingsReach.test.ts:104` then requires it to reach the server, unless it is in `NOT_FOR_THE_SERVER`).
     **Decided (spec § S1.7):** `SAFETY_KEYS`, and not `CoaiSettings`.
  5. **The test door** (K15): a host scenario must set and restore the real store. Either the door keeps a raw write,
     named test-only and refused outside test mode, or the scenarios restore by commits. **Decided (spec § S1.9):**
     set by commits; restore by one raw write that exists only in test mode.
  6. **A row written by an older build.** An older extension reads an automatic row as a person's "yes" (`isRow` ignores
     extra keys) and drops `how` on its next whole-list write. Named as a residual, or answered. **Named** (spec
     § S1.10, residual 4).
- **Interfaces with the other stories.** C1 exactly, and: S3 calls `admitHost` just before EVERY call and never another
  consent function; S3 writes the state row through `sessions.commitKey`; S3 listens to the change signal and reads
  `hostStandings()` to drop a refused host's reading and to decide whether the timer is armed (never `consentOf`); S4
  draws `hostStandings()`, `ignoredLayer()` and `allowsNewHosts()`, opens `changeHost` and `askHost`; the session
  road (`remoteRoad`) asks through the same store, and with the setting off an automatic row counts as no answer there too.
- **Scenarios the spec must contain at least** (it may split, refine or add; it keeps these ids or maps them in a table).
  The parent's test-plan rows they cover: *The consent store's commit and merge*, *The automatic "yes", the cap …* (the
  store's part), *The setting, at both values*.

| # | What must be shown |
|---|---|
| A1-T1 | The row's marker: a row with `how: 'auto'` reads as automatic; a row with no `how` as a person's; every row stored today reads as a person's; an unknown `how` value is decided by the spec and tested |
| A1-T2 | One road: `consentOf` (or its successor) is the one function that says whether a host may be contacted; the round's door and the session road both reach it; a companion scan finds no second reader of `coai.terminalHosts` |
| A1-T3 | The merge, with two views of one backing store, one of them stale: a "no" in window A against an automatic "yes" in window B, in both orders, and with B holding a list from before A's write → after both commits the store holds the "no", and B's next `admitHost` answers `refused` |
| A1-T4 | A person's "yes" replaces an automatic "yes"; an automatic "yes" never replaces any row; only a person's press changes a "no" |
| A1-T5 | No whole-list write: the commit seam is given a change, and a fake store proves every write follows a read in the same step with no `await` between (a test fails if the write seam is ever handed a list that was not read in that step) |
| A1-T6 | The refusal repair: a "no" this window wrote and then lost is written again before the next call, after the next commit and by the guard's timer. A person's *Read* of that host in another window is not undone by it. A *Forget* in ANOTHER window IS undone, with a notice; a *Forget* in the window that gave the "no" holds (K5 — the parent's entry 1.1) |
| A1-T7 | The cap: with 100 rows a new host drops the oldest AUTOMATIC row, never a person's; with 100 rows of a person the new host is `full`, not allowed and not contacted; 100 invented hosts and one "no" → the "no" holds; a person's answer at a full store follows the spec's rule (K7) |
| A1-T8 | A damaged store (`'junk'`, an array with one bad row) is never read as "allow everyone": `admitHost` answers `damaged` (`refused` for a "no" that can be read), nothing writes over it but a person's own answer for a host, and the damage is said in the host's row, at the session door and in the hosts list — with no notice and no question of its own (K6; fitted at G2) |
| A1-T9 | Read afresh before every call: a "no" committed between two calls stops the second; the setting turned off between two calls stops the automatic "yes" of the second |
| A1-T10 | The setting on: a host with no row gets the marked row BEFORE its call is spawned, `allowedNow` true once and false after; off: no row → `not-asked`, 0 writes; an automatic row counts as no answer for the round AND for the session road (it asks); a person's "yes" still counts; on again → the automatic row counts again (it was kept) |
| A1-T11 | The user layer only: the four mixes of a user value and a workspace value, and a folder value → only the user value (else the default `true`) decides; `ignoredLayerOf` names the layer that tried; the manifest: boolean, default `true`, `"scope": "application"`; the key is in the user-layer door, and `modelKeysAreReadOnce.test.ts` refuses a direct read of it; an imported config file is refused the key by name (`NEVER_TRANSFERRED`); its `SETTING_ALIAS` phrase is in the English help and the sentence is in all five languages |
| A1-T12 | The session doors: on an automatically allowed host (setting on) *take* and *add* read without asking; on a host with no row they ask; with the setting off an automatic row makes them ask |
| A1-T13 | *Forget* removes the row and the next `admitHost` (setting on) writes the automatic row again; *Not this host* turns an automatic row into a refusal |
| A1-T14 | The words: the question's title *"Read the Claude session and the AI accounts on “x” over ssh?"* and its detail listing what a "yes" covers; the same code `read-remote-session` and the same two buttons; the hosts list's four descriptions (*the Claude session and the AI accounts are read*; *allowed automatically on <day>: …*; an automatic row with the setting off; *not read*); *Forget*'s sentence that a forgotten host is allowed again by itself; `saidNoHost` no longer says the first read asks once |
| A1-T15 | The door never asks: `admitHost`'s pure core takes no ask seam; with a fake ask that throws, 100 admissions of new hosts raise nothing |
| A1-T16 | The commit primitive is generic: given another key and a field change it re-reads at the moment of the write (S3's state row uses it) |
| A1-H1 | Host scenario (CI only), through the real `globalState` and the test door: an automatic row written by `admitHost` is read back from a fresh `get`; a person's "no" then committed; the next `admitHost` answers `refused`; the store is restored in a `finally` |
| A1-T17 | (added by the spec) A person's answer is stamped later than the row it replaces, so "later" does not need two clocks to agree |
| A1-T18 | (added by the spec) The host half is wired as written: ONE timer for the guard; the hosts command takes no argument; the test door and its `restore` only in test mode; ONE new notice code |
| A1-H2 | (added by the spec) Host scenario (CI only): the setting through the real configuration — off: `not-asked` and no row; back to unset: `read` |

- **Docs (S1's part).** None beyond the help sentence its census needs; S5 writes the rest from the spec.
- **Done when.** The spec is written and reviewed by Opus (G2) BEFORE any S1 code; A1-T1…T18 green, each break-it of
  the spec seen red; the replaced E2 tests named in the summary; `npm run compile` exit 0; the whole suite and lint
  green; A1-H1 and A1-H2 green in CI's real-editor job, read from that job.

### E1.S2 — The accounts script, its reply and its parser (CORNERSTONE)

`model: fable` — **spec: Fable, [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_S2_spec.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_S2_spec.md)**
— the scenarios, the brief, the exact script text with its sha256, and the security judgement on that text. The
cornerstone of the quota (`Cornerstones: 1 of 1 — E1.S2`, unchanged). Coder: sonnet · review: opus (of the spec first,
gate G2; then of the code) · areas (a), (b) and (d). Depends on nothing in this epic; S3 and S4 depend on it.

**Why this story is the cornerstone** (parent § *Epics*): it is the only place a secret or the person's activity can leave
a VM, and the only place hostile or cut data can arrive and look right. A leak, a banner read as data or a missing number
read as zero gives no error.

- **Scope (as the spec fixed it).** FOUR new pure modules: `src/remoteFraming.ts` (the shared frame finder),
  `src/remoteAccountsScript.ts` (the script text and its constants), `src/remoteAccountsUsage.ts` (the Claude usage
  lines with the reset parse, and the Codex record) and `src/remoteAccountsReply.ts` (the reply's grammar and its
  parser); `src/remoteFrame.ts` (its private frame finder calls the shared one; its own behaviour and tests
  unchanged); `.github/workflows/ci.yml` (one step: `jq` for these tests, K12); `src/notifications.ts` (one pattern
  gets a name and an `export`); the tests, including the script run by a real `sh -s` (`dash`) and `bash --posix`
  against a temporary `HOME` with false `claude`, `codex` and `agy` and the REAL `jq`.
- **Not in scope.** The ssh call and its argv (S3, from C2's mode list); the words, the groups and the rows (S4); the fixed
  install strings (S4 — S2 owns only the closed manager word).
- **What the spec had to decide, beyond the parent's text — and what it decided.**
  1. **The frame extraction** (K11): `framedLines` is private and throws the session parser's own faults
     (`remoteFrame.ts:98`, `:121-130`). The shared function needs a result that names no fault of either parser, and every
     test of `remoteFrame.test.ts` and `remoteScriptRuns.test.ts` stays green with no edit. **Decided (spec § A2.6):**
     `framed(stdout, sentinels)` in `remoteFraming.ts`, a result that is a value and not a throw.
  2. **The real `jq` in the tests** (K12): this machine has none and `ci.yml` installs none. The spec says how the real-`jq`
     cases skip here with that reason, and that on CI's Linux runner a missing `jq` FAILS (as `remoteScriptRuns.test.ts:76-80`
     does for `sh`). Whether `ubuntu-latest` ships `jq` is UNVERIFIED; the spec either verifies it from a CI run or adds an
     install step to the test job (a CI change, not a VM install). **Decided (spec § A2.10):** a step of the test job
     installs `jq` where the runner has none, and prints its version.
  3. **The time the script's own limits cost a test** (2 s, 3 s, 8 s, 4 s): the cases that hang a false vendor wait for the
     real limit, because the limits are part of the text under test. The spec bounds the file's run time. **Decided
     (spec § A2.10):** six hang runs, started together in ONE test; the file stays under 30 s here and 60 s on CI.
  4. **Every word of the reply** (markers, states, keys), the line and byte limits, the reasons of `unreadable`, and where
     the manager word stands. **Decided (spec § A2.5, § A2.7).**
- **Interfaces with the other stories.** C2 exactly, and: S3 sends `ACCOUNTS_SCRIPT` (no other text) with one operand from
  `ACCOUNTS_MODES`, and hands an `answered` run to `readAccounts(run, mode, nowMs)` with the SAME mode and the round's
  clock; S3 decides the next mode from `claude.state`, `claude.usage.state` and `why`, and `claude.version`; S3 groups
  usage by `claudeAccountKey(found, host)` over `email` and `org`, with a key of its own for a host whose account is
  not known; S4 draws every arm and every `why` with its own sentence, and turns `jqMissing` into one of five fixed
  strings.
- **Scenarios the spec must contain at least** (it keeps these ids or maps them). The parent's test-plan rows they cover:
  *The accounts script, run for real*, *No secret leaves*, *The person's activity does not leave*, *The version is the only
  thing kept*, *The hash*, *The parser*, *The Claude usage lines*, *The Codex record*.

| # | What must be shown |
|---|---|
| A2-T1 | The shape: one function read to its end and only then called — the call with `</dev/null 2>/dev/null` first and the end line, in ONE brace group on the last line; the begin line first and the end line last. A script cut on the way does nothing: it starts no program, opens no path for writing and prints no line (real `sh -s` and `bash --posix`; the cut half maps the last line's absolute paths to a temporary folder — G2, S-1) |
| A2-T2 | The hash: a test holds the sha256 of the exported text; the spec holds the same value; an edit of one without the other is red |
| A2-T3 | The modes: the two words by a closed list in TypeScript and again in `sh`; a wrong or missing operand runs no vendor command and says so |
| A2-T4 | The `PATH`: a `claude` and an `agy` only in `$HOME/.local/bin` are found; nothing else of a profile is read |
| A2-T5 | Each vendor found, absent and signed out; each usage state; each mode |
| A2-T6 | The false `claude` and `agy` record their arguments, working folder (`/`), stdin (empty) and `TZ`; the test holds them to the parent's § 4 word for word: the account and version commands, and `claude -p "/usage" --output-format json --no-session-persistence --strict-mcp-config --setting-sources ""` with `TZ=UTC` for that one command |
| A2-T7 | `agy` is started with nothing but `--version`; `codex` is never started (`codex login status` included); no credential file is opened (a false file whose read is logged) |
| A2-T8 | A vendor command that hangs, and one that starts a child and hangs: the command's own limit ends both, and the reply says `failed timeout` (or the vendor's state) and still ends with its end line |
| A2-T9 | No secret leaves, SEEN: false credential files (`.credentials.json`, `.claude.json`, `auth.json`, agy's sign-in file and log) hold a marker token; the test fails if the marker is in any argument, the environment of any started program, stdout, stderr or a log line — seen RED once with a line that prints a file |
| A2-T10 | The activity does not leave, SEEN: a marker inside the *"What's contributing to your usage?"* section, and inside a line that starts with `Current ` and fits no pattern (it counts in `:unmatched`, its text is not sent); Codex transcript lines holding a marker and the word `rate_limits` — seen RED once with the filter removed |
| A2-T11 | The version only: a banner before it and a marker after it — the reply holds the digits and dots; 40 characters, letters, no dot → "not known", and the vendor is still found |
| A2-T12 | `jq` missing: each of the four manager words and `none`, from the four fixed paths; Claude and Codex `unreadable no-jq`; agy still read |
| A2-T13 | Nothing written: after a run, no file under the temporary `HOME` is new or newer, and the fakes saw `/` as their folder |
| A2-T14 | The frame: a banner before the begin line; a banner that holds it; the last begin and the first end after it, through the ONE extracted framing function; the session parser's tests green unchanged |
| A2-T15 | Every marker and every key; an unknown marker, an unknown key, a line that is neither, a line past 512 bytes, more than 64 lines, an exit code that is not 0 → the whole answer `unreadable`, each with its own reason |
| A2-T16 | The values: a percent of 101, `-1`, `1e3`, `NaN`; a value with control characters, with `$(`, of 10 KB; a credential-shaped value (40 or more of letters, digits, `_`, `-` with no space, `@` or `.`, or a known token prefix) dropped and its vendor `unreadable`; a manager word outside the list |
| A2-T17 | Absent is not zero: every missing number is `null`, and a test fails if any path of the parser turns one into 0 |
| A2-T18 | The Claude usage lines, as a table: the three measured lines; `3pm`, `3am`, `2:59am`; `12am` and `12pm`; a reset on 1 January read on 30 December; more than eight days ahead, in the past, another zone, no zone → a dash for the reset and the percent kept; no session line; two model lines; no matched line → `failed format`; `:turns 1` → `failed model-call` |
| A2-T19 | The Codex record, as a table: `primary` weekly and `secondary` null; both windows; a 300-minute window; another length; a reset that has passed; no record → `failed none`; a record further from the end than the 1 MiB tail; a time with milliseconds and `Z`; the record's time and the file's time; of two files the newer record wins; a file name outside `rollout-<ts>-<uuid>.jsonl` is not read |
| A2-T20 | The typed result: absent, signed out, no usage source, skipped, failed and unreadable are different values; no arm is an optional field to interpret; `readAccounts` never throws |
| A2-T21 | Nothing of a reply in a log or an error's text: the new modules are found by the log scan (`remoteLogScan.test.ts:71`) and are clean; a companion asserts they are found |
| A2-T22 | The script bounds its own reply: a false `claude` whose `result` is 10 MB → at most 64 lines of at most 512 bytes leave the VM |
| A2-T23 | On CI's Linux runner the real-`sh` and real-`jq` cases RUN: a missing `sh` or `jq` there is a failure, not a skip |
| A2-T24…T30 | (added by the spec) T24: the frame finder was moved, not copied. T25: the script keeps its own rules — the one brace group of its last line, and no package manager ever run. T26: the numbers in the script are the constants, and the readers' limits with ssh's connect sum under the 30 s ceiling. T27: the reply is held to the mode. T28: the credential net knows the redactor's key shapes. T29: `plainValue` is one rule. T30: no command waits for the script's stdin |

- **Docs (S2's part).** None in its own commits; S5 writes `research/module_extension.md` and `SECURITY.md` from the spec.
- **Done when.** The spec (with the script text and the security judgement) written and reviewed by Opus (G2) BEFORE
  any S2 code; A2-T1…T30 green, A2-T9 and A2-T10 seen RED once; the hash pinned in the spec and in a test, in one
  commit (the spec's pin says PENDING until then: its block changed at G2); `npm run compile` exit 0; the whole suite
  green on CI with the `sh` and `jq` cases RUN; lint green.

### E1.S3 — The runner and the round

`model: opus` (spec, this section) · coder: sonnet · review: opus · areas (c) (who is contacted and WHEN — the timing and
the count; the WHO is S1's door) and (d) (the reply arms decide the next mode). **Depends on S1 and S2 merged into the
branch** (it calls `admitHost` and sends `ACCOUNTS_SCRIPT`); its runner part (files 1) needs only C2's mode list and may
start once the S2 spec is reviewed. **This brief holds the rows of both specs' X tables that S3 must follow** (S1 X6,
X10, X14–X16; S2 X2, X3, X8, X9, X12, X14) **and gate G2's § 4.**

- **Goal.** One argv builder and one run for both callers, with the pinned options untouched; and a pure round that calls
  every distinct host that is not refused, one at a time, at most once — twice in the one case — usage once for each
  account, 30 s after the start and 10 minutes after the end of each round, never asking anybody, and stopping when no
  `Creds:` terminal is left or the person stops it.
- **Files and functions (what and how).**
  1. **`src/remoteSsh.ts` — the runner widened, not copied.**
     - A private `argvWith(host, words)` that returns `[...SSH_OPTIONS, '--', host, 'sh', '-s', '--',
       ...words.map(shQuoted)]` after the ONE host test of `:45` (moved into a private `hostOk(host)`). `sshArgv(host,
       operands)` (`:44`) = `callOf(operands)` check + `argvWith`; NEW `accountsArgv(host: string, mode: AccountsMode):
       readonly string[]` = `isAccountsMode(mode)` check + `argvWith(host, [mode])`. Both throw `REFUSED_WORD` (`:35`), a
       fixed text with no value. `SSH_OPTIONS` does not change (its comment `:22`: a change is a plan change).
     - NEW `export const ACCOUNTS_LIMITS: RemoteLimits = { ms: 30_000, maxStdoutBytes: 64 * 1024 }`.
     - `ranRemote` (`:131`) generalised into a private `ranAsked(host, ask, script, limits, seams)` where `ask = { argv:
       readonly string[] | undefined; label: string; subject: 'the Claude session' | 'the AI accounts' }`. `runRemote(host,
       session, operands, script, limits, seams)` keeps its signature and builds `{ argv: argvOf(host, session, operands),
       label: session, subject: 'the Claude session' }`. NEW `runAccounts(host: string, mode: AccountsMode, limits =
       ACCOUNTS_LIMITS, seams = defaultRemoteSeams()): Promise<RemoteRun>` builds `{ argv: accountsArgvOf(host, mode),
       label: 'accounts', subject: 'the AI accounts' }` and ALWAYS sends `ACCOUNTS_SCRIPT` (no script parameter: no other
       text can be sent). Both never reject (the `try` of `:121-128`, its sentence built from `subject`).
     - The two sentences that name the subject (`:127`, `:160`) take it as a parameter, so the session's text is byte for
       byte today's. The capped sentence (`:168`) says the cap in the unit it divides into evenly: `8 MiB` stays `8 MiB`,
       64 KiB says `64 KiB`. The timeout sentence is already generic (`“<host>” did not answer over ssh within 30 s.`).
     - The log: `logLine` keeps its four parameters and stays declared once and called once (`remoteLogScan.test.ts:90-93`);
       the accounts call's `session` value is the fixed word `accounts`. No `console.` is added (`:86-88` counts one).
     - `defaultSeams` (`:92`) becomes `export function defaultRemoteSeams()`, so the round's log gate writes through the
       same default seam and no second `console.` exists.
     - **The runner's two numbers are held to S2's** (C3): `ACCOUNTS_LIMITS.maxStdoutBytes >=
       ACCOUNTS_REPLY_BYTES_MAX`, and `ACCOUNTS_LIMITS.ms === ACCOUNTS_CEILING_SECONDS * 1000` — S2's A2-T26 sums the
       script's own limits against that ceiling, so the two numbers cannot part (A3-T4).
     - **One rule takes control and format characters out (S2 X12; G2: C-11 and N1).** The private `withoutControls`
       (`:216`) is deleted and the one that `remoteAccountsReply.ts` exports is imported in its place. In
       `src/remoteRead.ts`, `plainOf` (`:67`) becomes a call of it. One commit and no change of behaviour: it is the
       same expression, and the session reader's tests stay green with no edit. `remoteAccountsReply.ts` imports
       neither of the two files, so there is no import circle (A3-T28).
  2. **NEW `src/remoteAccountsRound.ts` (pure; every effect a seam).**
     - Constants: `ROUND_GAP_MS = 600_000` (from the END of a round, O4), `FIRST_DELAY_MS = 30_000`, `REFRESH_FLOOR_MS =
       60_000`, `HOURLY_MS = 3_600_000`; `type UsagePolicy = { kind: 'every-round' } | { kind: 'hourly' } | { kind:
       'on-press' } | { kind: 'off' }` and `export const USAGE_POLICY: UsagePolicy = { kind: 'every-round' }` — the one
       constant the release build sets from W6 (parent § 2's table) and from W7: `off` is W7's stated consequence,
       "the Claude usage read is then off and the list shows the account only" (S2 X8). E1 ships `every-round` (the
       pre-release build of E2 reads in every round); § 6, item 8 has the order that keeps a timer from running the
       usage command before W7.
     - `accountsHosts(live: readonly LiveTerminal[]): readonly string[]` — the distinct hosts of the live (not exited)
       terminals whose name gives `credsSession(...).kind === 'session'`, sorted by name (code-unit order, as `byHost`
       compares, `remoteRead.ts:160`).
     - `roundOrder(hosts, round: number)` — the sorted hosts rotated to start at `round % hosts.length`.
     - `HostMemory = { reading?: { read: Extract<AccountsRead, { kind: 'read' }>; atMs }; unreadable?: { why; atMs };
       failure?: { sentence: string; atMs }; admission?: Exclude<Admission['kind'], 'read'>; claudeAccount?: string;
       usageAtMs?: number; usageBlockedAt?: string /* the Claude Code version */ }` and `Memory = ReadonlyMap<string,
       HostMemory>`; every update returns a NEW map. A `refused` run sets `failure` and keeps `reading` (the list shows
       the older numbers and that the later read failed). **`admission`** is what the door answered when it was not
       `read` — `refused`, `not-asked`, `full` or `damaged`. The round sets it, and a `read` admission clears it. It is
       where S4 learns *not asked yet*, *full* and *damaged* from: nothing else holds the admission (G2, C-4; S1 X8).
     - `claudeAccountKey(found, host): string` — for a KNOWN account (`email` is not empty): the words `claude` and
       `account`, then `email` and `org`, joined by a character none of them can hold after the parser (`\u0000`).
       `org: null` is joined as the empty text, which no real `org` can be (the parser gives `null` for an empty one),
       so an account with no organization and one with an organization never share a key. Two hosts share a key only
       when the whole account text is equal (parent § 1). **For an account that is NOT known (`email` is `''`): the
       words `claude` and `host`, then the host's name** — a key of that host alone. So two unknown accounts never
       merge, whatever their `org`: each host reads its own usage, and S4, which groups by this same key, draws one
       group for each (G2, C-1; parent § 1: "An account that is not known is never grouped").
     - `modeFor(memory, host, roundUsage: ReadonlySet<string>, policy, now, trigger): AccountsMode` — `no-claude-usage`
       when: the host's last `claudeAccount` is in `roundUsage` (its usage was READ in this round); or the host is blocked
       (`usageBlockedAt` equals the version of its last reading); or the policy says the account is not due. Else
       `claude-usage`. A host never read, or whose last read failed, has no account → `claude-usage` (unless the policy
       says no). `due(policy, lastUsageAtMs, now, trigger)`: `every-round` always; `hourly` when no usage of that account is
       known or it is `HOURLY_MS` old; `on-press` only for `trigger === 'refresh'`; `off` never, for any trigger.
     - `needsSecondCall(sentMode, read, roundUsage, memory, host, policy, now, trigger): boolean` — true only when
       `sentMode` was `no-claude-usage`, the reply is `found` with an account whose key is NOT in `roundUsage`, the account
       is due, and the host is not blocked (parent § 2, finding 3). At most ONE extra call for a host in a round. With
       the policy `off` no account is ever due, so there is no extra call: a switch that is off is not gone round by
       finding 3 (S2 X14; G2, S-3).
     - `blockedAfter(read)` — a reply whose usage is `failed` with `why` `model-call` or `format` blocks the host at its
       Claude Code version; a reading with another version unblocks it. A reload starts a new `Memory` (unblocked).
     - `runRound(seams: RoundSeams, memory: Memory, round: number, trigger: 'timer' | 'refresh' | 'open'):
       Promise<RoundResult>` — for each host of `roundOrder(accountsHosts(seams.live()), round)`, in turn, AWAITED:
       (1) `seams.stopped()` → end the round; (2) the host still has a live terminal in `seams.live()` (read again: a host
       whose last terminal closed is not called — a judgement, § 10 J3); (3) `await seams.admit(host)`: anything but `read`
       → no call, and that kind is kept in the host's `HostMemory.admission` (`refused` also drops the host's reading); a
       `read` clears it; (4) `run = await seams.call(host, mode)`; (5) an `answered` run → `seams.parse(run, mode)` with
       the SAME mode — the seam's type is `(run, mode: AccountsMode) => AccountsRead`, and the host binds it as `(run,
       mode) => readAccounts(run, mode, seams.now())`, so the parser gets the round's clock and reads none itself (S2
       X2; G2, C-7); (6) the memory
       updated; (7) `needsSecondCall` → steps (1)–(6) once more with `claude-usage`; (8) `seams.landed()` after each host, so
       the list redraws. A host whose terminal closed while its call ran: its result is DROPPED (`seams.live()` read after the
       call). `RoundResult = { memory; allowedNow: readonly string[]; endedMs }`. `RoundSeams = { live; stopped; admit; call;
       parse; now; landed }` — **no ask seam**.
     - The timer, as pure decisions: `armFor({ eligible, armed, stopped, running }): 'arm-first' | 'clear' | 'keep'` (an
       eligible host = a live `Creds:` terminal of a host for which `sessions.hostStandings()` holds no "no". **S3
       never calls `consentOf`**: it is S1's one function, the setting is its third argument, and A1-T2 scans for a
       second caller. Whether a host is CONTACTED is `admitHost`'s answer alone — S1 X14; G2, C-2); `timerFor(decision,
       test)`, which answers `none` whenever `test` is true (the first test-mode lock, below); after a round,
       `nextAfterRound(...)` → `ROUND_GAP_MS` from `endedMs` or `clear`; `refreshAnswer({ running, lastEndMs, now }): 'start' |
       'busy' | 'floor'`; `openStarts({ running, lastEndMs, now, unreadHost })` (opening the list starts a round only when a
       host that is not refused has neither a reading nor a failure yet, and not inside the floor).
     - `logGate(write: (line: string) => void)` → `{ seamFor(host): (line) => void; answered(host): void }`: `seamFor`
       writes a host's line only when it differs from the last line written for that host; `answered` writes ONE fixed
       line, `ConnectOtherAIs: accounts read host=<host> answered again`, when that host's last written line was a failure,
       and forgets it. A host down for a day is two lines, not 144 (parent § 2).
     - The state row (C5): `accountsStateOf(value: unknown): { kind: 'state'; state: AccountsState } | { kind:
       'damaged' }`. `undefined` (never written) is the empty state; an object whose three fields are each absent or a
       string is that state, and its other keys are kept; anything else is `damaged`. **`stoppedOf(value: unknown):
       boolean` is true for a `stoppedUtc` AND for a damaged row** (G2, S-2): the stop is the way out when a read does
       harm on a VM (parent § 3), so a row that cannot be read must not open it. The write is `stateMerge:
       Merge<StateChange, StateCommitted>` in the form S1's § S1.4 types — `(stored: unknown, change) => { next,
       result }` — given to `sessions.commitKey` (S1 X6, X15; G2, C-3). So the value is read in the step of the write,
       and a stale window cannot erase another's stop. Over a state that can be read: `stop` sets `stoppedUtc`; `start`
       removes it (both are a person's press: the later commit wins); `told` sets `toldUtc` only when absent;
       `autoNoticed` keeps the later of the two times. **Over a damaged row only a person's press writes:** `stop`
       writes a new row with `stoppedUtc`, `start` writes a new empty row, and `told` and `autoNoticed` give no `next`
       — nothing is written. After such a `start` the two notice times are gone, so the first-round notice and the
       daily notice can each be shown once more: the price of not guessing them. The list over a damaged row is the
       stopped list, with its one row (S4): no new words.
     - The second test-mode lock, pure: `callFor(test, real, replaced)` answers `replaced ?? (test ? STUB_CALL : real)`,
       and `STUB_CALL` answers `refused` with one fixed sentence — *The accounts call is not replaced in this test
       run.* — and calls nothing.
  3. **NEW `src/remoteAccountsHost.ts` (host; its row in `sonarcloud.yml:268` in the same commit).** The seams only:
     - `bindTerminalAccounts(context, sessions: RemoteSessions, test: boolean): TerminalAccounts`, called once in
       `extension.ts` right after `bindRemoteSessions` (`:161`), with `test = context.extensionMode ===
       vscode.ExtensionMode.Test`. It holds the window's `Memory`, the round counter, the last `endedMs`, one
       `setTimeout` handle (never `setInterval`: K2), and a `oneAtATime` latch around `runRound`, so two rounds never run at
       once.
     - **Test mode: two locks (G2, § 4; S1 X16).** Three existing host scenarios open a terminal called `Creds: h / s`
       (`src/test/host/terminalHostsScenario.ts:56`, `src/test/host/terminalDoorsScenario.ts:60`, `:94`). A round armed
       by such a terminal would admit `h` — an automatic row, written under those scenarios' own store assertions — and
       start a REAL `ssh` to `h` 30 s later, in CI's real-editor job. It would also call A3-H1's fake more often than
       A3-H1 counts. So with `test` true: (1) **the round never arms itself** — the host still computes `armFor` and
       reports it through `armed()`, but `timerFor` answers `none` and no `setTimeout` is started; scenarios drive
       rounds with `runRoundNow` (C7); (2) **the default call is a stub** (`callFor`): it answers `refused` and spawns
       nothing, until `replaceCall` gives a call, and again after `replaceCall(undefined)`. A scenario that forgets one
       lock still starts no `ssh`. Outside test mode nothing changes: the parent's own host-scenario row has "the call
       replaced through the test door", and the self-arming is held by the pure A3-T20.
     - The seams: `live` = `liveTerminals(vscode.window.terminals)`; `stopped` = `stoppedOf(globalState.get(
       'coai.terminalAccountsState'))`, read before every call; `admit` = `sessions.admitHost`; `standings` =
       `sessions.hostStandings` (for `armFor`); `call` = `callFor(test, real, replaced)` with `real` =
       `runAccounts(host, mode, ACCOUNTS_LIMITS, { ...defaultRemoteSeams(), log: gate.seamFor(host) })`; `parse` =
       `(run, mode) => readAccounts(run, mode, Date.now())`; after an `answered` run, `gate.answered(host)`.
     - Arming: at activation, `armFor` with a `FIRST_DELAY_MS` timer when an eligible host exists;
       `onDidOpenTerminal` → `armFor` (30 s when not armed); `onDidCloseTerminal` → `armFor` (clear when none is eligible)
       and drop from `Memory` every host with no live terminal; the change signal of S1 → drop the readings of refused hosts
       at once and `armFor`. After a round, `nextAfterRound`. Disposed with the extension (`context.subscriptions`).
     - `refresh()`, `openStarts()`, `stop()` (commit `{ stop }`, clear the timer, drop `Memory`), `start()` (commit `{
       start }`, then a round at once), `memory()` and an event `onLanded` — for S4.
     - The test door C7, built in `extension.ts` in a THIRD spread behind `ExtensionMode.Test` (`:935-937` shows the shape),
       field `terminalAccounts?: TerminalAccountsTestDoor` in `CoaiExports` (`:104-122`): `replaceCall`, `runRoundNow`,
       `armed`, `memoryHosts`.
- **Scenarios** (all over `remoteAccountsRound.ts` with fake seams and a fake clock, unless named).

| # | Name (file) | Assertion | Break-it → the RED it must show |
|---|---|---|---|
| A3-T1 | the accounts argv is the pinned one (`remoteSsh.test.ts`, extended) | `accountsArgv('ai1-dev2', 'claude-usage')` deep-equals `[...SSH_OPTIONS, '--', 'ai1-dev2', 'sh', '-s', '--', "'claude-usage'"]`; the same for `no-claude-usage`; no `-n`; `SSH_OPTIONS` deep-equals its E2 value | add `-n` → `the remote sh -s would read an empty stdin` |
| A3-T2 | a bad host or mode never reaches ssh | hosts `-oProxyCommand=x`, `h;id`, `h\n`; modes `''`, `usage`, `claude-usage;id` → `accountsArgv` throws the fixed word; `runAccounts` answers `refused` with the bad-operand sentence and the fake `capture` saw NO call | drop the mode check → `a mode outside the list reached ssh` |
| A3-T3 | the session reader is unchanged | every existing test of `remoteSsh.test.ts` green with no edit; `sshArgv('ai1-dev2', ['small-tasks'])` as before; the session's refusal sentences byte for byte today's, `8 MiB` included | build the session sentence from `subject` with a typo → the existing S2-T7 companions are red |
| A3-T4 | the accounts call's limits and stdin | a fake `capture` records: `input === ACCOUNTS_SCRIPT`, `options` = `{ cwd: <ssh's folder>, maxStdoutBytes: 65536, killTree: true }`, `shell` false, `capMs` 30 000; a child that never closes with `limits.ms` 50 → `“h” did not answer over ssh within 0.05 s.`; a multi-byte stdout over the byte cap → `“h” sent more than 64 KiB, so the read was stopped.`; `ACCOUNTS_LIMITS.maxStdoutBytes >= ACCOUNTS_REPLY_BYTES_MAX` and `ACCOUNTS_LIMITS.ms === ACCOUNTS_CEILING_SECONDS * 1000` (S2 X9, X14) | send `REMOTE_SCRIPT` → red naming the script; set the limit to 45 s → `the runner's limit and the ceiling the script's limits are summed against differ`; say MiB → `0.0625 MiB is not a size a person reads` |
| A3-T5 | ssh's own failure is not an answer | exit 255 with `Permission denied (publickey).` → `refused`, the sentence ends with that line; exit 1 → `answered` with code 1 (the parser decides) | map 255 to `answered` → red |
| A3-T6 | the accounts sentences name the accounts | the not-started and no-ssh sentences of `runAccounts` say *the AI accounts on “h”*; `runRemote`'s say *the Claude session on “h”* as today | share one literal → red naming the subject |
| A3-T7 | the log is one line of four fields | `logLine` declared once, called once (the existing companion); a failed accounts call logs `session=accounts`; no stdout in it (the existing scanner over the new file) | log the mode as an operand list → the scanner is red |
| A3-T8 | a host that stays down is logged twice, not 144 times | `logGate`: the same failure line 144 times → 1 write; a different line → 1 more; `answered` after a failure → the fixed line once; `answered` again → nothing; a new failure → written | write every line → `a host down for a day wrote 144 lines` |
| A3-T9 | which hosts | live terminals `Creds: hB / s1`, `Creds: hA / s2`, `Creds: hA / s3`, `bash`, `Creds run: x`, `Creds: -h / s`, and an exited `Creds: hC / s` → `['hA', 'hB']` | keep exited terminals → `an exited terminal's host was read` |
| A3-T10 | the order moves one host each round | hosts `a`, `b`, `c`: round 0 → `a b c`, round 1 → `b c a`, round 3 → `a b c` | always start at `a` → `the last host is always read last` |
| A3-T11 | one call at a time | a fake `call` that resolves after a tick logs start and end: no start before the previous end; three hosts → three calls in order | `Promise.all` → `two reads ran at once` |
| A3-T12 | a failed host does not stop the round | host `a` `refused` with *“a” did not answer over ssh within 30 s.* → its `failure` holds that sentence and the time; `b` and `c` are still called; `a`'s older `reading` is kept | `return` on a refusal → `the round stopped at the first failed host` |
| A3-T13 | the door is asked before EVERY call, and its "no" holds | `admit` answers `refused`, `not-asked`, `full`, `damaged` → 0 calls to that host; `admit` answers `read` for `a` and, after `a`'s call, `refused` for `b` → 1 call; a `refused` drops `b`'s reading; each of the four kinds is kept in that host's `HostMemory.admission`, and a later `read` clears it (G2, C-4) | ask `admit` once per round → `a "no" given during a round did not stop the next call` |
| A3-T14 | the mode for each host (usage once for each account) | `a` and `b` of account X: `a` → `claude-usage`, its usage read → `b` → `no-claude-usage`; `a`'s usage `failed timeout` → `b` → `claude-usage`; a host never read → `claude-usage`; three hosts of one account → ONE `claude-usage`. **Two hosts whose account is not known (`email` `''`) and whose `org` is equal → two keys, BOTH `claude-usage`, and the second host's usage is read (G2, C-1).** A known account with `org` `null` and the same email with an `org` → two keys | send `claude-usage` to every host → `one account's usage was read twice in a round`; key an unknown account by its `org` → `two unknown accounts were merged, and the second host's usage was never read` |
| A3-T15 | the account that changed gets ONE more call (finding 3) | `b` last seen with X; X read on `a`; `b` called `no-claude-usage` answers Y → `b` called once more with `claude-usage` in the SAME round, before `c`; the extra call answers Z → no third; Y's usage already read on another host this round → no extra call; `b` blocked → no extra call | omit the extra call → `a new account's usage was a round late`; loop → `a host was called three times in a round` |
| A3-T16 | a usage read that went wrong is not repeated blindly | a reply `failed model-call` at version V → `b`'s next rounds are `no-claude-usage`; a reading at version W → `claude-usage` again once; `failed format` the same; `failed timeout` does not block; a new `Memory` (a reload) is unblocked | ignore `model-call` → `a model call was made six times an hour` |
| A3-T17 | the usage policy is one constant | `every-round` → `claude-usage` in each of 6 rounds 10 min apart; `hourly` → at most once in those 6 rounds for one account (fake clock); `on-press` → never on `timer` or `open`, once on `refresh`; **`off` → `no-claude-usage` for every host and every trigger, `refresh` among them, and `needsSecondCall` is false for a host whose account changed (S2 X8, X14)**; `USAGE_POLICY` is `every-round` | read the policy from a setting → red (no such setting); let finding 3's extra call run with `off` → `a usage read was made while the usage read is off` |
| A3-T18 | the calls of a round are bounded | five hosts, one of which changed account → at most 6 calls, at most 2 to one host; a companion counts the calls of A3-T15's round exactly | — |
| A3-T19 | a closed host is dropped, and a result in flight too | `b`'s last terminal closes before the round → no call, its memory dropped; it closes while its call runs → the result is not stored | store a late result → `a closed terminal's host kept its reading` |
| A3-T20 | the first round and the gap (fake clock) | an eligible host at activation → `arm-first`, a round at +30 s; none → no timer, and a reload with none starts nothing; the first `Creds:` terminal opens at T → a round at T+30 s; a round that ends at E → the next at E+600 s, also when it took 150 s; an open while armed → `keep` | count the gap from the start → `the next round began before 10 minutes after the last ended` |
| A3-T21 | the timer goes with the last terminal | the last eligible terminal closes → `clear`; only refused hosts left → `clear`; a terminal opens → `arm-first` again | leave the timer idle → `a timer ran with no Creds terminal` |
| A3-T22 | *Refresh now* and opening the list | `refreshAnswer`: no round running, the last ended 61 s ago → `start`; 59 s → `floor`; running → `busy`; after a refresh round the next timer round is 600 s after ITS end; `openStarts` true only for a host with neither reading nor failure, and not inside the floor | drop the floor → `Refresh started a round 30 s after the last` |
| A3-T23 | the stop holds, and a state row that cannot be read is a stop | `stateMerge` `stop`, then a stale window's `autoNoticed` through `commitKey` → `stoppedUtc` kept (the merge is given the stored value of NOW); `stopped()` true before a call → the round ends, 0 calls, 0 admissions (so no automatic row); `stoppedOf` of a stored stop after a reload → no timer is armed; `start` → `stoppedUtc` gone; `told` twice keeps the first. **A damaged row (G2, S-2):** for each of `'junk'`, `null`, `7`, `[]` and `{ stoppedUtc: 5 }` → `stoppedOf` is true, a round makes 0 calls and 0 admissions, no timer is armed, `told` and `autoNoticed` write nothing and the stored value is the same value; `start` writes `{}` and the next round runs; `stop` writes a row with `stoppedUtc`. Companion: `undefined` and `{}` are NOT stopped | read the stop once per round → `a stopped round made one more call`; read a damaged row as `{}` → `a state row that could not be read let the round run` |
| A3-T24 | nothing of a reading is stored | after three rounds with readings, the fake `globalState` holds only `coai.terminalHosts` rows written by `admit` and `coai.terminalAccountsState` with at most the three fields, under 150 bytes | store the memory → `an email reached globalState` |
| A3-T25 | the round never asks a person | `RoundSeams` has no member whose name contains `ask` (a type-level assertion: a seam object literal with `ask` does not compile, `@ts-expect-error`); with a fake `admit` that throws when asked to ask, 100 new hosts raise nothing | — |
| A3-T26 | the parser reads the mode that was sent, and the round's clock | a fake `parse` of the seam's type `(run, mode)` records both: every call's mode equals the one passed to `call` for it, the extra call's included. A scan of `remoteAccountsHost.ts` with its companion: `readAccounts(` is called once, with three arguments, inside the `parse` seam (S2 X2; G2, C-7) | parse with `claude-usage` always → `the reply was read against another mode` |
| A3-T27 | the test door is built only in test mode, and in test mode no round arms itself and no call reaches ssh (`remoteExportGuard.test.ts`, extended; `remoteAccountsRound.test.ts`) | ONE place builds `terminalAccounts: …TestDoorOf(`, right after `extensionMode === vscode.ExtensionMode.Test ? {`, in a spread of its own; the `CoaiExports` field is optional; `bindTerminalAccounts(` appears once, and its third argument is `context.extensionMode === vscode.ExtensionMode.Test` and nothing else. **Both locks (G2, § 4):** `timerFor(decision, true)` is `none` for every decision of `armFor`, and `timerFor('arm-first', false)` is not; `callFor(true, real, undefined)` answers `refused` with the fixed sentence and `real` was NOT called; `callFor(true, real, fake)` calls `fake`; `callFor(false, real, undefined)` is `real`. A scan of `remoteAccountsHost.ts` with its companion: the one `setTimeout(` of the round is reached only through `timerFor(`, and `runAccounts(` only through `callFor(` | build it unconditionally → red; `timerFor` ignores `test` → `a round armed itself in a test run`; `callFor` ignores `test` → `an accounts call reached ssh in a test run` |
| A3-T28 | one rule takes control and format characters out (`remoteLogScan.test.ts`, extended; S2 X12) | a scan of `src/*.ts`, tests left out: the character class `[\p{Cc}\p{Cf}]` stands in ONE file, `remoteAccountsReply.ts`; `remoteSsh.ts` and `remoteRead.ts` each import `withoutControls` from `./remoteAccountsReply`, and that file imports neither of them. Companion: the scan finds the class in a sample string. The tests of `remoteSsh.test.ts` and `remoteRead.test.ts` that hold a bidi mark, a zero-width space and a bell are green with no edit | keep the private copy in `remoteSsh.ts` → `the plain rule stands in two files` |

- **Host scenario A3-H1** (NEW `src/test/host/terminalAccountsScenario.ts`, registered in `scenarios.ts:234`): with the call
  replaced through the test door by a fake that answers a fixed reply and logs start and end, terminals `Creds: hA / s1`,
  `Creds: hA / s2` and `Creds: hB / s1` opened → `runRoundNow('timer')` → the fake was called ONCE for `hA` and ONCE for
  `hB`, one after the other, `hA` first; the store holds an automatic row for each (the setting's default); all three
  terminals closed → `armed()` false, and `runRoundNow('timer')` calls nothing. The scenario never waits for a timer:
  in test mode there is none, and `armed()` says what `armFor` answered. In a `finally`: the terminals disposed, the
  store put back with the road's test door — `restore(before)`, where `before = stored()` was taken before the
  scenario started (S1 X10) — and the call put back with `replaceCall(undefined)`, which in test mode is the stub
  again. CI only (§ 8).
- **Docs.** S5 writes them (the runner, the round, the timer, the log gate).
- **Done when.** A3-T1…T28 green, each break-it seen red; the existing `remoteSsh.test.ts`, `remoteLogScan.test.ts` and
  `remoteRead.test.ts` green with no edit beyond S1's; `sonarExclusions.test.ts` green with `remoteAccountsHost.ts` listed;
  `npm run compile` exit 0; the whole suite and lint green; A3-H1 green in CI's real-editor job on the head commit, read from
  that job.
- **Size.** About 1000 lines (≈ 120 runner, ≈ 300 round, ≈ 150 host, ≈ 430 tests).

### E1.S4 — The list and the icon

`model: opus` (spec, this section) · coder: sonnet · review: opus · area (d) on the screen, and the trigger. **Depends on
S3** (the memory, the round's buttons) **and S1** (`hostStandings`, `changeHost`, `askHost`, `ignoredLayer`,
`allowsNewHosts`, `dayOf`, and the words of a host's row over a damaged list). **This brief holds the rows of both
specs' X tables that S4 must follow** (S1 X7, X8, X12, X13; S2 X3–X7, X11, X13, X15).

- **Goal.** The icon opens a QuickPick that stays open, redraws when a host's read lands, groups by account, says every
  number as "left" in the mod's words with a dash for a missing one, gives every host its own sentence — the fixed command
  for a host without `jq` among them, with a copy button — and carries the two title buttons and the two notices.
- **Files and functions (what and how).**
  1. **NEW pure `src/usageWords.ts`** (no imports but types).
     - `percentLeft(used: unknown): number | null` — the mod's rule word for word (`limits.tsx:36-40`): `100 − used`,
       rounded, clamped 0–100, `null` for anything that is not a finite number.
     - `minutesLeft(resetsAtMs, nowMs)` — the mod's (`:88-91`): whole minutes, `null` while unknown or once the reset has
       come.
     - `resetText(minutes)` — the mod's (`:93-95`) under 24 hours: `1h 0m`, `59m`, `<1m`, `—`; **from 24 hours on**
       `<d>d <h>h` (1 440 → `1d 0h`, 3 790 → `2d 15h`), the form of the parent's § 1 sample (§ 10, K4).
     - `windowLabel(window)`: Claude `5h`, `week`, a model window by its model's name; Codex by `minutes`: 300 → `5h`,
       10 080 → `week`, another length in hours with at most one decimal (90 → `1.5h`).
     - `part(label, used)` → `<label> <n>% left`, or `<label> —` with no `% left` when `percentLeft` is `null`.
  2. **`src/gaugeWords.ts`** — `agoWords` (`:92`) gains `export`, nothing else changes (its tests green). The list says
     `read 4 min ago`, `from a Codex session 2 d ago` with it.
  3. **NEW pure `src/remoteAccountsRows.ts`.**
     - `forList(text: string, max: number): string` — the ONE road by which text from a VM reaches a row. It CALLS
       S2's `plainValue` (control and format characters out, every `$(` written `$ (`, 200 characters at most; doing
       it twice changes nothing) and then cuts at `max` characters with `…`. It holds no character class of its own,
       and `plainOf` of `remoteRead.ts` is NOT exported for it (S2 X7; G2, C-5). So the list never draws an icon a VM
       chose (U10; § 10, K3). **The runner's failure sentence passes `forList` too** (S2 X13; G2, S-5): it can end
       with the last line of ssh's stderr, which a remote login shell's rc file can print, and which has kept its
       `$(`. Hosts come from the terminal name's rule and are not VM text.
     - `INSTALL_JQ: Readonly<Record<ManagerWord, string | undefined>>` — exactly the parent's § 4 table: `apt-get` → `sudo
       apt-get install -y jq`; `dnf` → `sudo dnf install -y jq`; `yum` → `sudo yum install -y jq`; `apk` → `apk add jq`
       (the row adds *to be run as root*); `none` → no command and the sentence *Install `jq` on this host. CoAI found none
       of apt-get, dnf, yum and apk there.* The row's command is ONLY ever a value of this table.
     - `AccountsView = { live: boolean /* any Creds: terminal */; stopped: boolean; busy: boolean; memory: Memory;
       standings: readonly HostStanding[]; hosts: readonly string[]; ignored: 'workspace' | 'folder' | undefined;
       allowNew: boolean }` — `standings` from `sessions.hostStandings()`, `ignored` from `sessions.ignoredLayer()`,
       `allowNew` from `sessions.allowsNewHosts()` (S1 X7).
     - `accountsRows(view, nowMs): readonly Row[]` where `Row` = `{ kind: 'separator'; label }` | `{ kind: 'usage'; label;
       description; detail }` | `{ kind: 'host'; host; label; description; copy?: string }` | `{ kind: 'info'; label; action?:
       'start' }`. In this order: the ignored-layer row (*This workspace sets `coai.terminalAccountsAllowNewHosts`. It is
       ignored: only your own user settings count.*, *a folder* for a folder value); then, when stopped, ONLY *The accounts
       round is stopped. Choose this row to start it.*; when no `Creds:` terminal is open, ONLY *No `Creds: <host> /
       <session>` terminal is open, so there is no host to read.*; else the groups, then `── Hosts ──` and one row for each
       host.
     - **The groups** (parent § 1). Claude: one group for each `claudeAccountKey(found, host)` (S3) among the hosts'
       readings; its
       separator `Claude Code · <email> · <org>`; its row's label the mod's line — `5h <n>% left · resets in <t> · week <n>%
       left · <Model> <n>% left`, one model part for each model window `/usage` named — its description `week resets in
       <t>`, its detail `<host>, <host> · read <age>` from the NEWEST reading of the group (and `· the later read of <host>
       failed` when one has). A found account whose `email` is empty is not known: its key is its host's own, so it is one
       group for its host, whatever its `org`, and its separator names the host in the account's place. Codex: one group
       for each host (`Codex · account name not shown by Codex · plan <plan> · <host>`), its label the windows by
       `windowLabel` with `resets in`, its detail `from a Codex session <age> · read <age>` — with `asOf === null` the
       age is `—`, and with `statMissing` the row adds *this host has no stat, so the time of the Codex session file
       was not read* (S2 X4, X15); a window whose reset has passed → *no reading since this window was reset*. Antigravity: `Antigravity <version> · <host>` (or `Antigravity (version
       not known)`), row *agy states no usage, and its account is not read*. A host alias never groups; equal numbers never
       group.
     - **Usage not read says why**, never a row of dashes alone: `failed format` → *usage not read: Claude Code <version>
       on this host printed no usage line this version understands*; `failed model-call` → *usage not read: the usage
       command of Claude Code <version> asked the model, so CoAI stopped asking this host*; `failed timeout` → *usage not
       read: Claude Code did not answer within 8 s*; `no-jq` → the host row's command; `signed-out` → *signed out*. A
       `skipped` usage draws nothing of its own (another host of the account read it). **The words the S2 spec added
       (S2 X3–X5; its § A2.7 has each sentence):** usage `failed error` → *usage not read: the usage command of Claude
       Code <version> ended with an error*; Claude `unreadable auth` → *the account command of Claude Code printed
       nothing this version understands*; `unreadable timeout` → *the account command of Claude Code did not answer
       within 3 s*; Codex `failed no-sessions` → *no Codex session file is on this host*; `failed none` → *no usage
       record is near the end of the newest Codex sessions*; `failed error` → *the Codex session files could not be
       read*; any vendor `unreadable credential-shape` → *not shown: a value in the answer looked like a credential*; a
       whole answer that is `unreadable` → *The AI accounts on “<host>” could not be read: <clause>.*, the clause from
       `ACCOUNTS_UNREADABLE_SAID`. The seconds in these sentences come from `ACCOUNTS_SECONDS`, never from a literal
       (S2 X11).
     - **The host sentences**, one for each standing (C1 and C3): *turned off: your answer for this host is “Not this
       host”*; *not asked yet — choose this row to be asked* (setting off); *never read yet*; *the last read failed <age>:
       <the runner's sentence>*, the sentence through `forList`; *the list of hosts is full, so this host is not read*
       (`full`); over a list of hosts that cannot be read, S1's W11 word for word (`HOSTS_SAID`: choosing the row opens
       the change question, and the answer there repairs the list); *allowed automatically on <day>. Choose this row
       to change that.* (`<day>` is S1's `dayOf(askedUtc)`; also when the host has a reading); *jq is missing on this
       host. Run there: <INSTALL_JQ[word]>* with `copy` set to that same string. **Where each comes from:** *turned
       off* from a "no" among `standings`; *not asked yet*, *full* and the damaged sentence from
       `HostMemory.admission`, which S3's round sets (before a host's first admission, *not asked yet* also follows
       from no standing with `allowNew` false); *the last read failed* from `failure`.
     - `noticesDue(state: AccountsState, standings, firstRoundEnded: boolean, nowMs)` → `{ autoHosts: readonly string[];
       first: readonly string[] }`: `autoHosts` = the automatic rows whose `askedUtc` is after `lastAutoNoticeUtc` (all of
       them when it is absent), and only when it is absent or 24 h old; `first` = the person's "yes" rows older than the first
       round, once, while `toldUtc` is absent (the host then commits `{ told }` whether or not the list was empty).
  4. **`src/remoteAccountsHost.ts`** (S3's module) gains:
     - The command `coai.terminalAccounts`, registered in `bindTerminalAccounts` (as `coai.terminalHosts` is registered in
       `bindRemoteSessions`, `remoteSessionHost.ts:221`): ONE `createQuickPick` instance per press, kept while open; items
       from `accountsRows(view, Date.now())`; separators as `vscode.QuickPickItemKind.Separator` (as
       `conversationPickerCommand.ts:99`); `matchOnDescription`, `matchOnDetail`; `busy` while a round runs; the items
       rebuilt on S3's `onLanded` without closing (`keepScrollPosition`); disposed on hide.
     - Two title buttons: *Refresh now* (`refresh()`; on `busy` or `floor` the list shows it is busy or one row
       *A round ran less than a minute ago.*) and *Stop the accounts round* (`stop()`). An item button on a row with
       `copy`: `textCopier(...)` (`copyText.ts:203`) with that row's `copy` string — never a text built from the reply.
     - Choosing: a host row → `sessions.changeHost(host)`, or `sessions.askHost(host)` when it is *not asked yet*; the
       *start* row → `start()`; any other row → nothing.
     - Opening: `openStarts(...)` → a round.
     - The notices, after each round (`noticesDue`): the daily notice — `notifyThen` (`notify.ts:318`), `as: 'information'`,
       `class: 'outcome'`, `source: 'terminalAccounts'`, `code: 'accounts-hosts-allowed'`, a title naming the hosts allowed
       automatically, where to forbid one (*CoAI: hosts read over ssh*, *Not this host*), the setting
       `coai.terminalAccountsAllowNewHosts` and that it is on; ONE action *Open settings*, which runs
       `workbench.action.openSettings` with `coai.terminalAccountsAllowNewHosts` (UNVERIFIED in this repository: no caller
       uses that command today; R2/R11 observe it) and decides nothing; then commit `{ autoNoticed: now }`. The first-round
       notice — `notify`, `class: 'outcome'`, `code: 'accounts-read-every-ten-minutes'`: the hosts with a "yes" from before,
       that their AI accounts are now read every 10 minutes, and where to stop it; then commit `{ told: now }`. Two new
       sites: regenerate `notification-sites.json`, raise `PLACES_THIS_SPEAKS` to the counted number with a dated comment
       (188: S1 left 186 — S1 X13).
     - The test door gains `lastItems()`: the labels, descriptions, details and kinds of the items last given to the list.
  5. **`package.json`** — the command `coai.terminalAccounts`, title **AI accounts and usage**, category
     `ConnectOtherAIs`, icon `$(account)`; a `view/title` row `when: view == terminal`, `group: navigation@91`; NO
     `commandPalette` row (so it is in the palette); NO keybinding (the skip list does not change).
  6. **`src/test/chatWiring.test.ts` — the door census** (K1): `doorCommands()` (`:482-493`) counts every `view == terminal`
     title-bar row as a way into a chat, so the new button would need a `noteChatDoor` it must not have. A named allow-list
     `OPENS_NO_CHAT: readonly string[] = ['coai.terminalAccounts']` beside `ACTS_INSIDE_A_CHAT` (`:475`), filtered out in
     `doorCommands`, with a test that keeps it honest: every command on it is offered from the terminal title bar and its
     registration holds no `noteChatDoor(`; the E2 assertion that `coai.terminalHosts` is no door stays.
  7. **The help the census needs, in five languages**: a NEW article `ai-accounts-and-usage` in `helpContent.ts` and the four
     translations, all five sections, whose English names **AI accounts and usage** (`helpCoverage.test.ts:542` then finds the
     title). S5 writes its full text; S4 writes a correct first version.
- **Scenarios.**

| # | Name (file) | Assertion | Break-it → the RED it must show |
|---|---|---|---|
| A4-T1 | the mod's cases, as a table (`usageWords.test.ts`, new) | `percentLeft`: 13→87, 33.4→67, 100→0, 0→100; 130→0, −5→100, `Infinity`→`null`; `null`, `'13'`, `undefined`, `NaN`→`null` (P1–P3); `resetText`: 60→`1h 0m`, 59→`59m`, 1→`1m`, 0→`<1m`, `null`→`—` (A4); a line with every value absent is `5h — · resets in — · week — · Fable —` (L3) | round down → `33.4 used is not 66 left` |
| A4-T2 | from a day on, days and hours | 1 439→`23h 59m`; 1 440→`1d 0h`; 3 790→`2d 15h`; 7 139→`4d 22h` | keep `63h 10m` → `the week's reset is said in hours` |
| A4-T3 | windows by their names | Claude `5h`, `week`, `Fable`; Codex 300→`5h`, 10 080→`week`, 120→`2h`, 90→`1.5h` | name Codex `primary` by its slot → `a weekly window was called 5h` |
| A4-T4 | the Claude row is the mod's line | a reading of 56/77/49 used with resets → label `5h 44% left · resets in <t> · week 23% left · Fable 51% left`, description `week resets in <t>`; no session window → `5h — · resets in —`; the reset text from `resetsAtMs` at DRAW time (two `now` values, two texts) | draw "used" → `the list says 56% left where 44% is left` |
| A4-T5 | absent is never zero | every `null` of a reading → `—` with no `% left`; a scan of every label of 200 generated readings with `null`s finds no `0%` that was not a `0` in the input | `?? 0` → red naming the label |
| A4-T6 | the groups | one Claude account on `hA` and `hB` → ONE group naming both; two accounts with equal numbers → two; two hosts whose email is empty → two, also when their `org` is equal (the key is the host's own: G2, C-1); Codex on two hosts → two groups; no group by host alias | group by numbers → `two accounts were merged` |
| A4-T7 | the newest reading, and a later failure | `hA` read 4 min ago and `hB` 1 min ago → the group shows `hB`'s numbers and `read 1 min ago`; `hB`'s later read failed → `hB`'s older numbers stay and the detail says the later read of `hB` failed | draw the oldest → red |
| A4-T8 | every host says how it stands | one row for each standing of the brief, each its own sentence, all different; an automatic host with a reading still says *allowed automatically on <day>*; the *not asked yet*, *full* and damaged rows are drawn from `HostMemory.admission`, and the damaged one is S1's W11 word for word | one sentence for two standings → red naming both; ignore `admission` → `a host that is not read over a damaged list says never read yet` |
| A4-T9 | the fixed command for a host without `jq` | for each `ManagerWord` the host row shows exactly `INSTALL_JQ[word]` and its `copy` is that string; `none` → the sentence and no `copy`; the copy button hands `textCopier` exactly `copy` | build the command with the host's name → red |
| A4-T10 | no text from a reply is ever part of a shown command | 50 generated readings whose every text field holds a marker (`; rm -rf ~`, `$(id)`, a newline, a manager-like word) → no row's `copy` and no command text holds any marker; every `copy` is a value of `INSTALL_JQ` | interpolate a reply field → `text from the VM reached the command` |
| A4-T11 | usage not read says why | `failed format`, `failed model-call`, `failed timeout`, `failed error`, `no-jq`, `signed-out`, Claude `unreadable auth` and `unreadable timeout`, the three Codex failures, `credential-shape` for each vendor, agy → each its own sentence with the version where it has one; a whole answer `unreadable` → the one sentence with the clause of `ACCOUNTS_UNREADABLE_SAID`, for every `why` of that table; Codex `asOf` `null` → the age is `—`, and with `statMissing` the row says the host has no `stat`; `skipped` → nothing; never a row of dashes with no reason; the `8 s` and the `3 s` are `ACCOUNTS_SECONDS.usage` and `.account` | one sentence for format and model-call → red |
| A4-T12 | VM text is made plain at one road | `forList` of a text with a bidi override, a zero-width space (built with `String.fromCodePoint`), a bell, `$(zap)` and 10 KB → none of the four, `$ (zap)`, the cut with `…`; a companion scan: every reply text field reaches a row only through `forList(`, and `forList` calls `plainValue(` and holds no character class of its own. **The runner's failure sentence is on the same road (G2, S-5):** a `failure.sentence` that ends with `$(zap)` and a bidi override is drawn with `$ (zap)` and no override, and the scan finds `failure.sentence` only inside `forList(` | draw `email` as it is → `an icon a VM chose was drawn`; draw the failure sentence as it is → `an icon from ssh's stderr was drawn` |
| A4-T13 | the notices | `noticesDue`: the first automatic host → `autoHosts` at once; again 2 h later → none; 25 h later → only the hosts allowed since; none allowed → none; `first` once while `toldUtc` is absent, never after a `told` | show the daily notice every round → `the person was told 6 times an hour` |
| A4-T14 | the stopped list and the empty list | `stopped` → exactly one row, the start row; no `Creds:` terminal → exactly the one sentence; the ignored-layer row first when a workspace or folder sets the key | draw the groups while stopped → red |
| A4-T15 | the manifest (`terminalDoorWiring.test.ts`, extended) | one command row: title `AI accounts and usage`, category `ConnectOtherAIs`, icon `$(account)`; one `view/title` row, `when` exactly `view == terminal`, group `navigation@91`; no `commandPalette` row; no keybinding; `coai.askFromTerminal` still `navigation@90`; the skip-list default unchanged | `navigation@89` → `the accounts icon sits before Ask other AI` |
| A4-T16 | the icon is not a way into a chat (`chatWiring.test.ts`) | `doorCommands()` does not hold `coai.terminalAccounts`; `OPENS_NO_CHAT` holds only commands offered from the terminal title bar whose registration has no `noteChatDoor(`; the census tests of `:562-605` green | drop the filter → `coai.terminalAccounts … does not record exactly one invocation` |
| A4-T17 | the command is in the help (`helpCoverage.test.ts`) | the command test finds `ai accounts and usage` in the English corpus; the new article exists in five languages with five sections each | leave `helpDe.ts` without it → `untranslated: ai-accounts-and-usage/de` |
| A4-T18 | the census | `notification-sites.json` regenerated; `PLACES_THIS_SPEAKS` equals the counted sites; the two new codes are in its `codes` | — |
| A4-T19 | the log scan reaches the new files | `remoteAccountsRows.ts` and `remoteAccountsHost.ts` are scanned and clean; a companion asserts they are among `remoteFiles()` | — |

- **Host scenario A4-H1** (in `terminalAccountsScenario.ts`): `coai.terminalAccounts` is among `vscode.commands.getCommands`;
  with the fake call answering a reply whose `email` holds `$(zap)`, one round, then the command executed → `lastItems()`
  holds a separator `Claude Code · …$ (zap)…`, a `── Hosts ──` separator and one row for each host; the QuickPick is hidden
  in the `finally`. **What it cannot see:** what VS Code DRAWS for `$(`; no API reads a drawn row. **U10 is measured
  here as far as a host scenario can, and the scenario records what was CHECKED** (§ 10, K3; G2): the label text it
  handed to the list, raw and after `forList`; that no extension API reads a drawn row — checked: `QuickPick` and
  `QuickPickItem` of the locked type definitions, and nothing else. The record goes into the summary and into
  `research/module_tests.md` (S5). U10 stays "open" as the parent has it; nothing moves to E2.
- **Docs.** S5.
- **Done when.** A4-T1…T19 green, each break-it seen red; the door census and the help census green; `notification-sites.json`
  regenerated; `npm run compile` exit 0; the whole suite and lint green; A4-H1 green in CI's real-editor job, read from it.
- **Size.** About 1100 lines (≈ 120 words, ≈ 280 rows, ≈ 150 host and manifest, ≈ 80 help, ≈ 470 tests).

### E1.S5 — The documents

`model: opus` (spec, this section) · coder: sonnet · review: opus. **Depends on S1–S4 coded** (it writes what was built,
from the two specs and this document). No product code.

- **Goal.** Every document a person or an agent reads says what E1 built, in "we pass" words where the effect is a
  receiver's, and nothing E2 has yet to see.
- **Files and what each gets.**
  1. **The help, five languages** (`helpContent.ts` and `helpRu.ts`, `helpUk.ts`, `helpDe.ts`, `helpEs.ts`; bold labels and
     italic sentences in English in every language, as the E2 paragraph is):
     - the article `ai-accounts-and-usage` (S4's first version) completed: the icon and where it is; what is read on each
       vendor and what is not (agy's account, Codex's account name); the round every 10 minutes from the end of the last, by
       itself, one host at a time; usage once for each account; the words "left" and the dash; *Refresh now* and its
       one-minute floor; *Stop the accounts round*; a host without `jq` and the command it shows, which CoAI never runs;
       nothing of a reading is stored; what Claude Code writes on a usage read (four cache files, measured);
     - the chat article's remote paragraph rewritten: the per-host answer now covers the AI accounts; with
       `coai.terminalAccountsAllowNewHosts` on (the default) a host with no answer is allowed by itself and named in a
       notice at most once a day; only *Not this host* stops a host, and *Forget* lets it be allowed again — **with the
       K5 clause: a *Forget* given in another window does not lift a “Not this host” while the window that gave it is
       open; that window writes it again and says so; *Read* holds from every window** (the parent's entry 1.1; G2,
       C-6; S1's W3 has the English words, and the four translations say the same); only the user
       settings count; the sentence *"They only read: they change nothing on that machine"* rewritten (our script writes
       nothing; Claude Code writes its own caches on a usage read). The old sentence *"The first time for each host you are
       asked once"* goes.
  2. **`research/module_extension.md`** — a NEW section after `:13909`'s, `## The terminal panel shows the AI accounts and
     their usage (E1)`, with one subsection per story: the store and its merge (S1, from its spec); the script, the reply and
     the parser (S2, from its spec: the commands word for word, what is dropped on the VM, the sha256); the runner and the
     round (S3: the argv, the limits, the modes, the extra call, the policy constant, the timer, the stop, the log gate); the
     list (S4: the rows, the groups, the words, the fixed commands, the notices, the door-census allow-list); and one Mermaid
     sequence diagram of a round (the timer → `admitHost` → `runAccounts` → `readAccounts` → the memory → the list). No
     renderer runs in this repository's CI: the diagram is checked by reading.
  3. **`research/module_tests.md`** — a NEW row after `:317`, *Terminal accounts — THE READ-ONLY LIST (E1)*, naming every
     A1–A5 test and A1-H1, A1-H2, A3-H1, A4-H1; **not covered**: a real VM, a real Claude Code, Codex or agy, a real `jq`
     on this machine, what VS Code draws for `$(` (U10: with what A4-H1 CHECKED and what it did not, § 10, K3), two real
     windows (U12, R12), a remote window (R11) — E2's.
  4. **`research/architecture.md`** — in the outward edge's section (`:624`): a second kind of ssh call, started by a TIMER
     with no press, read-only, to a host that is not refused; and the NEW edge from a VM to a vendor — Claude Code, started by
     our script on a usage read, calls Anthropic with the VM's own login (no secret through our code). The module map names
     the new modules; the cross-cutting part (`:1654`) says the same in one bullet.
  5. **`SECURITY.md`** (`:126-147`) — the section becomes *The extension's outward ssh calls*: two kinds of call in E1;
     the sentences *"No ssh child is started before a "yes""* and *"The first read for a host asks once"* (`:130`) rewritten:
     a host is contacted unless it was refused; the timer; how a new host is allowed, stored and shown, and that only *Not
     this host* stops one; the merge of the store and the cap; that the window which gave a refusal writes it again when
     it is missing, so a *Forget* from another window does not hold while that window is open (K5, the parent's entry
     1.1; G2, C-6); that a list of hosts which cannot be read contacts nobody, and a state row which cannot be read is a
     stopped round; the setting and that only a user-level value counts; the
     daily notice; the stop button; road (a) and what the VM then calls; what Claude Code writes on a read (measured, W4);
     the start of `agy`; the person's activity dropped on the VM; the residuals of the parent's § *Security*. Not W6 or W7:
     their sentences are written after E2, from what was seen (parent § *Security*, last paragraph). Every sentence about an
     ssh option or a request (`TZ=UTC`) says what we PASS, not what it achieves.
  6. **The boundary note** (K10): one sentence beside *The person agrees to each host once* in
     `research/PLAN_the_chat_opens_from_a_terminal.md:310`, naming this plan and what it changed of that answer (the parent §
     *Boundaries* says it is owed; that plan was promoted on 2026-11-18 without it). A documentation edit of a promoted plan,
     not a ledger entry of it.
  7. **The CHANGELOG prose draft** — a NEW § 12 of THIS document, *The release notes, drafted in E1*: the `## Extension
     <version> — <date>` prose for the plan's one release, with its Security paragraph (findings F1 in plain words: a host
     nobody was asked about is read by a terminal's name; the setting that turns it off). `src_vs_code/CHANGELOG.md` is NOT
     edited in E1: the release is one for the whole plan, after E2 and C1, and its prose goes into the release-please pull
     request then (`release-please-config.json:3-30`).
  8. **The `POST_DEPLOY.md` question** — in the same § 12: the proposed item (*the released extension's icon opens the list,
     and its first round logs in once on each host that is not refused*), and that the file is at its cap of twelve
     (`POST_DEPLOY.md:468`), so the item enters only with a named one leaving. The question goes to the consultants, then the
     owner, before the release; `POST_DEPLOY.md` is not edited in E1.
- **Scenarios** (`helpCoverage.test.ts`, extended).

| # | Name | Assertion | Break-it → the RED it must show |
|---|---|---|---|
| A5-T1 | every language's accounts article names its controls | each of the five articles, not a fallback, holds `**AI accounts and usage**`, `**Refresh now**`, `**Stop the accounts round**`, `**Not this host**`, `` `coai.terminalAccountsAllowNewHosts` `` and `jq` | leave `helpUk.ts` without *Stop the accounts round* → red naming uk |
| A5-T2 | every language's chat article says the answer covers the accounts, and what *Forget* does from another window | each of the five chat articles holds `**CoAI: hosts read over ssh**` and the accounts clause, and NOT the sentence *"The first time for each host you are asked once"* (nor its translation, by a phrase per language the test names). **Each of the five holds the K5 clause** — a *Forget* from another window does not lift a refusal while the window that gave it is open — by a phrase per language the test names (G2, C-6) | keep the old sentence → red naming the language; leave `helpEs.ts` with *Forget* and no K5 clause → red naming es |
| A5-T3 | the old "change nothing" sentence is gone | no language's chat article holds *"They only read: they change nothing on that machine"* or its translation as written today | — |
| A5-T4 | the E2 help test still holds | `helpCoverage.test.ts:84-101` green with no edit | — |

- **Done when.** A5-T1…T4 green, each break-it seen red; every file of the list above updated; `research/module_extension.md`
  says only what the code does; `node .agents/conventions/tools/plan-lifecycle.mjs` clean; the whole suite and lint green.
- **Size.** About 600 lines (≈ 250 help in five languages, ≈ 250 research and `SECURITY.md`, ≈ 60 tests, ≈ 40 § 12).

## 6. Build order, and its gates

1. **G1 — the two Fable specs, before any S1 or S2 code.** Fable writes the S1 spec (security row) and the S2 spec (the
   cornerstone, with the exact script text, its sha256 and the security judgement on it), in parallel. If either spec needs
   something outside the parent's text (an ssh option, a file with a secret the parent does not name, a write by the script,
   a third kind of call — parent § 5, last paragraph), the caller records it in the parent's ledger and gets it approved
   BEFORE that story is coded. The coder never adds one.
2. **G2 — the Opus review of both specs.** Any change to C1 or C2 goes into § 4 and into the briefs of S3 and S4 in the same
   commit, before S3 or S4 is coded. **It ran on 2026-11-19**
   ([PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_G2.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_G2.md)):
   one change of the parent (1.1, K5), and its verdict applied in the two specs and in this document. S1 and S2 are
   coded from the specs as they stand after it.
3. **S1 and S2 coded** — Sonnet, from their specs; Opus reviews; Sonnet fixes. Their files are disjoint (S1: the consent
   files, the setting, the manifest's setting; S2: new modules and `remoteFrame.ts`), so two coders may work at once; they
   land on this branch S1 first, then S2.
4. **S3** — Sonnet from § 5; Opus reviews; Sonnet fixes. Its runner part may start after G2.
5. **S4** — Sonnet from § 5; Opus reviews; Sonnet fixes.
6. **S5** — Sonnet from § 5; Opus reviews; Sonnet fixes.
7. **One pull request for the epic.** The code round of the review gate gets the epic's scope (this document's § 1, § 4 and
   § 11, and the two specs' scope sections) as `planText`. Every CI check green, and the `extension · a real editor` job
   read by its own result (§ 8).
8. **Merge.** Build the pre-release VSIX from the merged head (`npm run package`). E2 starts there (parent build order 3–4).
   **The order E2 starts with (G2, S-3): W7 before the install.** E1 ships `USAGE_POLICY` `every-round`, so from its
   first start the pre-release runs the usage command by a timer on every host of an open `Creds:` terminal. W7 — the
   trace of every program start — is what could find a hook or an MCP server of the person starting there, so it is
   run BEFORE the owner installs that VSIX. It needs no window: one usage read with the pinned command, by hand, under
   the trace, on one of the owner's hosts, with the owner's go for its exact text (parent § 5). R2 (the first start
   after the install) and R7 come after W7's result is recorded. If W7 fails, its stated consequence comes first: a
   new PR sets `USAGE_POLICY` to `{ kind: 'off' }`, and the pre-release is built from that head (parent *Build order*,
   step 4). It is an order inside E2, and NOT a default of `off` in E1: the parent has the pre-release read in every
   round (§ 2). The E2 stories document takes it as its first step.

## 7. Test plan — the parent's rows this epic owns

| Parent row (§ *Test plan*) | Story | Tests |
|---|---|---|
| The accounts script, run for real | S2 | A2-T1, T3–T8, T12, T13, T22, T23, T25, T26, T30 |
| No secret leaves, seen and not assumed | S2 | A2-T9 (seen RED once) |
| The person's activity does not leave | S2 | A2-T10 (seen RED once) |
| The version is the only thing kept | S2 | A2-T11 |
| The hash | S2 | A2-T2 |
| The parser | S2 | A2-T14–T17, T20, T21, T24, T27–T29 |
| The Claude usage lines | S2 | A2-T18 |
| The Codex record | S2 | A2-T19 |
| The words and the rows | S4 | A4-T1–T12 |
| The round | S3 | A3-T9–T22, T26; A3-H1 |
| The consent store's commit and merge | S1 | A1-T1–T9, T16, T17 |
| The automatic "yes", the cap, the stop button and the notices | S1, S3, S4 | A1-T7, T10, T13, T15; A3-T23, T25; A4-T13 |
| The setting, at both values, user layer only | S1 | A1-T10, T11; A1-H2 |
| The runner | S3 | A3-T1–T8, T28 |
| The manifest and the help | S1, S4, S5 | A1-T11, T14, T18; A4-T15–T18; A5-T1–T4 |
| Host scenario | S1, S3, S4 | A1-H1, A1-H2, A3-H1, A4-H1 (U10: measured as far as a host scenario can, § 10, K3). A3-T27 holds that no scenario can arm a round or reach `ssh` |
| Webview pages are not changed | all | — |

## 8. Where each proof runs

- **Unit and wiring tests**: `npm test`, here and in CI (`ci.yml:576-578`, `ubuntu-latest`). The real-`sh` cases RUN in CI;
  the real-`jq` cases RUN in CI by A2-T23 (how is the S2 spec's, K12) and skip here with that reason.
- **Host scenarios** A1-H1, A1-H2, A3-H1, A4-H1: `npm run test:host` CANNOT run on this machine (no `libnss3`). In that
  run the round never arms itself and an accounts call that a scenario did not replace is a stub (S3: the two
  test-mode locks), so the three older scenarios that open `Creds: h / s` start no `ssh`. They run only in
  CI's job `extension · a real editor` (`ci.yml:596`, `xvfb-run -a npm run test:host` at `:630`). That job has
  `continue-on-error: true` (`:599`), so a red scenario does NOT turn the pull request red: read the job's own conclusion and
  log on the head commit and write the result (with the sha) in the pull request description. A red host scenario blocks the
  merge as if it were required.
- **A real window and real hosts**: E2 (parent § *E2*, R1–R12 and W6–W8), on the pre-release VSIX of the merged head. Not
  in E1.

## 9. Growth and security

- **Growth** (parent § *Growth surfaces*, made concrete): the memory — one `HostMemory` for each host with an open `Creds:`
  terminal, a few hundred bytes, dropped with its last terminal, its "no", the stop and the window; the log gate — one last
  line for each such host, the same life; the state row — under 150 bytes (A3-T24); the store — capped at 100 rows, a
  person's answer never pushed out by an automatic one (A1-T7); one ssh child at a time for each window, killed at 30 s with
  its tree, stdout at most 64 KiB; one timer for each window, cleared with the last eligible terminal (A3-T21); log lines —
  two for a host that is down for a day (A3-T8). On a VM, by our script: nothing (A2-T13). `~/.claude/backups/` is W6's
  (E2), and the policy constant (A3-T17) is how its answer reaches the build.
- **Security.** Two judgements are Fable's and come before code: the store (S1) and the script (S2). What this document
  decided inside the parent's text: the accounts call reuses the pinned options and the one argv builder, its one operand is a
  closed word quoted for the remote shell as the session operands are, and the runner sends no text but `ACCOUNTS_SCRIPT`
  (A3-T1, T2, T4); the round's seams have no ask (A3-T25); the stop and the "no" are read before every call (A3-T13, T23);
  a state row that cannot be read is a stop, as a list of hosts that cannot be read contacts nobody (A3-T23; A1-T8);
  the only command a row shows is one of five fixed strings (A4-T9, T10); VM text reaches a row through one function,
  which calls S2's `plainValue` and so breaks `$(` — the runner's failure sentence too (A4-T12). `SECURITY.md` gets
  its rewrite in S5.

## 10. What E1 does not do, judgements, conflicts with the parent, and open points

Not in E1: anything installed on a VM (candidate C1); `coai.terminalAccountsInstallMissingTools` (C1); a real host, a real
window or a real vendor (E2); road (b); a webview; a change to `SSH_OPTIONS`; a change to the session reader's pick list
(the `$(` note of the parent's § 1); a lock between windows for the round; the release.

**Judgements this document made inside the parent's text** (no plan change, said so the caller can see them):

- **J1.** The accounts call's `session` log label is the fixed word `accounts`, so `logLine` keeps its four parameters.
- **J2.** The first-round notice commits `toldUtc` at the first round even when no host had a "yes" from before (else it
  would be shown for a host answered later, which is not what "from before" means).
- **J3.** The round checks before EACH call that the host still has a live terminal (the parent drops a closed host "from the
  next round"; calling it later in the same round would be a call whose result is dropped anyway).
- **J4.** The two new modules that touch a reply or a log carry the `remote` prefix so the existing log scan covers them.
- **J5.** One host module for S3 and S4 (`remoteAccountsHost.ts`), as the parent's scope says ("a NEW host module").

**Conflicts between the parent and the code, found while verifying** (K-numbers are referred to above). Three of them
could have been plan changes: K3, K5 and K6. **The caller decided at gate G2, on 2026-11-19: K5 is the parent's ledger
entry 1.1; K3 and K6 are fitted to the parent's text, and are not changes.** The last column says what each became.

| # | What the parent says | What the code says | What this document does |
|---|---|---|---|
| K1 | The icon is one `view/title` row, `view == terminal` | `doorCommands()` counts every such row as a way into a chat and demands a `noteChatDoor` (`chatWiring.test.ts:482-493`) | S4 adds a named allow-list and its honesty test (A4-T16). A test change, not a plan change |
| K2 | *"A timer pattern exists"*: `everyMs` | `everyMs` is a private `setInterval` (`consultantHealthPanel.ts:214-218`); the parent's round counts 10 minutes from the END of a round | S3 uses one re-armed `setTimeout`; `everyMs` is not reused |
| K3 | **U10 is measured by E1's host scenario** (§ *Test plan*, *Host scenario*) | No extension API reads what a QuickPick DRAWS. The type definitions say `label`, `description` and `detail` render `$(<name>)` (`@types/vscode` 1.134.0, the locked version, `QuickPickItem`; documented, not observed) | A4-H1 asserts the item text has `$(` broken, which is what a host scenario can see. **Decided at G2: U10 is NOT moved, so the parent does not change.** A4-H1 measures U10 as far as a host scenario can, and records what was CHECKED (`planning-docs.md`: a result that says "impossible" names what was checked): the label text it handed over; that no extension API reads a drawn row — checked: `QuickPick` and `QuickPickItem` of the locked type definitions, nothing else; and, only if CI's runner already has a tool that captures the xvfb screen (UNVERIFIED; nothing is installed for it), the drawn row of a label with `$(account)` beside one with `$ (account)`. If nothing more can be seen, U10 stays "open" exactly as the parent has it, with that record. R3 gains nothing. U10 decides no E1 code: `$(` is broken in every case |
| K4 | The rules are the mod's own (`resetText`), and the § 1 sample shows `resets in 2d 14h` | The mod's `resetText` has no day form (`limits.tsx:93-95`: 3 790 minutes is `63h 10m`); its cases stop under 24 h (`reset.test.tsx:48-50`) | Under 24 h the mod's form, held to its cases; from 24 h on the sample's `<d>d <h>h` (A4-T2) |
| K5 | § 3: a window writes a missing "no" again (point 3); only a person's *Read* or *Forget* changes a "no" (point 2) | A *Forget* leaves no row, which the other window cannot tell from a lost "no"; a *Read* replaces the row with a "yes" | **Decided by the S1 spec, with no tombstone** (its § S1.5; A1-T6): the window that gave the "no" writes it again and says so, so a *Forget* from another window does not hold while that window is open. **A plan change: the parent's ledger entry 1.1** (2026-11-19, `approach`) |
| K6 | — (a damaged store is not discussed) | `rowsOf` reads a list with one bad row as EMPTY (`remoteRead.ts:127`), which the automatic "yes" would read as "allow everyone", and a commit would overwrite it | **Decided by the S1 spec** (its § S1.3; A1-T8): `damaged`, nobody is contacted, and nothing writes over it but a person's own answer for a host in the change question that exists. **Fitted at G2:** no notice and no question of its own, so it is inside the parent's "never overridden" |
| K7 | At the cap the oldest AUTOMATIC row goes | Today a person's answer at the cap drops the oldest row, a "no" included, and `remoteRead.test.ts:170-184` asserts it | **Decided by the S1 spec** (its § S1.4): a "no" is never dropped; that test is replaced by A1-T7 |
| K8 | A failed read is logged once, not every round | `runRemote` logs every call that did not answer 0 (`remoteSsh.ts:192-195`) | S3's log gate through the log seam (A3-T8) |
| K9 | The setting is registered in `src/settingsShape.ts` and joins `SAFETY_KEYS` | A key in `CoaiSettings` must reach the server (`settingsReach.test.ts:104`) unless it is in `NOT_FOR_THE_SERVER` (`:102`); `coai.teamServers`, the other `application` key, is not in `CoaiSettings`; `SAFETY_KEYS` is documented as server-refusal overrides (`modelKeys.ts:28-36`) | **Decided by the S1 spec** (its § S1.7): in `SAFETY_KEYS`, not in `CoaiSettings`; the user-layer read and the scan are kept |
| K10 | The other side of the boundary is owed at the terminal-door plan's promotion at the latest | That plan was promoted on 2026-11-18 and says nothing of this plan (`research/PLAN_the_chat_opens_from_a_terminal.md:310`) | S5 writes the sentence (item 6) |
| K11 | The frame is ONE framing function shared with `remoteFrame.ts`, extracted | `framedLines` is private and throws the session parser's own faults (`remoteFrame.ts:98`, `:121-130`) | **Decided by the S2 spec** (its § A2.6): NEW `src/remoteFraming.ts`, with a result that is a value |
| K12 | The script tests use the real `jq` | This machine has no `jq`; `ci.yml` installs none | **Decided by the S2 spec** (its § A2.10): a step of the test job installs `jq` where the runner has none; a missing `jq` on CI's Linux fails (A2-T23) |
| K13 | The words name an age as `gaugeWords` does | `agoWords` is private (`gaugeWords.ts:92`) | S4 exports it; no copy |
| K14 | The ignored-layer row uses *"the words `sideConfig.ts` uses for an ignored layer"* | `sideConfig.ts`'s words are another sentence (`:98-99`), and its layer finder is private and walks the model keys only (`modelKeys.ts:119-136`) | S4 uses the parent's own sentence; S1 exports `ignoredLayerOf` |
| K15 | Every write is a commit of a change; no whole list is written | The road's test door and two host scenarios set and restore the store by whole lists | **Decided by the S1 spec** (its § S1.9): set by commits, restore by one raw write in the test door only |
| K16 | — | The hosts list's empty notice says *"The first read … asks once"* (`remoteSessionHost.ts:113`) | S1 rewrites it (A1-T14) |
| K17 | The read is capped at 64 KiB | The capped sentence divides by 1 048 576 (`remoteSsh.ts:168`) and would say `0.0625 MiB` | S3 says the cap in the unit it divides into evenly (A3-T4) |
| K18 | — | `helpCoverage.test.ts` turns red the moment a command or a setting exists without its English needle, and a new article without its four translations | S1 and S4 add the help their census needs; S5 completes it (§ 3) |

**Open, for the people named.**

1. **K3, K5 (if a tombstone), and any change the two specs need** — **closed at gate G2, 2026-11-19:** K5 is the
   parent's entry 1.1; K3 and the damaged store are fitted; the specs need no other change.
2. **Whether `ubuntu-latest` ships `jq`** — still UNVERIFIED, and it no longer matters: the S2 spec gives the test job a
   step of its own (K12).
3. **What `inspect()` hands the extension for an `application`-scoped key set in a workspace** — UNVERIFIED (the parent says
   the same); the ignored-layer row may never appear. R11 observes it in E2.
4. **`workbench.action.openSettings` with the key as its query** — UNVERIFIED in this repository (no caller today); R2 and
   R11 observe what it opens.
5. **U12** (how soon a second window sees a `globalState` write) — E2's R12, a gate.
6. **The minimum VS Code** — the manifest's engine is `^1.85.0` (`package.json`); separators, item buttons and
   `keepScrollPosition` are in the locked type definitions (1.134.0); their presence on 1.85 is UNVERIFIED, and
   `conversationPickerCommand.ts` already depends on the first two.
7. **The order E2 starts with: W7 before the pre-release VSIX is installed** (§ 6, item 8; G2, S-3) — for the E2
   stories document, and for the owner, whose go W7 needs.
8. **The session script's call line** (`src/remoteScript.ts:428`) — a defect of shipped code that gate G2 confirmed
   (its § 5). Not this plan's, and no entry of its ledger:
   [PLAN_the_session_script_call_line_cannot_be_cut.md](PLAN_the_session_script_call_line_cannot_be_cut.md).

## 11. Definition of Done — epic E1

- [ ] The S1 and S2 specs were written by Fable and reviewed by Opus (G2) BEFORE their code; § 4 and the S3 and S4 briefs
      updated with any change they made; any change outside the parent's text recorded in the parent's ledger first.
      (G2 ran on 2026-11-19: the parent's entry 1.1; § 4 and the briefs hold both X tables.)
- [ ] S1–S5 coded to their specs; every scenario green; every break-it observed red, then green; both observations in the
      summary; A2-T9 and A2-T10 seen RED once.
- [ ] A "no" always wins: A1-T3…T8 green, and no round, reload, second terminal, second window, full store or damaged store
      removes a refusal or contacts a refused host (A3-T13).
- [ ] `coai.terminalAccountsAllowNewHosts` exists, is on by default, is read from the user layer only, is never exported,
      and is in the help of five languages; off, it restores ask-first (A1-T10, T11).
- [ ] The accounts script is fixed, its hash is in the S2 spec and in a test, and it writes and installs nothing (A2-T2,
      T13). No secret is in an argument, the environment, the reply or a log; the person's activity is not in the reply; of a
      version command only the version is.
- [ ] One ssh call for each host in a round, two only in finding 3's case; the pinned options untouched; usage once for each
      account; 30 s, then 600 000 ms from the end of a round; the timer asks nobody and stops with the last terminal and
      with the stop button (A3-T1…T28). A state row that cannot be read is a stopped round.
- [ ] The list: grouped by account; "left" in the mod's words; a missing value a dash and never 0; usage that was not read
      says why; every host its own sentence; the fixed `jq` command from a closed table only (A4-T1…T19).
- [ ] Nothing of a reading is persisted (A3-T24). Road (b) is not in the code.
- [ ] The session reader behaves as before but for the words of its question and of its hosts list; its tests green,
      changed only where they held those words or the cap rule S1 replaced.
- [ ] `npm run compile` exit 0; `npm test` green on CI with the `sh` and `jq` cases RUN; `npm run lint` green; no suppression
      count grew; `sonarExclusions.test.ts` green with `remoteAccountsHost.ts` listed; `notification-sites.json` regenerated;
      the door census and the help census green.
- [ ] A1-H1, A1-H2, A3-H1 and A4-H1 green in CI's `extension · a real editor` job on the head commit, read from that
      job, named in the pull request.
- [ ] Docs: the help in five languages; `research/module_extension.md`, `research/module_tests.md`,
      `research/architecture.md` (the timer's call and the edge from a VM to a vendor); `SECURITY.md` but for W6 and W7;
      the boundary sentence in the terminal-door plan; § 12 of this document (the release notes draft and the
      `POST_DEPLOY.md` question).
- [ ] The code gate passed; the pull request merged; a pre-release VSIX of the merged head built for E2.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
