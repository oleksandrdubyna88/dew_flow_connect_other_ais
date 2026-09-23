# PLAN — a local chat can act on the machine, when the person ticks it

> Status: **plan only, nothing implemented yet.** Scope: the chat's launch path in `src_vs_code`
> (`chatAdapter.ts`, the three vendor adapters, `cliChatLaunch.ts`, `chatProcess.ts`, `chatLaunch.ts`,
> `chatSession.ts`), the conversation record (`chatStore.ts`, `chatPersist.ts`, `chatShow.ts`,
> `chatConversationRestore.ts`, `chatArchive.ts`, `chatThread.ts`) and the page (`chatPage.ts`,
> `chatPanel.ts`, `chatMessages.ts`, `chatHooks.ts`). Issue #289.
>
> Related docs: [module_extension.md](../research/module_extension.md), [architecture.md](../research/architecture.md),
> [PLAN_closing_a_chat_ends_its_whole_tree.md](PLAN_closing_a_chat_ends_its_whole_tree.md).

## The symptom

Issue #289: the person asked a chat to translate a FILE, and codex and gemini "fell over or something
happened". Until now they had only ever handed the chat finished text. What they need: *"that they can
open files when I need them to"*, with a tick box, offered only when the vendor is local (not through a
Team server).

Why it fails today — every local chat is launched text-only, by design:

- the working directory is an empty temp directory `coai-chat-*`, never the workspace
  (`src_vs_code/src/cliChatLaunch.ts:239-242`, created by `emptyTempDir`, `chatLaunch.ts:39-47`);
- `agy` runs `--mode plan` (`agyAdapter.ts:18-26`), which writes nothing;
- `claude` runs `-p` with no `--permission-mode` (`claudeAdapter.ts:22-29`), so every tool that needs a
  permission is refused headless;
- `codex exec` runs with no `-s`, so its own default (read-only sandbox) applies (`codexAdapter.ts:61-66`),
  and on this Windows machine the codex sandbox itself fails with `error 1920` under both `read-only` and
  `workspace-write` (`research/RESULTS_first_real_run.md:100-102`);
- a turn has 180 s (`chatSession.ts:134`, `DEFAULT_BUDGETS.turnMs`), which a file task can outrun, and a
  persistent vendor that times out loses its context (`cliChatSession.ts:289-294`).

So the failure is not a bug in one adapter: the chat was never allowed to touch a file. The fix is a
second launch MODE, chosen per conversation.

## The decision (the operator's, 2026-09-23)

**Full access to the computer**, not a workspace-only sandbox. Asked with the choices laid out; chosen
over "write inside the workspace only". The flags below are read from each CLI's `--help` on this
machine on 2026-09-23, not from memory:

| vendor | text mode (today, unchanged) | agent mode |
|---|---|---|
| `claude` | `-p --verbose --input-format stream-json --output-format stream-json` | the same + `--permission-mode bypassPermissions` (choices: acceptEdits, auto, bypassPermissions, manual, dontAsk, plan) |
| `codex` | `exec [resume <id>] [-m M] --json - --skip-git-repo-check` | the same + `--dangerously-bypass-approvals-and-sandbox` — accepted by BOTH `exec` and `exec resume` (unlike `-s`, which `resume` refuses: `CodexConsultant.cs:17-22`), and with no sandbox the Windows `error 1920` has nothing to fail in |
| `agy` | `--mode plan --disable-slash-commands --input-format stream-json --output-format stream-json` | `--mode plan` replaced by `--dangerously-skip-permissions` ("Auto-approve all tool permission requests"); `--disable-slash-commands` kept — the text is still another AI's and may start with a slash |

In agent mode the working directory is the conversation's workspace (`Thread.workspace`,
`chatThread.ts:288`). A conversation with NO workspace is not offered agent mode at all — never a silent
fall back to the home directory, where "create a file" would land in the profile root (gemini, the plan
round). The directory is fixed for the life of the conversation, which is what codex's `resume` needs (a
thread resumes where it started), and it is checked to exist at launch: a deleted folder is a refusal
that names it, not a spawn error (local, the plan round).

## The design

1. **A type, not a flag.** `export type ChatAccess = 'text' | 'agent'` in `chatAdapter.ts`, and
   `ChatLaunch` (`chatAdapter.ts:94-110`) gains `access: ChatAccess` and `cwd` stays out of it (the
   spec decides the directory, see 3). `NEW_CONVERSATION` gets `access: 'text'`. The comment at
   `chatAdapter.ts:88-92` already names "permissions" as the third field a launch would grow; adding
   it makes the compiler ask all three `argv`s.
2. **Each adapter answers it.** `agyAdapter.argv`, `claudeAdapter.argv`, `codexAdapter.argv` build the
   table above. Text mode stays byte-identical — the existing argv tests keep passing untouched.
3. **`launchSpecFor(vendor, tempDir, launch, resolved, platform, workspace)`** — `cwd` is `tempDir`
   in text mode and `workspace` in agent mode; agent mode with an empty or missing workspace is REFUSED
   with a sentence naming it, and so is a remote row in agent mode (defence in depth: a remote row never reaches this function today,
   `chatLaunch.ts:68-72`). The `shell=true` path stays safe because `resolved` is a full path
   (`processLauncher.ts:115-131`) — `cmd.exe` searching the workspace first matters only for a bare name.
4. **A longer turn in agent mode.** `AGENT_BUDGETS = { startupMs: 30_000, turnMs: 1_200_000 }`
   (20 min) beside `DEFAULT_BUDGETS` in `chatSession.ts`; text mode keeps 180 s. `started(...)` picks by
   access.
5. **`started(vendor, resolved, model, remote?, access = 'text', workspace = '')`** and
   `chatProcessFor(vendor, home, resolved, model, access, workspace)` carry the mode into the per-turn
   factory.
6. **Changing the box mid-conversation relaunches, like a model switch.** Flags are fixed when a
   persistent process starts, so a change retires the session and starts a new one with the transcript
   carried — exactly `switchNow` (`chatLaunch.ts:238-326`). The replacement is a NEW `CliChatSession`, so
   codex's resume id is not carried across a mode change: the next turn starts a fresh codex thread with
   the transcript replayed, never `resume <id>` under different flags (gemini, the plan round). `switchNow`'s body becomes
   `relaunch(entry, vendor, modelId, access)`, shared by the model switch and a new
   `switchAccess(entry, access)`, which goes through the same turn queue as `switchModel`
   (`chatLaunch.ts:148`). The box is DISABLED while a turn runs, as the composer already is (`locked`), so
   a change can never be queued behind a 20-minute answer while the box claims otherwise; Stop is the way
   out of a running turn (codex, local, gemini, the plan round). Turning it ON asks first — a modal
   warning on the host, *"{model} will be able to read, write and run anything on this computer without
   asking. Turn agent mode on?"*; cancelling puts the box back (local, the plan round). Turning it off asks
   nothing.
   A model switch to a REMOTE row while agent mode is on turns agent mode off, persists that, and says
   so in the switch notice — it never tries an agent launch on a remote row (codex, the plan round). The notice says what changed: *"Agent mode
   is on: {model} may now read, write and run anything on this computer, without asking."* / *"Agent mode
   is off: {model} answers from the text alone."*
7. **The thread and its record.** `Thread.access: ChatAccess` (`chatThread.ts`), classified as
   *kept by a reset* in `chatArchive.ts:60-76`. `ConversationRecord.access?: 'agent'` (`chatStore.ts:94-120`)
   — absent means text, so every record written before this change reads back as text and
   `CONVERSATION_VERSION` stays 1 (`chatStore.ts:36-43`: raising it discards every old record).
   `recordFrom` accepts only `'agent'` or absence; `recordOf` (`chatPersist.ts:58-77`) writes it only when
   on; `show()`'s unchanged-check (`chatShow.ts:106-113`) compares it, or ticking the box is never saved;
   the memento copy (`chatPersist.ts:191-199`) and the restore (`chatConversationRestore.ts:152-230`) carry it.
8. **The page.** The host computes `agentOffered = !thread.forgetful && thread.workspace !== ''`
   (`forgetful === isRemote`, `chatModels.ts:113-115`) — the page decides nothing. `ChatPageState` / `ChatPushState` carry
   `access` and `agentOffered`; the `pickerRow` (`chatPage.ts:1186-1190`) gets a labelled checkbox
   *"Agent mode — full access to this computer"* with a `title` that says what it allows, hidden when not
   offered, and a warning outline on the row while it is on. The page posts
   `{type:'command', command:'access', agent: boolean}`; `chatMessages.ts` adds the command to
   `ChatCommand` and `chatCommandOf` (a non-boolean `agent` is refused); `chatPanel.ts` dispatches to a new
   hook `onAccess(entry, agent)`; `chatHooks.ts` refuses it for a remote thread and otherwise calls
   `switchAccess`.
9. **A new conversation starts in text mode.** The box is per conversation and off by default; nothing
   remembers "last time it was on". Full access is a thing a person turns on for a job, not a default
   they inherit.

## What is deliberately NOT here

- **No workspace-only sandbox mode.** Rejected by the operator in favour of full access; a third value
  of `ChatAccess` can add it later without changing the shape.
- **No change to the Team server chat.** A remote model runs on another machine; "access to this
  computer" means nothing there, so the box is not offered.
- **Killing the vendor's whole process tree on close** is `PLAN_closing_a_chat_ends_its_whole_tree.md`.
  Agent mode makes it matter more (an agent spawns shells), and the plan's risk section below says so,
  but it is that plan's work.
- **No rollback of what an agent wrote** when a turn times out or is stopped. The notice and the
  checkbox's title say the model acts without asking.

## Risks

- **A 20-minute turn holds the queue.** The existing Stop control ends the turn; the budget is the
  ceiling, not the expectation.
- **Grandchildren outlive a closed tab** until the tree-kill plan lands (see above).
- **The flags are vendor surface.** Pinned by argv tests, and checked live once per vendor before the
  pull request (the Test plan's last line), because an argv test proves what we SEND, not what the CLI does.

## Plan round (2026-09-23) — what was declined

- **Kill the whole process tree here** — real, and already `PLAN_closing_a_chat_ends_its_whole_tree.md`,
  which text mode needs too; the PR says agent mode raises its priority.
- **A schema version for the record** — an optional field read as absent is how this record has grown
  four times, and a version bump discards every record.
- **A heartbeat for the 20-minute turn** — a dead process already ends the turn on its exit/error events
  (`cliChatSession.ts:365-366`, `:488-491`); the budget bounds only a live, silent one.
- **Fallback flags for older CLIs** — an unknown flag exits at once with the vendor's own error, shown
  with its stderr tail; guessing flags for versions nobody measured would be worse.

## Build order

1. `ChatAccess` + `ChatLaunch.access` + `NEW_CONVERSATION`; the three adapters' agent argv
   (`chatAdapters.test.ts`, `chatModelReachesTheCli.test.ts` extended: text argv unchanged, agent argv per
   vendor, codex agent argv on a resume turn).
2. `launchSpecFor` cwd + remote refusal; `AGENT_BUDGETS`; `chatProcessFor` / `started` carry access and
   workspace (`cliChatLaunch` tests: cwd by mode, home fallback, remote refused; a session test that the
   agent budget is the one armed).
3. `Thread.access`, `chatArchive` classification, the record (`recordFrom`/`recordOf`), `show()`'s
   unchanged-check, memento, restore (`chatStore.test.ts`, `chatRestore.test.ts`: an old record reads back
   as text; an agent record survives a round trip; ticking the box alone triggers a save).
4. `relaunch` extracted from `switchNow`; `switchAccess` through the turn queue; the notices
   (`chatLaunch`/switch tests: the session is replaced, the transcript carried, a remote thread refused).
5. The command (`chatMessages.test.ts`), dispatch + hook (`chatPanel.test.ts`), the page checkbox and
   its state (`chatPage.test.ts`: shown for local, absent for remote, reflects `access`, posts the
   command the parser accepts).
6. Docs: `research/module_extension.md` (chat section: the two modes, the flags table, the budgets),
   CHANGELOG `## Unreleased`, help text if the chat page has a help key for the picker row.

## Test plan

- `cd src_vs_code && npm test` and `npm run lint` (complexity ≤ 4; no new suppressions).
- Unit tests listed per step above; every new behaviour has a test named after its guarantee.
- **Live check, once per vendor, before the pull request:** open a local chat on a scratch folder, tick
  the box, ask it to create `hello.txt` there and then to translate it; observe the file. Then ask it to
  read a file OUTSIDE the workspace and to run a shell command (`git --version`), because the box promises
  the whole computer and a check inside the folder does not prove that (codex, the plan round). Record what
  each vendor did in the PR description — observed, not assumed. A vendor that is not installed or
  signed in on this machine is reported as not checked.

## Definition of Done

- [ ] Agent mode launches each local vendor with the flags in the table, in the conversation's workspace.
- [ ] Text mode's argv, directory and budget are unchanged (existing tests untouched and green).
- [ ] The box is offered only for a local row; a remote row cannot be switched into agent mode by any path.
- [ ] Ticking or unticking relaunches with the transcript carried, after a running turn finishes.
- [ ] The choice survives a reload and a restore; an old record reads back as text.
- [ ] Tests added for every step; `npm test` and `npm run lint` green.
- [ ] Live check done per installed vendor and reported in the PR.
- [ ] `module_extension.md` and the CHANGELOG updated; this plan promoted to `research/`.
