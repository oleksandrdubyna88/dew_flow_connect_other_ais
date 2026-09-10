# PLAN — the chat is correct on the side it actually runs on

> Status: **plan only, nothing implemented yet, 2026-09-10.** Scope: `src_vs_code` — the
> `Chat with other AI` command (`selectionCapture.ts`, `chatCommand.ts`), the orphan sweep
> (`chatOrphans.ts`, `chatLedger.ts`), and the per-side settings overlay (`sideSettings.ts`,
> `panelProvider.ts`, `extension.ts`).
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [architecture.md](../research/architecture.md),
> [PLAN_wsl_local_engine.md](../research/PLAN_wsl_local_engine.md) — whose `wslNetwork.ts` this
> plan reuses rather than re-derives.

## The symptom

Open a workspace through Remote-WSL, put the cursor in Claude Code's panel, press `Ctrl+Alt+A`.
Nothing is captured. The notification says:

> copying the selection needs Windows — copy it yourself, then use "Chat with other AI" from the
> right-click menu

The sentence is false on that machine. The VS Code window IS a Windows Electron window; only the
extension host is Linux. `powershell.exe` is one interop hop away and the synthetic `Ctrl+C` would
land in exactly the window that holds the selection.

Pulling that thread found two more defects with the same shape — code that judges *the machine* by
`process.platform`, which describes only *the extension host* — and one more with the same
consequence: the chat behaving as though it ran on a side it does not run on.

| | Where | What happens today |
|---|---|---|
| **A** | `src_vs_code/src/selectionCapture.ts:150` | `platform !== 'win32'` refuses the capture, so a WSL window can never use the keybinding |
| **B** | `src_vs_code/src/chatCommand.ts:492`, `:514`, `:648` | `vendors` is read straight off the shared configuration, bypassing this side's overlay — so with *Separate settings for each side* on, the chat launches the CLI configured for the shared set, not the one installed in this distro |
| **B′** | `src_vs_code/src/extension.ts:253` | the same bypass in `readCoaiConfiguration`, which feeds `coai-mcp`'s own settings file — so the **gate** runs its reviewers off the shared vendor list too |
| **C** | `src_vs_code/src/chatOrphans.ts:146` | `process.platform !== 'win32'` returns `'unknown'`, so under WSL (and on native Linux) an orphaned vendor CLI is never ended, its ledger row is never settled, and it is retried at every activation for ever |

The repository already states the rule all four break, twice, in prose:
`panelProvider.ts:848-850` and `vendorTerminal.ts:180-182` — *"in a VS Code window connected to WSL
`process.platform` is `'linux'`, whatever the machine's badge says."* `localEngines.ts` obeys it by
asking `runningUnderWsl()` separately. Nothing in this plan invents that doctrine; it puts it in one
place and makes three more callers obey it.

B is not a flaw in the per-side machinery — that machinery is right. `panelProvider.ts:1019` already
computes the correct answer and its own doc already says why it must be the only one: *"One accessor,
because a read that goes around it is a setting that silently stays shared."* The accessor is
`private`, so three callers went around it.

## What was measured before any of this was designed

Every number below was taken on the operator's own machine on 2026-09-10, not reasoned out.

**The Windows side is reachable from the WSL extension host.** Its live process (`pid 1144`,
`~/.vscode-server/bin/.../node`) carries `WSL_INTEROP` and **33** Windows entries on its `PATH`,
including `/mnt/c/WINDOWS/System32/WindowsPowerShell/v1.0/`. `execvp` from exactly that environment
resolves `powershell.exe`. So `launch('powershell.exe', …)` needs no change at all — only the gate
in front of it does.

**The helper costs about a second through interop.** The full script (the `Add-Type`, the
`GetAsyncKeyState` wait, the sleeps) run from WSL: **1080 / 1059 / 1069 ms** with `cwd=/tmp`, and
**1875 / 1572 / 1360 ms** with `cwd=/mnt/c`. Natively on Windows the same script is ~630 ms, and the
feature's own documented budget is ~1.7 s. So `cwd: os.tmpdir()` — which under WSL already IS `/tmp`
— is the faster of the two, `pressCopy` needs no cwd change, and `ran()`'s 6000 ms cap keeps a factor
of five of headroom. **The progress label and the cap are both left alone**, and this paragraph is
why, so a reviewer does not read the untouched lines as an oversight.

**A Linux child's identity is readable without a subprocess.** Against a live process: the ctime of
`/proc/<pid>` gave `1789026765000` against a real start of `1789026765179` — 0.2 s out, inside the
existing `NEAR_ENOUGH_MS = 10_000` by three orders of magnitude. `stat -c %W` (birth time) returns
`0` on procfs and is NOT usable. `/proc/<pid>/task/<pid>/children` exists and answers.

**The image name must not be taken from `argv[0]` — measured, and it refutes the obvious design.**
On this machine's WSL the four CLIs are four different shapes:

| CLI | What it actually is |
|---|---|
| `claude` | `/usr/bin/claude` → a native ELF binary; `comm` is `claude` |
| `agy` | `/home/jinx/.local/bin/agy` → a native ELF binary |
| `codex` | `/mnt/c/Users/…/AppData/Roaming/npm/codex` → a `#!/bin/sh` script, in the **Windows** npm directory |
| `gemini` | same shape, same directory |

For a shebang script the kernel runs the interpreter, so `argv[0]` is `/bin/sh` and `/proc/<pid>/exe`
is `/bin/dash`. A verifier that compares the recorded image against `argv[0]` — or against `comm`
alone, which Linux also truncates to 15 characters — answers *"not ours"* for `codex` and `gemini`
and keeps their rows for ever: the same orphan that is never cleaned up, reached by a different
route. **The rule is therefore: `comm` OR the basename of ANY `cmdline` entry.**

That table is also the evidence for B. On this very machine a bare `codex` in WSL resolves through
the interop PATH into the Windows npm directory — which is exactly the trap
`CHANGELOG.md` recorded at extension 0.17.0, and exactly why a side's own `executablePath` must be
the one the chat reads.

## The design

### One new module names the missing concept

`src_vs_code/src/hostSide.ts` — small, pure but for one existing call, and it answers the question
the codebase asks in three places and gets wrong in a fourth.

```ts
/** This extension host's own operating system — never the remote machine's. */
export function hostPlatform(raw: string = process.platform): Platform;

/** How this host can reach a live Windows session to run a Windows-only operation. */
export type WindowsReachKind = 'direct' | 'interop' | 'none';
export interface WindowsReach { readonly kind: WindowsReachKind; }

/** The reach implied by platform and WSL-ness alone — NOT a live probe. */
export function classifyWindowsReach(platform: Platform, isWslHost: boolean): WindowsReach;

/** The same, asking the two cheap facts itself. Both injected, for the tests. */
export async function windowsReach(
  platform: string = process.platform,
  underWsl: () => Promise<boolean> = runningUnderWsl,
): Promise<WindowsReach>;
```

**Three, not two, is why this is a module and not an inline check.** `versionProbe.ts:194-196` and
`panelProvider.ts:2154-2156` are byte-identical narrowings of `process.platform`; `selectionCapture.ts:150`
is a third, wrong one. Both duplicates are deleted in favour of `hostPlatform()`; the two prose
statements of the doctrine shrink to a pointer at this file.

**`WindowsReach` is a three-valued type rather than a boolean because the person sees the
difference.** "There is no Windows side here" (darwin, native Linux), "the Windows side could not be
reached through interop" and "PowerShell itself would not run" are three different things to do next,
and a boolean can express two of them at most.

**Nothing here probes anything.** `classifyWindowsReach` is pure; `windowsReach` composes it with the
existing `runningUnderWsl()`. Whether interop then actually works is answered by ATTEMPTING it —
the same discipline `wslNetwork.ts:14-20` already imposed on itself when three reviewers refused a
gateway probe. `wslNetwork.ts` is imported from and otherwise **left untouched**: its `interop()` is
not moved, not wrapped and not merged, so `processLauncher.ts:8-13`'s standing objection to exactly
that refactor is answered by not doing it.

### A — the capture asks reach, and says which failure it hit

`ran()` stops returning `void` and returns what happened, so a helper that never started is no longer
indistinguishable from a selection that was empty:

```ts
export interface RunOutcome {
  readonly ok: boolean;
  /** Empty when ok. Why the HELPER could not finish — never why nothing was selected. */
  readonly refusal: string;
}
export function ran(child: ProcessHandle, after: …, capMs = 6000): Promise<RunOutcome>;

export async function captureSelection(
  run: () => Promise<RunOutcome>,
  clipboard: Clipboard,
  reach: WindowsReach,          // replaces `platform: string = process.platform`
): Promise<Capture>;
```

Four outcomes, four sentences:

| Situation | What is said |
|---|---|
| `kind === 'none'` | today's wording, unchanged — it is still true on darwin and native Linux |
| the helper never ran, `kind === 'interop'` | *the Windows side of this machine could not be reached through interop (…)* — deliberately the same phrasing already shipped at `panelProvider.ts:1206-1209`, not a fourth wording for one fact |
| the helper never ran, `kind === 'direct'` | *copying the selection failed (…)* — a case that existed before and could not be observed |
| the helper ran, the clipboard landed nothing | today's *nothing was copied — select the text first*, which is now only said when it is true |

The clipboard contract does not change: borrow, unique marker, restore only under `shouldRestore`,
never write over something that could not be read. A helper that never ran gives the borrow straight
back.

`pressCopy` keeps calling `argvFor(COPY_SCRIPT)` with the bare literal, so the structural guard at
`chatWiring.test.ts:77-95` — which forbids anything but that identifier reaching PowerShell — holds
by construction. No new `spawn` site is added anywhere in this plan: A reuses `launch()`, C uses
`process.kill`.

### B and B′ — one accessor, promoted

`sideSettings.ts` gains the decision `panelProvider.ts` already computes correctly:

```ts
export function sideConfigReader(
  shared: ConfigReader, perSide: boolean, store: KeyValueStore, side: Side,
): ConfigReader;
```

`panelProvider.ts:1019` becomes a one-line delegation to it. `chatCommand.ts` takes
`context: vscode.ExtensionContext` from its registration in `extension.ts` and builds the reader per
invocation (fresh, because the person may have edited settings or the switch since), replacing the
three `vendors` reads at `:492`, `:514`, `:648`. `extension.ts`'s `readCoaiConfiguration` captures the
`context` it already has and builds the same reader.

**What is deliberately NOT changed:** the reads of `chatAutoSend`, `language` and `teamServers`
(`chatCommand.ts:274`, `:478`, `:614`, `:1029`). Those keys are not in `OVERLAID_SETTINGS` and
`settingsShape.ts:235-237` says why — they are about the person reading the panel, not about the
company the work belongs to. Reading them off the shared configuration is correct today and stays.

### C — the sweep gets a POSIX half, and it is testable

`chatLedger.ts` (pure) gains the comparison; `chatOrphans.ts` gains the world-facing half with both
of its world-touching operations injected, so every branch is a test on a Windows dev machine with no
`/proc` and no signals:

```ts
// chatLedger.ts — pure
export function stillOurs(record: ChildRecord, observed: { image: string; startedMs: number }): boolean;

// chatOrphans.ts — the world, injected
type ProcEntry =
  | { kind: 'found'; image: string; startedMs: number }
  | { kind: 'absent' }     // /proc/<pid> is gone — safe to call it 'gone'
  | { kind: 'unknown' };   // anything else — never assumed dead

export async function endIfOursPosix(
  record: ChildRecord,
  observe: (pid: number) => Promise<ProcEntry> = procEntry,
  end: (pid: number) => void = (pid) => process.kill(pid, 'SIGKILL'),
): Promise<KillOutcome>;
```

`procEntry` reads `/proc/<pid>/comm`, `/proc/<pid>/cmdline` and the ctime of `/proc/<pid>`, and
derives the image by the **measured** rule above: `comm`, or the basename of any `cmdline` entry.
`endIfOurs` becomes a three-way dispatch on `hostPlatform()` — `win32` unchanged, `linux` new,
`darwin` explicitly `'unknown'` exactly as today.

**A single-pid kill, not a tree, and the reason is evidence rather than preference.** Windows needs
`taskkill /t` because a Windows vendor CLI is a shim tree (`codex.cmd` → `cmd.exe` → `node`).
`cliVersions.ts:184-191` gates `needsShell` to `win32 && /\.(cmd|bat)$/`, so on Linux `spawn` never
goes through a shell and the recorded pid IS the vendor process. `/proc/<pid>/task/<pid>/children`
was measured to work and is the way to add a walk later, on evidence, if a vendor is ever found to
daemonise.

`'unknown'` keeps meaning *keep the row and ask again* — the idiom this whole module is built on.
The `EPERM` case is `'unknown'`; only `ESRCH` is death.

## Build order

Each phase is green before the next starts.

0. Baseline: `cd src_vs_code && npm run compile && node scripts/run-tests.mjs`.
1. **`hostSide.ts`**, tests first. Then delete `versionProbe.ts:194-196` and
   `panelProvider.ts:2154-2156` in its favour and shorten the two prose comments.
2. **B and B′**: `sideConfigReader` with its tests, then `panelProvider.ts` (behaviour-identical),
   then `chatCommand.ts` + `extension.ts`, with the structural guards.
3. **A**: `ran()` → `RunOutcome`, `captureSelection` → `WindowsReach`, then `passageFor`. Migrate the
   existing capture tests, then add the new ones.
4. **C**: `stillOurs`, then `endIfOursPosix` + `procEntry`, then the dispatch.
5. Text: `helpContent.ts` and its four localisations, `CHANGELOG.md`.
6. `npm run typecheck`, full suite, then the gate's code round.

## Test plan

Every item below is written and watched to FAIL first, against the unfixed code, with a failure
message that names the real symptom.

**`hostSide.test.ts`** (new)
- *a Windows host can always reach itself directly, without asking whether it is WSL*
- *a WSL host is a candidate for interop, whether or not it turns out to work*
- *a plain Linux host with no WSL has no Windows side to reach*
- *darwin never has a Windows side to reach*
- *hostPlatform narrows anything that is neither win32 nor darwin to linux*
- *windowsReach never asks whether it is WSL when the host is already Windows* (a spy, 0 calls)

**`chatCapture.test.ts`** (extended; the existing tests migrate `'win32'` → `{ kind: 'direct' }`,
`'darwin'` → `{ kind: 'none' }`, and their `run` fakes return `{ ok: true, refusal: '' }`)
- *a WSL window captures the selection exactly as a Windows one does*
- *when the Windows side cannot be reached through interop, the refusal says so — not that nothing was copied*
- *a direct Windows host whose helper itself fails also says so, not that nothing was copied*
- *ran resolves ok on a clean exit, and not-ok with a reason on an error or on the cap*

**`sideSettings.test.ts`** (extended)
- *a side with its own overlay reads its own vendors, not the shared list*
- *the switch off reads the shared vendors even when this side has an overlay recorded*

**`chatWiring.test.ts`** (extended — structural, the technique the file already uses for `argvFor`)
- *the chat reads vendors only through the per-side reader, never straight off the shared configuration*
- *the server settings file is fed this side's settings, not only the shared ones*

**`chatLedger.test.ts`** (extended)
- *a process whose image and start time both match is still ours*
- *an image that does not match is not ours, whatever else does*
- *a start time outside the ten-second slack is not ours even under the same name*

**`chatOrphans.test.ts`** (new — `observe`/`end` injected)
- *a Linux child that still matches is ended*
- *a Linux child whose pid now belongs to something else is left running*
- *a Linux child that is simply gone is not asked to die twice*
- *a Linux child /proc could not be asked about is kept and retried, never assumed dead*
- *a kill that races the process's own exit is reported gone, not unknown*
- *a kill we are not allowed to make keeps the row*
- *a shebang CLI whose argv[0] is the interpreter is still recognised by its own name*

The last one is the measurement above turned into a regression: it fails against any implementation
that reads `argv[0]` or `comm` alone.

## Definition of Done

- [ ] `Ctrl+Alt+A` captures the selection in a Remote-WSL window, verified by hand in a real WSL window.
- [ ] A WSL window that cannot reach the Windows side says so, and does not claim nothing was selected.
- [ ] darwin and native Linux keep today's refusal, word for word.
- [ ] With *Separate settings for each side* on, the chat and the gate both use THIS side's `vendors`.
- [ ] With it off, every read is the shared value, exactly as before.
- [ ] An orphaned vendor CLI is ended under WSL and on native Linux, including `codex`/`gemini` shebang shims.
- [ ] `process.platform` is narrowed in exactly one place.
- [ ] No new `spawn` site; `argvFor` still receives only `COPY_SCRIPT`.
- [ ] Every RED test above was watched to fail with the real symptom, then to pass.
- [ ] `npm run typecheck` and the full suite are green.
- [ ] `helpContent.ts` and its four localisations no longer promise Windows-only copying.
- [ ] `CHANGELOG.md` carries the change; `research/module_extension.md` records the design.
- [ ] The gate's plan round and code round both reached `proceed`, with every finding resolved.
- [ ] This plan is promoted to `research/` with `IMPLEMENTED <date>` and its deviations recorded.
