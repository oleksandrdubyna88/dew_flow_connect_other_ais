# Changelog

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
