# PLAN — E1 gate G2: the Opus review of the S1 and S2 specs

> Status: **plan only, a review record — applied 2026-11-19; nothing implemented.** Gate G2 of
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1.md)
> (§ 6) over [the S1 spec](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_S1_spec.md) and
> [the S2 spec](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_S2_spec.md), judged against the parent
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md)
> (epoch 1, open, 0 changes when it was judged; one change, 1.1, since this verdict was applied). Judge: Opus, on
> `feat/terminal-accounts-e1` at `040a7fa0`. It keeps no ledger: what it counts goes into the parent's ledger, by the
> caller. No code was compiled and no test was run. One probe was run with `dash` in a scratch folder (§ 3, S-1).
> **§ 7 says where each item of this verdict was fixed.** §§ 1–6 are the verdict as it was given and are not edited.
> **The count of § 1 — one change — was overtaken on 2026-11-19 by the parent's entry 1.2** (the refusals and the Stop move to a file): it came from the cadence consultation `e319fe2e` on the E1 plan gate's finding, not from this gate, it supersedes K5 (entry 1.1), and it stopped the plan; this gate has not judged the file design.

Line numbers `:n` are the parent's unless another file is named — **the parent's at `040a7fa0`**. Since entry 1.1
the parent's lines stand 3 later from its ledger on, 6 later after the old `:398`, and 8 later after the old `:834`
(R8 is `:981`, R12 `:985`). The rule is
`.agents/conventions/common/plan-change-control.md`, § *What counts as a change*:

- **(def)** "A change is an accepted change to scope, goals, the Definition of Done, the merge conditions, epics or
  stories (added, removed, moved to another plan), the build order, a contract or the approach. A review finding
  accepted after approval that changes a planned step counts on the day it is accepted."
- **(not)** "Not a change: wording, a typo, a repaired reference, ticked boxes, a progress table, a fix that only
  satisfies the plan as written, an epic's stories document written when the epic starts. **In doubt, it is a change.**"
- **(cause)** "One cause is one change, however many lines it edits; independent causes count one each, even in one
  review round." And under *Never*: "fold independent causes into one entry."

## 1. The change count

### The verdict

**One change, if the caller takes the two fits below. Three, as the specs and the stories document stand.**

| Count | Item | Kind | Can it be fitted to the parent's text? |
|---|---|---|---|
| CHANGE | K5 — a *Forget* given in another window does not lift a refusal while the refusing window is open (S1 § S1.5, J2, X9, W3, W9) | approach | No. The other way (a tombstone) is a change too, of kind `contract`. This is the epoch's one change |
| CHANGE unless fitted | J3's two new surfaces — the mend modal `mend-remote-hosts` (W7) and the notice `remote-hosts-damaged` (W8) | approach | Yes, § 1.3 |
| CHANGE unless fitted | K3 — U10 moved from E1's host scenario to a person's look in E2 (stories document § 10) | approach (a planned step of the test plan moves to another epic) | Yes, § 1.3 |

**Merging them is not allowed.** K5 comes from "the store cannot tell a *Forget* from a lost no"; J3 from "the parent
never says what an unreadable store means"; K3 from "no API reads what a QuickPick draws". Each can be decided without
the others, and the stories document already lists them as three conflicts (K3, K5, K6). One entry such as "the
store's repair and refusal semantics" would fold K5 and J3 into one, and (cause) forbids that. A fail-closed
principle shared by K5 and J3 is a reason, not a single cause: either one can be taken and the other left. So the honest
way to stay at one change is to fit J3 and K3, not to merge.

### 1.1 The items that are changes

**K5, J2 and X9 — one cause, one entry.** J2 (the notice W9) and the W3 sentence exist only to tell the person about
K5, so they are the same cause as K5.

- Parent `:333-335`: "Only a person's own press (*Read*, *Forget*) changes a "no"." Parent `:397-398`: "***Forget* no
  longer stops the accounts read**: it removes the row, and the next round allows the host again." Test plan `:834`:
  "*Forget* removes the row and the next round allows the host again". R8 `:973`: "*Forget*: exactly 1 login in the next
  round, and the list says the host was allowed again".
- The spec (§ S1.5): "*Forget* in ANOTHER window IS undone, with a notice." And for E2: "R8's *Forget* is pressed in the
  window that gave the "no", or the record says which window".
- The other reading: parent `:336-337` ("if one of its refusals is missing, it writes it again") read literally gives
  the spec's behaviour, and `:335` says only that nothing BUT a press changes a "no". The two readings of the parent
  differ, and the stories document read it the other way (its A1-T6: "a person's *Read* or *Forget* of that host in
  another window is not undone"). That is doubt, and (not) says: in doubt, it is a change. It also adds a condition to
  how R8 is run, which is a planned step.
- **Verdict: a change.** Kind `approach`. The spec's choice is the right one of the two: it fails closed, keeps the
  row contract, and tells the person. A third way — treat "no row" as a *Forget* and let go — fails open when the "no"
  was given on a host that had no row (the session door's question on a new host), which is the silent failure the
  epic must not ship.
- **Proposed entry** (the caller writes it in the commit that accepts it):
  `- 1.1 · 2026-11-19 · reviewer · approach — a Forget from another window does not lift a refusal while the window that gave it is open: that window writes it again and says so (K5)`.
  The free-text line under it names W3, W9, A1-T6 as X9 changes it, and R8's run condition.

### 1.2 The items inside the parent's text

| # | What | The parent's line | Verdict |
|---|---|---|---|
| S1 J1 | The repair also runs once in 60 s while a window guards a refusal | `:336` "A refusal is repaired, never lost"; `:337` names two moments; `:282` clears the round's timer, so a window with no call never looks | Inside: (not) "a fix that only satisfies the plan as written". Without it a stale write erases a "no" and nobody writes it again. Its timer is a growth surface and § S1.14 names it |
| S1 J2 | The notice W9 | `:360-372`, the list of what the person is told | Part of K5's entry (same cause) |
| S1 J3 | A damaged list contacts nobody and is never written over | `:319` "Never overridden: … not by a reload"; `:237` "A host whose answer is "no" is refused and is never contacted"; DoD `:871-872` | The fail-closed core is inside: reading a damaged list as empty overrides the refusals it held. The two NEW surfaces (W7 modal, W8 notice) are in doubt, so a change unless fitted (§ 1.3) |
| S1 J4 | A person's answer is stamped later than the row it replaces | `:333-335` the merge rule | Inside: how "later" is computed, no contract or behaviour moves |
| S1 J5 | The key is not listed in `src/settingsShape.ts` | `:384` "Registered in `package.json`, `src/settingsShape.ts`, …"; E1 scope `:924` | Inside, as a repaired reference. `:379-382` is the governing rule ("never the merged value"), and a `CoaiSettings` field is read by `settingsFrom` through `readerFor`, the overlay road (S1 F13). The analogue `kimiAllowNetworkSignIn` reaches the server; this key does not. If the caller is in doubt: list it in `CoaiSettings` with a `NOT_FOR_THE_SERVER` reason and a scan that no code reads that field — that fits the text at the cost of a dead field |
| S1 J6 | At a full list a person's answer may push out the oldest person's "yes", never a "no"; 100 refusals answer `full` | `:345` "The cap protects the person's answers"; DoD `:871-872` "no … full store removes a refusal" | Inside: the parent rules only the automatic row; this applies its own rule to the person's case, and the test that held the defect is replaced |
| S1 X1–X13 | New module `remoteHosts.ts`, change kinds `repair` and `mend`, `consentOf(value, host, allowNew)`, the test door's `restore`, `dayOf`, the census | `:321-324` "`consentOf` stays the one function"; `:330` "One function writes the store" | Inside: refinements of the stories document's C1 at G2, which § 4 of that document provides for. `restore` is a test-mode-only write (A1-T18, `remoteExportGuard.test.ts`) |
| S2 R1 | `stat -c %Y`, an optional tool, for the newest file's time | `:417` the tool list; `:546-547` "A `timestamp` that does not parse falls back to the file's own time" | Inside: the parent's fallback needs a file time and the list does not give one. See S-7 on how its absence is said |
| S2 R2 | More reply words (`COAI-JQ`, `unreadable auth`, `failed error`, `failed no-sessions`, …) | `:438` "the spec fixes the exact words" | Inside |
| S2 R3 | `timeout -k 1 <n>` | `:297-298` each command has its own limit | Inside: the KILL is what makes the stated limit hold |
| S2 R4 | Codex is found by `codex` on the `PATH` or `~/.codex` | `:531-538` the Codex read never starts `codex` | Inside: the parent does not define "found" for Codex |
| S2 R5 | The three patterns need the reset clause | `:510-511` the patterns | Inside: they are the parent's patterns |
| S2 | The call line with the redirections first; `cd /` for the whole function; `:version` after the account keys; both times in `:as-of`; the wider credential net (a run anywhere, `eyJ`, `ya29.`, `-----BEGIN`) | `:406-408`, `:503`, `:441-464` (a sketch), `:546-547`, `:474-476` | Inside. The credential net is the safe reading of an ambiguous sentence, and its false positives are named (§ A2.14, residual 1) |
| S2 X1–X11 | Two more modules, `readAccounts(run, mode, nowMs)`, the new `why` words, `plainValue` doing the `$(` break, `UsagePolicy` `off`, the CI step, `VENDOR_KEY_SHAPE` | `:228-230` "Text from a VM is made plain at one road in … `$(` is broken"; `:577`, `:979` W7 "the Claude usage read is then off"; `:781-783` "`jq` is the real one" | Inside. `off` is the value the parent's own W7 consequence needs. The `ci.yml` step installs on a CI runner, never on a VM |
| Stories J1–J5, K1, K2, K4, K8–K18 | As the stories document lists them | the parent lines it quotes | Inside. K4 follows the parent's own sample (`:181`, `:184`) and keeps the mod's cases |
| Security § | Nothing in the specs moves a row of the parent's § *Security* table (`:623-632`), its edges (`:614-618`) or its "not claimed" sentence on W7 (`:643-645`) | — | Inside |
| ssh-call limits | 30 s, 64 KiB, one call a host, two in finding 3's case | `:246-260`, `:295-301` | Inside (but see S-4 on the arithmetic) |
| Files touched | `sideConfig.ts`, `extension.ts`, `remoteHosts.ts`, `notifications.ts`, `ci.yml` beyond the parent's scope list | `:919-927` | Inside: the files follow the parent's measures; none is a new part, product or kind of call |

### 1.3 The two fits that keep the count at one

1. **J3 without new surfaces.** Keep the fail-closed core exactly as § S1.3 has it. Drop the notice W8: the person learns
   of the damage from W6 (the session door), W11 (the host row of the accounts list, "Every host has its own sentence",
   `:211`) and *CoAI: hosts read over ssh*. Fold the mend into the existing change question (code
   `change-remote-host-answer`): over a damaged list, a person's *Read*, *Not this host* or *Forget* on a host commits a
   `mend` (the readable rows, refusals first) together with that answer, and the question's detail carries W7's
   sentences. No new code, no new button, no new census site; the mend stays a person's press, as `:335` wants.
   Census: S1 then adds one site (W9), not three.
2. **K3 not moved.** A4-H1 measures U10 as far as a host scenario can, and records what was CHECKED (`planning-docs.md`,
   *names what was CHECKED*): the item text it handed over, that no API reads the drawn row, and — if the runner can
   capture the xvfb screen (UNVERIFIED; for example `ffmpeg -f x11grab`) — the drawn width of a label `$(account)`
   beside `\$(account)`. If nothing more could be seen, U10 stays "open" as the parent already has it (`:159`), with
   that record, and R3 gains nothing. U10 decides no E1 code: the `$(` is broken in every case (`:228-231`).

If the caller keeps W7 and W8 as written, or moves U10 into R3, each is one more entry, and the second entry is the full
stop: `state: stopped`, no code on any epic, the whole plan re-reviewed.

## 2. Contradictions between the specs and the stories document

| # | Where | The contradiction | The fix (inside the text) |
|---|---|---|---|
| C-1 | Stories S3 `claudeAccountKey(found)` (`E1.md:418-419`); S2 X3 `email: ''` = not known; parent `:196-197` | An unknown account has `email ''`. The key joins email and org, so two unknown accounts with the same org share a key: the second host gets `no-claude-usage`, its usage is never read, and S4 does not group it with the first — it shows no usage and no reason | `claudeAccountKey` answers `undefined` for `email ''`; `modeFor` treats `undefined` as an account of its own (`claude-usage`); A3-T14 gains the row. Also say how `org: null` is joined |
| C-2 | Stories S3 `armFor` (`E1.md:441-442`, "a host whose `consentOf` is not `no`"); S1 A1-T2 ("`consentOf(` is called in `remoteHosts.ts` and in `readsKey` only") and the new signature `consentOf(value, host, allowNew)` | S3 as written would break S1's one-road scan and skip the guard | S3 decides eligibility through the keeper (`hostStandings()` or `consent(host)`), never `consentOf` itself |
| C-3 | Stories C5 and S3 (`E1.md:217`, `:450-453`): `accountsStateOf` reads a damaged value as `{}`, and `withStateChange(state, change)` takes a parsed state; S1 X6 and § S1.4: `Merge<C, R> = (stored: unknown, change) => { next; result }`, never written over damage | The signature does not fit the primitive, and the principle differs: a damaged state row opens the stop (S-2) | `withStateChange` becomes a `Merge` over `unknown`; a damaged state row follows S-2 |
| C-4 | Stories S3 step (3) (`E1.md:434-435`: `full`, `not-asked`, `damaged` "are noted for the list"); `HostMemory` (`:414-416`) has no field for it; S1 X8 "S4 learns "damaged" from the admission" | Nowhere holds the admission result, so S4 cannot draw W11, *full* or *not asked yet* | `HostMemory` gains `admission?: Exclude<Admission['kind'], 'read'>`, set by the round and cleared by a `read` |
| C-5 | Stories S4 item 3 (`E1.md:539-541`: `forList` calls `plainOf`, "exported for it") and § 3 *Reuse first*; S2 X7 (`forList` calls `plainValue`; `plainOf` is NOT exported) | Two different roads named for one rule | X7 wins; S4's text and A4-T12's scan name `plainValue`. N1 (`plainOf` and `withoutControls`, two private copies) has no owner: S1 edits `remoteRead.ts` and says nothing of it. The caller names S1 or S4 for `remoteRead.ts` and S3 for `remoteSsh.ts`, or leaves N1 a todo |
| C-6 | Stories S5 help (`E1.md:663-664`: "only *Not this host* stops a host, and *Forget* lets it be allowed again"); parent help line `:370-371`; S1 W3 | After K5 the sentence is true only in the window that gave the "no" | S5's help and `SECURITY.md` carry the K5 qualifier, in five languages; A5-T2 gains it |
| C-7 | Stories C2 `readAccounts(run, mode)`, S3 `parse` seam and A3-T26 ("a fake `parse` records `(run, mode)`"); S2 X2 `(run, mode, nowMs)` | Covered by X2, but A3-T26's wording and the `RoundSeams.parse` type must change with it | In the G2 commit |
| C-8 | Stories § 7 and § 11; S1 adds A1-T17, A1-T18, A1-H2; S2 adds A2-T24…T30 | Neither X table updates § 7's map (for example "Host scenario: A1-H1, A3-H1, A4-H1" lacks A1-H2) or the stories S1 scope (`sideConfig.ts`, `extension.ts`) | In the G2 commit |
| C-9 | S2 § A2.3, the time sentence ("5 + 4 + 10 + 6 = 25 s") against its own table and its Codex read (`timeout -k 1 10 jq`) | The sum omits the Codex reader's 10 s: 35 s, and 40 s with ssh's 5 s to connect | S-4 |
| C-10 | S1 "One open point for S3" and stories A3-H1 / C7 | The three existing scenarios that open `Creds: h / s` are not in S3's brief | § 4 below goes into S3's brief |
| C-11 | S2 N1's proposal puts one exported `withoutControls` in `remoteFraming.ts`; X7 puts the rule in `plainValue` (`remoteAccountsReply.ts`) | Two homes proposed for one rule | One home: the first step of `plainValue`, exported from `remoteAccountsReply.ts` |

The shared names were checked and agree after the X tables: `readAccounts`, `UsagePolicy` (with `off`), `commitKey`
(keeper and bound sessions), `remoteHosts.ts`, `remoteFraming.ts`, `plainValue`, `Admission`, `HostStanding`. The
scenario ids keep their meanings; S1 maps A1-T1…T16 and A1-H1, S2 maps A2-T1…T23.

## 3. Security findings

| # | Severity | Finding | What to do |
|---|---|---|---|
| S-1 | **Medium** | **A cut inside the call line's `2>/dev/null` creates a file on the VM, as root.** Probed with `dash -s` (stdin ending with no line end): `printf 'f() { echo ran; }\n</dev/null 2>./nu' \| dash -s` created `./nu`. So a text cut after `</dev/null 2>/d`, `…2>/dev/n`, `…/dev/nu` or `…/dev/nul` opens that path for writing: as root (the measured host's user is `root`) it creates `/d`, `/dev/nu` and the like. The parent says the script writes nothing (`:411`) and that a cut script does nothing (`:406-408`); the spec says "no cut can" (§ A2.1, 4). M-A8 and A2-T1 cannot see it: they ran as a user who cannot write `/` or `/dev`, and listed `HOME` only | Make the last line ONE brace group: `{ </dev/null 2>/dev/null coai_accounts "$@"; printf 'COAI-ACCOUNTS-END\n'; }`. The shell parses a compound command whole before it runs any of it: probed, a cut before the `}` is a syntax error, nothing runs and nothing is created. A2-T1's cut half runs the text with the absolute paths of that line mapped to a temporary folder (as A2-T12 (b) maps the manager paths) and asserts no new entry there; A2-T25 holds the new last line. The block, its line and byte counts and the provenance hash move in one spec commit before code |
| S-2 | **Medium** | **A damaged state row opens the stop.** C5 reads a damaged `coai.terminalAccountsState` as `{}`: no `stoppedUtc`, so the round runs, and the next field commit writes over it. The stop is "the way out when a read does harm on a VM" (`:390-392`) | Fail closed, as J-d does for the hosts list: a damaged state row reads as stopped; the list shows its one row *The accounts round is stopped. Choose this row to start it.* (`:226-227`); only a person's *start* or *stop* writes over it. A3-T23 gains the row |
| S-3 | **Medium** | **The W7 gate comes after the timer has already run the usage command on the owner's hosts.** The pre-release reads usage in every round (`:271-272`; S2 § A2.8). If a hook or MCP server of the person starts, it starts every 10 minutes on several hosts before the trace that would find it | In E2, run W7 (one trace, the owner's go) before the pre-release is installed, or at least before R2 and R7. An order inside E2, not a change. `off` (X8) must also make `needsSecondCall` false; A3-T17 holds it |
| S-4 | Low | **The time bounds do not add up under 30 s.** The readers' limits are 5 + 4 + 10 + 10 (Codex `jq`) + 6 = 35 s, plus 5 s to connect. `ls`, `stat` and the Codex `tail <file` have no limit (a hung network home). The runner's 30 s kill still bounds the call, but the whole reply is then lost — every vendor of the host — where the parent wanted each command's own limit (`:297-300`) | Lower the readers' limits so that their sum and ssh's 5 s stay under 30 s; put `ls`, `stat` and the Codex `tail` under `timeout`; A2-T26 asserts that sum, not only the four command limits |
| S-5 | Low | **The runner's failure sentence is VM-influenced text that skips the one road.** The host row draws *the last read failed <age>: <the runner's sentence>*, and that sentence can end with ssh's stderr tail. A remote login shell's rc file can print there, or exit 255. `withoutControls` takes out control characters but keeps `$(` | S4 passes the runner's sentence through `forList` (so `plainValue`); A4-T12's scan covers the failure sentence as well as the reply's fields |
| S-6 | Low | **No test holds that the missing-`jq` path never runs a package manager.** The script is right today: `coai_manager` only tests `-x`. But A2-T25's scan would not see an added `apt-get install` line | A2-T25: `apt-get`, `dnf`, `yum` and `apk` appear only in `[ -x … ]` tests and in `printf` words; the text has no `sudo` and no `install` |
| S-7 | Low | **A missing `stat` reads as a time not known.** The parent: "a missing tool is said, never read as "nothing found"" (`:412`). Without `stat`, `:as-of` ends with `-`, the same as a `stat` that failed | Either say it (the last word `no-stat`, one more closed word) or name it as a residual in § A2.14 |
| S-8 | Info | **The Codex "newest record" compares `timestamp` as text.** `…36Z` sorts after `…36.474Z` (`Z` is above `.`). Measured files all carry milliseconds | Name it in § A2.14, or have the `jq` program compare the times after it brings them to one form |
| S-9 | Info | Checked and sound: a damaged hosts list fails closed at every door (§ S1.3); a refusal is never dropped by the cap; the round's door has no ask seam; `restore` and `letGo` exist only in test mode; the parser is pure, closed, bounded, never run, `$(` broken, credential-shaped values dropped with their vendor; the account program prints three named fields and not `orgId`; the usage text leaves the VM only through anchored patterns, and only when `num_turns` is 0; `codex` is never started; no credential file is named in the text; W7 is not claimed ("we pass") | — |

## 4. The open point from S1: the scenarios that open `Creds: h / s`

`terminalHostsScenario.ts:56` and `terminalDoorsScenario.ts:60`, `:94` open a terminal of host `h`. After S3, the round
arms itself 30 s later in CI's real-editor job: it would admit `h` (an automatic row written under those scenarios'
own store assertions) and start a REAL `ssh` to `h`. It would also call A3-H1's fake more often than A3-H1 counts.

**Recommended, inside the parent's text:** in `ExtensionMode.Test` the round is never armed by itself, and an accounts
call that a scenario has not replaced never reaches `ssh`.

- `bindTerminalAccounts` takes `armSelf = context.extensionMode !== vscode.ExtensionMode.Test`. With it false the host
  still computes `armFor` and reports it through `armed()`, but starts no `setTimeout`; scenarios drive rounds with
  `runRoundNow` (C7).
- In test mode the default `call` is a stub that answers `refused` with a fixed sentence and spawns nothing, until
  `replaceCall` gives one. Two locks, so a scenario that forgets one still starts no `ssh`.
- A3-T27 (extended): both switches are the test-mode check and nothing else, in the test-mode spread; a production
  build cannot carry either.
- The parent's own host-scenario row already says "the call replaced through the test door" (`:853-854`), and the
  self-arming is held by the pure A3-T20. The product's behaviour outside test mode does not change.

This goes into S3's brief in the G2 commit.

## 5. N2 from S2: the session script's call line

**Yes, a real defect of the shipped E2 code, and a separate bug: a todo outside this plan.**

`src_vs_code/src/remoteScript.ts:428` is `coai_main "$@" </dev/null 2>/dev/null`. Probed with `dash -s`: a stdin that
ends right after `f "$@"` (no line end) RUNS the function, with the script's pipe as its stdin and the caller's stderr
as its stderr. A cut inside `2>/dev/null` runs it with its stderr in a new file (`/dev/nu` and the like), which as root is
a write on the VM. The script's own doc says "a script cut on the way does nothing" (`remoteScript.ts:130-131`); at
that line it does not. The harm is small: the tools are `tmux` and `ps`, the stdin is at its end, and a cut needs the
stream to stop in a window of about 25 bytes.

- Not fixed in E1: the text's hash is in its promoted spec, and the session reader "behaves as before" (`:879`).
- The fix is the brace-group form of S-1, with the promoted spec's text, line count and hash moved together, and a
  cut test that maps the absolute paths as S-1 says.
- **The caller opens a todo for it** (for example in the bug list or a short `todo/` plan naming
  `research/PLAN_the_chat_opens_from_a_terminal.md` as the plan whose shipped script it is). It is not an entry of this
  plan's ledger.

## 6. Recommendations, in order

1. Record K5 as entry 1.1 in the parent's ledger (§ 1.1), in the commit that accepts it, before S1 is coded. Its shape
   of the work does not change (no epic or story moves), so trigger 5 of `coai-consultant.md` does not fire; the
   caller may still ask a consultant one question on it before it records the entry.
2. Take the two fits of § 1.3 (J3 without W7 and W8 as new surfaces; U10 not moved). Otherwise the second entry stops
   the plan.
3. In ONE G2 commit: S1's X1–X13 and S2's X1–X11 into the stories document's § 4 and the S3 and S4 briefs, together
   with C-1…C-11 of § 2, S-2 (the state row), S-5 (the failure sentence), § 4 (test mode) and § 7's map.
4. Before S2 code: S-1 (the brace-group last line, its cut test, the block's counts and provenance hash), S-4 (the
   readers' limits), S-6, and S-7 or its residual — all in the S2 spec, by the caller, before the coder starts.
5. E2's order: W7 before R2 and R7 (S-3).
6. The N2 todo (§ 5), outside this plan.

## 7. Applied 2026-11-19 — where each item was fixed

The caller's decisions: exactly ONE change of the parent (K5); J3 and K3 fitted; the eleven contradictions and the
security findings fixed in the documents they live in; N1 folded inside E1's files; N2 a todo outside this plan.
"Stories" is the E1 stories document, "S1" and "S2" the two specs. No code was written.

| Verdict item | What was decided | Where it was fixed |
|---|---|---|
| § 1.1 — K5, with J2, W3, W9, X9 and R8's run condition | ONE change, kind `approach` | The parent: ledger entry 1.1; its status block; § 3 *How a person forbids a host*; the test plan; R8. S1: *Changes to the parent*, § S1.5. Stories: the status block, § 4 C1, A1-T6, § 10 K5 |
| § 1.3, fit 1 — J3, the damaged list | Fitted: no notice `remote-hosts-damaged`, no modal `mend-remote-hosts` | S1: § S1.3 (mended by a person's *Read*, *Not this host* or *Forget* in the question `change-remote-host-answer`), § S1.4 (`mend` carries that answer, one write), § S1.5 (no `damaged` seam), § S1.8 (W7 is that question's detail, W8 dropped, W4c, W5, W6, W11), A1-T8, A1-T14, A1-T18, B28, B29, X13 (the census: one new site, 186). Stories: § 4 C1, the S1 scenarios, § 10 K6 |
| § 1.3, fit 2 — K3, U10 | Fitted: U10 is not moved; R3 gains nothing | Stories: A4-H1 (it records what was CHECKED), § 10 K3, S5 item 3 |
| C-1 — an unknown account's key | A key of the host's own | Stories: § 4 C4, S3 `claudeAccountKey(found, host)` (and how `org: null` is joined), A3-T14, S4 the groups, A4-T6 |
| C-2 — `armFor` and `consentOf` | Eligibility through `sessions.hostStandings()` | Stories: § 4 C1, S3 the timer, § 3. S1: X14 |
| C-3 — the state row and `Merge` | `stateMerge` is a `Merge` over `unknown`, through `commitKey` | Stories: § 4 C5, S3 the state row, A3-T23. S1: X15 |
| C-4 — where the admission is kept | `HostMemory.admission` | Stories: § 4 C4, S3 `HostMemory` and `runRound` step 3, A3-T13, S4 the host sentences, A4-T8. S1: X8, § S1.6 |
| C-5 and N1 — `forList`, and one rule in two copies | `forList` calls `plainValue`; ONE exported `withoutControls`; S3 folds the two older copies (both files are in E1's scope) | S2: X7, X12, § A2.7, A2-T29, B46, § A2.14 N1. Stories: § 3, § 4 C2 and C6, S3 file 1, A3-T28, S4 `forList`, A4-T12 |
| C-6 — the help after K5 | The K5 clause in five languages | Stories: S5 items 1 and 5, A5-T2 |
| C-7 — `readAccounts(run, mode, nowMs)` | The seam keeps `(run, mode)`; the host binds the clock | Stories: § 4 C2, S2's interfaces, S3 `runRound` step 5 and the seams, A3-T26 |
| C-8 — the map and the scopes | Updated | Stories: § 7, § 8, § 11, the scope of S1 and of S2, the rows A1-T17, A1-T18, A1-H2 and A2-T24…T30 |
| C-9 — the time sentence | Replaced by a table with the sum | S2: § A2.3, *The time of one call* |
| C-10 and § 4 — the scenarios that open `Creds: h / s` | In test mode the round never arms itself, and the default call is a stub until `replaceCall` gives one | Stories: S3 *Test mode: two locks* (`timerFor`, `callFor`), A3-T27, A3-H1, § 3, § 7, § 8. S1: X16 |
| C-11 — two homes for one rule | One: `remoteAccountsReply.ts` | S2: X12, § A2.7, § A2.14 N1 |
| S-1 — a cut inside `2>/dev/null` | The last line is ONE brace group; a cut test over a temporary folder | S2: § A2.1 decision 4, § A2.3 (the block; the pin stays PENDING and the text says the block changed), rule 1, A2-T1, A2-T25, B1, § A2.9 J9, M-G1 and M-G2. Stories: A2-T1 |
| S-2 — a damaged state row | It reads as STOPPED | Stories: § 4 C5, S3 `stoppedOf`, A3-T23, § 9, § 11 |
| S-3 — W7 and the timer | W7 is run before the pre-release VSIX is installed, so R2 and R7 come after it; `off` is not a default in E1 (the parent has the pre-release read in every round); with `off`, no extra call | Stories: § 6 item 8, § 10 open point 7, S3 `UsagePolicy`, A3-T17. S2: § A2.8, X14 |
| S-4 — the time bounds | Readers 4, 2, 9, 4 s; `ls`, `stat`, each Codex `tail` 1 s; the Codex `jq` 3 s: 24 s, 29 s with ssh's connect | S2: § A2.1 decision 6, § A2.3 (the block, the time table, the bounds), § A2.10, A2-T8 (v), A2-T26, B43, B45, § A2.14 residuals 9–11. Stories: § 4 C3, A3-T4 |
| S-5 — the runner's failure sentence | Through `forList`, so `plainValue` | S2: X13. Stories: § 4 C6, S4 `forList` and the host sentences, A4-T12 |
| S-6 — no package manager is run | A scan and a run | S2: A2-T12 (c), A2-T25, B42 |
| S-7 — a missing `stat` | Said: the closed word `no-stat` | S2: R1, § A2.3, § A2.4, § A2.5, § A2.7 (`statMissing`), A2-T15, A2-T19, B44, X15. Stories: § 4 C2, S4 |
| S-8 — `timestamp` compared as text | Named | S2: § A2.14 residual 8 |
| § 5 — N2, the session script's call line | A todo outside this plan | [PLAN_the_session_script_call_line_cannot_be_cut.md](PLAN_the_session_script_call_line_cannot_be_cut.md); S2 § A2.14 N2; Stories § 10 open point 8 |

**What did not fit cleanly, said so the caller can judge it.**

1. **The amended script was parsed, not run.** `dash -n` and `bash --posix -n` accept the block of the S2 spec's
   § A2.3; M-A1…M-A14 were run on the text before this gate. The brace group was probed with a toy function only
   (M-G2). The pin stays PENDING.
2. **S-4 holds for the limits, not for every KILL.** The sum of the readers' limits and ssh's connect is 29 s. If
   both version programs also ignore their TERM while every other step runs to its limit, the call takes 31 s, and
   the runner's kill is the bound. After a KILL, the exit record of the account or usage command can come too late for
   its reader, and the word is then `unreadable auth` or `failed error` and not `timeout`. Lower limits for the vendor
   commands would close both; those four numbers are in the parent's text, so they were not touched.
3. **S-3 is written in the E1 documents only.** The parent's build order (steps 3 and 4) and its E2 section are not
   edited: the one parent change of this epoch is K5. "W7 before the install" stands on the verdict's own ruling that
   it is an order inside E2. If the caller reads it as touching the parent's step 3, that is a second entry.
4. **J3 has a cost.** With no notice of its own, a damaged list is said only where a person looks: a host's row, the
   session door, the hosts list. Until then every host is not read and nothing has told them why (S1 § S1.10,
   residual 8).

## Definition of Done — gate G2

- [x] The caller read § 1 and recorded 1.1 (and any item it judges otherwise) in the parent's ledger before S1 code.
- [x] § 1.3's fits taken, or each refused one recorded as a further entry with its stop.
- [ ] The G2 commit of § 6, item 3, made before S3 or S4 is coded. (The edits of § 7 are in the working tree; the
      caller commits them.)
- [x] The S2 spec amended for S-1, S-4, S-6 and S-7 before S2 code.
- [x] The N2 todo opened outside this plan.
- [x] `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
