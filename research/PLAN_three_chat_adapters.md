# PLAN — All three CLIs answer a chat, not just one

> Status: **IMPLEMENTED, 2026-09-09.** All three vendor CLIs answer a chat, behind one seam
> (`chatAdapter.ts`) with three implementations. Verified live through this build's own session:
> two turns each, the second asking for a number planted in the first, all three kept it.
>
> **What shipped differently.** The plan expected two shapes and found a third axis: **who speaks
> first**. `claude` says nothing at all until it is asked — given an empty stdin it exits without a
> word — so a session that waits for a ready event spends its whole startup budget and reports a
> CLI that never started, for one that was working. That is `ChatAdapter.announces`, and phase 0
> had only half-caught it (it knew `init` came once per TURN; it did not know none comes before
> one). Two more the plan did not foresee: `spawn` searches neither PATHEXT nor the shell, so a
> bare `codex` on Windows dies with `ENOENT` at the first turn — `resolvedExecutable` and
> `LaunchSpec.shell` answer that now; and `codex --json` names its thread, so the resume is by ID
> rather than by `--last`, which would have made two chat tabs answer each other.
>
> **What the gate added over three rounds.** A codex thread id is validated before it can reach an
> argv that goes through `cmd.exe`; a thread that cannot be resumed is dropped instead of retried
> for ever; a per-turn exit names its code; and the live check became `npm run test:live`, in the
> repository rather than in a scratch directory.
>
> Scope: `src_vs_code`. Related docs: [../todo/PLAN_chat_with_other_ais.md](../todo/PLAN_chat_with_other_ais.md)
> (the master plan this extends), [module_extension.md](module_extension.md).

## Why this plan exists

The master plan's build split recorded a limitation as fact:

> "Only `antigravity` was measured; `claude`'s stream-json schema differs and `codex exec` has no
> multi-turn stdin. 3.1 filters local rows to runtimes with an adapter (`antigravity` today) and
> refuses the rest naming the supported runtime."

The owner asked for all three to work (2026-09-08). **The limitation was then measured, and half of
it was wrong.** `codex` does hold a conversation — through session resume rather than a persistent
pipe — and `claude` holds one exactly as `agy` does. Nothing here is a guess: every row below is a
run on this machine.

## What was measured (2026-09-08)

| | `agy` (antigravity) | `claude` | `codex` |
|---|---|---|---|
| **Shape** | one process, many turns | one process, many turns | **one process PER turn**, resumed |
| **Invocation** | `--input-format stream-json --output-format stream-json` | `-p --verbose --input-format stream-json --output-format stream-json` | `codex exec "…"`, then `codex exec resume --last "…"` |
| **Turn in** | `{"event":"user","message":{"role":"user","content":"…"}}` | `{"type":"user","message":{"role":"user","content":[{"type":"text","text":"…"}]}}` | argv (or stdin with `-`) |
| **Ready event** | `{"event":"init"}` | `{"type":"system","subtype":"init"}` — **once per turn**, not once per process | none; the process exits when done |
| **Answer event** | `{"event":"result", result.status==="SUCCESS", result.response}` | `{"type":"result","subtype":"success","result":"…"}` | last stdout line, or `--json` events, or `-o <file>` |
| **Context preserved** | **yes** — turn 3 resolved "now simpler" against turn 2 | **yes** — both turns carry one `session_id` | **yes** — planted 4271, `resume --last` returned **4271** |
| **Measured timing** | first turn ~11.6 s · explanation 9.4 s · follow-up 4.3 s | **two turns in 4.9 s total** (init 1.3 s, first result 3.1 s, second 4.5 s) | 5.3 s and 5.2 s per turn |
| **Refusals found** | none | `--output-format=stream-json requires --verbose` | `Not inside a trusted directory and --skip-git-repo-check was not specified`; stdin must be closed or it waits for more input |

**Two findings that change the design.**

1. **`claude` is the fastest of the three by a wide margin** — two turns in less time than `agy` takes
   to answer one. If the point of the feature is "explain this passage quickly", the default model
   should be reconsidered once all three exist.
2. **`codex` needs a directory, not just a binary.** It refuses outside a trusted git repository
   unless `--skip-git-repo-check` is passed — and that flag's own help says it runs "without
   persisting session files to disk", which is what `resume` reads. The measurement that worked ran
   inside the repository. **Whether `--skip-git-repo-check` and `resume` could coexist was the one thing
   left unmeasured — **and it was measured before this plan was committed. They can.** See phase 0
   below.

## The seam

`cliChatSession.ts` today does two jobs: it owns the process lifecycle (budgets, serialisation, the
four ways to end, context-loss reporting) and it speaks `agy`'s wire protocol. The lifecycle is
vendor-neutral and already tested; only the protocol differs. So the split is:

```
chatAdapter.ts        the seam: encode a turn, classify a line, say what shape the vendor is
  agyAdapter.ts       persistent pipe, agy's NDJSON
  claudeAdapter.ts    persistent pipe, Anthropic's stream-json
  codexAdapter.ts     process per turn, resumed by session id
cliChatSession.ts     unchanged in behaviour: lifecycle, budgets, queue, context loss
```

```ts
interface ChatAdapter {
  /** argv for a process that will answer turns. `resumeOf` is set for a per-turn vendor. */
  readonly argv: (resumeOf: string | undefined) => readonly string[];
  /** How this vendor is driven. */
  readonly shape: 'persistent' | 'per-turn';
  /** One turn, as the line to write — persistent vendors only. */
  readonly encode: (text: string) => string;
  /** What a line means: ready, an answer, a failure, or nothing. */
  readonly classify: (line: string) => AdapterEvent;
}
```

**Why a seam rather than three sessions.** A gate reviewer asked for exactly this on story 1.3's code
round and it was rejected THEN — correctly, because two of the three vendors had not been measured
and a seam designed against unmeasured shapes is a guess with an interface around it. They are
measured now. The rejection is recorded in that round; this plan is what supersedes it.

**The `per-turn` shape is the real work.** `codex` has no pipe to hold, so:

- there is no `init` to wait for, and the startup budget instead bounds the whole first turn;
- the conversation lives in the vendor's session store, so the session must remember the id (or use
  `--last`, which is what was measured) and pass it on every subsequent turn;
- a killed process is not a lost conversation — `resume` still works — so `contextLost` must NOT be
  reported for this shape when the process merely exited, which is the opposite of the persistent
  rule and is the single most likely place for this change to go wrong.

## What the BUILD measured, beyond phase 0 (2026-09-09)

Three things the plan did not know, each found by running the thing rather than by reading it.

**`claude` says NOTHING until it is asked.** Given an empty stdin it exits without a word, and its
`init` arrives WITH the first answer rather than at startup. A session that waits for readiness
before sending anything therefore waits out its whole 60-second startup budget and reports a CLI
that never started — for one that was working perfectly and had not been spoken to. That is what
the first live check did. It is now `ChatAdapter.announces`, a measured property of a vendor, and
the early return it drives in `ensureStarted`.

**`spawn` searches neither PATHEXT nor the shell.** A bare `codex` on Windows means `codex.cmd`,
and spawning the bare name dies with `ENOENT` at the first turn — deep inside a conversation,
where it reads as the model refusing rather than as a CLI that is not installed. Both new vendors
are npm shims on this machine. `versionProbe` had known this since it was written (its
`versionProbeCandidates` is the list); the resolution is now exported as `resolvedExecutable`,
`LaunchSpec` carries the `shell` decision `needsShell` makes, and a CLI that cannot be found is
refused in a sentence BEFORE a tab or a process exists.

**`codex --json` gives events, and a `thread_id`.** `thread.started` names the conversation and
`item.completed` carries the answer as an `agent_message`. Resuming by that id rather than by
`--last` is the difference between two chat tabs holding two conversations and two chat tabs
answering each other's questions — `--last` is the most recent codex session on the MACHINE.

**The live check, run through this build's own `CliChatSession` and adapters** — two turns each,
the second asking for a number planted in the first:

**Subject** `9765c23` · **harness** [`src_vs_code/scripts/live-chat.mjs`](../src_vs_code/scripts/live-chat.mjs)
(`npm run test:live`) · **pinned**: the planted number 7431, two turns per vendor, the second asking
for it back, each conversation in an empty temp directory of its own · **machine**: Windows 11,
node 24.18.0, `agy` 0.7.x, `claude` 2.1.258, `codex` on the npm shim · **date** 2026-09-09.

| Vendor | Shape | Turn 1 | Turn 2 | Context |
|---|---|---|---|---|
| `agy` | persistent | 8.0 s | 1.4 s | kept |
| `claude` | persistent | **3.0 s** | **1.6 s** | kept |
| `codex` | per-turn | 7.3 s | 6.7 s | kept, and `contextLost` correctly false |

`claude` is the fastest of the three by a wide margin, which phase 0 predicted and this confirms.
A 76 059-byte prompt was answered by `codex` through stdin in five seconds, which is why the
prompt does not travel in argv: Windows caps a command line at 32 767 characters and a carried
conversation is bounded at 60 000.

## Build order

**Phase 0 — DONE, 2026-09-08.** Both questions answered before anything else was written.

**Does `codex exec resume` work with `--skip-git-repo-check`, outside a repository?** **Yes** — and
the flag's own help is misleading. Run in a directory that is not a git repository at all
(`fatal: not a git repository`), `codex exec --skip-git-repo-check` planted the number 8842 in 4.5 s,
and `codex exec resume --last --skip-git-repo-check` returned **8842** in 4.7 s. The session store
survives the flag whose own help says it runs "without persisting session files to disk"; `--last`
finds it anyway. So the master plan's empty-temp-directory rule holds for `codex` too, and the
deviation this plan was braced for is not needed.

**Does `claude`'s per-turn `init` break the ready latch?** **No**, and the answer is in the code
rather than in a run: the startup budget is armed only inside `ensureStarted`, which returns early
once `ready` is set, so a second `init` mid-conversation re-sets a flag that is already true and
arms nothing. `waitingForInit` is cleared by then, so it resolves nothing either. The adapter still
has to CLASSIFY `system/init` as "ready" rather than as an answer, which is phase 2's job.

**Phase 1 — `chatAdapter.ts` + `agyAdapter.ts`.** The seam, with the CURRENT behaviour moved behind
it. `cliChatSession.test.ts` must pass unchanged: this phase is a refactor, and a green suite is what
proves the seam did not change what already works.

**Phase 2 — `claudeAdapter.ts`.** Persistent, so it reuses the whole lifecycle. Its own tests plus a
live check.

**Phase 3 — `codexAdapter.ts`** and the `per-turn` shape in the session: no `init` latch, a remembered
session id, and the inverted context-loss rule.

**Phase 4 — the model list.** `chatModels.ts` stops filtering to `antigravity`; `vendor-routing.md`
still binds — a Claude model goes through the `claude` CLI and never through `agy` or `codex`.

## Test plan

| Test | What it pins |
|---|---|
| `agyAdapter.test.ts` | The `{event:'user'}` shape; `init`/`result`/`ERROR` classification; a non-JSON line is nothing. |
| `claudeAdapter.test.ts` | The Anthropic block-content shape; `system/init` and `result/success` classification; `--verbose` is in the argv, because without it the CLI refuses outright. |
| `codexAdapter.test.ts` | The first turn's argv differs from a resumed one; the answer is taken from the right place; a session id is carried. |
| `cliChatSession.test.ts` (existing) | Unchanged and green after phase 1 — the refactor's only real proof. |
| `cliChatSession.test.ts` (new) | The `per-turn` shape: no `init` wait, an exit between turns is NOT a lost context, and a resumed turn carries the id. |
| live checks | One real conversation per vendor, two turns each, with the second turn depending on the first. Timings recorded beside the table above. |

## Risks

1. ~~**`codex --skip-git-repo-check` may disable the session store.**~~ **Retired by measurement**
   (phase 0): it does not. `resume --last` recovered a planted number from outside a git repository
   with the flag set, so the empty-temp-directory rule needs no exception.
2. **`claude` emits `init` per turn.** The session latches `ready` on the first one; a second `init`
   mid-conversation must not re-arm the startup budget. Cheap to handle, easy to miss.
3. **Three adapters is three vendor bills.** The live checks cost real turns on three accounts.

## Definition of Done

- [x] Phase 0's two measurements are recorded here, with what was run.
- [ ] All three vendors hold a two-turn conversation where the second turn depends on the first,
      verified live, with timings recorded.
- [ ] `cliChatSession.test.ts` passes unchanged after the seam is introduced.
- [ ] `contextLost` is reported for a persistent vendor that died, and NOT for a per-turn vendor that
      merely exited between turns.
- [ ] `chatModels.ts` offers all three; a Claude model still routes only through the `claude` CLI.
- [ ] `research/module_extension.md` records the seam and the three shapes.
- [ ] This plan is promoted to `research/` with `IMPLEMENTED <date>` and its deviations recorded.
