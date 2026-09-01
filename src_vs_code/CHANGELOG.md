# Changelog

## 0.13.0 — 2026-09-01

**A help page, behind the yellow ? in the title bar.** Searchable, in English or Russian, with the
± text size every page of its sibling product carries. Seventeen articles in one fixed shape —
what it is, why, how to set it up, how to use it, what can go wrong.

The first four are the first four things you do: install the server, choose reviewers, tell your AI
to use the gate, set the gate. Then one article per panel control, then the machinery you cannot
see from the panel: where a reviewer actually runs, what happens when one fails, how a setting
reaches the server, and what the audit trail holds.

**The prompts, in full.** Every prompt the product sends, verbatim, held byte-for-byte against the
server's own files by a test — so the page cannot quietly describe a question the product stopped
asking. Overriding one is a file in the server's data directory, and the article says how.

Two tests keep the help alive: one fails the build when a command or setting has nothing written
about it, and it found two gaps on its first run.

## 0.12.0 — 2026-09-01

**A `call_human` verdict now reaches the human.** It used to be an instruction to the calling AI,
and whether a person ever heard about it depended on what that AI did next — so a gate could
exhaust its rounds and the panel would sit empty all day. The server raises a notice with the open
findings; it appears where every other question does, and answering it works the same way.

**The rounds file and the panel tell the same story.** Two renderers over one file had drifted:
`PlanReview` in one and `plan review` in the other, the round's subject in one and not the other.
Same columns, same words, one function.

**A billion-minute round is gone.** Rounds written before the start time existed carry .NET's
default date — year one — and the subtraction rendered `1065396701m 44s`. A duration longer than a
day is a missing start, not a long review.

**Gemini is marked retired in the picker**, because it is: Google closed Code Assist for
individuals and the CLI now refuses before reaching a model. Pair with **coai-mcp 0.5.3**, which
reports that failure — and four others — as what to DO rather than as a stack frame.

## 0.11.1 — 2026-09-01

**A round says what it was about.** Recent rounds led with `main · PlanReview 4`, which names the
gate and nothing that went through it — a week of work read as a column of numbers. Each round now
carries the plan's title, or its file name when a path was passed, and the stage is spoken the way
a person says it: *plan review 4 · main · call_human · 3 gating*.

Needs **coai-mcp 0.5.2**, which is what derives and records the subject; rounds written by an
older server simply have none.

## 0.11.0 — 2026-09-01

**The Server section tells you what is published.** It shows the installed version, the newest
published one, and an Update button when they differ — plus *Check again* for an answer right now.
The published version is shown even when it matches, because "up to date" and "the check never
ran" look identical when only a mismatch is displayed.

And the check really never ran: it asked GitHub for the newest release of ANY kind, and this
repository publishes extension releases too, so an extension tag was answering the question "is
there a newer server" and the comparison concluded no. Every time, since the extension line
started.

## 0.10.1 — 2026-09-01

**Antigravity is a runtime the panel actually knows.** It was in the server and missing from the
extension's own list, so a vendor configured as `antigravity` was stored as `codex` — the panel
would have run the wrong vendor's model and reported the answer under the right vendor's name. Its
model list is there too: `agy models` on a Pro subscription reaches Gemini, Claude and GPT-OSS.

**The chart's time tabs work.** The repaint key left out which window was showing, so clicking
Today or Month changed the state and repainted nothing. One missing number in a ledger line no
longer turns every total into `NaN` either.

Pair with **coai-mcp 0.5.1**: failed reviewers are now counted in the spending record with what
they actually consumed (they used to read as free, under-reporting a round by about half), an
answer that cannot be parsed leaves the vendor's real transcript on disk instead of an empty file,
the ledger survives a second server writing beside it, and the repair launch no longer hands an
agentic reviewer a checkout — which is what made one code round in three lose a reviewer.

## 0.10.0 — 2026-09-01

**Prompts per round.** Each reviewer role now has a universal prompt and two narrow lenses, and the
new *Prompts per round* section picks which one each round uses — or rotates through them
automatically. Rotation is off by default, and the README says plainly what the measurement behind
the lenses does and does not establish: the same prompt on the same text three times produced 6, 4
and 5 findings whose overlaps were 3, 1 and zero, so the lenses are an aim rather than a proven
improvement.

**What each AI has used.** A new section charts tokens, money and time per vendor over a day, a
week, a month or a year, with totals and averages. Failed reviewers are counted too: a run that
burned ninety seconds and answered nothing is exactly what a spending record must not hide. A
vendor that does not price its own runs shows a dash, never `$0.00`.

Pair it with **coai-mcp 0.5.0**, which adds the Antigravity CLI as a vendor — Google retired Gemini
Code Assist for individuals, and `agy` is the migration — reads each vendor's token accounting the
way that vendor actually reports it, and writes the spending ledger this chart reads.

## 0.9.1 — 2026-08-31

**The packaged extension is the extension you built.** `vsce package` was bundling after it had
already collected the files, so a release could ship a stale `dist/`. Ordered properly now — this
is the first build where the version on the Marketplace is guaranteed to be the code in the tag.

Pair it with **coai-mcp 0.4.0**, which is where this release's real news is: a per-reviewer audit
trail, settings that apply to the next round instead of the next restart, and six defects a real
end-to-end run found — including a vendor configured as `claude` that was silently running codex.

## 0.9.0 — 2026-08-31

**A round tells you what it is doing while it does it.** The server now writes a round to disk the
moment it starts, not when it ends, and updates it as each reviewer moves — so a ten-minute code
gate shows "4 of 6 answered, 2 running" with every reviewer named, in the panel and in the rounds
view, instead of showing nothing at all until it finished. A round abandoned by a crashed server
reads as *interrupted* rather than running forever.

**Tokens and money, per round.** Each round reports what it consumed, read out of each vendor's own
reporting: tokens from every CLI that says, and money only from a CLI that prices its own run
(Claude does). A vendor that reports no price is shown as "no cost reported" — never as $0.00,
because unknown is not free.

**The rounds view is a real file.** It is written to `rounds.md` in the server's data directory and
opened from there, so closing it no longer asks whether to save. It is rewritten while it is open,
so a running round advances on screen.

**The dropdowns stay open.** The panel repainted itself on a timer, and a repaint closed whatever
picker you had open after two or three seconds. Now only a change you make repaints; the live parts
are patched in place.

**A ▶ beside each reviewer** opens that vendor's own CLI in a terminal with its usage command ready
at the prompt — for checking an account, reading what you have spent, or signing a CLI in.

**Removing a reviewer asks first**, and every reviewer that ships as a default can now be added back
from the presets — Gemini could not be, which made removing it permanent.

**Fixed.** `resolve` failed with an invocation error unless the human-override argument was passed,
which broke the ordinary path of every round. Every prompt reaching a vendor carried a stray
byte-order mark, and a CLI that exited before reading its input could crash the launch instead of
failing as one reviewer. The human "proceed" override could skip a configured escalation ladder.

## 0.7.0 — 2026-08-31

First public release.

**The gate.** Two stages, both driven by your own AI through the `coai` MCP server: the plan before
anything is implemented, the diff after. Three independent reviewers per vendor on the code stage —
architecture, security and reliability, UX-DX and code performance.

**A count you can trust.** Only blocking and major findings gate. The same defect from two vendors
merges into one finding carrying both names, resolving toward the worse severity when they disagree.
A finding rejected with a reason stops counting unless it is raised again with a new argument, and a
rejection without a reason is refused. A partial round says which reviewers were missing and why.

**When a decision is yours.** The question appears in VS Code — dialog, status-bar item, and the
panel — with the findings that still gate, and blocks until you answer. After the timeout it tells
the AI to ask you in the chat; the question stays open. Nothing is decided by your silence.

**Your language.** English, Español, Deutsch, Русский, Українська. A question already in your
language is left alone; anything else is translated by a small fast model, and your answer is
translated back for the AI that asked. If the translator cannot run you get the original with the
reason, never an error in its place.

**Reviewers are a list, not a fixed set.** Add a vendor from a preset or by name and base URL,
remove one, switch one off, choose its model. Codex's model list comes from the CLI's own cache;
Gemini's and Claude's are curated, and the panel says which is which.

**Nothing to leak.** No port is opened — the halves talk through files they both already use. No
secret is stored: keys live in one CredsForDevs entry, and the extension holds only a revocable pass
to it. Reviewers run read-only in a worktree pinned to one commit, with the write tools denied.

**Known limits.** macOS server builds are not published yet, and the installer says so rather than
downloading something that cannot run. De-duplication compares wording, so two vendors describing
one defect in entirely different words can still be counted twice — it errs toward gating, which is
the safe direction.
