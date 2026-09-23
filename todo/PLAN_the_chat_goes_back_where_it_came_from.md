# PLAN — the chat goes back to where it came from

> Status: **plan only, nothing implemented yet.** Scope: `src_vs_code` — a new command in the coai
> chat's own context menu, a pure decision module, its executor, the page's `data-vscode-context`, the
> manifest, and the structural door test. Issue #314.
>
> Related docs: [module_extension.md](../research/module_extension.md), [architecture.md](../research/architecture.md).

## The symptom

Issue #314: *"go back to the matching Claude Code window. We have **go to** on the right-click, which
opens the matching coai chat. It needs the other direction too — a right-click command inside the chat
that activates the matching Claude Code tab."*

Today the road runs one way. `coai.goToConversation` (`src_vs_code/package.json:263-266`, menus
`:946-948` on `webviewId == 'claudeVSCodePanel'`, chord `ctrl+alt+g` `:989-992`; registered
`src/extension.ts:543-557`; decided by `chatGoto.ts:228`, executed by `chatGotoCommand.ts:70`) takes a
Claude tab or an editor to its conversation. The coai chat (`createWebviewPanel('coaiChat', …)`,
`chatPanel.ts:222-223`) has no context-menu entry at all — every `webview/context` item is scoped to the
Claude panel (`package.json:928-953`) — so the way back is to hunt through the tabs by eye.

## What the conversation already knows about where it came from

- **The live key.** A chat opened from a tab is registered under that `vscode.Tab` object
  (`chatPanels.ts:111` `keyOf`, `chatRegistry.ts:125` `tabStillOpen`). A chat restored after a reload or
  opened from the picker has a synthetic `{}` key (`chatConversationRestore.ts`, pinned by
  `chatGotoWiring.test.ts:312`).
- **The durable source.** `Thread.source` (`chatThread.ts:278`, type `chatStore.ts:70-76`):
  `{kind:'claude', sessionId}`, `{kind:'file', uri}` or `{kind:'none'}`. A Claude chat starts `none` and is
  pinned to its session in the background once the session walk finds exactly one (`chatSessionJoin.ts:145-185`).

## Activating a Claude Code tab — two proven ways (read from the installed extension, 2.1.280)

- **B — by the `vscode.Tab` object, the editor's own recipe.** Claude Code's own `bringTabToFront`
  (`extension.js:1043`) focuses the tab's group (`workbench.action.focusFirstEditorGroup` …
  `focusEighthEditorGroup`, by `tab.group.viewColumn`) and then runs
  `workbench.action.openEditorAtIndex` with `tab.group.tabs.indexOf(tab)`. Nothing of Claude's is called.
- **A — by session id.** `claude-vscode.editor.open(sessionId, prompt, viewColumn, groupId, fullEditor,
  {programmatic})` (`extension.js:1103`, `package.json:203-204` of the extension): an open panel of that
  session is revealed, otherwise a remembered tab is revived or a new panel opened on that session. Called
  with `{programmatic: true}` so it does not move Claude's preferred location or grab its input. It is
  another extension's internal command, not an API — so it is asked for only when B cannot be used, and
  only if `vscode.commands.getCommands(true)` lists it.

## The design

1. **A pure decision, `chatReturn.ts`** — `backTo({ liveTab: boolean, source: ConversationSource })`
   answers a closed union: `{ kind: 'tab' }` (the live key is a tab that is still open — B), `{ kind:
   'session', sessionId }` (A), `{ kind: 'file', uri }` (`showTextDocument(uri, {preview:false})`), or
   `{ kind: 'nowhere', why }`. The live tab wins over the durable source because it is exact — it is the
   very tab the chat was opened from, even before the session walk pinned it — and needs no other
   extension. A FILE source goes back to its file: the same command, because "where it came from" is the
   question either way.
2. **The executor, `chatReturnCommand.ts`** — finds the conversation, asks `backTo`, does the one thing
   the answer names, and says so when it cannot (`notify`, refusal class): *"This conversation does not
   know where it came from — it was opened from the picker or restored before its source was recorded."* /
   *"Claude Code does not offer a way to open session {id} here."* A tab that did not become active within
   a second (the recipe's own wait) is a refusal too, never silence.
3. **Which chat was right-clicked.** A `webview/context` command receives only `{webview: 'coaiChat'}`
   plus the page's `data-vscode-context` (`chatTrigger.ts:30-38`). The page's `<body>` gets
   `data-vscode-context='{"coaiConversation":"<saveId>"}'` (`chatPage.ts:2346`), rewritten when *New chat*
   gives the tab a new id (`chatPage.ts:2121-2126`). The chord has no argument at all, so it falls back
   to the ACTIVE chat panel: `RevealablePanel` gains `isActive()` from `panel.active`.
4. **The manifest.** `coai.backToSource` — *"CoAI: back to where this chat came from"* — in
   `contributes.commands`; a `webview/context` item `when: webviewId == 'coaiChat'`; the SAME chord as *go
   to* (`ctrl+alt+g` / `cmd+alt+g`) with `when: activeWebviewPanelId == 'coaiChat'`, so one chord goes both
   ways and the two `when`s never overlap. Registered in `extension.ts` beside *go to*, with its own
   `.catch` → `notify`. **Not a chat door** — it opens no conversation — so `noteChatDoor` is not called.
5. **The door census.** `chatWiring.test.ts:462-470` derives the doors from EVERY `webview/context` item
   and would count this one. The derivation excludes items scoped to `webviewId == 'coaiChat'` — our own
   page's menu acts on a conversation that is already open — with the two-way check `:472-490` already
   uses for `actsOnOurPicker`: every excluded item really is scoped to our page, and nothing that opens a
   chat hides behind the scope.

## What is deliberately NOT here

- **No new binding between a chat and a tab.** The command only follows what the conversation already
  records; it never re-files a conversation (that is *go to*'s `bind`).
- **No guessing by title.** A `none` source with no live tab is `nowhere`, said out loud — the title rule
  of `chatGoto.ts` (never a key) holds here too.

## Risks

- `claude-vscode.editor.open` is internal to Claude Code and can change. Mitigated: B first, A only when
  the command is listed, and a refusal naming the session when it is not.
- Claude's panel key after `/clear` or a fork may not be the `.jsonl` UUID the source holds; then A opens
  a panel on the recorded session rather than the tab the person is thinking of. Checked live (below).

## Build order

1. `chatReturn.ts` + `chatReturn.test.ts` — every arm of `backTo`, the live tab winning, `none` → nowhere.
2. `RevealablePanel.isActive()`; the page's `data-vscode-context` and its rewrite on *New chat*
   (`chatPage.test.ts` / the page-run harness: present, carries the id, follows a reset).
3. `chatReturnCommand.ts` — conversation lookup (argument first, active panel second), the B recipe, A
   behind `getCommands`, `showTextDocument`, refusals.
4. Manifest + registration; `chatReturnWiring.test.ts` (command, menu scope, chord and its `when`,
   registration with `.catch`, no `noteChatDoor`, every arm of the union executed with an exhaustive
   `never`); `chatWiring.test.ts` door derivation with the two-way scope check; notification census.
5. Docs: `research/module_extension.md`, CHANGELOG `## Unreleased`, `research/module_tests.md` flow row.

## Test plan

- `cd src_vs_code && npm test`, `npm run lint`, `plan-lifecycle.mjs`.
- Every new guard proved by breaking it (the RED-GREEN-RED order).
- **In a real editor (`npm run test:host`, the harness CI runs):** a scenario opens a document, opens a
  coai chat keyed on that document's tab, activates another editor, runs `coai.backToSource`, and asserts
  the document's tab is the active one — the B recipe executed by VS Code itself, not by a stub. If the
  harness cannot open a chat keyed on a tab, the scenario says so and the gap is recorded.
- **By hand, which this agent cannot do:** right-click inside a chat opened from a Claude Code tab, and
  the chord, and a reload (path A). Listed in the PR as NOT observed by the author, for the person to try.

## Definition of Done

- [ ] Right-click in a coai chat offers the command, and it activates the tab the chat came from.
- [ ] The chord does the same from an active chat, and *go to* still works from a Claude tab.
- [ ] A restored chat goes back by its recorded session; a file chat to its file; an unknown source says so.
- [ ] The door census does not count the new command, and fails if a real door hides behind the scope.
- [ ] Tests for every step; `npm test`, `npm run lint` green; live check reported.
- [ ] `module_extension.md`, `module_tests.md` and the CHANGELOG updated; this plan promoted to `research/`.
