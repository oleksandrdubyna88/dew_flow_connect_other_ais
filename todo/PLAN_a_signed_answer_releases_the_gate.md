# PLAN — a signed answer releases the gate

> Status: **plan only, 2026-10-22 — a stub; R1 of PLAN_only_a_person_releases_the_gate.md.** Scope: the answer file `escalations/<id>.answer.json` — `src_mcp/src/Server/Escalations.cs` (`ReadAnswer`, `DecisionOf`), `src_mcp/src/Server/Rounds/CurrentAnswer.cs`, and the writer in `src_vs_code/src/escalationWatcher.ts` / `escalationAnswer.ts`. Nothing in this document is built, and its design is deliberately left open.
>
> Related docs: [PLAN_only_a_person_releases_the_gate.md](PLAN_only_a_person_releases_the_gate.md) (§8 R1, §11.1 — the plan that makes the answer file the only road and names this one as its follow-up), [module_server.md](../research/module_server.md) (*`call_human` stops the review*), [module_extension.md](../research/module_extension.md) (*Active questions*, *Answer…*).

## 1. The symptom

[PLAN_only_a_person_releases_the_gate.md](PLAN_only_a_person_releases_the_gate.md) closes the PROTOCOL road: no tool argument of the calling AI releases a held gate, and only a written answer to the hold's own notice does. It does **not** make that answer person-authored.

The answer is a file in the data directory, `escalations/<id>.answer.json`, with the shape `{ id, answer, decision, answeredUtc }`. The server reads it by the question's identity (`Escalations.AnswerPath`, `Escalations.cs:154`; `Escalations.ReadAnswer`, `Escalations.cs:343`) and decides from its `decision` word (`Escalations.DecisionOf`, `Escalations.cs:308`; the notice rule in `CurrentAnswer.DecisionOnTheNotice`, `CurrentAnswer.cs:132`). The extension writes it (`escalationWatcher.ts:250`). Nothing in the file says who wrote it.

So an AI agent with a shell or a file tool, running as the same user on the same machine, can write `<notice>.answer.json` itself — with `"decision": "proceed"` — and release the gate it is held by. The plan that shipped the person-only road says so in its own words (§8 R1): *the agent cannot proceed THROUGH THE PROTOCOL.* This plan is the other half, and the symptom is exactly that sentence's limit.

## 2. Goal (to be sharpened)

A Proceed — and every other decision that moves a held gate — is accepted only when the server can tell the extension wrote it: a signed answer, verified by the server, with the key held where the calling agent's tools cannot read it.

## 3. What is not decided, and must be before any code

- **Where the secret lives.** A key the extension holds and the server can verify with has to be provisioned to both without passing through anything the AI can read; the data directory and the process environment are both in the agent's reach. Candidates to weigh: the OS credential store, a vault entry (the family's CredsForDevs), a per-session secret handed to the server at start. Each has a cost on WSL-and-Windows installations that share one data directory.
- **What is signed.** At least the question id, the decision, the answer text and a timestamp; whether the session and the hold's identity are bound in as well.
- **Old files and old halves.** An unsigned answer from an older extension — what the server does with it, and with a held session that outlives an upgrade. The same mixed-version window the person-only plan paid for.
- **What this cannot do.** A same-user process that can read the extension's memory or the key store can still sign. The claim has to be written as narrowly as the person-only plan's: what it closes, and what stays open.
- **Whether it is wanted.** The person-only plan recommended this as a follow-up and left the decision to the owner (its §10 question 5).

## 4. Build order, test plan, Definition of Done

Not written: the design above has to be chosen first. When it is, this document gets, in the order the planning rule asks for — verified `file.cs:line` references, a build order of stories test-first, the RED tests named (the first of them an agent that writes an unsigned `proceed` file and is not released), a test plan including the mixed-version window, and a Definition of Done checklist.

- [ ] The design questions in §3 are answered by the owner.
- [ ] This stub is replaced by a plan carrying the sections §4 lists.
