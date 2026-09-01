# PLAN — the pasted snippet carries a version, and the panel notices when it is old

> Status: **IMPLEMENTED, 2026-09-01.** Scope: `src_vs_code/src/claudeSnippet.ts`,
> `panelView.ts`, `panelProvider.ts` and their tests. No server change.
>
> Related docs: [module_extension.md](module_extension.md).

## The symptom

Found while checking whether this gate displaces the `feature-dev` plugin's own reviewers. The copy
pasted into `dew_flow_creds_for_devs/CLAUDE.md` was **two revisions behind** the snippet the button
hands out: it predated the SCOPE rule, so the AI reading it would call `review_code` with a commit
subject and meet a server refusal with no explanation anywhere in its instructions.

Nobody was careless. That is simply what happens to text somebody pastes: the source moves, the copy
does not, and the copy is the one being obeyed. Handing out text to paste without a way to notice it
has gone stale is the design defect, not the stale copy.

## What must be true when this is done

1. The snippet carries a version a machine can read, and a person pasting it cannot lose it.
2. **The version cannot silently go stale.** A number somebody has to remember to bump is the same
   failure one level up — so a test must fail when the snippet text changes and the number does not.
3. The panel says which version this workspace has: current, older (with the numbers), or not
   pasted at all.
4. An unmarked copy — everything pasted before today — reads as "predates versioning", not as an
   error and not as "version 0".
5. Nothing nags. A repository that has deliberately not adopted the gate is not a problem to report.

## Why a number and not a hash

A hash is automatic and cannot be forgotten, which is the attractive half. But it only answers
"different", and the useful sentence is "**older** than the current one" — a hash cannot order two
copies, so it cannot tell a stale paste from a locally edited one, and those want opposite advice.

So: an ordered integer, with a test that pins it to the text's hash. The number is meaningful and
the hash makes forgetting it a red build. Both halves, neither on its own.

## Build order

1. `SNIPPET_VERSION` and a marker line inside the snippet text.
2. `snippetVersionIn(text)` — pure, reads the marker out of a pasted file.
3. `snippetStatus(pasted)` — pure, three outcomes: `current`, `older`, `unversioned`, plus `absent`
   when there is no instruction file carrying it at all.
4. The panel reads the workspace's instruction files (`CLAUDE.md`, `AGENTS.md`, `GEMINI.md`,
   `.github/copilot-instructions.md` — the same four the server reads for the conventions pass) and
   shows one line in the **Server** section.
5. The guard test: hash of the snippet body ↔ `SNIPPET_VERSION`.

## Test plan

RED first: the marker is present and parseable; a pasted copy at an older number reads as older; an
unmarked copy reads as unversioned rather than as 0; no instruction file reads as absent; the panel
prints each of the four; and the hash guard fails when the text moves without the number.

## Definition of Done

- [ ] Every rule above holds with a test that was watched fail.
- [ ] The hash guard's failure message says exactly what to do (bump the number, update the hash).
- [ ] `research/module_extension.md` records why a number and not a hash.
- [ ] The copy in `dew_flow_creds_for_devs/CLAUDE.md` is re-pasted at the current version.

## What shipped differently

**Numbering starts at 2.** v1 names the text already pasted in repositories before the marker
existed — a real generation, and the number an unmarked copy would honestly take if anybody ever
hand-marked one. Starting at 1 would have left "older than current" untestable on the first release,
which is the case the whole feature is for.

**Five outcomes, not three.** `ahead` was not in the plan: an extension older than the repository is
a real state on a machine that has not updated, and it wants the opposite advice — update this build
rather than paste over the repo. `absent` and `current` both render nothing, for different reasons
that are written down where the silence is decided.

**The guard prints the fix.** The hash test does not just fail; its message carries the next version
number and the new hash, so bumping is a copy and a paste at the one moment it is cheap.
