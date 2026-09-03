# PLAN — a finished round can be opened, and it says which model spent the eleven minutes

> Status: **IMPLEMENTED, 2026-09-03.**
>
> **The deviation is the whole design, and the gate is why.** The plan proposed a plain `<details>`
> that carries `open` while the round is running. Both halves were refused, one as Blocking: this
> list is patched into the page every five seconds, so a disclosure holding its state in the DOM
> closes under the person mid-read; and a card open only BECAUSE the round is running snatches the
> reviewers away at the moment it finishes, which is when they are most worth reading. The open set
> now lives in `PanelState` exactly as the sections do — the webview posts a `round` message on
> toggle, the provider keeps the set, the render carries `open` — with the `toggle` listener on
> `document` in the CAPTURE phase, because `toggle` does not bubble and the elements are replaced by
> every patch. A running round opens itself ONCE, recorded, so closing it sticks.
>
> The code round added: a closed card builds no reviewer rows at all (this list is rebuilt every five
> seconds), both view-state sets are pruned to rounds that still exist, an empty `data-round` posts
> nothing, and a duration that is not a finite number reads as no duration.
>
> Not built, and stated rather than implied: the plan's line about rendering per-reviewer TOKENS
> "without owning them" — the fields are rendered when present, and the work that writes them was in
> flight in another session's checkout at the time.
>
> Scope: `src_mcp/src/Server/{SessionStore,LiveRound}.cs`, `src_mcp/tests/LiveRoundTests.cs`,
> `src_vs_code/src/{rounds,panelView,panelProvider,helpContent,helpRu,helpUk,helpDe,helpEs}.ts`.
>
> Related docs: [module_extension.md](module_extension.md), [module_server.md](module_server.md).

## The symptom

The Recent rounds list shows one line per round: subject, stage, branch, verdict, gating count, then
`11m 2s · 220k in / 9.4k out · no cost reported`. Two complaints from the person using it, both about
the same thing — **the round is the only unit anything is reported in**:

1. **The total time is there and the per-model time is not.** A round is as slow as its slowest
   reviewer, so "11m 2s" for nine reviewers says nothing about which one cost the eleven minutes. The
   server measures each reviewer already (`ReviewerProgress.Elapsed`, which the scheduler times with a
   stopwatch around the run) and throws the number away at `LiveRound.Report`.
2. **While a round RUNS the panel shows the reviewers, and the moment it finishes they vanish.**
   `roundCard` renders `reviewerLines(round)` only when `isRunning(round)`. So the view is richest
   about the round you can still watch, and poorest about the one you want to understand afterwards —
   which is the wrong way round.

And the list they live in is 320px tall (`#live-rounds { max-height: 320px }`) in a sidebar that is
usually much taller than that, so five rounds fill it and everything else is behind a scrollbar.

## What must be true when this is done

1. **Each reviewer's own duration is recorded** by the server and survives in the session file, so
   the panel can state it rather than derive it.
2. **A finished round can be opened** and shows what it showed while it ran: every reviewer, its
   status, how many findings it produced, how long it took, and what it consumed.
3. **A running round still shows its reviewers without being opened** — that is the state somebody is
   watching, and a click to see it would be a regression.
4. **The summary line is unchanged** when closed: the same subject, verdict, gating count, total time
   and total tokens that are there today.
5. **The list is twice as tall** (640px), so a person sees roughly ten rounds instead of five.
6. **An older session file still renders.** A round written before this change has no per-reviewer
   duration; it must show the reviewers it does have and say nothing about time.
7. **The help text explains what opening a round shows**, since the affordance is new.

## Constraints

- `rounds.ts` and `panelView.ts` stay pure and `vscode`-free; the rendering is asserted in tests
  rather than by opening a sidebar.
- The expansion is `<details>`/`<summary>`, not scripted state: the panel repaints on a five-second
  tick and any state held in JavaScript would be lost on every repaint. A `<details>` element keeps
  its own open state in the DOM the patch replaces, so the summary must carry `open` when the round
  is running and the patch must not fight the person.
- No new panel command: this is markup, not a round trip.
- The server's field is additive with a default, because a session file from an older server is read
  by a newer panel every time somebody updates.
- Per-reviewer TOKENS are being added by other work in flight in this checkout; this plan adds the
  DURATION and the disclosure, and renders whatever token fields the state carries without owning
  them.

## Build order

1. **`ReviewerState.Seconds`** (`SessionStore.cs`), filled in `LiveRound.Report` from
   `ReviewerProgress.Elapsed` — and only when it is greater than zero, so a "running" report cannot
   erase the number of a reviewer that has already finished. *(Written ahead of this plan while
   reading the code; the rest of the order is untouched.)*
2. **`ReviewerState.seconds`** on the extension side, optional, with the same "absent means an older
   file" contract the other optional fields have.
3. **`reviewerLines`** states the duration when there is one: `codex/Architecture — done (3 findings,
   38.7s)`.
4. **`roundCard` becomes a `<details>`**: the summary is today's line, the body is the reviewer
   lines. `open` when the round is running.
5. **The list is 640px**, and the reviewer rows inside a closed card cost nothing.
6. **Help**: one entry describing what a round opens into.
7. Docs: `module_extension.md` (the disclosure and why `<details>` rather than script),
   `module_server.md` (the per-reviewer duration), and the manifest/CHANGELOG for the release.

## Test plan

`src_mcp` — the MTP executable:

- a reviewer reported `done` with an elapsed time keeps that time in the session file;
- a later `running` report does not erase it;
- a round from a file with no `seconds` reads as zero rather than failing.

`src_vs_code` — `npm test`:

- `reviewerLines` includes the duration when present and omits it when absent;
- a finished round renders a `<details>` whose summary carries the same text as today's line;
- a running round's `<details>` carries `open`;
- the reviewer rows are inside the details for a finished round;
- the CSS names 640px for the rounds list.

By hand: open the panel, expand a finished round, and read the nine reviewers of a code round with a
duration each.

## Definition of Done

- [ ] A finished round opens and shows every reviewer with findings, duration and tokens.
- [ ] A running round still shows them without a click.
- [ ] The closed summary is what it is today.
- [ ] The list is twice as tall.
- [ ] An older session file renders with no duration and no error.
- [ ] `CoaiMcp.Tests.exe` and `npm test` pass.
- [ ] `module_extension.md`, `module_server.md`, the help entry, the manifest and the CHANGELOG are
      updated; the plan is promoted.
