# PLAN — the chat opens from a terminal

> Status: **plan only, nothing implemented yet, 2026-11-10 — plan gate round 1 `proceed` (codex, antigravity;
> 6 findings: 4 accepted and folded into this approved text, 2 rejected with reasons).** Scope: `src_vs_code`
> only — a terminal door for *Chat with other AIs*, a `terminal` conversation source, and (stage 2) a
> read-only ssh reader for a Claude Code session that runs inside tmux on another machine. No change to
> `src_mcp`, `src_server` or CredsForDevs. Three epics; one extension release per epic.

Related: [../research/PLAN_chat_with_other_ais.md](../research/PLAN_chat_with_other_ais.md) (the feature),
[../research/PLAN_the_chat_opens_anywhere.md](../research/PLAN_the_chat_opens_anywhere.md) (the file door and
reading Claude's session files), [../research/PLAN_a_conversation_survives_a_reload.md](../research/PLAN_a_conversation_survives_a_reload.md),
[../research/PLAN_the_chat_goes_back_where_it_came_from.md](../research/PLAN_the_chat_goes_back_where_it_came_from.md).

## Plan changes

> Epoch 1 · approved 2026-11-10 · by reviewers · state: open

- 1.1 · 2026-11-10 · agent · build-order, merge-conditions — each epic's real-window checks run on a pre-release VSIX the owner installs, before its release is tagged
  (cadence consult ea91bb56, codex: a door whose prerequisites are unproven must not ship. No real VS Code can
  start on this machine, so the owner runs R1–R5 and R9 for E1, R6–R7 for E2, R8, R10 and R11 for E3 on the
  VSIX built from the epic's merged head; a failed check is fixed before the tag. If R1 fails only under a
  user-level skip list, the skip-shell repair of E3 is pulled into E1 under this same entry.
  The same consult's two other points satisfy the plan as written and go to the E2 stories document, not here:
  an E1 `Creds:` conversation restored under E2 gains *Asked* (its `fromSession` is derived again from the
  source on restore and reuse), and a tmux session with several panes or several `claude` descendants is
  ambiguity, so it gives a list or a refusal, never a pick.)

## The symptom

The owner, 2026-11-10: six or more Claude Code sessions now run as the **CLI** on two Linux VMs, several
Linux users per VM, inside tmux so they work for days. Each is opened from the local Windows VS Code window
as a CredsForDevs *Terminal* entry, whose command is (one real entry, shape kept):

```
$h="ai1-dev2"; $s="access-slack-integration"; ssh -t "$h" "tmux has-session -t '$s' 2>/dev/null ||
 (tmux new-session -d -s '$s' -c '/home/git/knowledge_base' && sleep 1 &&
  tmux send-keys -t '$s' 'cd /home/git/knowledge_base && claude' C-m && sleep 4 &&
  tmux send-keys -t '$s' -l '/rename $s' && sleep 1 && tmux send-keys -t '$s' C-m);
 tmux attach-session -t '$s'"
```

The terminal is titled `Creds: ai1-dev2 / access-slack-integration` (CredsForDevs,
`dew_flow_creds_for_devs · src_vs_code/src/terminalTitles.ts:44-48`). Gate cards still reach VS Code.
**Chat with other AIs does not**: selecting text in that terminal and pressing Ctrl+Alt+A does nothing, and the
command palette says *"Open this from a Claude Code session tab or from a file"*. The owner lost the
second opinion they used several times an hour.

## Owner decisions (2026-11-10, final)

1. **Trigger:** an icon button in the terminal panel's title bar, tooltip **"Ask other AI"**, shown whenever
   the extension is installed; and **Ctrl+Alt+A** while a terminal has focus. (The owner's right-click in the
   terminal is copy/paste, so no context-menu door.)
2. **Any terminal** is a source. The panel title is the terminal name without a leading `Creds: `
   (`ai1-dev3 / small-tasks`); other terminals keep their full name.
3. **No selection** → the warning *"Select text in the terminal first"*, nothing sent.
4. **Back to source** brings that terminal forward (`terminal.show()`), on request only — nothing switches
   by itself.
5. For a `Creds: <host> / <session>` terminal the extension **reads the remote Claude session over plain ssh**
   (checked by the owner: `ssh ai1-dev2 echo ok` answers from Windows PowerShell with no prompt; each folder
   — `ai1-dev2`, `ai1-dev3`, `ai1-root` — is a different Linux user with its own `~/.claude`), so *take the
   question*, *add the question* and *Asked* work. **The AI runs locally on Windows**, as today. Other
   terminals: selection only.
6. **After a reload or restart**, a conversation re-attaches to the terminal with the same name, **only for
   `Creds: …` terminals** and only when it is unambiguous.
7. **Order:** a minimal version first, released and checked in a real window; then the ssh reading; then
   the polish.
8. The owner's VM tmux has `mouse off` (`ssh ai1-dev2 tmux show -g mouse` → `mouse off`, 2026-11-10), so a
   mouse drag is an xterm selection, which VS Code can copy.

The question consultants were asked before this plan (consult `f7a90c66…`, 2026-11-10; codex and gemini
answered, one row refused for its folder grant). What was taken from them is marked *(consultants)* below.

## What the code does today — verified on `ad5f783c`

All paths under `src_vs_code/`.

- **The trigger.** `coai.chatWithOtherAi` (`src/chatCommand.ts:208`). Keybindings `ctrl+alt+a` only when
  `activeWebviewPanelId == 'claudeVSCodePanel'` or `editorTextFocus` (`package.json:1297`, `:1302`);
  `coai.goToConversation` likewise (`:1307-1309`). No `terminalFocus` anywhere in the extension.
- **The source.** `matchedSource` (`src/chatCapture.ts:174`) snapshots `vscode.window.tabGroups` and asks
  `sourceSession` (`src/sessionKey.ts:141`): a Claude panel tab, then a `file`/`untitled` tab. A terminal is
  not a tab, so the command refuses with `no-source-tab` (`chatCommand.ts:233`) — **or, when the last
  active editor tab is a file, silently takes that FILE's text** while the person is looking at a terminal.
- **The passage.** The Claude-tab door copies through a PowerShell `keybd_event` Ctrl+C plus a clipboard
  borrow (`src/selectionCapture.ts:207` `captureSelection`, restore rule `:196` `shouldRestore`). In a
  terminal a synthetic Ctrl+C with no selection is **SIGINT to ssh/tmux/claude**, so that path must never
  run for a terminal.
- **The key.** Panels are keyed by the host's `Tab` object (`src/chatPanels.ts:128` `open`, `:168` `rekey`).
  The label re-key in `sessionKey.ts` treats any known panel whose key is not among the TABS as orphaned
  — so a terminal-keyed panel would be "orphaned" for ever and **a Claude tab with the same label would
  steal its conversation** (`matchedSource`, `chatCapture.ts:181`; `askedForGoto`, `src/chatSessionJoin.ts:254`).
- **The durable source.** `ConversationSource` is `claude | file | none` (`src/chatStore.ts:70`), validated by
  `sourceFrom` (`:244`), `agreeOnOrigin` (`:276`) and `sameSource` (`:458`). `deliverPassage`
  (`chatCommand.ts:275`) derives it from a boolean and a uri.
- **Back to source** (`src/chatReturnCommand.ts`, decision `src/chatReturn.ts:20` `BackTo`, `:40`
  `recordedRoute`) and **go to** (`src/chatGoto.ts`) know only tabs; `chatGoto.ts:44` and `:235` state that a
  terminal cannot own a conversation. This plan reverses that statement.
- **Agent mode** is offered by `agentOffered(remote, workspace)` (`src/chatAccessRules.ts:12`), called at
  `chatCommand.ts:104`, `chatShow.ts:110`, `chatConversationRestore.ts:51`. A terminal conversation is filed
  under the first workspace root, so it would be offered agent mode in a folder that has nothing to do with
  the remote session.
- **Session reading** is local only: `claudeSessions.ts` reads `os.homedir()/.claude/projects/<dir>`
  (`projectDirNames` `:72`, `namedAmong` `:333`, `waitingIn` `:512`, `promptsFrom` `:930`,
  `waitingQuestion` `:1117`); `pinSession` (`chatSessionJoin.ts:185`, called at `chatCommand.ts:197` and
  `chatConversationRestore.ts:266`) walks the LOCAL home by title. For a remote session it would pin a
  local namesake. The pure line rules are reusable: `lastAsked` (`src/claudeQuestion.ts:291`),
  `collectNames` (`:110`), `humanSaid` (`:327`), `MOST_PROMPTS` (`claudeSessions.ts:686`).
- **Claude Code's session registry** — measured on this machine 2026-11-10: `~/.claude/sessions/<pid>.json`
  holds `pid`, `sessionId`, `cwd`, `name`, `nameSource`, `status` (`busy`/`idle`) among others. CredsForDevs
  already reads it (`dew_flow_creds_for_devs · src_broker_client/src/CallerIdentity.cs:67`, with the warning
  that pids recycle and a stale file is possible). This gives an EXACT session id for the `claude` process in
  a tmux pane; the title is the fallback *(consultants)*.

## The design

### Stage 1 — the terminal door (the minimal version)

- **A new command `coai.askFromTerminal`**, title "Ask other AI", icon `$(comment-discussion)`.
  - `menus.view/title`: `when: view == terminal`, `group: navigation@90`.
  - `keybindings`: `ctrl+alt+a`, `when: terminalFocus`.
  - `configurationDefaults`: `terminal.integrated.commandsToSkipShell: ["coai.askFromTerminal"]`, so the chord
    is not sent to the shell. This default is lost when the person has their own list *(consultants)*;
    stage 3 detects that and offers to add it.
  - The two existing chords gain `&& !terminalFocus`, so `coai.chatWithOtherAi` and `coai.goToConversation`
    can never win a chord typed into a terminal while a Claude tab is the active editor.
  - It reads `vscode.window.activeTerminal` and never looks at tabs — the file-text defect cannot occur on
    this door. `coai.chatWithOtherAi` keeps its behaviour, and its command-palette entry gains
    `when: !terminalFocus`, so the palette no longer offers the file door while a terminal has focus (R9
    measures that the palette sees the terminal's focus; if it does not, the result is recorded and the
    defect stays as today, named under *What this plan does NOT do*).
- **Capture.** `workbench.action.terminal.copySelection`, inside the existing clipboard borrow: extract
  `borrowClipboard(clipboard, run)` from `captureSelection` (`selectionCapture.ts:221-252`) with the marker
  and the restore rule unchanged, and the restore moved into `finally` so a rejected `executeCommand` still
  restores. `captureSelection` keeps its Windows reach check and calls the extracted function. The terminal
  path is serialised with `oneAtATime` like the keyboard path. The marker still on the clipboard means *no
  selection* → the owner's sentence. Only text survives the borrow (an image on the clipboard is lost) —
  already true of the Claude-tab door (`selectionCapture.ts:33`).
- **Source and key.** A pure `src/terminalSource.ts`: `terminalLabel(name)` (drops `Creds: ` at the start
  only; `Creds run: x`, `Creds VPN: x` stay whole), `terminalPassage(capture)`, `oneLiveNamed(names, name)`
  (`one | none | several`, never a guess), and `tabSourced(known, sourceOf)` that removes terminal-sourced
  panels from what `sourceSession` may re-key (the steal above). The panel key is the `vscode.Terminal`
  object; `ChatPanels` needs no change.
- **A new durable source kind** `{ kind: 'terminal'; name: string }` — the FULL terminal name (display
  stripping is for the title only *(consultants)*). `sourceOfTerminal(name)` (empty → `none`); `sourceFrom`
  accepts a non-empty string name up to 500 characters; `sameSource` compares names; `agreeOnOrigin` accepts
  either `fromSession`. Written from stage 1 so back-to-source works after a reload and stage-1
  conversations can be re-attached in stage 3. `deliverPassage` takes the `ConversationSource` in place of
  `sourceUri`; its three callers build it.
- **`fromSession` is `false` for every terminal conversation in stage 1.** `pinSession` runs on `fromSession`
  alone (`chatCommand.ts:197`, `chatSessionJoin.ts:185`), so `true` would pin a LOCAL namesake and *Asked*
  would read a local session. Stage 2 turns it on for `Creds:` terminals together with the guard that keeps
  `pinSession` off the local home for a `terminal` source.
- **Agent mode off for terminals.** One pure rule beside `agentOffered`: no agent box for a `terminal`
  source, used at the three call sites.
- **Back to source.** `BackTo` gains `{kind:'terminal'; name}`; `recordedRoute` returns it (the exhaustive
  switch makes every reader a compile error until handled). The command shows the live keyed terminal if
  it still exists, else `oneLiveNamed` over `window.terminals` (excluding exited ones): one → `show()`;
  none → *"The terminal “X” is not open."*; several → *"N terminals are called “X” — open the one you
  mean."*
- **Doors are counted.** `'terminal'` joins `Door` (`src/chatDoors.ts`); the handler calls `noteChatDoor` first,
  as every door does.
- Help: one paragraph naming the button (the help-coverage test requires it); five languages.

### Stage 2 — the remote Claude session, read over ssh

- **Which terminals.** `credsSession(name)` matches strictly
  `^Creds: ([A-Za-z0-9][A-Za-z0-9._-]*) / ([A-Za-z0-9][A-Za-z0-9_-]*)$` → `{host, session}`; anything else is
  selection-only. A name that starts with `Creds: ` but fails the pattern still shows the two overflow items,
  and pressing one answers with a sentence naming the part that failed (plan round 1), so the person is never
  left guessing why a creds terminal reads nothing. A leading `-` is impossible by the pattern (no ssh option
  injection); a session name with
  `.` is refused (tmux rewrites it and `.` is the target's window separator).
- **The person agrees to each host once.** The terminal name is only a name: any program can open a
  terminal called `Creds: x / y`. So the first remote read for a host asks *"Read the Claude session on
  “x” over ssh?"*; the answer is remembered per host in `globalState`, and a "no" makes that host
  selection-only until the person changes it from the panel.
- **One ssh call per question on the registry path, read-only.** Local `ssh` (Windows OpenSSH where the host is Windows, the
  side the terminals run on), argv only, no shell, and **no `-n`** (it would give the remote `sh -s` an empty
  stdin — plan round 1): `-T -o BatchMode=yes -o ConnectTimeout=5 -o
  RequestTTY=no -o RemoteCommand=none -o ClearAllForwardings=yes -o ForwardAgent=no -o ForwardX11=no -o
  LogLevel=ERROR -- <host> sh -s -- <session>`. The script travels on **stdin**, so the remote login shell
  does not matter. `ClearAllForwardings` and `RemoteCommand=none` matter: the owner's `Host` blocks carry a
  `LocalForward` the open terminal already holds, and a `RemoteCommand` would run tmux.
- **What the script does** (POSIX `sh`, prints a `COAI-BEGIN` sentinel first so login banners are ignored):
  1. `tmux display-message -p -t "=$1:" '#{pane_pid}'` — `=` forces an exact session match.
  2. the `claude` process among that pane's descendants (`ps -o pid=,comm= --ppid …`, walked);
  3. `cat "$HOME/.claude/sessions/<pid>.json"` (digits-only pid, at most 64 KiB) → `sessionId`, `cwd`, `name`;
  4. the transcript found BY ITS ID, `"$HOME"/.claude/projects/*/<sessionId>.jsonl` (the id must match the
     UUID shape in `sh` before it is used; exactly one file must match) — its **tail**, at most 4 MiB
     (`tail -c`), first partial line dropped and the cut reported. Finding the file by its id means the
     script needs no copy of the folder-name rule; the extension then checks locally that the file's folder
     is one of `projectDirNames(cwd)` for the registry's `cwd`, and refuses otherwise;
  5. when steps 2–3 find nothing (no live `claude`, no registry file): the script prints the pane's
     `#{pane_current_path}` and the names of the folders in `~/.claude/projects` instead. That is the
     **fallback**, and it costs up to three calls: (i) this one; (ii) the extension computes the candidate
     folders with `projectDirNames` locally and asks for only their title rows, newest first, bounded by the
     existing scan budget (`grep -F -e '"custom-title"' -e '"ai-title"'`); (iii) the chosen file's tail,
     read by its id as in step 4.
  The extension parses everything locally with the existing pure rules; nothing is matched in shell *(the
  clean architect's point: no second copy of `namedAmong` in sh)*.
- **Identity.** The registry path is exact. The fallback picks by `namedAmong` over the titles: one → that
  file; several → a QuickPick of candidates, newest first, nothing chosen by itself; none → a sentence naming
  host, session and folder. Absent, ambiguous, unreadable and truncated are four different sentences
  *(consultants)*. A session id the person picked in the fallback is kept in memory for that terminal and
  sent as the id for step 4 on the next question; it is dropped when the registry path succeeds, when its
  file is gone, or with the terminal.
- **Use.** `takeFromTerminal` / `addFromTerminal` mirror `takeTheQuestion` (`chatCommand.ts:505`) with the
  remote reader; the sentence mapping in `questionWaitingHere` (`:448`) moves to one exported function so the
  local and remote paths say the same words. *Asked* (`chatHooks.ts:361`) reads through the conversation's
  reach. `pinSession` never walks the local home for a `terminal` source.
- **Where the buttons are.** Two overflow items in the terminal title bar, *CoAI: take the question* and
  *CoAI: add the question*, `when: view == terminal && coai.terminalReadsSession`; the context key follows
  `onDidChangeActiveTerminal`.
- **Limits.** Kill at 15 s; stdout capped at 8 MiB; logs carry host, session, exit code and the stderr tail —
  never stdout, which is the conversation.

### Stage 3 — polish

- **Re-attach after a reload**, only for names that pass the strict `credsSession` pattern of stage 2: pure `reattachPlan(orphans, liveNames, name)` binds only
  when exactly one live terminal and exactly one orphaned conversation carry the name, re-checked just before
  `panels.rekey` *(consultants)*. Runs on `onDidOpenTerminal` (creds may reopen terminals late), after the
  serializer restores, and at the start of the door.
- **Go to from a terminal:** `TabKind` gains `'terminal'`; Ctrl+Alt+G on `terminalFocus` (added to the skip
  list); an overflow item.
- **Skip-shell health:** if the person's own `commandsToSkipShell` hides the default, one notification with an
  *Add* button that appends the ids to the user setting; pressing it is the consent; the answer is remembered.

## What this plan does NOT do

- No change to CredsForDevs, `coai-mcp` or the Team server; nothing is installed on the VMs.
- No agent mode for a terminal conversation; the vendor CLI still runs in its empty temp directory.
- No automatic switching between a chat and its terminal.
- No reading of a terminal's screen buffer (`shellIntegration` streams are raw escape sequences from the
  moment of subscribing — rejected).
- No proposed API (`Terminal.selection`): an installed extension cannot use one.
- No following of a renamed terminal and no re-check of a picked session against a newer namesake: the
  owner did not ask for them. While the terminal lives, its conversation is keyed by the `Terminal` object, so a
  rename keeps it; after a reload the recorded name (the name at the first press) no longer matches a renamed
  terminal, so that terminal starts a new conversation.
- If R9 shows the command palette cannot see the terminal's focus, `coai.chatWithOtherAi` run from the palette
  with a terminal focused still takes the last file tab's text, as today; the terminal door is the cure.

## Security

- The only new outward edge is a **read-only** ssh from the extension host to a host the person agreed to
  once (above), as the user the person's own `~/.ssh/config` logs in as. The terminal name chooses the host,
  and a name is not proof, which is why the consent exists. No secret is handled: ssh uses the
  person's keys and agent; `BatchMode=yes` means a password prompt is a refusal, never a hang.
- Remote command construction: argv only (`shell: false`), the host after `--`, the script on stdin, every
  operand validated (the regexes above) and single-quoted; a NUL or newline in an operand throws.
- The transcript text is held in memory only, never written to disk or logged.
- `SECURITY.md` gains one paragraph on the ssh edge (stage 2).

## Growth surfaces

- **Conversation records with a `terminal` source**: the existing `chat-conversations/` store and its sweep;
  one record per conversation as today — no new growth surface.
- **The ssh child**: one process per question, killed at 15 s, nothing left behind; stdout capped at 8 MiB.
- **A picked session id per terminal** (stage 2, fallback only): in memory, one entry per open terminal, dropped
  with the terminal (`WeakMap`).
- **The per-host consent** (stage 2): one `globalState` list of `{host, answer, askedUtc}`, capped at 100
  entries; at the cap the oldest answer is dropped (that host is simply asked again). Projected size: the
  owner has three hosts today, so a few hundred bytes; at the cap under 10 KiB (plan round 1).
- Nothing grows on the VM: every remote command is read-only.

## Build order

1. **E1** — stage 1, one PR, one extension release; the owner's real-window check before E2 starts.
2. **E2** — stage 2, one PR, one extension release; real-window check on a VM.
3. **E3** — stage 3, one PR, one extension release; then `review_feature` (three epics) and the promotion.

## Test plan

- **Pure (`npm test`)**: `terminalSource` (label, passage, `oneLiveNamed` 0/1/2, the steal: a Claude tab with a
  terminal conversation's label gets `new`, not `rekey`, and the test goes RED without the filter);
  `borrowClipboard` (restore when untouched, no restore when changed, empty clipboard never overwritten, marker
  back = nothing copied, a rejecting `run` still restores; the existing `captureSelection` tests stay green);
  `chatStore` (terminal round trip; empty or non-string name refused; `sameSource`; `agreeOnOrigin`);
  `chatReturn` (live and recorded terminal routes); the agent rule; manifest tests (the button, the chord with
  `terminalFocus`, every `terminalFocus` binding's command in the skip-list default, no old chord matching under
  `terminalFocus`, the door counted).
- **Host scenario (`npm run test:host`)**: a terminal named `Creds: h / s` echoes a probe; `selectAll`; the door's
  capture returns it and restores the clipboard; after `clearSelection` the owner's sentence and no clipboard write;
  `inspect('terminal.integrated.commandsToSkipShell').defaultValue` holds the id.
- **Stage 2**: operand validation against hostile strings through a real `sh -c`; the parsers (banner before the
  sentinel, registry JSON, NUL lists, a cut tail, the title rows); the script itself run by `sh -s` on Linux CI
  against a temporary `HOME` with a fake `tmux` and `ps` on `PATH` — the text sent to the VM is the text under
  test; the pick list (one, several, cancel); the transcript found by id (zero or two matches refused; a file whose
  folder is not one of `projectDirNames(cwd)` refused); the per-host consent (asked once, "no" remembered, no ssh
  spawned before a "yes"); `fromSession` stays `false` for a non-`Creds:` terminal and `pinSession` never reads the
  local home for a `terminal` source.
- **Stage 3**: `reattachPlan` (1/1 binds; 2 terminals or 2 conversations do not; an already bound key does not; a name
  that fails `credsSession` never binds); go-to with a terminal; the skip-shell check over the `inspect()` shapes.
- **Webview pages are not changed**; if one must be, it is tested by running it (`bundledPage.test.ts`).

## Real-window checks (recorded in this plan when measured)

| # | Check | Epic |
|---|---|---|
| R1 | In a `Creds:` tmux terminal running `cat -v`, and with the Claude TUI, Ctrl+Alt+A prints nothing in the pane and the chat opens. | E1 |
| R2 | A Claude tab is the active editor and the terminal has focus: Ctrl+Alt+A runs the terminal door. | E1 |
| R3 | The button copies the selection after the click moved focus; split terminals pick the active one; the clipboard is restored; no selection → the sentence, clipboard untouched. | E1 |
| R4 | `terminal.name` stays `Creds: host / session` while tmux and Claude send title sequences. | E1 |
| R5 | Back to source shows the right terminal, in the panel and in the editor area. | E1 |
| R6 | One ssh call from Windows: time to the first answer (target under 4 s); the owner's real `Host` blocks with `LocalForward`. | E2 |
| R7 | The registry path finds the right session on each of the three users; the fallback list works when two sessions share a name. | E2 |
| R8 | After a VS Code reload, creds reopens its terminals and the conversations re-attach. | E3 |
| R9 | With a terminal focused, the command palette no longer lists `coai.chatWithOtherAi` (its `!terminalFocus` entry). | E1 |
| R10 | Ctrl+Alt+G in a `Creds:` tmux terminal opens go-to and prints nothing in the pane. | E3 |
| R11 | With a user-level `commandsToSkipShell` that lacks the ids, the notification appears once, *Add* appends them, and R1 then passes. | E3 |

## Definition of Done

- [ ] From any terminal, the title-bar button and Ctrl+Alt+A open a coai chat with the selected text, named
      after the terminal; with no selection the owner's sentence and nothing sent; no Ctrl+C ever reaches the
      terminal.
- [ ] A Claude tab can never take a terminal's conversation, and the old chords never fire in a terminal.
- [ ] A terminal conversation is saved with a `terminal` source, has no agent mode, and *back to source*
      brings its terminal forward or says why it cannot.
- [ ] For a `Creds: <host> / <session>` terminal, *take the question*, *add the question* and *Asked* read the
      remote session read-only over ssh, after the person agreed to that host once — ONE call on the registry
      path, by the exact session id; the title fallback (at most three calls) gives a list on ambiguity, never a
      guess.
- [ ] After a reload a `Creds:` conversation re-attaches only when unambiguous; go-to works from a terminal; a
      hidden skip-list default is offered back with the person's consent.
- [ ] Every real-window check R1–R11 is recorded here with its result.
- [ ] Three extension releases cut and verified (tag pushed alone, its run observed); `review_feature` run;
      the plan promoted with its deviations.

## Epics

Three epics in the build order above, one PR and one extension release each. An epic starts only after the release
and the real-window checks of the epic before it. Stories documents are NOT written now: one per epic,
`todo/PLAN_the_chat_opens_from_a_terminal_E<n>.md`, written by Opus when that epic starts (`subagent-models.md`,
pipeline step 3). Cornerstone quota: Q = ⌈2·E/5⌉ = ⌈2·3/5⌉ = 2. Fable names the cornerstone AREAS here; Opus marks the
concrete stories in the stories documents, within the quota, and updates the ledger line in the same commit.

*Question consultants (2026-11-10, codex and gemini), used for this split.* What is important, as areas:
**(a)** clipboard and focus capture — never a synthetic Ctrl+C into a terminal, the restore in `finally`, the meaning
of the marker; **(b)** source identity against display label — the `terminal` source kind, the full name as the
identity, a Claude tab never re-keys a terminal conversation, re-attach only with one live terminal and one orphan;
**(c)** local against remote execution — the vendor CLI stays local, no agent mode, a remote transcript is context
only; **(d)** ssh command construction, quoting, cancellation and limits; **(e)** absent, ambiguous, unreadable and
truncated session data are four different results.

Cornerstones: 1 of 2 — E1.S2

*Recommendation for the Opus story writer (not a mark).* One cornerstone in E1, one in E2, none in E3.
**E1: the story that adds the `terminal` source and its key — area (b).** The record is on disk from the first
release, E2 and E3 read it, and a wrong identity or a stolen conversation gives no error. Area (a) is not recommended:
the plan fixes its design, its failures are loud, and R1 and R3 catch them before E2 starts.
**E2: the story that defines the contract of the remote reader — the ssh argv, the stdin script and the typed result,
areas (d) and (e).** It is the only outward edge, and the only place where wrong or cut data can arrive and look
correct. If these become two stories, mark the script with its result type; the judgement on the ssh edge (which
host, whose keys, no forwarding) is authentication and authorisation — Fable by the security row, outside the quota.

### E1 — The terminal door (stage 1, the minimal version) · depends on nothing

- **Goal.** From any terminal, the title-bar button and Ctrl+Alt+A open a chat with the selected text, named after the
  terminal. The conversation is saved with a `terminal` source, has no agent mode, and *back to source* shows its
  terminal. Selection only: reading a Claude session is E2.
- **Scope.** `src_vs_code` only. `package.json` (the command `coai.askFromTerminal`, the `view/title` button, the
  `terminalFocus` chord, the skip-shell default, `!terminalFocus` on the existing chords and on the palette entry of
  `coai.chatWithOtherAi`); new `src/terminalSource.ts`; `fromSession` `false` for every terminal conversation;
  `borrowClipboard` extracted in `src/selectionCapture.ts`; the `terminal` kind in `src/chatStore.ts`; `deliverPassage`
  and its three callers (`src/chatCommand.ts`); the agent rule beside `agentOffered` (`src/chatAccessRules.ts`) at its
  three call sites; `BackTo` and `recordedRoute` (`src/chatReturn.ts`, `src/chatReturnCommand.ts`); the `tabSourced`
  filter where a panel is re-keyed (`src/chatCapture.ts`, `src/chatSessionJoin.ts`); `Door` (`src/chatDoors.ts`); the
  help paragraph in five languages; the documents that describe the changed behaviour — `research/module_extension.md`
  (the chat section), `research/architecture.md` where a module boundary moves, and every new flow catalogued in
  `research/module_tests.md` (plan round 1; the same holds for E2 and E3, with `SECURITY.md` in E2).
- **Depends on.** Nothing.
- **Done when.** The stage-1 rows of the test plan are green (`npm test`, `npm run test:host`), and the steal test was
  seen RED without the filter. DoD items 1–3 hold. The PR is merged; the extension release is cut and verified (tag
  pushed alone, its run observed) AFTER the owner did R1–R5 and R9 in a real window on the pre-release VSIX (entry 1.1);
  each result is recorded in the R-table.
  E2 does not start before that.
- **Cornerstone areas.** (a) the capture; (b) identity and key — the recommended cornerstone; (c) its first part only:
  no agent mode for a `terminal` source (a rule the types enforce — Opus).
- **Stories.** [PLAN_the_chat_opens_from_a_terminal_E1.md](PLAN_the_chat_opens_from_a_terminal_E1.md) — three stories,
  written by Opus 2026-11-10: E1.S1 the capture, E1.S2 the `terminal` source and its identity, E1.S3 the door. **E1.S2 is
  the cornerstone** (area (b), spec by Fable).

### E2 — The remote Claude session, read over ssh (stage 2) · depends on E1

- **Goal.** For a `Creds: <host> / <session>` terminal, *take the question*, *add the question* and *Asked* read the
  Claude session on the VM through a read-only ssh call. The AI still runs locally. Every other terminal stays
  selection-only.
- **Scope.** `credsSession(name)`; the ssh runner (argv, options, the 15 s kill, the 8 MiB cap, the log rule) and the
  POSIX script on stdin; the local parsers over the existing pure rules (`src/claudeQuestion.ts`,
  `src/claudeSessions.ts`); identity — the registry first, the title fallback, the QuickPick; `takeFromTerminal` /
  `addFromTerminal` and the one exported sentence mapping (`src/chatCommand.ts`); *Asked* through the reach of the
  conversation (`src/chatHooks.ts`); `pinSession` never local for a `terminal` source (`src/chatSessionJoin.ts`); the
  two overflow items and the context key `coai.terminalReadsSession` (`package.json`); the per-host consent; the
  picked session id kept per terminal; `fromSession` `true` for `Creds:` terminals; one paragraph in `SECURITY.md`.
- **Depends on.** E1 released, R1–R5 and R9 recorded. R4 is the important one: E2 uses the terminal name to choose the host,
  so the name must be proven stable first. E2 uses the `terminal` record of E1 as it is.
- **Done when.** The stage-2 rows of the test plan are green, with the script run by `sh -s` on Linux CI and the
  hostile operands through a real `sh -c`. DoD item 4 holds. Absent, ambiguous, unreadable and truncated each give
  their own sentence, each with a test. No log line carries stdout. `SECURITY.md` has the paragraph. The PR is merged;
  R6 and R7 are measured on a VM with the pre-release VSIX (entry 1.1) and recorded; then the release is cut and verified.
- **Cornerstone areas.** (c) local against remote; (d) the ssh command; (e) the four results — (d) and (e) together
  are the recommended cornerstone.

### E3 — Polish (stage 3) · depends on E2

- **Goal.** After a reload a `Creds:` conversation finds its terminal again, only when unambiguous; go-to works from a
  terminal; a hidden skip-shell default is offered back with consent.
- **Scope.** `reattachPlan` (names that pass `credsSession` only) and its three triggers (`onDidOpenTerminal`, after
  the serializer restores, the start of the door), with the re-check before `panels.rekey` (`src/chatPanels.ts`);
  `TabKind` `'terminal'`, Ctrl+Alt+G on `terminalFocus`, its skip-list entry and its overflow item
  (`src/chatGoto.ts`, `package.json`); the skip-shell notification and the write to the user setting.
- **Depends on.** E2 released, R6–R7 recorded. Re-attach reads the records E1 writes and the `credsSession` pattern
  of E2.
- **Done when.** The stage-3 rows of the test plan are green. DoD item 5 holds. The PR is merged; the third release is
  cut and verified after R8, R10 and R11 were recorded on the pre-release VSIX (entry 1.1). Then `review_feature` over the three epics, and the plan is promoted with its
  deviations — the parent and the three stories documents together (DoD items 6–7).
- **Cornerstone areas.** (b) again — the re-attach rule (one live terminal, one orphan, checked again before the
  re-key). No cornerstone story is recommended here: the rule is small, pure and already specified.

### Notes from the split, and how they were settled (2026-11-10, before the first plan round)

Fable raised six gaps while splitting; all were settled in the plan text above, none left open:
(1) the transcript is found by its session id, so the registry path is one call and the folder rule stays local;
the fallback is up to three calls; the picked id's life is stated. (2) `fromSession` is `false` for every terminal
in E1. (3) A per-host consent, asked once. (4) Rename follow and the stale pin were removed (not asked for); R10 and
R11 were added. (5) Re-attach uses the strict `credsSession` pattern. (6) The palette entry of `coai.chatWithOtherAi`
gains `!terminalFocus`, measured by R9, with the fallback named under *What this plan does NOT do*.
