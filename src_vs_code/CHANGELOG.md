# Changelog

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
