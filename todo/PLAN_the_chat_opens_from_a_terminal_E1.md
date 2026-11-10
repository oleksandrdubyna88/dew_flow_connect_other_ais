# PLAN — E1 stories: the terminal door

> Status: **plan only, nothing implemented yet, 2026-11-10.** Epic E1 of
> [PLAN_the_chat_opens_from_a_terminal.md](PLAN_the_chat_opens_from_a_terminal.md) (its `## Epics`, and its § *Stage 1*).
> Written by Opus when the epic started. A stories document keeps no change ledger: a change to this epic is an entry
> in the parent.
>
> Scope: `src_vs_code` only, plus `research/` and the help. Three stories. **Cornerstone: E1.S2** — the `terminal`
> conversation source, its key and identity rules, and the steal filter (area (b)). Fable writes its scenarios and
> brief in `todo/PLAN_the_chat_opens_from_a_terminal_E1_S2_spec.md`; that file does not exist yet. The parent's ledger
> line reads `Cornerstones: 1 of 2 — E1.S2`.

## 1. Goal, before any solution

The owner's Claude Code sessions now run as the CLI inside tmux on two VMs. Each one is open in VS Code as a
CredsForDevs terminal called `Creds: <host> / <session>`. In such a terminal, *Chat with other AIs* does not work:
Ctrl+Alt+A does nothing, the palette says *"Open this from a Claude Code session tab or from a file"*, and when the last
editor tab is a file the command silently takes that FILE's text (parent § *The symptom*, § *What the code does today*).

When E1 is done:

1. From any terminal, the title-bar button **Ask other AI** and Ctrl+Alt+A open a chat with the selected text. The
   chat is named after the terminal (`Creds: ` dropped from the start of the title only).
2. With no selection, the person sees *"Select text in the terminal first"* and nothing is sent.
3. No synthetic Ctrl+C ever reaches a terminal. The copy is `workbench.action.terminal.copySelection`, inside the
   clipboard borrow, and the clipboard is given back even when the copy command rejects.
4. The conversation is saved with a `terminal` source (the full terminal name). It has no agent mode. *Back to
   source* brings its terminal forward, or says why it cannot.
5. A Claude tab can never take a terminal's conversation, and the old chords never fire in a terminal.

The silent failures this epic must not ship: a stolen conversation (a Claude tab that re-keys a terminal's
conversation by label gives no error — the answer simply arrives in the wrong place), a SIGINT sent to ssh/tmux/claude,
and a clipboard left holding our marker.

## 2. What exists — verified at `ad5f783c`, 2026-11-10

The worktree is at `7a6bc01b`; `git diff ad5f783c HEAD -- src_vs_code` is empty, so every line below is the same at
both. All paths are under `src_vs_code/`.

| Fact | Where |
|---|---|
| The chat command; its `matchedSource` call and the `rekey`; the three `deliverPassage` callers | `src/chatCommand.ts:208`, `:232-246`; `:264`, `:549`, `:580` |
| `deliverPassage` takes a tab match, a `fromSession` boolean and a `sourceUri`, and builds the source from them | `src/chatCommand.ts:275-286`, `:327-337` (the rule is `:336`) |
| A new conversation's agent box, its origin, its pin | `src/chatCommand.ts:104`, `:151`, `:197` |
| The door recorder | `src/chatCommand.ts:412` `noteChatDoor`; called in `src/extension.ts:666-672` and the five handlers after it |
| The keyboard capture: the latch, the host clipboard, `passageFor` | `src/chatCapture.ts:103` `oneCapture`, `:75-80` `hostClipboard`, `:108` |
| One snapshot of the tabs; the source match over it | `src/chatCapture.ts:25` `snapshots`; `:174` `matchedSource`, `:181-184` |
| The borrow, inline in `captureSelection`; the restore rule | `src/selectionCapture.ts:207`, `:221-252`; `:196` `shouldRestore`; `:184` `Clipboard` |
| The label re-key: a known panel whose key is not among the TABS counts as orphaned | `src/sessionKey.ts:141` `sourceSession`, `:162-171` |
| The second place that asks the same question | `src/chatSessionJoin.ts:248` `askedForGoto`, `:253-255` |
| A restored conversation is keyed by a fresh `{}`, labelled with its title | `src/chatConversationRestore.ts:256` |
| The panel registry; `known`, `open`, `rekey` | `src/chatPanels.ts:118`, `:128`, `:168` |
| `ConversationSource` and its builders | `src/chatStore.ts:70`, `:87` `sourceOfSession`, `:90` `sourceOfFile` |
| Its validators | `src/chatStore.ts:244` `sourceFrom`, `:276` `agreeOnOrigin`, `:334` and `:383` (record and meta), `:458` `sameSource` |
| The save refuses a record its own reader would drop | `src/chatStoreFile.ts:531` |
| Back to source: the decision and its exhaustive switch; the executor and its exhaustive switch | `src/chatReturn.ts:20` `BackTo`, `:40` `recordedRoute`, `:69` `recoveredTab`; `src/chatReturnCommand.ts:45`, `:100` `sourceTab`, `:108` `go`, `:120`, `:130` `fromRecord` |
| Agent mode: the rule and every place that decides it | `src/chatAccessRules.ts:12` `agentOffered`, `:23` `accessOn`, `:28` `agentRefusal`; decided at `src/chatCommand.ts:104`, `src/chatShow.ts:110`, `src/chatConversationRestore.ts:51`, `src/chatAccess.ts:69`, `src/chatTurn.ts:191`, `src/chatLaunch.ts:298` |
| The doors | `src/chatDoors.ts:37` `Door`, `:40` `DOORS` |
| The chords and the palette | `package.json:1294-1310` (the `when` lines are `:1298`, `:1303`, `:1309`), `:1227-1240` `commandPalette`, `:1175` `view/title`, `:268-271` the command |
| The help article, five languages | `src/helpContent.ts:287`; `src/helpRu.ts:51`, `src/helpUk.ts:115`, `src/helpDe.ts:51`, `src/helpEs.ts:51` |
| Tests that pin TEXT this epic changes | `src/test/chatSourceWiring.test.ts:171` (the `:336` rule), `src/test/bugzReviewWiring.test.ts:482` (the `:580` call), `src/test/chatDoors.test.ts:87-88` (the door list), `src/test/chatAgentMode.test.ts:139-154`, `:274-275` (the agent rules) |
| The door census, derived from the manifest | `src/test/chatWiring.test.ts:555-585` |
| The help census: every command's title is in an article | `src/test/helpCoverage.test.ts:444-455` |
| The extension-host harness, and the CI job that runs it | `scripts/run-host.mjs`, `src/test/host/scenarios.ts:244`; `.github/workflows/ci.yml:592` (job `extension · a real editor`), `:596` `continue-on-error: true`, `:627` `xvfb-run -a npm run test:host` |
| Lint: `complexity` 4 and 50 lines per function on `src/**`; suppressions may only shrink, per file and rule | `eslint.config.mjs:113-115`; `.github/workflows/ci.yml:192` |
| The notification census | `notification-sites.json`, `scripts/count-notifications.mjs --write` |
| The changelog: no `[Unreleased]` section; the extension's prose is written in the release-please pull request | `release-please-config.json` `$order`; `CHANGELOG.md` sections `## Extension <version> — <date>` |

## 3. Conventions every story follows

- **Worktree.** `/home/jinx/git/wt/coai-chat-terminal`, branch `feat/chat-from-a-terminal`. Never the main checkout.
- **Models.** Spec (scenarios and brief): Opus for S1 and S3 (this document), Fable for S2. Code and tests: Sonnet.
  Review: Opus. Fixes: Sonnet.
- **RED-GREEN-RED.** Each scenario below names its assertion and its break-it: the production line to remove, and the
  failure message the test must then show. For new code the first RED is the test written before the code; the
  break-it is the RED that proves the test has teeth. Report both observations in the summary.
- **Every test file** starts with `import '../../scripts/inRunFolder.cjs';` (the run-folder guard).
- **No new host module.** A module that imports `vscode`, also through another module, must be listed in
  `sonarcloud.yml`, and `sonarExclusions.test.ts` checks that list is exact. Put host code in the host files that exist
  (`chatCapture.ts`, `chatCommand.ts`, `chatReturnCommand.ts`, `extension.ts`). `src/terminalSource.ts` stays pure.
- **Lint.** A new function keeps `complexity` ≤ 4 and ≤ 50 lines. The suppression counts in `eslint-suppressions.json`
  must not grow for any file (CI step *Lint suppressions only shrink*). Split, do not suppress.
- **A new `notify` call site** changes the census: run `node scripts/count-notifications.mjs --write` and commit
  `notification-sites.json`.
- **No webview page changes** in E1. If one becomes necessary, it is tested by running it (`bundledPage.test.ts`).

Commands (from `src_vs_code/`, the conventions mount is `../.agents/conventions`):

```bash
npm ci                                                        # once
npm run compile                                               # read the exit code: tsc emits even when red
node scripts/run-tests.mjs out/test/<name>.test.js            # one file, after compile
node ../.agents/conventions/tools/low-priority.mjs npm test   # the whole suite (compile + run-tests)
npm run lint                                                  # eslint src
node scripts/count-notifications.mjs --write                  # after any new notify call
npm run test:host                                             # CANNOT start on this machine (no libnss3) — see § 9
```

From the worktree root: `node .agents/conventions/tools/plan-lifecycle.mjs` before every commit that touches `todo/`.

## 4. Shared contracts — fixed here so S1 and S3 can be briefed while S2's spec is written

S2's spec may refine C2. If it changes a name or a signature below, it updates this section and S3's brief in the
same commit.

- **C1 — the borrow (S1 owns).** In `src/selectionCapture.ts`:
  `borrowClipboard<T>(clipboard: Clipboard, run: () => PromiseLike<T>): Promise<Borrowed<T>>`, with
  `Borrowed<T> = { readonly landed: boolean; readonly text: string; readonly outcome: T }`. `landed` is false when the
  clipboard still holds this borrow's marker after `run`. If `run` rejects, the borrow restores and then rejects with
  the same reason.
- **C2 — the terminal source (S2 owns).** In `src/chatStore.ts`: the kind `{ readonly kind: 'terminal'; readonly name:
  string }` and `sourceOfTerminal(name: string): ConversationSource` (empty → `none`). In `src/terminalSource.ts`:
  `terminalLabel(name: string): string` (the title) and `terminalMatch(known, terminal: object, name: string):
  SessionMatch` (`existing` when `known` holds that key, else `new`; never `rekey`). In `src/chatCommand.ts`:
  `deliverPassage(panels, extensionUri, ready, match, passage, send, fromSession, origin: ConversationSource, append?)`
  — `origin` replaces `sourceUri`.
- **C3 — the door (S3 owns).** `'terminal'` joins `Door` and `DOORS`; it is not an asking door.

## 5. Stories

Build order: S1, then S2, then S3, one coder at a time on the one branch. Fable may write S2's spec while S1 is coded.

### E1.S1 — The capture: a terminal's selection through the borrow, never a keystroke

`model: opus` (spec, this section) · coder: sonnet · review: opus · area (a).

- **Goal.** A pure, tested way to copy the active terminal's selection: `copySelection` inside the existing clipboard
  borrow, the restore in `finally`, the marker meaning *no selection*, and one latch shared with the keyboard capture.
- **Files and functions.**
  - `src/selectionCapture.ts` — extract `borrowClipboard` (C1) from `captureSelection` (`:221-252`). `captureSelection`
    keeps its Windows reach check (`:212-219`) and its sentences, and calls `borrowClipboard(clipboard, run)`.
  - `src/terminalSource.ts` — NEW, pure (no `vscode`): `TERMINAL_COPY = 'workbench.action.terminal.copySelection'`;
    `NO_TERMINAL_SELECTION = 'Select text in the terminal first.'` (the owner's words, decision 3);
    `terminalPassage(borrowed: Borrowed<unknown>): Capture`; `captureTerminalSelection(execute: (command: string) =>
    PromiseLike<unknown>, clipboard: Clipboard): Promise<Capture>`.
  - `src/chatCapture.ts` — host: `fromTheTerminal(): Promise<Capture>` runs
    `captureTerminalSelection((id) => vscode.commands.executeCommand(id), hostClipboard)` through the SAME `oneCapture`
    latch (`:103`) under a `withProgress` *"Copying the terminal selection…"*.
- **Brief (what and how).**
  1. `borrowClipboard`: read `borrowed`; `restorable = borrowed.length > 0`; `marker = restorable ? markerFor() : ''`;
     write the marker only when restorable. Then `try { outcome = await run(); captured = await clipboard.read(); }
     finally { if (restorable && shouldRestore(await clipboard.read(), captured ?? marker)) await
     clipboard.write(borrowed); }`. Return `{ landed: captured !== marker, text: landed ? captured : '', outcome }`.
     Keep the read order *borrow, landed, current*: `chatCapture.test.ts:126` counts reads in that order. Do not change
     `shouldRestore` or `markerFor`.
  2. `captureSelection(run, clipboard, reach)`: unchanged contract. After the reach check: `const b = await
     borrowClipboard(clipboard, run); return b.landed ? { text: b.text, failure: '' } : { text: '', failure:
     refusalFor(reach, b.outcome) };`. Every test in `chatCapture.test.ts` stays green with NO edit to that file.
  3. `captureTerminalSelection`: `borrowClipboard(clipboard, () => execute(TERMINAL_COPY))`, then `terminalPassage`. A
     rejection becomes `{ text: '', failure: 'The terminal selection could not be copied: <reason>' }` (the reason as
     `reason instanceof Error ? reason.message : String(reason)`). It never calls anything else: no PowerShell, no
     `launch`, no reach check — the terminal path needs no Windows side.
  4. `terminalPassage`: not landed, or landed text that is only whitespace → `{ text: '', failure:
     NO_TERMINAL_SELECTION }`; else `{ text, failure: '' }`. The text is not trimmed (the selection is the passage).
  5. `fromTheTerminal` in `chatCapture.ts`: ONE latch for both captures, because both borrow the one clipboard. A second
     press while either capture runs gets the existing refusal *"That selection is still being copied — the second
     press was ignored."*.
  6. If the host scenario (S1-H1) shows the marker still on the clipboard right after the awaited command while a
     selection exists, add a bounded re-read (≤ 500 ms, 50 ms steps) inside `captureTerminalSelection` only, never in
     `borrowClipboard`, and record the measurement in `research/module_extension.md`. Do not add it before that
     evidence.
- **Scenarios** (new file `src/test/terminalCapture.test.ts`, fakes like `fakeClipboard` in `chatCapture.test.ts:30`).

| # | Name | Assertion | Break-it → the RED it must show |
|---|---|---|---|
| S1-T1 | a rejected copy still gives the clipboard back | `run` rejects; `borrowClipboard` rejects with that reason; the clipboard holds the original; writes are `[marker, original]` | move the restore out of `finally` (after `await run()`) → `a rejected copy left the marker on the clipboard` |
| S1-T2 | a copy that wrote nothing is "nothing landed" and the clipboard comes back | `landed` false, `text` `''`, held = original | compare `captured` with `borrowed` instead of `marker` → `landed` true with the marker as text |
| S1-T3 | a copied selection comes back and the clipboard is restored | `landed` true, the text, held = original | drop the restore → `the clipboard was taken, not borrowed` |
| S1-T4 | a clipboard somebody changed during the copy is left alone | reads: borrow, landed, then a third value → no write after the marker | drop `shouldRestore` in `finally` → `the newer clipboard was overwritten by the restore` |
| S1-T5 | an empty clipboard is never written | `fakeClipboard('')`, copy writes nothing → writes `[]`; copy writes text → held = text | write the marker even when not restorable → `an unreadable clipboard was overwritten` |
| S1-T6 | the terminal capture runs exactly one command, the copy | `execute` sees `['workbench.action.terminal.copySelection']`, nothing else | call `execute` twice or with another id → the list differs |
| S1-T7 | no selection gives the owner's sentence | marker back → failure `Select text in the terminal first.`; whitespace-only text → the same | remove the whitespace check → a passage of `'  \n'` is returned |
| S1-T8 | a copy command that rejects says so, and not "select text" | `execute` rejects `new Error('no terminal')` → failure names `no terminal`, clipboard restored | map every failure to `NO_TERMINAL_SELECTION` → the sentence lies about the cause |
| S1-T9 | the terminal path presses no key and shares the keyboard latch | in `chatCapture.ts`, the body of `fromTheTerminal` contains `oneCapture(` and `captureTerminalSelection(` and none of `pressCopy`, `COPY_SCRIPT`, `powershell`; companion: the body of `passageFor` still contains `pressCopy` (the scan is alive) | give `fromTheTerminal` its own `oneAtATime` → red; call `pressCopy` → red |
| S1-T10 | every existing capture test is unchanged | `chatCapture.test.ts` green with no edit | — (it is the regression net for the extraction) |

- **Host scenario S1-H1** (new `src/test/host/terminalCaptureScenario.ts`, added to `SCENARIOS` in `scenarios.ts`):
  create a terminal named `Creds: h / s`; `sendText('echo coai-probe-<random>')`; within 10 s, poll `selectAll` +
  `fromTheTerminal()` until the text holds the probe; assert the clipboard equals the sentinel put there first. Then
  `workbench.action.terminal.clearSelection`; `fromTheTerminal()` → failure `Select text in the terminal first.`,
  clipboard still the sentinel. Dispose the terminal in `finally`. It runs only in CI (§ 9).
- **Docs.** `research/module_extension.md`: one paragraph under a new section *The chat opens from a terminal (E1)* —
  the borrow extracted, the restore in `finally`, the one latch, why no keystroke. `research/module_tests.md`: extend
  the row *Chat with other AI — the trigger* (`:297`) with the terminal capture and its tests.
- **Done when.** S1-T1…T10 green; each break-it seen red with its message, then restored; `npm run compile` exit 0;
  the whole suite green; lint suppressions unchanged.
- **Size.** About 260 lines (≈ 70 code, ≈ 190 tests).

### E1.S2 — The terminal source and its identity (CORNERSTONE)

`model: fable` — the scenarios and the brief are Fable's, in `todo/PLAN_the_chat_opens_from_a_terminal_E1_S2_spec.md`.
Coder: sonnet · review: opus · area (b). Why this story and not another: the record is on disk from the first release,
E2 and E3 read it as it is, and a wrong identity or a stolen conversation gives no error. Area (a) has loud failures
that S1's tests and R1/R3 catch before E2; area (c)'s first part is a small rule the types enforce (S3).

- **Goal.** A terminal conversation has a durable identity — the FULL terminal name — and a live key — the
  `vscode.Terminal` object. Every reader of a conversation's source handles the new kind. A Claude tab can never
  re-key a terminal's conversation. *Back to source* brings the terminal forward, or refuses with a sentence.
- **Scope.** `src/chatStore.ts` (the kind, `sourceOfTerminal`, `sourceFrom`, `agreeOnOrigin`, `sameSource`, the record
  and meta readers); `src/terminalSource.ts` (`terminalLabel`, `terminalMatch`, `oneLiveNamed`, `tabSourced`);
  `src/chatSource.ts` (`knownFolder`); `src/chatCapture.ts` and `src/chatSessionJoin.ts` (the filter where `known` is
  read); `src/chatCommand.ts` (`deliverPassage` takes `origin`, its three callers, `fromSession` false for a terminal);
  `src/chatReturn.ts` and `src/chatReturnCommand.ts` (the `terminal` road back); the two pinned regexes
  (`chatSourceWiring.test.ts:171`, `bugzReviewWiring.test.ts:482`) rewritten to the new call shape, not deleted; the
  comments at `src/chatGoto.ts:44` and `:235`, which say a terminal cannot own a conversation.
- **Not in scope.** The door, the manifest, the agent rule (S3); the capture (S1); go-to from a terminal and re-attach
  (E3); `fromSession` true for `Creds:` terminals (E2).
- **Areas the spec must cover.**
  1. **The kind.** Its shape; the name as identity (full, unstripped) against the label as display; empty, too long
     (the parent says ≤ 500 characters), control characters and newlines in a name; exact or case-blind compare.
  2. **Every reader** in § 6, each with its required behaviour and a test. The exhaustive switches
     (`knownFolder`, `recordedRoute`, `fromRecord`) get a `terminal` arm, never a default that hides it.
  3. **Disk.** The round trip through `recordFrom` and `metaFrom`; `agreeOnOrigin` accepts either `fromSession`
     (an E2 record read by an E1 build after a rollback); the save self-check at `chatStoreFile.ts:531`; whether
     `CONVERSATION_VERSION` stays 1. **What an older build (0.85.0) does with a `terminal` record**: `sourceFrom` returns
     `undefined`, so the record is kept on disk but hidden, and its index entry is logged as torn on every listing
     (`chatStoreFile.ts:704-706`). The spec decides whether that is acceptable and says it in the release note text.
  4. **The live key.** The panel key is the `Terminal` object. The door never re-keys by label (`terminalMatch` never
     answers `rekey`). The same terminal → the same conversation; a new terminal of the same name → a new conversation
     (E1 has no re-attach); two live terminals of one name → two conversations with equal sources.
  5. **The steal filter.** `tabSourced(known, sourceOf)` removes every panel whose THREAD source is `terminal` from what
     `sourceSession` may re-key, at both sites (`chatCapture.ts:181-184`, `chatSessionJoin.ts:253-255`). It must judge
     by the durable source and not by the key's type: a restored terminal conversation is keyed by a plain `{}`
     (`chatConversationRestore.ts:256`). The steal test goes RED without the filter (parent § *Test plan*).
  6. **The other doors into a terminal conversation.** Go-to's name fallback (`chatGoto.ts:273` `sameName`) can list a
     terminal conversation for an ambiguous Claude tab; binding is refused by `bindable` because that tab's source is
     `none`, and a live one is only revealed (`chatGotoCommand.ts:208`, `chatRegistry.ts:107-112`). The spec states it
     and pins it with a test.
  7. **`deliverPassage`.** `origin: ConversationSource` replaces `sourceUri`; ONE function keeps the tab doors' rule
     (`fromSession ? none : sourceOfFile(uri)`) so the two Claude/file callers do not each carry a copy; the bug door
     passes `sourceOfFile(fileUriOf(chat))`. `fromSession` is `false` for every terminal conversation, so `pinSession`
     (`chatSessionJoin.ts:185`) never walks the local home for it.
  8. **Back to source.** `BackTo` gains `{ kind: 'terminal'; name }`; `recordedRoute` returns it. The executor shows
     the live keyed terminal if it is still in `window.terminals` and has not exited, else `oneLiveNamed` over
     `window.terminals` without exited ones: one → `show()`; none → *"The terminal “X” is not open."*; several →
     *"N terminals are called “X” — open the one you mean."* (`X` is the full name). `sourceTab` (`chatReturnCommand.ts:100`)
     finds no tab for a terminal key, so the decision is the recorded road. `recoveredTab` returns nothing for it.
     A host scenario proves `show()` makes it the active terminal.
  9. **Break-its.** At least: the filter removed (steal test red); the `terminal` arm of `sameSource` removed; the live
     keyed terminal skipped (a namesake is shown instead); `oneLiveNamed` guessing among several.
- **Done when.** The spec's tests and break-its; `npm run compile` exit 0; the whole suite green; the two rewritten
  wiring regexes pass; lint suppressions unchanged.
- **Size (Opus's estimate, the spec decides).** About 520 lines (≈ 170 code, ≈ 350 tests).

### E1.S3 — The door: the button, the chord, the skip list, agent mode off, the help

`model: opus` (spec, this section) · coder: sonnet · review: opus · areas (c) first part, and the trigger.

- **Goal.** The person can use the door: an icon button and Ctrl+Alt+A in any terminal open a chat from the selection,
  through S1's capture and S2's source. The old chords and the palette stop competing with it in a terminal. A terminal
  conversation is never offered agent mode, at every place that decides it. The door is counted and described.
- **Files and functions.**
  - `package.json`:
    - `contributes.commands`: `{ "command": "coai.askFromTerminal", "title": "Ask other AI", "category":
      "ConnectOtherAIs", "icon": "$(comment-discussion)" }`.
    - `menus.view/title`: `{ "command": "coai.askFromTerminal", "when": "view == terminal", "group": "navigation@90" }`.
    - `keybindings`: `{ "command": "coai.askFromTerminal", "key": "ctrl+alt+a", "when": "terminalFocus" }`.
    - The three existing chords gain `&& !terminalFocus`: both `coai.chatWithOtherAi` rows (`:1298`, `:1303`) and
      `coai.goToConversation` (`:1309`, written `(activeWebviewPanelId == 'claudeVSCodePanel' || editorTextFocus) &&
      !terminalFocus`).
    - `menus.commandPalette`: `{ "command": "coai.chatWithOtherAi", "when": "!terminalFocus" }`.
    - `contributes.configurationDefaults`: `{ "terminal.integrated.commandsToSkipShell": ["coai.askFromTerminal"] }`.
  - `src/chatDoors.ts` — `'terminal'` in `Door` and at the end of `DOORS` (C3); update the doc comment's count.
  - `src/chatAccessRules.ts` — `agentRuledOutBy(source: ConversationSource): boolean`, an exhaustive switch (`terminal`
    → true; `claude`, `file`, `none` → false; `never` default). `agentOffered(remote, workspace, source)` and
    `accessOn(remote, asked, workspace, source)` gain the REQUIRED source parameter, so the compiler lists every site.
    New `TERMINAL_NO_AGENT = 'Agent mode is not offered for a terminal conversation: the session it came from may be on
    another machine.'` and `agentRefusalFor(remote, workspace, source)` (terminal → that sentence, else
    `agentRefusal(remote, workspace)`). `agentRefusal` itself keeps its signature: `cliChatLaunch.ts:234` uses it at
    launch, where no source is known, and a terminal conversation never reaches a launch in agent mode because
    `accessOn` and the box refuse first.
  - The six decision sites pass the source: `chatCommand.ts:104` (`state.source`), `chatShow.ts:110`,
    `chatTurn.ts:191`, `chatLaunch.ts:298` (`thread.source`), `chatConversationRestore.ts:51` (`saved.source`);
    `chatAccess.ts:69` calls `agentRefusalFor`.
  - `src/chatCommand.ts` — `export async function askFromTerminal(panels, extensionUri, terminal =
    vscode.window.activeTerminal)`, in small helpers (complexity ≤ 4):
    1. `readyForChat()`; not ready → `notReady` (as every door).
    2. No terminal → warning, code `no-terminal-here`, *"There is no terminal here — open one and select text in it
       first."*
    3. `const passage = await fromTheTerminal()` (S1). Empty text → warning, code `nothing-selected-in-terminal`,
       title `passage.failure` (the owner's sentence, or the copy failure).
    4. `deliverPassage(panels, extensionUri, ready, terminalMatch(panels.known(), terminal, terminal.name), passage,
       sendsImmediately(settings.autoSend, false), false, sourceOfTerminal(terminal.name))` (`sendsImmediately` from `chatSettings.ts`). The send rule is the
       chord's: the button and the chord both follow **Who presses send** (`coai.chatAutoSend`).
    It never calls `matchedSource`, `snapshots` or `sourceSession`: the file-text defect cannot happen on this door.
  - `src/extension.ts` — after `coai.chatWithOtherAi` (`:666-672`): `registerCommand('coai.askFromTerminal', () => {
    noteChatDoor('terminal'); askFromTerminal(chatPanels, context.extensionUri).catch(...) })`, the catch shaped like
    `coai.takeTheQuestion`'s (`:690-711`): console detail, a `failure` notify, code `terminal-door-failed`. The door
    census (`chatWiring.test.ts:555-585`) then finds the chord's command and requires exactly this order.
  - Help, five languages, in the `chat-with-other-ai` article (`helpContent.ts:287`, `helpRu.ts:51`, `helpUk.ts:115`,
    `helpDe.ts:51`, `helpEs.ts:51`): ONE paragraph that names **Ask other AI** in English in every language — the icon
    in the terminal panel's title bar and Ctrl+Alt+A in a terminal; the selection is copied through the clipboard and
    the clipboard is given back; no selection → the sentence; the chat is named after the terminal; no agent mode; a
    person's own `terminal.integrated.commandsToSkipShell` hides the default, and adding `coai.askFromTerminal` to it
    brings the chord back. And in the existing back-to-source paragraph, *"or the terminal"* beside the Claude tab and
    the editor.
- **Scenarios.**

| # | Name (file) | Assertion | Break-it → the RED it must show |
|---|---|---|---|
| S3-T1 | the terminal door is declared with its icon (`terminalDoorWiring.test.ts`, new) | the command row: title `Ask other AI`, icon `$(comment-discussion)` | remove the icon → `the title-bar button has no icon` |
| S3-T2 | the button sits in the terminal panel's title bar | one `view/title` row: `when` `view == terminal`, `group` `navigation@90` | change `when` → red naming the row |
| S3-T3 | the chord is Ctrl+Alt+A under terminal focus | one keybinding: `ctrl+alt+a`, `when` `terminalFocus` | remove it → `the chord does nothing in a terminal` |
| S3-T4 | every command bound under terminal focus is in the skip-list default | DERIVED: each keybinding whose `when` requires `terminalFocus` (not `!terminalFocus`) names a command that is in `configurationDefaults["terminal.integrated.commandsToSkipShell"]`; companion: the derivation finds `coai.askFromTerminal` | remove the id from the default → `coai.askFromTerminal is bound in a terminal but not in the skip list — the chord goes to the shell` |
| S3-T5 | no older chord can win a chord typed into a terminal | every other binding with key `ctrl+alt+a` has `!terminalFocus`; the `coai.goToConversation` binding has `!terminalFocus` | drop it from the Claude-panel row → red naming that row (R2's static half) |
| S3-T6 | the palette does not offer the file door while a terminal has focus | a `commandPalette` row for `coai.chatWithOtherAi` with `when` `!terminalFocus` | remove the row → red |
| S3-T7 | the terminal is a door, and not an asking door (`chatDoors.test.ts`) | `DOORS` holds `terminal`; `asking('terminal')` false; update the list at `:88` | — (the list is data; the census below is the teeth) |
| S3-T8 | the door records itself first (existing `chatWiring.test.ts:555-585`) | green with the new handler | move `noteChatDoor('terminal')` after `askFromTerminal(` → `records the invocation after the work` |
| S3-T9 | the terminal door never reads the tabs (`terminalDoorWiring.test.ts`) | the body of `askFromTerminal` contains none of `matchedSource(`, `snapshots(`, `sourceSession(`; companion: the body of `chatWithOtherAi` contains `matchedSource(` | call `matchedSource` in it → red |
| S3-T10 | a terminal conversation is never offered agent mode (`chatAgentMode.test.ts`) | `agentOffered(false, 'D:/work', sourceOfTerminal('Creds: h / s'))` false; the same with `sourceOfFile(...)` true | remove the `terminal` arm → red |
| S3-T11 | agent mode cannot be switched on or carried into a terminal conversation | `accessOn(false, 'agent', 'D:/work', terminal)` → `text`; `agentRefusalFor(false, 'D:/work', terminal)` = `TERMINAL_NO_AGENT` | make `agentRefusalFor` skip the source → `''` |
| S3-T12 | every language's chat article names Ask other AI (`helpCoverage.test.ts`, beside `:26`) | for each of the five languages, not a fallback, contains `**Ask other AI**`; the census at `:444-455` passes by the title | remove it from `helpUk.ts` → `the uk chat article never names Ask other AI` |

  The existing tests at `chatAgentMode.test.ts:139-154` and `:274-275` gain the source argument (`{ kind: 'none' }`
  keeps their meaning); none of their assertions change.
- **Host scenario S3-H1** (in `terminalCaptureScenario.ts` or its own file): `vscode.workspace.getConfiguration()
  .inspect('terminal.integrated.commandsToSkipShell')?.defaultValue` includes `coai.askFromTerminal`. If this is red in
  CI, the `configurationDefaults` route does not work for this setting: stop and report it to the owner, do not work
  around it (ledger entry 1.1 already says what happens if R1 fails).
- **Docs.** `research/module_extension.md`: the section *The chat opens from a terminal (E1)* — the door, the chords,
  the skip list and its limit, agent mode off, the door count. `research/module_tests.md`: one row *Chat with other
  AI — the terminal door* (the manifest tests, the agent rule, the help, S1-H1/S3-H1, and **not covered**: the
  keystroke itself, the button click and the palette's view of `terminalFocus` — R1, R3, R9 on the VSIX); and the
  doors row (`:300`) gains `terminal`. `research/architecture.md` (`:604-613`, the roads back): one sentence — a
  terminal comes forward through VS Code's own `Terminal.show()`, no other extension is called. No module boundary
  moves in E1; the ssh edge is E2's. No `CHANGELOG.md` edit in this pull request: the extension's prose goes into the
  release-please pull request (§ 2).
- **Done when.** S3-T1…T12 green, each break-it seen red; the door census and the help census green; the six agent
  sites compile only with a source; `notification-sites.json` regenerated; the whole suite and lint green.
- **Size.** About 330 lines (≈ 110 manifest and code, ≈ 60 help in five languages, ≈ 160 tests).

## 6. Every reader of `ConversationSource` — the list S2's spec must cover

Found by `grep -rn "ConversationSource\|source\.kind\|sameSource\|sourceFrom\|sourceOfFile\|sourceOfSession\|followable(\|originOf(\|reorigin("`
over `src/` (tests excluded), then each hit opened. `chatOrphans.ts`, `chatLedger.ts` and `chatStoreSweep.ts` were
checked and read no source.

| Reader | Where | What a `terminal` source must do there |
|---|---|---|
| The type and its builders | `chatStore.ts:70`, `:87`, `:90` | the new kind and `sourceOfTerminal` |
| `sourceFrom` (record and meta) | `chatStore.ts:244`, used at `:334`, `:383` | accept a valid name, refuse a bad one |
| `agreeOnOrigin` | `chatStore.ts:276`, used at `:338` | either `fromSession` |
| `sameSource` | `chatStore.ts:458` | equal names; never equal to another kind |
| The save self-check | `chatStoreFile.ts:531` | a terminal record must pass its own reader |
| `refile` (a renamed FILE) | `chatStoreFile.ts:469-491` | never matches a terminal (via `sameSource`) |
| `bySource` (go-to's candidates) | `chatStoreCache.ts:153` | via `sameSource` |
| `knownFolder` (exhaustive) | `chatSource.ts:95` | `''` — filed under the first root |
| `followable` | `chatSource.ts:284` | false (only `file:`) |
| `filedFor`, `originOf`, `reorigin` | `chatRoots.ts:77`, `:89`, `:94` | through `knownFolder` |
| Rename follow, live and on disk | `chatFollow.ts:58`, `:85` | skipped by `followable` |
| The thread's field | `chatThread.ts:284` | holds it |
| The record writer | `chatPersist.ts:69` | writes it |
| A new conversation | `chatCommand.ts:76`, `:104`, `:151`, `:336` | `origin` from the door |
| The restore's re-pin | `chatConversationRestore.ts:265` | never pins (`fromSession` false) |
| The pin and the by-id lookup | `chatSessionJoin.ts:122`, `:163` | never reached for a terminal |
| Go-to's tab source | `chatSessionJoin.ts:274-284` | unchanged in E1 (a terminal is not a tab) |
| Go-to's decision and bind check | `chatGoto.ts:121`, `:253`, `:305`, `:373` `sameName`, `:433-437` `bindable` | no bind by name (area 6) |
| Back to source: the decision | `chatReturn.ts:17`, `:40` (exhaustive), `:69` | the `terminal` road; no tab |
| Back to source: the executor | `chatReturnCommand.ts:53-54`, `:100`, `:108`, `:120`, `:130` (exhaustive) | `show()` or a refusal |
| Agent mode (S3, not S2) | `chatCommand.ts:104`, `chatShow.ts:110`, `chatConversationRestore.ts:51`, `chatAccess.ts:69`, `chatTurn.ts:191`, `chatLaunch.ts:298` | off |
| Where `known` is re-keyed by label | `chatCapture.ts:181-184`, `chatSessionJoin.ts:253-255` (`sessionKey.ts:162-171`) | filtered out (area 5) |
| A live conversation moved onto a tab | `chatRegistry.ts:107-112` `revealBound` | only from go-to, never for a terminal source |

## 7. Build order

1. **S1** — spec here; Sonnet codes; Opus reviews; Sonnet fixes. Meanwhile Fable writes the S2 spec.
2. **S2** — from the Fable spec; Sonnet codes; Opus reviews; Sonnet fixes.
3. **S3** — spec here; Sonnet codes; Opus reviews; Sonnet fixes.
4. One pull request for the epic. The code round of the review gate gets the epic's scope (this document's § 1 and
   § 9) as `planText`. Every CI check green, and the `extension · a real editor` job read by its own result (§ 9).
5. Merge. Build the VSIX from the merged head (`npm run package`). The owner installs it and does R1–R5 and R9
   (parent ledger entry 1.1 and E1 *Done when*). Each result goes into the parent's R-table. A failed check is fixed
   before the tag.
6. Release the extension: the release-please pull request carries the `## Extension <version> — <date>` prose; the tag
   is pushed alone and its run observed. E2 does not start before that.

## 8. Test plan — the parent's rows this epic owns

| Parent row (§ *Test plan*, Pure and Host) | Story | Tests |
|---|---|---|
| `terminalSource`: label, passage, `oneLiveNamed` 0/1/2, the steal (RED without the filter) | S1 (passage), S2 (the rest) | S1-T7, S1-T8; the S2 spec |
| `borrowClipboard`: restore, no restore when changed, empty never overwritten, marker = nothing, a rejecting run restores; `captureSelection` green | S1 | S1-T1…T5, S1-T10 |
| `chatStore`: round trip, bad name refused, `sameSource`, `agreeOnOrigin` | S2 | the S2 spec |
| `chatReturn`: live and recorded terminal routes | S2 | the S2 spec |
| The agent rule | S3 | S3-T10, S3-T11 |
| Manifest: the button, the chord, the skip list, no old chord under `terminalFocus`, the door counted | S3 | S3-T1…T8 |
| Host: a `Creds: h / s` terminal, `selectAll`, capture and restore; `clearSelection` → the sentence; the skip-list default | S1, S3 | S1-H1, S3-H1 |

## 9. Where each proof runs

- **Unit and wiring tests**: `npm test`, here and in CI (`ci.yml:575`, required).
- **Host scenarios** (S1-H1, S3-H1, and S2's back-to-source scenario): `npm run test:host` CANNOT run on this machine —
  a real VS Code does not start here (no `libnss3`). They run only in CI's job `extension · a real editor`
  (`ci.yml:592`, `xvfb-run -a npm run test:host` at `:627`). That job has `continue-on-error: true` (`:596`), so a red
  scenario does NOT turn the pull request red. Read the job's own conclusion and log on the head commit, and write
  that result (with the sha) in the pull request description. A red host scenario blocks the merge as if it were
  required.
- **Real window** (parent R-table): the owner, on the VSIX built from the merged head — R1 (no keystroke reaches the
  shell, with `cat -v` and with the Claude TUI), R2 (a Claude tab active, terminal focused: the terminal door wins), R3
  (the button after a click moved focus; split terminals; clipboard restored; no selection), R4 (the name stays
  `Creds: host / session` under title sequences), R5 (back to source in the panel and in the editor area), R9 (the
  palette hides `coai.chatWithOtherAi` while a terminal has focus).

## 10. Growth and security

- **Growth.** None new. A terminal conversation is one record in the existing `chat-conversations/` store, under its
  existing ninety-day sweep. The panel registry holds the `Terminal` object as a key until the panel closes, as it
  holds a `Tab` today.
- **Security.** No new outward edge in E1. The clipboard is borrowed and given back, never kept; only text survives the
  borrow (an image is lost — already true of the Claude-tab door). No keystroke is synthesised for a terminal.

## 11. What E1 does not do, and open questions for the owner

Not in E1: reading a Claude session over ssh (E2); `fromSession` true for `Creds:` terminals (E2); re-attach after a
reload, go-to from a terminal, the skip-list repair (E3).

Open, recorded here and NOT built (each would be a plan change):

1. **`coai.switchConversations`** has an unscoped chord (`ctrl+shift+alt+g`), and `conversationPickerWiring.test.ts`
   says it reaches the picker *"from ANYWHERE … a terminal"*. A terminal sends a chord to the shell unless its command
   is in `commandsToSkipShell`, so in a terminal that chord most likely does not reach the picker today. S3-T4 derives
   its list from `when` clauses that name `terminalFocus`, so it does not require this id. Should it join the skip-list
   default? (Not measured.)
2. **The other palette entries** — `coai.chatNow`, `coai.chatChoose`, `coai.takeTheQuestion`, `coai.addTheQuestion` —
   run from the palette with a terminal focused still read the last file tab or the local session. The plan names only
   `coai.chatWithOtherAi`.
3. **`coai.backToSource`'s chord** (`ctrl+alt+g`, `activeWebviewPanelId == 'coaiChat'`) has no `!terminalFocus`. It
   does not matter in E1; when E3 binds Ctrl+Alt+G on `terminalFocus`, it must add `!terminalFocus` here too, or the
   two chords compete as R2's do.

## 12. Definition of Done — epic E1

- [ ] S1, S2 and S3 coded to their specs; every scenario green; every break-it observed red, then green, and both
      observations in the summary.
- [ ] `npm run compile` exit 0; `npm test` green; `npm run lint` green; no suppression count grew.
- [ ] `notification-sites.json` regenerated; the door census and the help census green.
- [ ] The host scenarios green in CI's `extension · a real editor` job on the head commit, read from that job, named in
      the pull request.
- [ ] `research/module_extension.md`, `research/module_tests.md` and `research/architecture.md` updated; the help in
      five languages.
- [ ] The pull request merged; the owner's R1–R5 and R9 recorded in the parent's R-table from the VSIX of the merged
      head.
- [ ] The extension release cut from `main`: its changelog prose in the release-please pull request, the tag pushed
      alone, its run observed.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
