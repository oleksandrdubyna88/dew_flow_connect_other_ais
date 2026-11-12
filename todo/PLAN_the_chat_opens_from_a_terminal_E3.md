# PLAN — E3 stories: the chat finds its terminal again, go-to from a terminal, the skip list

> Status: **written by Opus 2026-11-12 when the epic started; plan gate owed.** Epic E3 of
> [PLAN_the_chat_opens_from_a_terminal.md](PLAN_the_chat_opens_from_a_terminal.md) (its `## Epics` § *E3*, its § *Stage 3*,
> the stage-3 rows of its § *Test plan*, its real-window checks R8, R10 and R11, its DoD items 5–7 and its ledger entry 1.1).
> Written on `feat/chat-from-a-terminal-e3` from `main` `4efa0a90` (extension 0.87.0). A stories document keeps no change
> ledger: a change to this epic is an entry in the parent. The parent is in epoch 2 with one change (2.1), so **the next
> accepted change stops the plan for a whole re-review**. § 10 lists every point here that may be such a change; none of
> them is taken in this document.
>
> Scope: `src_vs_code` only, plus `research/` and the help. Three stories. **No cornerstone**: the parent recommends none for
> E3, and the quota is full (`Cornerstones: 2 of 2 — E1.S2, E2.S2`), so the ledger line does not change (§ 10, O11).

## 1. Goal, before any solution

E1 (extension 0.86.0) and E2 (extension 0.87.0) are released. From a terminal, the person opens a chat with the text they
copied; in a `Creds: <host> / <session>` terminal, *take the question*, *add the question* and *Asked* read the Claude session
on the VM. Three gaps are left, and the parent's § *Stage 3* names them:

1. **After a reload, a terminal's conversation is lost to its terminal.** The serializer brings the chat tab back under a
   key of its own (a plain `{}`), and creds brings the terminal back as a NEW `Terminal` object. Nothing joins the two. The
   next press of *Ask other AI* in that terminal starts a SECOND conversation beside the restored one. Each reload adds one
   more conversation with the same source (E1 § 11, carried note).
2. **Go-to does not start from a terminal.** Ctrl+Alt+G in a terminal goes to the shell, and `coai.goToConversation` reads
   the active editor TAB, which is not the terminal the person looks at.
3. **A person's own skip list hides the chord.** E1 put `coai.askFromTerminal` into the DEFAULT of
   `terminal.integrated.commandsToSkipShell`. A person who has their own value of that setting hides the default, and
   Ctrl+Alt+A then goes to the shell. Today only the help says so.

When E3 is done (parent DoD item 5):

1. After a reload or a restart, a `Creds: <host> / <session>` conversation re-attaches to the terminal of the same name —
   only when exactly ONE live terminal and exactly ONE orphaned conversation carry that name, and only for a name that
   passes E2's strict `credsSession` pattern. It is checked again just before the re-key. It runs when a terminal opens,
   after the serializer restores a conversation, and at the start of the terminal door.
2. Ctrl+Alt+G in a terminal, and an item in the terminal panel's overflow menu, open go-to for THAT terminal.
3. When the person's own user-level `commandsToSkipShell` hides the default, ONE notification offers **Add**. Pressing it is
   the consent: the missing ids are appended to the person's user setting, and nothing else in it changes. The answer is
   remembered.

The silent failures this epic must not ship:

- **A conversation bound to the wrong terminal** — two terminals or two conversations of one name and one is picked; a
  `bash` conversation from last week bound to today's `bash`; a terminal that already has a conversation given another; a
  conversation moved from one live terminal onto its namesake. Nothing would look wrong: the answer simply arrives in the
  other chat.
- **A Claude tab's or a file's conversation taken by a terminal** — the reverse of E1's steal.
- **A change to the person's settings they did not agree to** — a write before the press, a write to the workspace file,
  a reordered or de-duplicated list, an entry of theirs removed, a list created where they had none.
- **A notification that nags** — shown again after the person answered, or twice at once.

## 2. What exists — verified at `4efa0a90`, 2026-11-12

All paths are under `src_vs_code/` unless they start with `research/`. Every name below was opened at this commit.

| Fact | Where |
|---|---|
| The panel registry: `known`, `open`, `rekey` (refuses when the destination key is taken, changes nothing then) | `src/chatPanels.ts:118`, `:128`, `:168-178` |
| A restored conversation is keyed by a fresh `{}` and labelled with its title; its source is CARRIED from the record | `src/chatConversationRestore.ts:119` `restoreConversation`, `:176`, `:256` |
| The serializer: `registerWebviewPanelSerializer('coaiChat')` → `restoreAfterReload` → `restoreChatTab` → `restoreConversation`; it first waits for the migration up to 5 s | `src/extension.ts:858-866`; `src/chatRestorePanel.ts:59`, `:103`, `:114`; `src/chatRestore.ts:56` `MIGRATION_WAIT_MS` |
| Every road that re-keys a panel today: the tab re-key of `sessionKey` (twice) and `revealBound` (go-to's reopen and the picker's accept) | `src/chatCommand.ts:250`, `:604`; `src/chatRegistry.ts:104-112` (`rekey` at `:106`); callers `src/chatGotoCommand.ts:208`, `src/conversationPickerCommand.ts:305` |
| The steal filter: a TAB is matched only against panels whose thread source is not `terminal` | `src/terminalSource.ts:100` `tabSourced`, `:108` `tabPanels`; `src/chatCapture.ts:192` `tabMatch` |
| The terminal rules E3 reuses: the title, the match by the `Terminal` object (never `rekey`), the live terminal, the one live terminal of a name | `src/terminalSource.ts:60` `terminalLabel`, `:65` `terminalMatch`, `:74` `LiveTerminal`, `:87` `oneLiveNamed` |
| The strict name rule: `session` / `other` / `malformed` | `src/credsSession.ts:65` `credsSession` |
| The source kind and its rules | `src/chatStore.ts:82` (`terminal`), `:102` `TERMINAL_NAME_MAX`, `:108` `isTerminalName`, `:111` `sourceOfTerminal`, `:543` `sameSource` |
| The thread of a panel | `src/chatThread.ts:340` `threadUnder` |
| The window's terminals read as `LiveTerminal` — twice, each its own copy | `src/chatReturnCommand.ts:156` `asLive`; `src/remoteSessionHost.ts:71` (a live name), `:281` |
| The terminal door: the name guard, the clipboard read, then the match | `src/chatCommand.ts:303` `terminalDoorName`, `:342` `askFromTerminal`, `:365` `terminalMatch(` |
| E2's terminal listeners (the context key, the picked id) | `src/remoteSessionHost.ts:212-224` `bindRemoteSessions` (`onDidChangeActiveTerminal` `:218`, `onDidCloseTerminal` `:219`); called at `src/extension.ts:147` |
| Go-to's decision: `TabKind` (`claude`, `document`, `other`, whose comment says go-to does not start from a terminal "before stage 3"), `tabKindOf`, `goto`, `eligible` (exhaustive), the "several" arm called *impossible by construction*, `bindable` | `src/chatGoto.ts:39-49`, `:205`, `:232`, `:238-242`, `:313-317`, `:335`, `:435` |
| Go-to's host: one press at a time, `arrive`, `bind`, `startingFor` (runs `chatWithOtherAi`), `bindingTo` (asks `tabStillOpen`) | `src/chatGotoCommand.ts:67`, `:70`, `:88`, `:175`, `:229`, `:261-279` |
| Go-to's question, built from the TABS | `src/chatSessionJoin.ts:257` `askedForGoto` |
| The picker titles of a narrowed go-to (exhaustive) | `src/conversationChoice.ts:87` `narrowedTitle` |
| The manifest: the go-to command and the road back; the terminal title bar; the palette; the chords; the skip-list default | `package.json:315`, `:320`; `:1196-1210` (`view/title`: the button `navigation@90`, the two E2 items `coai@1`, `coai@2`); `:1268` (`commandPalette`); `:1347-1376` (`keybindings`: go-to `ctrl+alt+g` with `!terminalFocus` at `:1364`, **the road back `ctrl+alt+g` when `activeWebviewPanelId == 'coaiChat'` with NO `!terminalFocus` at `:1370`**); `:1387-1389` (`configurationDefaults`) |
| The manifest tests E3 extends: the derived skip list, no older chord under `terminalFocus` (Ctrl+Alt+A and the go-to rows only) | `src/test/terminalDoorWiring.test.ts:42-46`, `:72-85`, `:87-101` |
| The chat-scoped command list: `coai.backToSource`'s `when` must be EXACTLY `activeWebviewPanelId == 'coaiChat'` or `webviewId == 'coaiChat'` | `src/test/chatWiring.test.ts:475-477`, `:493-518` |
| The door census (reads `view/title` rows of `view == terminal` since E2) | `src/test/chatWiring.test.ts:480-491` `doorCommands` |
| Go-to tests that pin the tab path for a terminal in the editor area (kind `other` → the whole list) | `src/test/chatGoto.test.ts:215-224`, `:534-556`; `src/test/chatGotoWiring.test.ts:75-88` |
| The doc that says the two Ctrl+Alt+G `when`s never overlap | `research/module_extension.md:10319` |
| A layered setting, narrowed (`defaultValue`, `globalValue`, `workspaceValue`, `workspaceFolderValue`) | `src/modelKeys.ts:43` `Inspected` |
| Writes to the person's user settings today (all `ConfigurationTarget.Global`) | `src/sideConfig.ts:186`, `src/uiScaleHost.ts:32`, `src/configTransferCommands.ts:202` |
| A notification with a button whose answer is recorded; a prompt that waits for the person | `src/notify.ts:351` `notifyAndAsk`; `src/personWait.ts:35` `askPerson` |
| A one-at-a-time latch | `src/oneAtATime.ts:32` `oneAtATime` |
| The skip-list default in a real editor (E1's S3-H1) | `src/test/host/terminalCaptureScenario.ts:63` `theSkipListDefaultHoldsTheTerminalDoor` |
| The host module list `sonarExclusions.test.ts` holds exact | `.github/workflows/sonarcloud.yml:268` |
| The help, five languages: the terminal paragraph names `coai.askFromTerminal` and says that a person's own list hides the default | `src/helpContent.ts:294`, `src/helpDe.ts:53`, `src/helpEs.ts:53`, `src/helpRu.ts:53`, `src/helpUk.ts:117` |
| The docs of E1 and E2 | `research/module_extension.md:13364`, `:13524`; `research/module_tests.md:306`, `:307` |
| The invisible-character scan over every source file | `src_bugs/tests/NoSourceFileCarriesAnInvisibleCharacterTests.cs` |

## 3. Conventions every story follows

- **Worktree.** `/home/jinx/git/wt/coai-chat-terminal-e3`, branch `feat/chat-from-a-terminal-e3`. Never the main checkout.
- **Models.** Spec (scenarios and brief): Opus, this document, for S1, S2 and S3. Code and tests: Sonnet. Review: Opus.
  Fixes: Sonnet. No Fable: no cornerstone, and no story judges cryptography, authentication or authorisation (§ 9).
- **RED-GREEN-RED.** Each scenario names its assertion and its break-it: the production line to break, and the failure
  message the test must then show. Every NEW test is seen RED (before its code, or a compile error for a new module), then
  GREEN, then RED again with its guarded line broken, then GREEN. Report the three observations in the summary.
- **Every test file** starts with `import '../../scripts/inRunFolder.cjs';`.
- **Pure first, with seams.** Unit tests cannot load a module that imports `vscode`. So every decision AND every
  orchestration a test must see lives in a pure module and takes its effects as parameters: `terminalReattach.ts` (S1),
  `chatGoto.ts` (S2), `skipShell.ts` (S3). Two NEW host modules are allowed, each with its row in
  `sonar.coverage.exclusions` in `sonarcloud.yml` in the same commit (`sonarExclusions.test.ts` holds the list exact):
  `src/terminalHost.ts` (S1) and `src/skipShellHost.ts` (S3). They only supply the `vscode` seams.
- **Reuse first.** Called, never re-typed: `credsSession`, `isTerminalName`, `sourceOfTerminal`, `sameSource`, `terminalLabel`,
  `oneLiveNamed`, `threadUnder`, `ChatPanels.rekey`, `goto`, `bindable`, `revealBound`, `filedUnder`, `notifyAndAsk`,
  `askPerson`, `oneAtATime`, `Inspected`. The window's terminals as `LiveTerminal` are read in ONE host function (S1 extracts
  it; the two copies at `chatReturnCommand.ts:156` and `remoteSessionHost.ts:281` call it).
- **Lint.** A new function keeps `complexity` ≤ 4 and ≤ 50 lines; no suppression count grows (CI step *Lint suppressions
  only shrink*). Split, do not suppress.
- **A new `notify` call site** changes the census: `node scripts/count-notifications.mjs --write`, commit
  `notification-sites.json`.
- **No literal bidi or invisible character in any source file.** `src_bugs/tests/NoSourceFileCarriesAnInvisibleCharacterTests.cs`
  scans for Trojan Source. A test that needs such a character builds it with `String.fromCodePoint(…)`.
- **No webview page changes in E3.** If one becomes necessary, it is tested by RUNNING the page (`bundledPage.test.ts`); a
  stylesheet question goes through the shared `src/test/cssRules.ts`. **A NEW behavioural assertion over page source text is
  refused** (`.coderabbit.yaml:121`, `.agents/PROJECT.md`). Source scans over TypeScript HOST code, where no seam reaches,
  stay legitimate (E1 and E2 used them); each has a companion that proves it still finds what it scans for.
- **A test that changes the editor's state puts it back.** S3-H1 writes a REAL user setting: it saves the old value first and
  restores it in a `finally`, also when the assertion fails.
- **Host scenarios** run only in CI's job `extension · a real editor` (§ 8). No real VS Code starts on this machine.

Commands (from `src_vs_code/`):

```bash
npm ci                                                                                 # once
npm run compile                                                                        # read the exit code: tsc emits even when red
node ../.agents/conventions/tools/low-priority.mjs node scripts/run-tests.mjs out/test/<name>.test.js   # one file, after compile
node ../.agents/conventions/tools/low-priority.mjs npm test                            # the whole suite (compile + run-tests)
npm run lint                                                                           # eslint src
node scripts/count-notifications.mjs --write                                           # after any new notify call
npm run test:host                                                                      # CANNOT start on this machine — see § 8
```

From the worktree root: `node .agents/conventions/tools/plan-lifecycle.mjs` before every commit that touches `todo/`.

## 4. Shared contracts

- **C1 — re-attach (S1 owns).** NEW pure `src/terminalReattach.ts`:
  - `Orphan = { readonly key: object; readonly name: string }` — an open panel whose thread source is `terminal` (its
    `name` is the source's full name) and whose key is NOT a terminal of the window (not in `window.terminals`).
  - `HeldTerminal = LiveTerminal & { readonly held: boolean }` — `held` is true when the registry already holds a
    conversation under that terminal's key.
  - `reattachPlan(orphans: readonly Orphan[], liveNames: readonly HeldTerminal[], name: string): Reattach` — the parent's
    pure rule, with
    `Reattach = { kind: 'bind'; from: object; to: object } | { kind: 'leave'; why: Leave }` and
    `Leave = 'not creds' | 'no terminal' | 'several terminals' | 'held' | 'no orphan' | 'several orphans'`, decided in that
    order: `credsSession(name).kind !== 'session'` → `not creds`; the live (not exited) terminals of exactly `name`: none →
    `no terminal`, more than one → `several terminals`; the one is `held` → `held`; the orphans of exactly `name`: none →
    `no orphan`, more than one → `several orphans`; else `bind` from that orphan's key to that terminal's key. Names are
    compared exactly, as `oneLiveNamed` and `sameSource` do. It never picks.
  - `orphansOf(known, sourceOf, windowKeys): readonly Orphan[]` — pure, from `ChatPanels.known()`, a thread-source lookup and
    the keys of `window.terminals`. Judged by the DURABLE source (as `tabSourced` is), never by the key's type or the label.
  - `reattachOnce(read: () => ReattachView, rekey: (from: object, to: object) => boolean, name: string): Reattach` — the one
    road: read, plan; on `bind`, **read AGAIN and plan again** (the parent's re-check), and call `rekey` only when the second
    plan is `bind` with the same `from` and `to`; it answers what was done (`bind` only when `rekey` answered true).
    `ReattachView = { orphans; liveNames }`.
- **C2 — the window's terminals (S1 owns).** NEW host `src/terminalHost.ts`: `liveTerminals(): readonly LiveTerminal[]`
  (the one reader; `chatReturnCommand.ts` and `remoteSessionHost.ts` switch to it), `reattachByName(panels, name): Reattach`
  (builds the view from `panels`, `threadUnder` and `window.terminals`, then `reattachOnce` with `panels.rekey`), and
  `bindReattach(context, panels)` (the `onDidOpenTerminal` listener).
- **C3 — go-to for a terminal (S2 owns).** In `src/chatGoto.ts`: `TabKind` gains `'terminal'`; `eligible('terminal')` is
  true; `Narrowing` gains `{ kind: 'several terminals'; count: number }`; `terminalGotoAsked(input): GotoAsked` builds the
  question from values the host read (no `vscode`). A NEW command `coai.goToFromTerminal` reads `window.activeTerminal`, as
  `coai.askFromTerminal` does: a terminal is not a tab, so it never goes through the tab match (§ 10, J3).
- **C4 — the skip list (S3 owns; S2 adds its id).** NEW pure `src/skipShell.ts`: `SKIP_SHELL_IDS` (exactly the manifest's
  `configurationDefaults` list, in its order: `coai.askFromTerminal`, then `coai.goToFromTerminal`), `skipShellState`,
  `withIds`, `offered`, `checkSkipShell(seams)`. S2 adds `coai.goToFromTerminal` to the manifest default; S3's test S3-T9
  holds the constant and the manifest equal.

## 5. Stories

Build order and its gates: § 6.

### E3.S1 — Re-attach after a reload: `reattachPlan`, its three triggers, the re-check before the re-key

`model: opus` (spec, this section) · coder: sonnet · review: opus · area (b).

- **Goal.** After a reload or a restart, the conversation of a `Creds: <host> / <session>` terminal is keyed again by the
  terminal of that name — only when it is unambiguous — so the door, *back to source*, *take*/*add* and *Asked* use THAT
  conversation and no second one is started.
- **Files and functions (what and how).**
  1. NEW `src/terminalReattach.ts` (C1), pure. `reattachPlan` is the parent's rule word for word: *"binds only when exactly
     one live terminal and exactly one orphaned conversation carry the name"*, for names that pass `credsSession`, and
     never to a terminal that already holds a conversation (*"an already bound key does not"*, parent § *Test plan*).
     Several stored records with one name are normal (E1 § 11, carried note): only OPEN panels are orphans, so records on
     disk are never counted. A panel whose source is not `terminal` is never an orphan, whatever its label — the mirror of
     E1's steal filter.
  2. NEW host `src/terminalHost.ts` (C2), with its row in `sonarcloud.yml`. `liveTerminals()` maps `window.terminals` to
     `LiveTerminal` (`exited` = `exitStatus !== undefined`); `chatReturnCommand.ts:156` `asLive` and
     `remoteSessionHost.ts:281` call it (reuse-first move 2; their tests stay green with no edit). `reattachByName(panels,
     name)` reads the view twice through `reattachOnce`. No `await` between the second read and `panels.rekey`.
     `panels.rekey` itself still refuses a taken destination (`chatPanels.ts:168-171`); that refusal answers `leave`, not
     an error.
  3. **The three triggers** (parent § *Stage 3*):
     - **A terminal opens.** `bindReattach(context, panels)`: `vscode.window.onDidOpenTerminal((opened) =>
       reattachByName(panels, opened.name))`, registered in `activate` beside `bindRemoteSessions` (`extension.ts:147`).
       This is the race the parent names: creds opens its terminals late, after the serializer restored the chats.
     - **After the serializer restores.** In `restoreChatTab` (`chatRestorePanel.ts:103`), right after
       `restoreConversation(…)` (`:114`) for a record whose source is `terminal`: `reattachByName(deps.panels,
       record.source.name)`. This is the other order of the race: the terminal was already open (or opened during the
       migration wait of up to 5 s, `chatRestore.ts:56`), so its `onDidOpenTerminal` found no orphan yet. The retry path
       goes through the same function, so it re-attaches too.
     - **At the start of the door.** In `askFromTerminal` (`chatCommand.ts:342`), after the clipboard read and right before
       `terminalMatch(` (`:365`), with no `await` between: `reattachByName(panels, door.name)`. It covers every state the two
       events could not see (for example: two terminals of one name at the open, and one of them closed since). Only this
       door: § 10, O5.
  4. **What re-attach says.** No notification: it is housekeeping, and a press of the door or of *back to source* still
     says why when it cannot find the terminal (E1, E2). One `console.info` line for `several terminals` and
     `several orphans` only: the name and the reason, so a person who asks why a chat did not re-attach can see it.
  5. **Comments** that state the old rule: `chatConversationRestore.ts:253-256` (a reload gives a key of its own — now: and a
     `Creds:` terminal of the same name may take it back by the re-attach rule); `terminalSource.ts:64` (`terminalMatch` stays
     as it is: the DOOR never re-keys; re-attach is a separate road that runs before it).
- **Scenarios** (`terminalReattach.test.ts`, new, unless named).

| # | Name (file) | Assertion | Break-it → the RED it must show |
|---|---|---|---|
| S1-T1 | one terminal and one orphan bind | `reattachPlan([{o,'Creds: h / s'}], [{t,'Creds: h / s',exited:false,held:false}], 'Creds: h / s')` → `bind` `o`→`t` | answer `leave` always → `a restored conversation did not find its one terminal` |
| S1-T2 | two terminals of the name never bind | two live terminals `Creds: h / s` → `leave` `several terminals`; one of them exited → `bind` to the live one | bind the first terminal → `two terminals of one name and one was taken` |
| S1-T3 | two orphans of the name never bind | two orphans `Creds: h / s`, one terminal → `leave` `several orphans` | bind the first orphan → `two conversations of one name and one was bound` |
| S1-T4 | a terminal that holds a conversation is never given another | the one terminal is `held` → `leave` `held`, for one orphan and for two | drop the `held` check → `a terminal that has a conversation was given a second` |
| S1-T5 | only a strict `Creds:` name binds | 1 terminal + 1 orphan named `bash`, `Creds run: x`, `Creds VPN: x`, `creds: h / s`, `Creds: h / s.x`, `Creds: -h / s`, `Creds: before x`, `Creds: h / s ` → `leave` `not creds` each, and no terminal list is read for them | test `isTerminalName` instead of `credsSession` → `a bash terminal was re-attached by its name` |
| S1-T6 | names are compared exactly | orphan `Creds: h / s`, terminal `Creds: h / S` or `Creds: h / s2` → `leave` `no terminal` | compare case-blind or by prefix → `a terminal of another name took the conversation` |
| S1-T7 | an orphan is a terminal conversation with no terminal of the window | `orphansOf`: a panel keyed `{}` with a `terminal` source → an orphan; a panel keyed by a terminal in `windowKeys` (live or exited) → not; a panel keyed `{}` whose source is `claude`, `file` or `none` and whose LABEL is `h / s` → never | judge by the label or by `key` not being a terminal of any kind → `a Claude tab's conversation was offered to a terminal` |
| S1-T8 | the plan is checked again before the re-key | `reattachOnce` with a `read` seam that answers view A, then view B: B adds a second terminal of the name, or removes the orphan, or marks the terminal held, or renames it → `rekey` NOT called, the answer is B's `leave`; A then A → `rekey(o, t)` called ONCE | act on the first plan → `the plan was acted on without a second look` |
| S1-T9 | a refused re-key is not reported as a bind | `rekey` answers false → the answer is `leave` `held`, and a real `ChatPanels` with `t` already registered keeps both entries as they were | report `bind` whatever `rekey` says → `a refused re-key was reported as done` |
| S1-T10 | the race, in both orders (over a real `ChatPanels` and fake views) | (a) the orphan registered first, then the terminal's open trigger → bound; (b) the terminal open first (its trigger finds no orphan → `no orphan`), then the restore trigger for that record's name → bound; (c) both triggers in either order with a second terminal of the name → never bound | drop the restore trigger → `a terminal that opened before its chat was restored stayed apart` |
| S1-T11 | the three triggers are wired (`terminalReattachWiring.test.ts`, new; host code, no seam reaches it) | `extension.ts` calls `bindReattach(`; `bindReattach` registers `onDidOpenTerminal` and calls `reattachByName(` with the opened terminal's `name`; `restoreChatTab`'s body calls `reattachByName(` after `restoreConversation(` for a `terminal` source; `askFromTerminal`'s body calls `reattachByName(` before `terminalMatch(` with no `await` between. Companions: each scan finds its function and its anchor | remove one call, or move the door's call before `await fromTheTerminal(` → red naming the trigger |
| S1-T12 | one reader of the window's terminals | `chatReturnCommand.ts` and `remoteSessionHost.ts` hold no `exitStatus !== undefined` map of their own and call `liveTerminals(`; companion: `terminalHost.ts` holds the one map | leave the copy in `chatReturnCommand.ts` → red naming it |

- **Host scenario S1-H1** (new `src/test/host/terminalReattachScenario.ts`, registered in `scenarios.ts`): in a real editor,
  with its OWN `ChatPanels` and `bindReattach` on it (as `terminalReturnScenario.ts` uses its own registry): register a
  conversation keyed `{}` whose thread source is `sourceOfTerminal('Creds: h / s')`; `createTerminal({ name: 'Creds: h / s'
  })`; wait (`until`) for `panels.get(theTerminal)` to hold that conversation. Then a second orphan of `Creds: h / t` and
  TWO terminals of that name: after both opened, neither holds it. Dispose every terminal in a `finally`.
- **Docs.** `research/module_extension.md`: a new section *The chat finds its terminal again (E3)* after the E2 section — the
  rule, the orphan, the three triggers and the race, the re-check, the console line. `research/module_tests.md`: a new row
  *Chat with other AI — RE-ATTACH, GO-TO, THE SKIP LIST (E3)* with S1's tests; **not covered**: a real reload with creds —
  R8.
- **Done when.** S1-T1…T12 green, each break-it seen red; S1-H1 green in CI's real-editor job on the head commit;
  `sonarExclusions.test.ts` green with `terminalHost.ts` listed; the whole suite and lint green.
- **Size.** About 520 lines (≈ 150 code, ≈ 370 tests and the host scenario).

### E3.S2 — Go to from a terminal: `TabKind` `'terminal'`, Ctrl+Alt+G on `terminalFocus`, the skip list, the overflow item

`model: opus` (spec, this section) · coder: sonnet · review: opus · area (b), and the trigger.

**Gate G1 before this story's manifest commit:** § 10, O1 (the road back's chord) must be answered.

- **Goal.** In a terminal, Ctrl+Alt+G and an overflow item open go-to for THAT terminal: its open conversation, its saved one,
  a list when there is more than one, or the offer to start one. Go-to never moves a conversation from one live terminal onto
  its namesake.
- **Files and functions (what and how).**
  1. `package.json`:
     - `contributes.commands`: `{ "command": "coai.goToFromTerminal", "title": "CoAI: go to this terminal's conversation",
       "category": "ConnectOtherAIs" }`.
     - `menus.view/title`: `{ "command": "coai.goToFromTerminal", "when": "view == terminal", "group": "coai@3" }` — the
       overflow, after E2's two items; not `navigation`.
     - `menus.commandPalette`: `{ "command": "coai.goToFromTerminal", "when": "false" }`, as E2's two items.
     - `keybindings`: `{ "command": "coai.goToFromTerminal", "key": "ctrl+alt+g", "mac": "cmd+alt+g", "when":
       "terminalFocus" }`.
     - `configurationDefaults["terminal.integrated.commandsToSkipShell"]`: `["coai.askFromTerminal",
       "coai.goToFromTerminal"]` (the parent: *"Ctrl+Alt+G on `terminalFocus` (added to the skip list)"*).
     - **Only if G1 says so:** the road back's chord (`:1370`) becomes `activeWebviewPanelId == 'coaiChat' &&
       !terminalFocus`, with the edits O1 lists.
  2. `src/chatGoto.ts` (C3):
     - `TabKind` gains `'terminal'` — *a terminal of the window, named by its full name*; the comment at `:44-48` loses
       "before stage 3"; `eligible('terminal')` → true; the comment at `:239-240` says a terminal in the EDITOR AREA reached
       through the tab path is still `other` (that path is unchanged).
     - `GotoAsked.namesakes` is documented for a terminal: the number of LIVE terminals of exactly this name (this one
       included).
     - In `goto`, one new helper (complexity ≤ 4), asked after `mine` is split: for a `terminal` tab with `namesakes > 1` and
       exactly one `mine` → `pick` with `why: { kind: 'several terminals', count: namesakes }`, `bind: true` — with two
       terminals of one name, a single saved conversation is not evidence for THIS one; the person chooses. The comment at
       `:313-317` (*impossible by construction*) gains: for a terminal source it is the normal case (each reload before E3 made
       one more record of the name).
     - `terminalGotoAsked({ name, liveId, namesakes, candidates, inRoot, roots, fallback, caseBlind, index })` → `GotoAsked`
       with `tab: { kind: 'terminal', label: terminalLabel(name), path: '' }`, `source: sourceOfTerminal(name)`,
       `ambiguous`, `severalSessions` and `walkFailed` false. A name that `isTerminalName` refuses gives `kind: 'other'` (the
       whole list, never an error — the operator's rule at `:238-241`).
  3. `src/conversationChoice.ts:87` `narrowedTitle` — the arm `several terminals`: *"<N> terminals are called “<tab>” — which
     conversation did you mean?"*.
  4. `src/chatSessionJoin.ts` — `askedForTerminal(panels, index, terminal)` beside `askedForGoto` (`:257`), the host half:
     `liveTerminals()` (S1), `threadUnder(panels, terminal)?.saveId`, `index.bySource(sourceOfTerminal(name))`, the
     entries of `filedUnder('', whereToLook(), conversationWorkspace(), NAMES_ARE_CASE_BLIND)` (where `knownFolder` files a
     terminal conversation), then `terminalGotoAsked`. It never calls `tabMatch(`, `snapshots(` or `sessionsNamed(`.
  5. `src/chatGotoCommand.ts`:
     - `goToFromTerminal(panels, index, store, extensionUri, terminal)` — under the SAME `running` latch (`:67`) as
       `goToConversation`; no terminal → a warning, code `no-terminal-for-goto`, *"There is no terminal here — open one,
       then press go to."*; else `arrive` with the terminal's question. `arrive` (`:88`) takes the question and the two
       seams below as one parameter object, so the tab road and the terminal road share every arm of the exhaustive switch.
     - `bindingTo` (`:261`) takes a `stillThere(key)` seam: the tab road passes `tabStillOpen`; the terminal road passes
       *"the terminal is still in `window.terminals`, has not exited, and its LIVE name still gives the same
       `sourceOfTerminal`"* (a rename while the picker is open refuses the bind).
     - `startingFor` (`:229`) takes the door to run: the tab road keeps `chatWithOtherAi(panels, extensionUri, [], false)`;
       the terminal road runs `askFromTerminal(panels, extensionUri, terminal)` for THAT terminal — never the tab door, which
       would read a file tab (parent § *What the code does today*).
  6. `src/chatRegistry.ts:104` `revealBound` — the ONE road both go-to's reopen and the picker's accept take (`:208`,
     `conversationPickerCommand.ts:305`): it gains a seam `isLiveTerminal(key)`, and when `where` is a live terminal that is
     not `onto`, it only reveals and never re-keys. A conversation keyed `{}` (restored, or reopened by the picker) is still
     moved onto the terminal: that is the person's own re-attach. The guard is on the road, not at the two callers
     (`security.md`, *a measure at SOME of its sites*).
  7. `src/extension.ts` — after `coai.goToConversation` (`:797`): `registerCommand('coai.goToFromTerminal', …)` with
     `noteChatDoor('goto')` FIRST, then `goToFromTerminal(chatPanels, housekeeping.index, chatStore, context.extensionUri,
     vscode.window.activeTerminal)`, its catch shaped like go-to's (`:799-811`), code `conversation-not-opened`.
  8. Help, five languages (`helpContent.ts:294` and the four translations; the bold title and `Ctrl+Alt+G` in English): one
     sentence after the *Ask other AI* paragraph — in a terminal, `Ctrl+Alt+G` or **CoAI: go to this terminal's
     conversation** in the overflow menu of the terminal panel opens that terminal's conversation, or a list when more than one
     could be meant. The skip-list sentences name both ids (S3 then rewrites them once more, item 4 of S3).
- **Scenarios.**

| # | Name (file) | Assertion | Break-it → the RED it must show |
|---|---|---|---|
| S2-T1 | the terminal go-to is declared and sits in the overflow (`terminalDoorWiring.test.ts`) | one command row (title, category); one `view/title` row, `when` exactly `view == terminal`, group `coai@3` (not `navigation`); one palette row `when: false`; one keybinding `ctrl+alt+g`, `mac` `cmd+alt+g`, `when` `terminalFocus` | group `navigation` → `the go-to item is not in the overflow menu` |
| S2-T2 | the skip-list default holds both ids | the existing derived test (`:72-85`) finds `coai.goToFromTerminal` among the `terminalFocus` bindings (a second companion) and the default is exactly the two ids in order | remove the id from the default → `coai.goToFromTerminal is bound in a terminal but not in the skip list — the chord goes to the shell` |
| S2-T3 | no other Ctrl+Alt+G binding can fire under terminal focus (extends `:87-101` to `ctrl+alt+g`) — **only if G1 allows the edit; else this row is NOT written and O1's answer is recorded here** | every `ctrl+alt+g` row but the terminal go-to carries `!terminalFocus`; companion: the derivation finds go-to's and the road back's rows | drop `!terminalFocus` from the road back → `coai.backToSource bound to ctrl+alt+g … can still fire in a terminal` |
| S2-T4 | a terminal is a kind go-to starts from (`chatGoto.test.ts`) | a `terminal` tab: `live` → `reveal`; one `mine` and `namesakes` 1 → `reopen`; none → `start` with the offer `h / s`; `building` → `building`; `unavailable` → `pick` | make `eligible('terminal')` false → `a terminal got the whole list instead of its conversation` |
| S2-T5 | several records of one terminal name are a list, not a guess | three `mine` with the source `Creds: h / s` → `pick`, why `several`, `bind: true` | answer `reopen` with the first → `one of several conversations of a terminal was opened without a choice` |
| S2-T6 | two live terminals of one name are a list even for one record | one `mine`, `namesakes` 2 → `pick`, why `{ several terminals, count 2 }`, `bind: true`; `narrowedTitle` gives *"2 terminals are called “h / s” — which conversation did you mean?"* | drop the helper → `a conversation was bound to one of two terminals of its name without a choice` |
| S2-T7 | the terminal question is built from the terminal, never the tabs | `terminalGotoAsked`: label `h / s` for `Creds: h / s`, `bash` for `bash`; source = the FULL name; `path` `''`; `inRoot` filed under the first root; an empty or 501-unit name → kind `other` | build the source from the label → `a terminal conversation was looked up by its title` |
| S2-T8 | a conversation is never moved from one live terminal to its namesake (`chatRegistry` seam, a real `ChatPanels`) | `revealBound(panels, t1, t2, isLive)` with `t1` live → `t1` still holds it, `t2` holds nothing, the panel was revealed; `where` = `{}` (not a terminal) → moved onto `t2`; `onto` undefined → only revealed | remove the guard → `a terminal's conversation was moved onto its namesake` |
| S2-T9 | the bind re-checks the terminal at the press | the terminal road's `bindingTo` refuses when the terminal is gone, has exited, holds a conversation, or its live name now gives another source; it binds when all hold and `bindable` passes | skip the rename check → `a conversation was bound to a terminal that changed its name` |
| S2-T10 | the terminal go-to never reads the tabs, and starts the terminal door (`chatGotoWiring.test.ts`) | the bodies of `goToFromTerminal` and `askedForTerminal` hold none of `tabMatch(`, `snapshots(`, `sessionsNamed(`, `askedForGoto(`; its `start` runs `askFromTerminal(` and never `chatWithOtherAi(`; companion: `goToConversation` still calls `askedForGoto(` | start with `chatWithOtherAi` → `the offer from a terminal opened the file door` |
| S2-T11 | the press is a door, recorded first (`chatWiring.test.ts`, the census) | the census finds `coai.goToFromTerminal` (its `view/title` row) and its `noteChatDoor('goto')` comes first | move `noteChatDoor` after the call → `records the invocation after the work` |
| S2-T12 | the tab road is unchanged | the existing tests at `chatGoto.test.ts:215-224` and `:534-556` (a terminal in the editor area is `other` → the whole list) are green with no edit | — (they are the guard) |
| S2-T13 | every language names the item (`helpCoverage.test.ts`) | each of the five articles, not a fallback, contains `**CoAI: go to this terminal's conversation**` and `Ctrl+Alt+G`; the help census passes for the new title | leave `helpEs.ts` as it is → `the es chat article never names go to from a terminal` |

- **Host scenario S2-H1** — E1's `theSkipListDefaultHoldsTheTerminalDoor` (`terminalCaptureScenario.ts:63`) asserts BOTH ids
  in the default (`inspect(…).defaultValue`), each with its own message.
- **Docs.** `research/module_extension.md` (E3 section): go-to from a terminal — the command, the chord, the item, the skip
  list, the question built from the terminal, the namesake rule, the guard in `revealBound`, the start through the terminal
  door; and `:10319` corrected if G1 changes the road back's chord. `research/module_tests.md`: S2's tests; **not covered**:
  the chord in a real terminal and the item on screen — R10.
- **Done when.** S2-T1…T13 green (S2-T3 as G1 decides), each break-it seen red; S2-H1 green in CI's real-editor job; the door
  census and the help census green; `notification-sites.json` regenerated; the whole suite and lint green.
- **Size.** About 560 lines (≈ 170 code and manifest, ≈ 60 help in five languages, ≈ 330 tests).

### E3.S3 — Skip-shell health: one notification, *Add* appends to the user setting, the answer remembered

`model: opus` (spec, this section) · coder: sonnet · review: opus · the trigger, and the person's settings.

- **Goal.** When the person's OWN user-level `terminal.integrated.commandsToSkipShell` hides E1's default, they are told once
  and offered **Add**. The press is the consent. The ids are appended to that list and nothing else changes. The answer is
  kept, so the notification does not come back.
- **What the parent fixes and what this story reads from it.** *"if the person's own `commandsToSkipShell` hides the
  default, one notification with an *Add* button that appends the ids to the user setting; pressing it is the consent; the
  answer is remembered"* (§ *Stage 3*), and R11's *"a user-level `commandsToSkipShell` that lacks the ids"*. So: the TARGET is
  the user setting (`ConfigurationTarget.Global`) and nothing else; the write APPENDS to a list the person already has; one
  button. What is outside that sentence is § 10 (O3, O4, O9, O10), not built.
- **Files and functions (what and how).**
  1. NEW pure `src/skipShell.ts` (C4):
     - `SKIP_SHELL_IDS = ['coai.askFromTerminal', 'coai.goToFromTerminal'] as const`.
     - `skipShellState(inspected: Inspected | undefined, ids): SkipShellState` over the `inspect()` shapes, the layers read
       in VS Code's order (folder, workspace, user, default):
       - `{ kind: 'repository'; layer: 'folder' | 'workspace' }` — a folder or workspace value is set. No offer (O3).
       - `{ kind: 'unreadable' }` — the user value is set and is not an array of strings. No offer and no write: the
         extension never rewrites a value it cannot read.
       - `{ kind: 'user'; list: readonly string[]; missing: readonly string[] }` — the person's own list. `missing` = the
         ids that are not in it AND are not removed by the person with a leading `-` (`-coai.askFromTerminal` is their
         choice, and it is kept: never offered).
       - `{ kind: 'default'; missing }` — no user, workspace or folder value. `missing` non-empty means the default itself
         lacks the ids (for example another extension's default for the same setting won — not measured): no offer (O4).
     - `withIds(list, missing): readonly string[]` — a NEW array: every entry of `list`, in its order, unchanged (its own
       duplicates too), then each missing id once, in `SKIP_SHELL_IDS` order, skipping one that is already there.
     - The answer: `SkipShellAnswer = { id: string; answer: 'added' | 'dismissed'; askedUtc: string }`;
       `offered(missing, remembered: unknown): readonly string[]` — the missing ids that have NO remembered answer. A
       remembered list that is not an array of valid rows reads as empty, so the person is asked again (never a write
       without a press). `rememberedWith(remembered, ids, answer, now)` — a new array, one row per id, rows of ids no longer
       in `SKIP_SHELL_IDS` dropped.
     - `checkSkipShell(seams): Promise<SkipShellOutcome>` — the one road, every effect a seam (`inspect`, `ask`, `update`,
       `memory`, `failed`):
       1. `skipShellState(seams.inspect())`; not `user`, or `offered(…)` empty → `nothing`, no prompt.
       2. `ask(ids)` — the notification (below). Not `'Add'` (closed, Escape) → remember `dismissed` for those ids; no write.
       3. `'Add'` → **read AGAIN** (`inspect()`), and decide again from the fresh value: not `user` any more, or nothing
          missing now → no write (remember `added` when the ids are there). Else `update(withIds(fresh.list,
          fresh.missing ∩ asked))` — the person's list as it is NOW, never the one read before the prompt.
       4. **Read back.** `inspect()` once more: the ids are in the user value → remember `added`. They are not, or `update`
          rejected → `failed(sentence)` and nothing remembered, so the next start asks again.
  2. NEW host `src/skipShellHost.ts` (its row in `sonarcloud.yml`) — the seams only:
     - `inspect` = `vscode.workspace.getConfiguration('terminal.integrated').inspect<unknown>('commandsToSkipShell')`.
     - `update(next)` = the same configuration's `update('commandsToSkipShell', next, vscode.ConfigurationTarget.Global)` —
       the USER settings, never `Workspace` or `WorkspaceFolder`.
     - `ask(ids)` = `notifyAndAsk` (`notify.ts:351`, which waits through `askPerson` and records the asking and the answer):
       `as: 'information'`, `source: 'skipShell'`, code `skip-list-hides-terminal-keys`, `action: 'Add'`, title *"Your own
       setting terminal.integrated.commandsToSkipShell hides the terminal keys of CoAI, so the shell gets them. Add
       <ids, joined with “ and ”> to the end of that list in your user settings?"*. Only the ids that would be added are
       named.
     - `failed` = `notify` warning, code `skip-list-not-written`: *"<ids> could not be added to
       terminal.integrated.commandsToSkipShell: <reason>. Nothing else in the setting was changed."*; the read-back case:
       *"<ids> were written to terminal.integrated.commandsToSkipShell, but the setting does not show them when it is read
       back. Check your user settings."*.
     - `memory` = `context.globalState` key `coai.skipShellAnswers`.
     - **When it runs:** once at activation, and on `onDidChangeConfiguration` when
       `affectsConfiguration('terminal.integrated.commandsToSkipShell')`. Both through ONE `oneAtATime` latch: a second
       trigger while a notification is open does nothing (one notification, never two at once).
     - In `ExtensionMode.Test` only, the activation's return value carries a test door (as E2's `testDoorOf`) that lets the
       host scenario replace `ask` and read the bound `checkSkipShell`.
  3. `src/extension.ts` — `bindSkipShell(context)` in `activate`.
  4. Help, five languages: the skip-list sentences of the terminal paragraph (`helpContent.ts:294` and the four
     translations) say: both ids are in the default; a list of your own hides that default; then CoAI tells you once and
     **Add** appends the missing ids to your user settings, and changes nothing else; if you close the notice it is not shown
     again, and you can add the ids yourself. A list set for a workspace or a folder is not changed: add the ids there
     yourself (O3).
- **Scenarios** (`skipShell.test.ts`, new, over `skipShell.ts` with fake seams, unless named).

| # | Name (file) | Assertion | Break-it → the RED it must show |
|---|---|---|---|
| S3-T1 | the state over every `inspect()` shape | `undefined` and `{ defaultValue: ids }` → `default`, nothing missing; `{ defaultValue: ['other.x'] }` → `default` with both missing (no offer); `globalValue ['a']` → `user`, both missing; `['a','coai.askFromTerminal']` → only go-to missing; both present → none; `['-coai.askFromTerminal']` → only go-to missing; `workspaceValue` set (with or without `globalValue`) → `repository` `workspace`; `workspaceFolderValue` → `folder`; `globalValue` `'x'`, `[1]`, `{}` → `unreadable` | read the workspace layer as the user's → `a repository's list was offered a write to the user settings` |
| S3-T2 | the person's list is kept, in order, and the ids are added once | `withIds(['b','a','b'], both)` → `['b','a','b', ask, goTo]`; an id already there is not added again; the input array is unchanged (frozen in the test) | sort or de-duplicate → `the person's own entries were reordered or removed` |
| S3-T3 | no write before the press | `ask` answers `undefined` → `update` never called, `dismissed` remembered for the asked ids; `ask` answers `'Add'` → `update` called ONCE with the value `[…list, …missing]` (the target is the host's, S3-T10); the fake records that `update` came after `ask` resolved | call `update` before `ask` → `the person's settings were changed before they agreed` |
| S3-T4 | the answer is remembered and the notice does not come back | after `dismissed` (or `added`) for both ids, a second `checkSkipShell` with the same list → no `ask`; a damaged memory (`'junk'`, `[{id:1}]`) → asked again; a remembered answer for `coai.askFromTerminal` only, with go-to missing → asked for go-to only | ignore the memory → `the notice came back after the person answered` |
| S3-T5 | the write uses the list as it is at the press | `inspect` answers `['a']` before the prompt and `['a','c']` after → `update(['a','c', …])`; after the prompt the ids are already there → no `update`, `added` remembered; a workspace value appeared → no `update` | write from the first read → `an entry the person added while the notice was open was lost` |
| S3-T6 | a failed write is said and not remembered | `update` rejects → `failed` with the reason, nothing remembered; `update` resolves but the read-back lacks an id → `failed` with the read-back sentence, nothing remembered | remember `added` without the read-back → `a write that did not take was remembered as done` |
| S3-T7 | nothing is offered where Add cannot help or must not write | `repository`, `unreadable` and `default` states → no `ask`, no `update`, nothing remembered | offer for `default` → `a user list was created where the person had none` |
| S3-T8 | one notification at a time | two `checkSkipShell` calls at once under one latch (`Promise.all`) → `ask` ONCE | no latch → `two notices were shown at once` |
| S3-T9 | the code's ids are the manifest's (`terminalDoorWiring.test.ts`) | `SKIP_SHELL_IDS` deep-equals `configurationDefaults["terminal.integrated.commandsToSkipShell"]`, in order, and equals the set of commands bound under `terminalFocus` | add an id to the manifest only → `the skip-list check does not know coai.x` |
| S3-T10 | the target is the user settings, and nothing else writes the setting (`skipShellWiring.test.ts`, host code, no seam reaches it) | `skipShellHost.ts` calls `update(` with `ConfigurationTarget.Global` and with no other target; no other file in `src/` writes `commandsToSkipShell`; companion: the scan finds that one `update(` | pass `ConfigurationTarget.Workspace` → red naming the line |
| S3-T11 | every language says what Add does (`helpCoverage.test.ts`) | each of the five articles names both ids and **Add**, and says a workspace list is not changed | leave `helpRu.ts` with E1's sentence → `the ru chat article does not say what Add does` |

- **Host scenario S3-H1** (new `src/test/host/skipShellScenario.ts`, registered in `scenarios.ts`): through the test door of the
  BOUND check, with `ask` replaced by a fake that answers **Add** and counts. Save the user value of
  `terminal.integrated.commandsToSkipShell` and the memory key; write the user value `['workbench.action.quickOpen']`; run
  the check → the REAL `inspect().globalValue` deep-equals `['workbench.action.quickOpen', 'coai.askFromTerminal',
  'coai.goToFromTerminal']`, and the fake was asked once; run it again → asked 0 more times and the value is the same. In a
  `finally`: write the saved user value back (`undefined` when there was none) and the saved memory.
- **Docs.** `research/module_extension.md` (E3 section): the check, the four states, the consent, the re-read, the read-back,
  the memory and its size. `research/architecture.md`: one line in the cross-cutting part — the extension appends to one
  VS Code user setting it does not own, only after a press. `research/module_tests.md`: S3's tests and S3-H1; **not
  covered**: the notification on screen, a real `settings.json` with comments, and R1 after the write — R11.
- **Done when.** S3-T1…T11 green, each break-it seen red; S3-H1 green in CI's real-editor job, and the user setting of that
  editor back as it was; `sonarExclusions.test.ts` green with `skipShellHost.ts` listed; `notification-sites.json`
  regenerated; the whole suite and lint green.
- **Size.** About 600 lines (≈ 170 code, ≈ 70 help in five languages, ≈ 360 tests and the host scenario).

## 6. Build order, and its gates

1. **Plan gate.** This document goes to `mcp__coai__review_plan` on `feat/chat-from-a-terminal-e3` (with the parent's E3
   section), and to the Opus self-review at the same time. The `todo/README.md` row of this document is committed WITH this
   document (`planning-docs.md`).
2. **G0 — the open points that would be plan changes (§ 10).** Each is answered by the caller (consultants first, then the
   owner) BEFORE the story it touches is coded. One accepted as a change stops the plan (epoch 2 has used its one change).
3. **S1** — Sonnet codes; Opus reviews; Sonnet fixes. S1 extracts `liveTerminals()` first, in its own commit.
4. **G1 — the road back's chord (O1)**, answered before S2's manifest commit.
5. **S2** — Sonnet codes; Opus reviews; Sonnet fixes. S2 needs S1's `liveTerminals()`.
6. **S3** — Sonnet codes; Opus reviews; Sonnet fixes. S3 needs S2's id in the manifest (S3-T9).
7. One pull request for the epic. The code round of the review gate gets this document's § 1, § 4 and § 11 as `planText`.
   Every CI check green, and the `extension · a real editor` job read by its own result (§ 8).
8. Merge. Build the pre-release VSIX from the merged head (`npm run package`). The owner installs it and does **R8, R10 and
   R11** (parent ledger entry 1.1; E3 *Done when*). Each result goes into the parent's R-table with its date, the VSIX version
   and its sha. A failed check is fixed before the tag.
9. Release the extension: the release-please pull request carries the `## Extension <version> — <date>` prose; the tag is
   pushed alone and its run observed.
10. Then, as the parent's build order says: `mcp__coai__review_feature` over the three epics (O2 is about this order), and the
    promotion of the parent with its deviations — the parent, the three stories documents and the two spec documents
    together, each with its `research/README.md` mention.

## 7. Test plan — the parent's rows this epic owns

| Parent row (§ *Test plan*, *Stage 3*, and the stage-1 host row) | Story | Tests |
|---|---|---|
| `reattachPlan`: 1/1 binds | S1 | S1-T1, S1-T10, S1-H1 |
| 2 terminals or 2 conversations do not bind | S1 | S1-T2, S1-T3, S1-T10, S1-H1 |
| An already bound key does not bind | S1 | S1-T4, S1-T9 |
| A name that fails `credsSession` never binds | S1 | S1-T5, S1-T6 |
| Re-checked just before `panels.rekey`; the three triggers | S1 | S1-T8, S1-T10, S1-T11 |
| Go-to with a terminal | S2 | S2-T4…T10, S2-T12 |
| The skip list holds every id bound under `terminalFocus` (stage 1, extended) | S2, S3 | S2-T2, S2-H1, S3-T9 |
| The skip-shell check over the `inspect()` shapes | S3 | S3-T1, S3-T7 |
| Consent, append-only, the answer remembered | S3 | S3-T2…T6, S3-T8, S3-T10, S3-H1 |
| Webview pages are not changed | all | — (no page edit in E3) |

## 8. Where each proof runs

- **Unit and wiring tests**: `npm test`, here and in CI (`ci.yml:575`, required, `ubuntu-latest`).
- **Host scenarios** S1-H1, S2-H1, S3-H1: `npm run test:host` CANNOT run on this machine (no `libnss3`). They run only in CI's
  job `extension · a real editor` (`ci.yml:593`, `xvfb-run -a npm run test:host` at `:627`). That job has
  `continue-on-error: true` (`:596`), so a red scenario does NOT turn the pull request red: read the job's own conclusion and
  log on the head commit, and write the result (with the sha) in the pull request description. A red host scenario blocks
  the merge as if it were required.
- **Real window** (parent R-table), on the pre-release VSIX of the merged head, BEFORE the tag — the owner:
  - **R8** — *"After a VS Code reload, creds reopens its terminals and the conversations re-attach."* With two or more
    `Creds:` terminals, each with its conversation open in a tab: *Developer: Reload Window*; when the terminals are back,
    **Ask other AI** in each terminal reveals THAT terminal's existing chat (no new tab), and *back to source* from each chat
    shows its own terminal. Recorded with it (the parent's R8 row is not edited): which side brought the terminals back —
    VS Code's own terminal revival or creds — and whether any name appeared twice (O8); the same after a full restart, if it
    can be done.
  - **R10** — *"Ctrl+Alt+G in a `Creds:` tmux terminal opens go-to and prints nothing in the pane."* As R1: with `cat -v` in
    the pane and with the Claude TUI, nothing is printed and go-to opens that terminal's conversation (or the list, or the
    offer). Recorded with it: the overflow item **CoAI: go to this terminal's conversation** is there and does the same; and
    the same chord while a coai chat is the active editor (the road back's chord, O1).
  - **R11** — *"With a user-level `commandsToSkipShell` that lacks the ids, the notification appears once, *Add* appends
    them, and R1 then passes."* With `["workbench.action.quickOpen"]` in the user `settings.json`: one notification after the
    reload, naming both ids; **Add**; the file now holds the person's entry first and the two ids after it, once each; a
    second reload shows no notification; R1 (Ctrl+Alt+A) and R10 (Ctrl+Alt+G) pass. Recorded with it: in a second run, the
    notification closed without **Add** is not shown after the next reload; and whether the comments and the layout of
    `settings.json` stayed as they were.

## 9. Growth and security

- **Growth** (the parent's § *Growth surfaces*, made concrete). Re-attach: nothing new — it moves a panel that exists from
  one key to another (`ChatPanels`, one entry either way). Go-to: nothing new. The skip list: ONE `globalState` key,
  `coai.skipShellAnswers`, at most one row per id of `SKIP_SHELL_IDS` (2 today, about 80 bytes each), rows of ids that left
  the list dropped on the next write — under 1 KiB for as long as the list stays short; nothing in flight survives a crash
  (a notification open at a reload is no answer, and is asked again). The user setting gains at most two strings, once. The
  conversation records: unchanged (E1's store and its ninety-day sweep).
- **Security.** No new outward edge: E3 starts no process and calls no network. Two judgements:
  - **The terminal name is not proof** (parent § *Security*). Any program can open a terminal called `Creds: h / s`, and
    re-attach would then key a conversation to it. That gives it nothing it could not get before E3: the conversation's
    remote reads still take the host from the live name and go through E2's per-host consent, and *back to source* already
    found a terminal by its name (E1). Re-attach binds only a strict `Creds:` name, only one live terminal against one
    orphan, never a held terminal.
  - **The one new write is to the person's own configuration.** Only after the press; only to the user layer; only
    appending the named ids; never creating the list; never a value it cannot read; checked by a read-back
    (`security.md`, *a measure you have not observed is a comment*: the write is VS Code's, so its effect is read, not
    assumed). No secret is handled. No authentication, authorisation or cryptography is judged, so the Fable security row
    is not owed (O11).

## 10. What E3 does not do, judgements, and open points

Not in E3: following a renamed terminal, or re-checking a session against a newer namesake (parent § *What this plan does
NOT do*); re-attach of a terminal whose name is not a strict `Creds:` name; a change to how a TAB is re-keyed; agent mode for
a terminal conversation; any change to CredsForDevs, `coai-mcp` or the Team server; E1's open points 1, 2, 4 and 5 (the
switch chord in the skip list, the other palette entries, go-to from the palette with a terminal focused, the menu path
outside the latch) — they stay open as E1 recorded them.

**Judgements this document made inside the parent's text** (no plan change; said so the caller can see them):

1. **J1 — what an orphan is.** The parent's word *"orphaned"* is read as the code already uses it (`sessionKey.ts`, parent
   § *What the code does today*: *"a known panel whose key is not among the TABS"*): an open panel with a `terminal` source
   whose key is not a terminal of the window. That covers a reload, a restart, a conversation reopened from the picker
   (also keyed `{}`), and one whose terminal was closed in this window. A terminal that has EXITED but is still open keeps
   its conversation (it is still in `window.terminals`). O6 asks whether a narrower reading is wanted.
2. **J2 — only open panels count.** Several stored records of one terminal name are normal (E1 § 11, carried note); counting
   them would make every terminal ambiguous after two reloads. Stored records are go-to's business (S2-T5).
3. **J3 — a command of its own for go-to.** `coai.goToFromTerminal`, because `coai.goToConversation` reads the TAB (`askedForGoto`)
   and its chord must keep `!terminalFocus` (`terminalDoorWiring.test.ts:87-101`). The same reason E1 gave
   `coai.askFromTerminal` its own command. The parent names no command id for go-to.
4. **J4 — no move between live namesakes, and a list for two terminals of one name** (S2-T6, S2-T8). This satisfies E1.S2's
   contract as written (*"two live terminals of one name → two conversations with equal sources"*) on the go-to road,
   which E1 left untouched.
5. **J5 — a person's `-coai.…` entry is their choice** and is never offered back (S3-T1).
6. **J6 — closing the notification is an answer** (`dismissed`, remembered). The parent says *"the answer is remembered"*
   and gives one button; a notice still open at a reload is no answer. O10 asks whether a way to be asked again is wanted.

**Open points.** Each says whether it would be a plan change. None is taken here.

| # | Point | Would it be a plan change? |
|---|---|---|
| O1 | **The road back's chord.** `coai.backToSource` is bound to `ctrl+alt+g` when `activeWebviewPanelId == 'coaiChat'`, with no `!terminalFocus` (`package.json:1370`). With a coai chat as the active editor and a terminal focused, it matches beside the new terminal go-to; if VS Code resolves the chord to the road back, which is not in the skip list, the key goes to the shell and R10 fails. E1 § 11 item 3 said E3 *"must add `!terminalFocus` here too"*, and listed it among points that *"would be a plan change"*. Taking it also needs `chatWiring.test.ts:493-518` (the exact `when`) and `research/module_extension.md:10319` (*"the two `when`s never overlap"*) changed. This document reads it as satisfying DoD item 2 as written (*"the old chords never fire in a terminal"*), but E1 called it a change, so it is **in doubt, and in doubt it is a change**. Gate G1. | **Maybe — the caller rules before S2's manifest commit.** If it is a change, it stops the plan; the alternative (leave the chord, record R10's result with a chat active) needs no change. |
| O2 | **When the feature review runs.** The parent's build order and E3 *Done when* put `review_feature` AFTER the third release; `coai-feature-gate.md` says *"after every epic is built … and before the release"*. | **Yes** (build-order), if the order is moved. As written, the stories follow the parent. |
| O3 | **A workspace or folder list hides the default.** **Add** to the user setting cannot fix it (the workspace layer wins), so E3 offers nothing and says nothing (S3-T7); the help says to add the ids there. Saying it in a notification, or writing the workspace file, is outside the parent's sentence. | **Yes** (scope), if either is wanted. |
| O4 | **A default that lacks the ids** with no user value (for example another extension's `configurationDefaults` for the same array setting won — not measured whether VS Code merges array defaults across extensions). Writing a user list then would hide that other default. E3 offers nothing. | **Yes** (scope), if an offer is wanted. |
| O5 | **"At the start of the door."** Read as the terminal door, `coai.askFromTerminal`, only. *Take*/*add* from a terminal and go-to from a terminal do not run re-attach first; go-to reaches an orphan by its own rules (a person's choice, J4). Running it at the start of those three too is a wider reading. | **Maybe** — in doubt, yes (approach). |
| O6 | **A narrower orphan** (only panels the serializer restored, not one reopened from the picker or one whose terminal closed in this window) needs a mark on the panel the code does not keep. J1 takes the parent's own meaning of *"orphaned"*. | **Yes**, if the narrower reading is wanted (approach). |
| O7 | **No trigger when a terminal closes.** Two terminals of one name, then one closes: nothing re-attaches until a terminal opens or the door is pressed. The parent names three triggers. | **Yes** (approach), if a fourth is wanted. |
| O8 | **Who brings a creds terminal back after a reload** — VS Code's own terminal revival (the same name, a revived process, possibly already in `window.terminals` at activation) or creds (a new terminal), or both (then two terminals of one name, and the rule binds nothing). Not measured; R8 records it. If R8 shows both, the rule as written leaves every conversation apart. | No, until R8 measures it. A fix would be. |
| O9 | **A Remote window.** Where `ConfigurationTarget.Global` writes `terminal.integrated.commandsToSkipShell` from a Remote window (the local user settings or the remote machine's), and which layer the terminal there reads, is not measured. The owner's windows are local. | No (not built for; named). |
| O10 | **A way to be asked again** after *dismissed* (a command, or a row in a panel). Not in the parent; the help says how to add the ids by hand. | **Yes** (scope), if wanted. |
| O11 | **No cornerstone.** The parent recommends none for E3, and the quota is full (2 of 2). The re-attach rule is area (b), and its failure is silent; that is why S1 carries twelve scenarios, a re-check scenario and a host scenario. No reason found to mark it. The Fable security row is not owed (§ 9). | A mark would exceed the quota: it goes to the operator, not into this document. |

**Where the code at `4efa0a90` differs from what the parent says** (none of them changes a decision):

- D1 — The parent's E3 scope lists `src/chatGoto.ts` and `package.json` for go-to. Go-to's host is also `chatGotoCommand.ts`
  (`bind`, `bindingTo`, `startingFor`), `chatSessionJoin.ts` (the question), `chatRegistry.ts` (`revealBound`,
  `tabStillOpen`) and `conversationChoice.ts` (`narrowedTitle`). S2 touches them as the parts of the feature the parent names.
- D2 — The road back's Ctrl+Alt+G chord has no `!terminalFocus` (O1); `module_extension.md:10319` says the two Ctrl+Alt+G
  `when`s never overlap, which stops being true once Ctrl+Alt+G is bound under `terminalFocus`.
- D3 — `chatGoto.ts:313-317` calls several conversations of one source *"impossible by construction"*. For a terminal source
  it is the normal case (E1 § 11, carried note).
- D4 — The serializer waits up to 5 s for the migration (`chatRestore.ts:56`) before a restored conversation is registered,
  so a terminal can open in that window and find no orphan: this is why the restore trigger exists beside the open trigger.
- D5 — The parent's § *What the code does today* cites `chatGoto.ts:44` and `:235` as saying a terminal cannot own a
  conversation. E1 rewrote both; at `4efa0a90` they are `:44-48` and `:239-240` and say go-to does not start from a terminal
  *"before stage 3"*. S2 rewrites them again.
- D6 — The terminal liveness map exists twice (`chatReturnCommand.ts:156`, `remoteSessionHost.ts:281`); S1 makes it one.

## 11. Definition of Done — epic E3

- [ ] The plan gate passed for this document, and every open point of § 10 that touches a story was answered before that
      story was coded; none was taken as a change without the parent's ledger entry (and its full stop).
- [ ] S1–S3 coded to this document; every scenario green; every new test seen RED, GREEN, RED with its guarded line broken,
      and GREEN again, with the observations in the summary.
- [ ] Re-attach binds only a strict `Creds:` name, only one live terminal against one orphan, never a held terminal, and only
      after the second look (S1-T1…T10); its three triggers are wired (S1-T11); the window's terminals are read in one place
      (S1-T12).
- [ ] Go-to works from a terminal by Ctrl+Alt+G and by the overflow item; the skip-list default holds both ids; a conversation
      is never moved between live namesakes (S2-T1…T13); the road back's chord as G1 decided.
- [ ] The skip-shell check offers **Add** only for the person's own user list, writes only after the press, appends only the
      missing ids, re-reads before the write, reads back after it, and remembers the answer (S3-T1…T11).
- [ ] `npm run compile` exit 0; `npm test` green; `npm run lint` green; no suppression count grew; `sonarExclusions.test.ts`
      green with `terminalHost.ts` and `skipShellHost.ts` listed; `notification-sites.json` regenerated; the door census and
      the help census green.
- [ ] S1-H1, S2-H1 and S3-H1 green in CI's `extension · a real editor` job on the head commit, read from that job, named in
      the pull request; the editor's user setting restored by S3-H1.
- [ ] `research/module_extension.md` (the E3 section), `research/module_tests.md` (the E3 row) and `research/architecture.md`
      (the user-setting write) updated; the help in five languages.
- [ ] The pull request merged; **R8, R10 and R11 measured on the pre-release VSIX of the merged head, before the tag**, and
      recorded in the parent's R-table with what § 8 says to record beside them.
- [ ] The extension release cut from `main`: its changelog prose in the release-please pull request, the tag pushed alone,
      its run observed.
- [ ] `review_feature` run over the three epics, at the place the parent's build order puts it (O2); the plan promoted with
      its deviations — the parent, the three stories documents and the two spec documents together (parent DoD items 6–7).
- [ ] The completion question of `post-deploy-checks.md` asked: does E3 change what must be true on the installed extension
      that a green build would hide? (R8, R10 and R11 are the manual answer for this release.)
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
