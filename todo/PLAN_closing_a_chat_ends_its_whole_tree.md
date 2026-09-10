# PLAN — closing a chat ends its whole tree

> Status: **plan only, nothing implemented yet, 2026-09-10.** Scope: `src_vs_code/src/processLauncher.ts`.
> Finding 8 of [the product audit of 2026-09-09](../research/REVIEW_product_audit_2026-09-09.md).
>
> Related docs: [module_extension.md](../research/module_extension.md);
> [PLAN_the_chat_is_correct_on_its_own_side.md](../research/PLAN_the_chat_is_correct_on_its_own_side.md)
> — the orphan sweep, which collects what this plan stops creating.

## The symptom

`killTree` (`processLauncher.ts:373-383`) reaches for `taskkill /t` only on Windows AND only when the
child was started through a shell (`:375`). Everywhere else — Linux, macOS, WSL, and Windows without a
shell — it is `child.kill()` (`:376`), which ends the direct child and nothing under it. Two things
make that the ordinary case rather than a corner:

- a vendor CLI spawns children of its own (MCP servers, helpers), and
- on Linux `codex` and `gemini` are `#!/bin/sh` shims (`module_tests.md`), so the direct child IS a
  shell and the CLI is its grandchild. `child.kill()` ends the shim and leaves the conversation
  process running — mid-turn, still spending, still signed in as the person.

The audit reproduced it in WSL: a node child that launched `/bin/sleep 3`; after `handle.kill()` the
parent was gone and **the grandchild was alive 100 ms later**. The extension's own header at `:10`
records the same shape being found once already, for a probe. The orphan ledger recovers such a
process on the NEXT activation; closing a tab is supposed to end it now.

## The change

1. **A process group on Unix.** `launch` spawns with `detached: true` on every platform but Windows
   (`:113`), so the child leads a group of its own — and nothing calls `unref()`, so the extension
   host still waits for it exactly as now. `killTree` then ends the GROUP: `process.kill(-pid, 'SIGTERM')`,
   and `SIGKILL` after a short grace if the direct child has not exited.
2. **Windows without a shell takes the `taskkill /t` path too** — the condition at `:375` drops the
   `shell` half. The absolute-path rule for `taskkill` and the reason for it stay as they are.
3. **The pid-reuse caveat in the file's header still governs.** A group kill by `-pid` has the same
   hazard as a kill by `pid`; it is guarded by the handle's own `exited` flag, and `child.kill()`
   remains the fallback when the group is already gone (`ESRCH`).

No growth surface.

### What the gate's plan round changed (2026-09-10, accepted)

- **`SIGKILL` is not conditional on the direct child** (codex, Major). As first drafted the grace step
  escalated only while the direct child was still alive — which is exactly backwards for the case this
  plan exists for: on Linux the direct child is a `sh` shim that exits IMMEDIATELY on `SIGTERM` while the
  CLI under it keeps running. So after the grace the group is signalled again unconditionally
  (`process.kill(-pid, 'SIGKILL')`), and `ESRCH` — nothing left in the group — is the success case rather
  than an error. Test 1 covers a grandchild that ignores `SIGTERM`.
- **The handle resolves on `exit`, never on `close`** (gemini, Major). `'close'` fires when the stdio
  pipes close, and a grandchild inheriting them holds them open after its parent is gone — so anything
  awaiting `'close'` waits for the very process this is trying to end. The handle's completion is bound
  to `'exit'`, and the pipes are destroyed after it. This is true of the code as it stands today and not
  something `detached` introduces; it is fixed here because this is the change that makes it reachable.

## Test plan

| # | Test | Holds |
|---|---|---|
| 1 | `processLauncher.test.ts` — *killing a chat ends its grandchild* — a real child that starts a real grandchild (`sh -c 'sleep 30 & wait'` · `cmd /c start /b ping …`), the grandchild's pid found through the platform's process list; after `handle.kill()` it is gone within two seconds | the audit's repro, on both platforms. RED today on Unix and on no-shell Windows |
| 2 | *a launch on Unix leads its own process group* — the spawn options carry `detached` off Windows and not on it | the structural half, so the effect test above cannot go green for the wrong reason |
| 3 | The existing launcher tests | the four ways a child ends still say what they said |
| 4 | *a handle whose grandchild holds the pipes still reports the child exited* | the `exit`/`close` distinction, which is what test 1 would otherwise hang on |

## Definition of Done

- [ ] Tests 1–2 written, watched fail for the real symptom, passing; test 3 unedited and green.
- [ ] Closing a tab ends the CLI and everything it started, on Windows, Linux, macOS and WSL.
- [ ] `module_extension.md` records the group and the grace.
