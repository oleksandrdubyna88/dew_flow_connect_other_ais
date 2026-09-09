# PLAN — Chat with other AIs, from a selection

> Status: **IMPLEMENTED, 2026-09-09.** All six phases shipped, each through this product's own gate:
> the trigger and the panel section in 0.31.9–0.31.12, all three vendor CLIs in 0.31.13, and the
> Team-server path in 0.31.15. A selection in a Claude Code session, one keypress, a tab named after
> that session holding another vendor's answer — locally through a long-lived CLI process, or
> remotely as a job on the company subscription, bounded at three turns.
> One item is owed and it is not ours to build: chat turns are DISTINGUISHABLE in the spending view
> (they carry no review role) but the view does not yet group on it, which needs a server change —
> [../todo/PLAN_the_server_knows_a_chat_from_a_review.md](../todo/PLAN_the_server_knows_a_chat_from_a_review.md).
>
> **Deviations.** Three, all of them measurements overruling the plan. (1) The plan wrote off two of
> the three vendor CLIs — *"claude's schema differs and codex exec has no multi-turn stdin"* — and
> both halves were wrong: `claude` holds a conversation exactly as `agy` does and answers faster than
> either, `codex` holds one through a session it resumes rather than a pipe it keeps. That refutation
> became its own story, [PLAN_three_chat_adapters.md](PLAN_three_chat_adapters.md). (2) The plan said
> the transcript is never truncated; three reviewers refused it from two directions and the budget is
> now measured and bounded — 60 000 characters to a pipe, 20 000 to a server, the smaller one because
> a JSON body through whatever sits in front of a server is where a 413 arrives at turn three.
> (3) The plan assumed a chat turn would carry a `Chat` role; the live server refuses that in 0.2 s
> and is right to, so a chat carries no role at all — and, since 0.31.15, a `kind` field sent ahead of
> the server that will read it.
>
> **The tail that became its own plan**: a vendor process that dies mid-conversation still loses the
> context, and re-sending the whole transcript to cover somebody else's crash spends money silently —
> [../todo/PLAN_a_dead_process_could_carry_the_conversation_too.md](../todo/PLAN_a_dead_process_could_carry_the_conversation_too.md),
> deliberately gated on how often it actually happens.
>
> Scope: `src_vs_code` only — one command, one webview panel per Claude Code
> session tab, a long-lived vendor-CLI process behind each panel, and the Team server's existing job
> endpoint for remote models. **No change to `src_mcp` or `src_server` was needed**, which held.
>
> Related docs: [architecture.md](architecture.md), [module_extension.md](module_extension.md),
> [../ARCHITECTURE.md](../ARCHITECTURE.md).

## The symptom

Claude Code answers in dense English prose. The owner reads it, does not trust his reading of a
passage, and today performs five manual steps, several times an hour:

1. select the passage in the Claude Code panel,
2. `Ctrl+C`,
3. switch to the Gemini app (an Edge hub app on `https://gemini.google.com/app`),
4. **New chat**, `Ctrl+V`, type "объясни",
5. `Enter`.

The goal is one step: select, invoke, and land in a conversation with another vendor's model —
inside VS Code, in a tab that can be zoomed, that remembers which Claude Code session it belongs to,
and that can be talked back to.

## What was measured before this plan (2026-09-08)

Every number below is an observation on this machine, not an estimate. They are recorded because
three of them killed a design that looked obvious — and one of them later refuted a reviewer.

| Question | Answer | How it was established |
|---|---|---|
| Can a prompt be pre-filled through the Gemini URL? | **No.** `?q=…` opens an empty new chat. | Launched `msedge --app=https://gemini.google.com/app?q=TEST-COAI-PREFILL-12345`; the composer was empty, confirmed by the owner. |
| Is there a Gemini desktop app / `gemini://` handler? | **No.** It is an Edge hub app (`hub_app_preferences/user_generated`, id `8276dd57-…`). | Registry `Classes` scan; Start-Menu and PWA directory scan; `Preferences` walk. |
| Can the Gemini window be driven from outside? | **Yes, but only via WinAPI.** `SendKeys` delivers nothing to Edge; `keybd_event` does. | Same window, same second: `SendKeys` readback 0 chars, `keybd_event` readback 17 107 chars. |
| Does the `gemini` CLI work here? | **No.** `IneligibleTierError: This client is no longer supported for Gemini Code Assist for individuals.` | `gemini -p …` from a clean directory. The repository already knew: `vendors.ts:270` migrates a `gemini` runtime row to `antigravity`. |
| Does `agy -p` read stdin? | **No.** Piped text never reaches the model. | Piped `probe.txt`; the model replied "Вы не прикрепили сам фрагмент". |
| `agy` NDJSON input schema | `{"event":"user","message":{"role":"user","content":"…"}}` | The CLI rejected `type:` with `stream input message is missing the "event" field`; the binary's own strings name the rest. |
| Latency, cold one-shot | **10–14 s**, of which **3.6–6.6 s is process startup** | `--output-format stream-json`, per-line timestamps. |
| Latency, warm session | first turn ~11.6 s · real explanation **9.4 s** · short follow-up **4.3 s** | Three turns down one NDJSON pipe into one process. |
| Does the answer stream in? | **Not usefully.** Eight seconds of silence, then the whole text in about one second. | Per-event timestamps on the 9.4 s turn. |
| **Does `--mode plan` survive multi-turn?** | **Yes.** One process answered three turns, all `SUCCESS`; turn 3 resolved "now simpler" against turn 2 without the passage being repeated. | The same three-turn run. This is the measurement that refuted gate finding 10. |

**The consequence.** The Gemini *app* can be driven, but only by clicking coordinates in somebody
else's UI. The `gemini` CLI is dead. `agy` works, keeps context, and answers a real question in
about nine seconds — so the conversation belongs **inside VS Code**, and the window automation is
not built. The owner chose this route and accepts the latency.

## What the Claude Code webview allows

Read out of the installed extension, `anthropic.claude-code-2.1.263-win32-x64`:

| Capability | Verdict | Evidence |
|---|---|---|
| Inject a floating button next to the selection | **Impossible** | Neither `data-vscode-context` nor `webviewSection` occurs anywhere in `extension.js` or `webview/index.js`. There is no DOM seam to attach to, so the owner's ideal — a button that appears on selection — cannot be built by anyone but Anthropic. |
| Add an item to the panel's right-click menu | **Confirmed live** | Phase 0. |
| Bind a key that fires inside the panel | **Confirmed live** | Phase 0. |
| Receive the selected text as a command argument | **No** | The menu hands the command `{"webview":"claudeVSCodePanel"}` and nothing else. |
| Identify which session the selection came from | **Confirmed live** | Phase 0. |
| Generate menu items from settings | **Impossible** | `contributes.menus` is a static manifest: neither the count nor the titles can follow a user's model list. Only visibility can be gated (`when: config.…`). This is why there is ONE menu item and the model is chosen in the tab. |

## Phase 0 — DONE, 2026-09-08

A throwaway extension (`~/.vscode/extensions/coai-probe`, three files, deleted by removing the
folder) settled the three questions that could not be settled by reading.

| # | Question | Result |
|---|---|---|
| **R1** | Does a THIRD-PARTY `webview/context` item render in another extension's webview? | **Yes.** The item appeared under Claude Code's own three, and VS Code hands the command `[{"webview":"claudeVSCodePanel"}]` — so the invocation even names the webview it came from. |
| **R2** | Does a synthetic `Ctrl+C` capture the webview's selection? | **From a keybinding, yes. From the menu, no.** Keybinding: 638 chars of a real paragraph, clipboard sequence number moved, 1725 ms. Menu: `clipboard untouched`, twice, with the foreground window correctly reported as `proc=Code`. |
| **R3** | Can the source session be named from outside? | **Yes.** `tab.input.viewType` is `mainThreadWebview-claudeVSCodePanel` and `tab.label` is the session's name — correct even with the panel in a second editor group. |

**Why the menu path cannot copy.** Closing the context menu takes the keyboard focus (or the
selection) out of the webview; the keys arrive at VS Code and there is nothing to copy. The
clipboard sequence number does not move at all, so this is not a timing race.

**Refuted, and recorded so it is not retried:** issuing `workbench.action.focusActiveEditorGroup`
before the copy was meant to put the keyboard back. It made things worse — with the Claude Code
panel in a second editor group beside a Welcome tab it moved focus to the *other* group, and the
keybinding path that had been working stopped capturing too. Reverted after one control run.

**The two capture strategies, both measured live:**

| Trigger | Strategy | Measured |
|---|---|---|
| Keybinding, panel-scoped | Save the clipboard, synthetic `Ctrl+C`, read it, restore it. | 638 chars in 1725 ms — nearly all of it PowerShell startup |
| `webview/context` item | **Read the clipboard as-is** — the person pressed `Ctrl+C` themselves, and *Copy* is one line above ours in the same menu. Never write a sentinel on this path: it would destroy the text they just copied. | 18 chars in **2 ms** |

## What the gate changed (2026-09-08)

The plan went through this product's own gate on branch `plan/chat-with-other-ais`
(session `ded44830`): three reviewers — codex, gemini, local — answered, 15 findings, verdict
`good_enough` at the one-round plan budget. **Fourteen were accepted and are folded into the design
below; one was rejected.**

| Theme | What was wrong | Where it is answered now |
|---|---|---|
| Session identity (2 reviewers) | A label is not an identity. Two Claude Code tabs called `main` collapse into one panel and a follow-up lands in the wrong conversation. | *Which session a panel belongs to* |
| Process lifecycle (4 findings) | Nothing said the CLI is killed when the tab closes, what happens when it exits mid-turn, or what happens to children when VS Code is force-killed. | *The life of a session process* |
| Remote transcript growth (2) | Left as an "open question" while the design depends on it: the re-sent transcript grows until the request is refused. | *A remote conversation is bounded* |
| `429` handling | The plan called it "not a failure" without saying what it IS. | *A remote conversation is bounded* |
| Clipboard freshness (3) | The menu path can spend a real vendor call on text copied an hour ago, and showing it afterwards does not prevent that. | *Sending at once, or waiting for a press* |
| Clipboard restore race | The 1725 ms window can clobber something the person copied meanwhile. | *Sending at once, or waiting for a press* |
| Overlapping turns | Nothing said a second turn cannot be sent while the first is in flight. | *The life of a session process* |

**The one rejection — finding 10, `--mode plan` blocks multi-turn (gemini, Blocking).** The reviewer
argued that a planning mode is a single-prompt batch that exits or refuses follow-ups down an NDJSON
pipe. That is true of some CLIs and was **measured false for this one** before the plan was written:
one `agy --mode plan --input-format stream-json --output-format stream-json` process took three
NDJSON turns and answered all three `SUCCESS` (init 3.6 s; results at 11.6 s, 21.3 s, 25.8 s), and
turn 3 — "now simpler, in two sentences", with the passage not repeated — was answered against turn
2. Both the process and the conversation survived. The flag choice came from that run, not from
documentation. **What the finding does earn is a regression test**, because a CLI upgrade could make
the reviewer right later: `cliChatSession.test.ts` pins that a second turn is answered by the same
process.

## Design

### The surfaces

- **A panel section named `Chat other AIs`**, built like `Reviewers`: the model rows this feature may
  use, each with its vendor, its model, and whether it is enabled. Remote rows appear when a Team
  server is configured and reachable, sourced from `/api/catalog`. Above them, **a multi-line text
  box holding the prompt the captured passage is sent with — default, the single word `Explain`.**
  A textarea rather than a one-line field because the useful prompts here grow, and a prompt that has
  to be written on one line stays short by accident rather than by choice.
- **One context-menu item, `Chat with other AI`**, in the Claude Code panel. One, not one per model,
  because the manifest cannot be generated. It takes the clipboard as-is.
- **A keybinding, `Ctrl+Alt+A`,** that captures the selection itself. Each model also gets its own
  command id, so anyone who wants a key straight to one model can bind it in VS Code's own
  keyboard-shortcuts UI — no setting of ours required.
- **The tab** is the conversation: the captured passage at the top, the model picker (shown when more
  than one model is configured), the messages, a composer, the ± zoom header.

### Which session a panel belongs to

*Answers gate findings 0 and 5.* `tab.label` is a display name, not an identity — two sessions can
share it, and the first version of this plan accepted that as a risk. It is not acceptable: the
consequence is a follow-up delivered to the wrong conversation, which is silent and wrong rather than
visible and broken.

The key is **the `vscode.Tab` object itself**, held by reference in the map. While a tab is open its
object is the identity the host already maintains, and two tabs named `main` are two objects. The
label is used for the panel's title and nothing else. Two consequences are designed in rather than
discovered:

- **A tab object that is gone** (window reloaded, tab closed and reopened) is not the same session,
  and its conversation is not resurrected. The panel closes with it.
- **If the host ever hands back a new object for the same live tab** — a rename is the case to fear —
  the panel is re-attached by label, and only then; a label match is a fallback, never the key.
  `sessionKey.test.ts` pins both directions, including the two-tabs-one-label case that started this.

### The life of a session process

*Answers gate findings 2, 6, 9, 11 and 14.* A long-lived child process is a resource with four ways
to end, and the first version of the plan described one of them.

1. **The panel closes → the process is killed**, through the existing `killTree` (`versionProbe.ts:135`),
   not `child.kill()`: the shim is not what must die. Disposal removes the map entry *and* kills.
2. **The process exits on its own** — token exhaustion, a crash, an OS kill. The session surfaces a
   terminal state, the panel says so in words, and the next message re-creates the process rather than
   writing to a closed stdin. An `EPIPE` is a state to report, never an unhandled rejection.
3. **A turn never terminates.** Every turn carries a budget; when it expires the turn fails visibly
   instead of leaving the page thinking forever. Startup has its own, separate budget — a process that
   never reaches `init` failed differently from one that never answers.
4. **VS Code is force-killed and disposal never runs.** Children are started so the OS reaps them
   where that is possible, and on activation the extension reconciles: any child it recorded and did
   not stop is killed before a new one is started. Orphaned, authenticated vendor processes are the
   failure this exists to prevent.

**One turn at a time.** While a turn is in flight the composer is disabled and further input is
queued, so two turns can never interleave down one pipe. This is a property of the session, tested,
not a hope about how fast people type.

### A remote conversation is bounded

*Answers gate findings 3, 7 and 12.* The Team server holds no conversation, so a follow-up re-sends
the transcript. Unbounded, that ends in a refused request after the person has built something worth
keeping — the worst possible moment to discover a limit.

- The transcript carries a **character budget**, checked before the request is built, not after it is
  refused. Approaching it is visible in the panel; crossing it offers the two honest actions —
  start again, or drop the oldest turns — and never silently truncates.
- **A remote conversation is capped at THREE turns.** The owner’s decision, 2026-09-08. A server
  model holds no conversation, so turn four re-sends everything said so far for the fourth time;
  the cost of a thread grows quadratically while its usefulness does not. At the cap the panel says
  so and offers the two honest actions - start again, or move this thread to a local model, which
  does have memory. A cap is kinder than a budget nobody can see coming.
- **`429` is transient, not terminal.** It means the queue is full, and the server says so with a
  `Retry-After`. The session backs off and retries within the turn's budget, reporting "waiting for a
  free account", and gives up only when the budget is gone.
- Every other non-2xx is terminal and is reported with what the server said.

### Sending at once, or waiting for a press

*Answers gate findings 4, 8 and 13.* The menu path cannot know whether the clipboard holds the
passage just selected or something copied an hour ago — phase 0 established that it cannot copy for
itself, and a passage shown *after* the request has already been paid for is not a check. The
keybinding has no such doubt: it took the selection itself, one moment ago.

That asymmetry is real, so it is the **default** — but it is not a law imposed on the person, because
which risk is worth which keystroke is their call, not the gate's. `coai.chatAutoSend` decides:

| Value | Keybinding | Menu item |
|---|---|---|
| `always` | sends | sends |
| `keyboard` *(default)* | sends | fills the composer, focuses it, waits |
| `never` | fills the composer | fills the composer |

The default is the only one of the three that can be defended without knowing the person: it spends a
vendor call automatically **only** where the text is certainly the one just selected. `always` is
there because the owner asked for it and may prefer it once the menu path has earned his trust;
`never` is there for anyone who wants to edit the prompt before every single question.

Filling the composer means the prompt and the passage are already in it and it holds the focus — the
cost of the safe setting is one keypress, not a re-typed question.

**The restore is guarded.** The keybinding path saves the clipboard for ~1.7 s while PowerShell runs.
Before restoring it, the clipboard **sequence number** is compared with the one taken at the start
(`GetClipboardSequenceNumber`, the same call the probe already used): if something else wrote to the
clipboard during the window, the newer content stays and the restore is skipped. Somebody else's copy
is never overwritten to tidy up after ours.

### The modules

| Module | Role | Follows / reuses |
|---|---|---|
| `chatCommand.ts` | The trigger: decide the path from the command's own argument, capture the text, resolve the source session, open or reveal its panel. | `extension.ts:116-130` registration block |
| `selectionCapture.ts` | The keybinding path: save the clipboard, send `Ctrl+C`, read it, restore it **only if nothing else wrote meanwhile**. Windows only; elsewhere a refusal that names the menu path. | the probe's `copy.ps1`, measured |
| `sessionKey.ts` | Which Claude Code tab is active, its `Tab` object as the key, its label as the title. | `vscode.window.tabGroups` (**first use in this repo**) |
| `chatModels.ts` | The one list the picker and the section share: local vendor rows plus the Team server's catalog when it answers. | `vendors.ts:90-91`, `teamServerApi.ts:278` (`fetchCatalog`) |
| `cliChatSession.ts` | One long-lived `--input-format stream-json --output-format stream-json` process per panel: one NDJSON line per turn, one turn at a time, budgets on startup and on each turn, exit surfaced and recoverable. | the hardened spawn in `versionProbe.ts:76` + `killTree` at `:135` |
| `remoteChatSession.ts` | The same interface over the Team server: submit, poll with `wait`, back off on `429`, cancel on dispose, re-send a **bounded** transcript. | `teamServerApi.ts:104` (`ask<T>`); the protocol is already implemented in C# at `src_mcp/src/AskRemote.cs:143/357/425` |
| `chatPanel.ts` | **N** webview panels keyed by the source `Tab`, each revealed or created on demand, each killing its process on disposal. | `roundsLogPanel.ts:50` — but generalised from one panel to a map (see *Deviation*) |
| `chatPage.ts` | The page: captured passage, messages, composer, thinking indicator, zoom header, model picker, the disabled-while-running state. **Pure** — no `node:` imports. | `zoomControlHtml` (`zoomControl.ts:44`), `escapeHtml` (`escapeHtml.ts:18`), `pushUiScaleTo` (`uiScaleHost.ts:34`) |
| `chatPrompt.ts` | Builds the opening instruction around the captured text. | `prompts.ts` catalog shape |

**Deviation from `RoundsLogPanel`, stated up front.** Every webview in this extension today is a
singleton — "one webview panel per window, reused while open" (`roundsLogPanel.ts:17`). This feature
needs one panel *per Claude Code session*. The panel class therefore owns a `Map<Tab, …>`, and
disposal removes one entry and kills one process rather than clearing a field.

**Two session kinds behind one interface.** `cliChatSession` holds a real conversation;
`remoteChatSession` has none and re-sends a bounded transcript. The panel must not know which it
holds — but the person must, because the difference shows up in latency, in cost and in the
conversation's length limit. The model picker says so on the remote rows.

### Which vendors can answer

The build split recorded that only `antigravity` had an adapter, because `claude`’s stream-json
schema differs and `codex exec` was believed to have no multi-turn stdin. The owner asked for all
three on 2026-09-08 and the belief was then MEASURED: `codex` holds a conversation through session
resume, and `claude` holds one exactly as `agy` does — and faster. That work is its own plan,
[PLAN_three_chat_adapters.md](PLAN_three_chat_adapters.md), because it changes the session’s shape
rather than this feature’s surface. Until it lands, a non-`antigravity` row is refused by name.

### Settings

- `coai.uiScale` (existing) — the ± zoom, already pushed to every open page.
- the vendor rows of `coai.vendors` (existing, `vendors.ts:90-91`) — where local models come from.
- **`coai.chatModels`** (new) — which of them this feature may use, and which is the default.
- **`coai.chatLanguage`** (new, default **English**) — the language answers are asked in. Its own
  setting, NOT `coai.helpLanguage`: that one is set to English on the owner's machine, so reusing it
  would have delivered English explanations — exactly what the feature exists to avoid.
- **`coai.chatPrompt`** (new, multi-line string, default `Explain`) — the instruction the captured
  passage travels with, edited in the section's textarea.
- **`coai.chatAutoSend`** (new, `always` | `keyboard` | `never`, default `keyboard`) — whether a
  captured passage is sent at once or put in the composer for the person to send. See *Sending at
  once, or waiting for a press*.

**Why prompt and language are two settings and not one.** The prompt says WHAT to do with the
passage; the language says WHICH LANGUAGE the answer comes back in. Folding the language into the
prompt would mean rewriting the prompt by hand every time it changes, and the default prompt is one
word precisely so it never has to be rewritten. The turn is assembled as prompt + language
instruction + the passage, and the passage is always last so a long selection cannot push the
instruction out of the model's attention.

### How a turn is sent

`--input-format stream-json` reads one NDJSON message per line, so the captured text travels on
**stdin** and never through argv — the trap this family has already been bitten by on Windows.
`--mode plan` keeps the agent read-only (measured to survive multi-turn — see the rejection above)
and `--disable-slash-commands` is mandatory: a passage that happens to begin with `/` must not be
expanded as a command. The process runs in an empty temp directory, not the repository: the task is
"explain this paragraph", and handing a third-party agent the source tree buys nothing but startup
time.

## Build order

**Phase 0 — the probe. DONE (2026-09-08),** results above.

**Phase 1 — `cliChatSession.ts`.** The long-lived process, NDJSON in, events out, one turn at a time,
startup and per-turn budgets, exit surfaced and recoverable, `killTree` on disposal, orphan
reconciliation on activation. The hardening in `versionProbe.ts` is extracted into a shared spawn
helper used by both `capture` and the session — widened, not copied.

**Phase 2 — `chatPage.ts` + `chatPanel.ts`.** The page and the panel map, driven by fixtures. The
thinking indicator and the disabled-while-running composer are part of this phase, not polish: the
measurement says the page sits silent for eight seconds.

**Phase 3 — the trigger. DONE (2026-09-08).** `sessionKey.ts`, `selectionCapture.ts`,
`chatCommand.ts`, `chatSettings.ts`, `chatTrigger.ts`, `chatModels.ts`, `cliChatLaunch.ts`, the
`package.json` command + keybinding + `webview/context` contribution, and `coai.chatAutoSend` with
its three values. `chatModels.ts` and the four settings arrived here rather than in phase 4 because
the trigger cannot choose a model or a prompt without them; what phase 4 still owns is the panel
SECTION that edits them.

**Phase 4 — the section.** The `Chat other AIs` section beside `Reviewers`, editing the four
settings phase 3 already reads, and the per-model command ids that make a personal keybinding
possible.

**Phase 5 — `remoteChatSession.ts`.** The Team-server path: submit, poll, `429` backoff, bounded
transcript with its visible limit, cancel, and the picker rows that say what a remote model cannot do.

**Phase 6 — docs and release.** `research/architecture.md`, the help pages (all five languages —
`helpCoverage.test.ts` enforces parity), CHANGELOG, version bump.

## Test plan

The suite is `node --test` over compiled files (`scripts/run-tests.mjs`), so every new unit must be
pure enough to test without a VS Code host — the discipline `providers.ts:6` and `roundsDbRead.ts:8`
already follow, and `bundledPage.test.ts` enforces that no page module drags `node:child_process`
into the webview bundle.

| Test | What it pins |
|---|---|
| `chatPrompt.test.ts` | The default prompt is the single word `Explain`; a multi-line prompt keeps its newlines; the language instruction is added without editing the prompt; the passage comes last and is not truncated; a passage starting with `/` is still delivered as content. |
| `cliChatSession.test.ts` | NDJSON framing is exactly one line per turn; `init`/`step_update`/`result` are parsed; an `ERROR` result surfaces as an error rather than an empty answer; **a second turn is answered by the same process** (the regression test the rejected finding earned); a turn sent while one is in flight is queued, never interleaved; a process that exits mid-turn fails that turn and is re-created for the next; a startup that never reaches `init` fails with its own message; disposal kills the tree. |
| `remoteChatSession.test.ts` | A turn becomes one job with vendor+model+prompt and **no role**; polling stops on a terminal status; **`429` backs off and retries within the turn's budget** rather than failing; the transcript is re-sent and is refused with a visible limit before it grows past its budget. |
| `chatPanel.test.ts` | Two panels for two `Tab` objects **that share a label**; the same tab reveals the existing panel; disposal removes only its own entry and kills only its own process. |
| `sessionKey.test.ts` | A Claude Code tab is recognised by `viewType`; the key is the tab object, not the label; two tabs with one label are two keys; a re-attached tab falls back to the label only after the object is gone; a non-Claude tab yields nothing. |
| `chatCapture.test.ts` | The menu argument **never writes to the clipboard**; the restore is skipped when the clipboard sequence number moved during the window; a non-Windows platform refuses with a message naming the menu path; and all three `coai.chatAutoSend` values are pinned — `always` sends on both paths, `keyboard` sends only from the keybinding, `never` sends from neither. |
| `chatModels.test.ts` | Local rows and catalog rows merge into one list; the catalog is absent when no server answers, and that is not an error. |
| `chatPage.test.ts` | The page escapes its content, shows the captured passage, carries the zoom control, renders the thinking state, disables the composer while a turn runs, and hides the picker when only one model is configured. |
| `bundledPage.test.ts` (existing) | `chatPage.ts` stays free of node imports. |
| `helpCoverage.test.ts` (existing) | The new help section exists in all five languages. |

A live check is also owed, in the spirit this repository already applies to seams: capture a real
selection, watch a real answer land in the panel over BOTH paths (a local CLI model and a
Team-server model), kill the CLI process by hand mid-conversation and watch the panel recover, and
record the observed timings beside the ones measured above.

## What grows, and who retires it

The orphan ledger writes files, which makes it a growth surface — and the rule is that a plan
introducing one names its size, its owner and what an interruption leaves behind, before the first
write. It was written first and the section second; the gate was right to ask.

| What | How big | Who retires it | Interrupted |
|---|---|---|---|
| One ledger file per extension HOST, under `globalStorageUri` | A few hundred bytes: one row per live chat child, and a conversation has one at a time. A window with three chats open holds three rows | The owning window, on every change — the file is rewritten from memory as children start and end, and holds `[]` when nothing is running | A force-kill leaves the file with its rows. The next activation of ANY window reads it, ends what is still provably that window's, and deletes the file |
| Files belonging to windows that are gone | One per force-kill that is never followed by another activation. In practice zero or one | The next activation, which removes the file once every row is settled | A sweep that runs out of its 30-second budget leaves the unasked rows in place and is retried at the next activation |
| Rows nobody could resolve | Bounded by a week (`FORGET_AFTER_MS`), after which they are not asked about at all | The same sweep | A machine with no PowerShell keeps its rows and retries for a week, then stops asking. It kills nothing in the meantime |
| A file whose owner pid was recycled onto a long-lived process | Would be invisible for ever, so it is removed once everything in it is past the week bound | The sweep, without asking about anything in it | Nothing: it is a delete, and a delete that fails is retried next time |

Nothing here is unbounded, and the failure direction is always the same one: a file kept too long
costs a few hundred bytes, and a file deleted too early costs a process nobody can find.

## Risks and open questions

1. ~~**`Tab` object identity is an assumption about the host.**~~ **Measured 2026-09-08 and it
   holds.** A reviewer called it Blocking on the claim that VS Code recreates `Tab` objects on any
   tab-group change; the probe extension logged identity across three invocations with a tab switch
   between them and reported SAME object every time. The label fallback stays as a fallback, for a
   future host rather than for this one.
2. **PowerShell startup dominates the keybinding path** — 1725 ms measured, for four WinAPI calls.
   A native binding would cut it to milliseconds and shrink the clipboard window with it; not worth
   the build complexity today, and the sequence-number guard removes the harm rather than the delay.
3. **Windows-only capture.** The keybinding path refuses elsewhere with a sentence naming the menu
   path, and that refusal is tested. The menu path is cross-platform as it stands.
4. **Cost is counted, and counted separately.** The owner’s decision, 2026-09-08: chat turns go
   into the spending view like review turns, and are DISTINGUISHED from them. Same vendors, two
   different questions - "what did the gate cost me" and "what did asking cost me" - and a single
   total answers neither. The turn carries which it was; the view groups on it.
5. **The transcript budget’s number is MEASURED, not chosen.** Confirmed by the owner, 2026-09-08.
   Phase 5 finds what the real server actually refuses and cites it; a number picked on the day
   would be a guess wearing a limit’s clothes. The three-turn cap above bounds the thread while
   that measurement is still outstanding.

## Definition of Done

- [x] **Phase 0 passed** — the menu item appeared in the Claude Code panel, the keybinding captured
      638 chars of a real selection, and the session was named from `tab.input.viewType`. Recorded
      above, with the one refuted repair.
- [x] **The plan passed this product's own gate** — three reviewers, 15 findings, 14 accepted and
      folded in, 1 rejected against a measurement, every decision recorded through `resolve`.
- [x] **Phase 3 passed the gate on its own** (2026-09-08) — 15 findings, 5 accepted, 10 rejected
      against the code or a measurement. What was taken: the copy helper now WAITS for the person’s
      own modifiers to come up (`GetAsyncKeyState`) instead of forcing them up under their fingers;
      a clipboard that reads back empty — an image, a file — is never written to, because it can
      never be given back; the conversation’s temp directory is made where a process actually runs
      and removed when the tab closes, instead of once per keypress and never; and the manifest
      wiring is now a test rather than a live check nobody reruns.
- [x] **Selecting a passage in a Claude Code session and invoking the command opens a tab named
      after that session, holding a conversation with the chosen model in the configured language.**
      Seen live on 2026-09-08 in 0.31.9: the tab carried the session’s own name, the passage stood
      at the top, the opening turn read `Explain` / `Answer in English.` over the fence, and the
      answer arrived in the tab with the composer waiting under it.
- [x] Two sessions **sharing a label** get two panels, and a follow-up never lands in the other one.
      Keyed by the entry’s own id rather than by the tab, which cost a whole code round to learn.
- [x] Follow-up questions are answered in the same conversation without re-sending the passage on the
      local path, and correctly with a bounded transcript on the remote one — 20 000 characters,
      newest turns kept, and a cut that says so inside the turn.
- [x] **Closing a tab kills its process; a process that dies is reported and re-created; VS Code
      being force-killed leaves no orphan behind — ON WINDOWS, WHERE THE MACHINE CAN BE ASKED.**
      Every child is written down as it starts and struck out as it ends, and the next activation
      ends what is still provably ours: the pid, the image and the start time must all match,
      because a pid alone would be a licence to kill whatever the operating system handed that
      number to next. **Two cases it deliberately does not cover**, because the alternative is
      killing strangers: an activation that cannot run PowerShell keeps the record and retries
      rather than guessing, and an owner pid the operating system has recycled onto a long-lived
      process makes its file unreadable — bounded by removing it after a week, and by the reboot
      that would end the orphan anyway. Named here rather than left to be discovered.
- [x] A turn cannot start while another is running. Three times over: the page disables its composer,
      the session serialises its own turns, and the thread chains them — because the keybinding does
      not go through the composer.
- [x] `coai.chatAutoSend` decides who sends: at the default the keybinding sends and the menu waits
      with the composer filled and focused, and both other values behave as the table says. The
      clipboard is never restored over something newer.
- [x] A remote conversation stops at three turns, saying so and offering the local model that has
      memory - rather than growing a transcript until the server refuses it.
- [ ] **NOT DONE, and moved out**: chat turns appear in the spending view, separated from review
      turns. They are already distinguishable — a usage row with no review role is a conversation —
      but grouping on it is a server change:
      [../todo/PLAN_the_server_knows_a_chat_from_a_review.md](../todo/PLAN_the_server_knows_a_chat_from_a_review.md).
- [x] The model picker lists local rows and, when a Team server answers, its catalog rows too — and
      a row that cannot answer is listed WITH ITS REASON rather than quietly missing.
- [x] The section holds a multi-line prompt box, defaulting to the single word `Explain`, and what it
      holds is what the passage is actually sent with.
- [x] The page zooms with the existing ± control and the setting is shared with every other page.
- [x] A running turn is visibly running — and on a Team server it says where in the queue it is —
      and a failed turn says what failed.
- [x] All tests above pass, `npm test` green (996 tests, 995 pass, 1 pre-existing skip), and both
      paths were measured live: a local CLI turn in 4.5 s cold / 1.6 s warm, a Team-server turn in
      3.8 s through `coai.remsoft.dev`.
- [x] `research/architecture.md`, the five help languages and the CHANGELOG are updated.
- [x] This plan is promoted to `research/` with `IMPLEMENTED <date>` and its deviations recorded.
