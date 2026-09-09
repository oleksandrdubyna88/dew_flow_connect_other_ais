# PLAN — a picture in the question

> Status: **PHASE 0 IS DONE (measured 2026-09-09) — Phase 1 is not built, and the measurement says
> it is worth building.** `claude` and `agy` both read a number out of a real PNG and said it back;
> `codex` is untested rather than refused (its account hit a usage limit mid-probe) and must be
> re-run before it is refused by name anywhere. **The mechanism is a file PATH named in the prompt**
> — the one vector that works for both vendors that answered — which means the vendor process opens
> the file itself, and the temp file's location and lifetime are part of the contract rather than an
> implementation detail. Kind: **feature**.
> Scope: the page's paste handling and CSP (`chatPage.ts:287`), a host handler in `chatCommand.ts`,
> a temp-file discipline beside `chatOrphans.ts`, and the session seam plus adapters. Origin:
> [BUGS_2026-09-09.md](BUGS_2026-09-09.md), entry 13.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_three_chat_adapters.md](../research/PLAN_three_chat_adapters.md).

## The goal

Paste a picture from the clipboard into the composer, the way Claude Code and Gemini take one.
Three separate problems, and only the first is layout.

## Phase 0 — measure, per adapter, before any design

The part that decides whether any of this is worth building: `ChatSession.send` takes
`text: string` and nothing else (`chatSession.ts:37`), and **whether each vendor CLI accepts an
image in a non-interactive turn, and how, is not known.** Establish for `claude`, `codex`, `agy`:

1. Does it take an image at all in the mode we run it?
2. By what mechanism — a file path in the prompt, a flag, base64 on stdin — and does that survive
   `cmd.exe` on Windows (the PATHEXT/stdin traps the adapters already record)?
3. The Team server: an image is a wire-format change on a server deployed by hand and shipped
   separately; the client will be able to send one long before the server can take one. Out of
   scope here; recorded as the server's next item.

Results as a table in this file. If no local adapter takes one, promote this plan as a record.

### Phase 0 RESULTS — measured 2026-09-09

Harness: [`src_vs_code/scripts/measure-image.mjs`](../src_vs_code/scripts/measure-image.mjs), run on
Windows 11, node v24.18.0, against the real signed-in CLIs, with the launch spec **imported** from
`launchSpecFor` so it cannot measure a mode the product does not run.

**The image says something, and that is the whole design of the probe.** A CLI exiting 0 does not
prove the model received a picture — an unknown field in a JSON turn can be ignored silently and an
unsupported payload dropped, and either way the process answers cheerfully. So
[`tokenPng.mjs`](../src_vs_code/scripts/tokenPng.mjs) draws a four-digit number into a real PNG
(hand-rolled: a CRC, `node:zlib`, and 5×7 digit glyphs — no dependency, and readable in a diff), the
prompt asks for that number back and nothing else, and **only the number appearing in the answer
counts as receipt**.

| vendor | vector | outcome | what it said |
|---|---|---|---|
| `claude` | inline base64 block | **received** | answered `7431` |
| `claude` | a path in the prompt | **received** | answered `7431` |
| `agy` | inline base64 block | not representable | the turn's `message.content` is a STRING — there is no block to put an image in |
| `agy` | a path in the prompt | **received** | answered `7431` |
| `codex` | inline base64 block | not representable | the turn IS the prompt text — there is no structure to carry a block |
| `codex` | a path in the prompt | **inconclusive — the account, not the vendor** | *"You've hit your usage limit… try again at Sep 10th, 2026 12:41 AM"* |

#### The answers, question by question

**1. Does it take an image at all?** **`claude` and `agy`: yes, proven** — both read the number out
of the picture and said it back. `codex` is **not yet known**: its account ran out of credit during
the probe, and that is a fact about this afternoon rather than about the vendor. It must be re-run
before anything is decided for it.

**2. By what mechanism?** **A path to a file on disk, named in the prompt** — and that is the
finding that matters, because it is the ONE vector that works for both vendors that answered.
`claude` also accepts an inline base64 `image` block in its `stream-json` turn, which its
`message.content` array has room for; `agy` and `codex` have nowhere to put one — their turns carry
a string and a bare prompt respectively, so inline is not merely unsupported there, it is
unrepresentable in the shape this extension sends.

That also says something Phase 1 needs: with the path vector the CLI **opens the file itself**, so
what is being built is not an attachment travelling with the turn but a file the vendor process must
be able to read. The temp file's location and lifetime are therefore part of the contract, not an
implementation detail.

**3. Does it survive `cmd.exe`?** The question mostly dissolves: a path is a couple of hundred
characters and goes on stdin with the rest of the prompt, exactly as prose does today. The base64
route is the one that would have strained a command line — 556 characters for a 415-byte PNG, and a
real screenshot is measured in megabytes — and it is only available on `claude`, which takes it on
stdin too.

### The decision

**Phase 1 is worth building, and the mechanism is a file path.** Two of the three local vendors are
proven to read a picture that way, the third is untested rather than refused, and the plan's own
`{ path, mime }` attachment shape turns out to be right — now measured rather than assumed.

Two amendments Phase 1 should carry from this:

* **The base64-over-the-bridge step is only needed to get the clipboard image to the HOST.** From
  there it is a file on disk and a path in the prompt; no adapter needs to carry base64, and only
  `claude` could take it if one did.
* **`codex` must be re-probed** (`npm run measure:image -- codex`) before it is given a refusal by
  name in the page. Refusing a vendor on the strength of a spent credit balance is exactly the
  silent feature-kill this probe was shaped to avoid.

## Phase 1 — only where Phase 0 said yes

- **Taking the paste.** A `paste` listener on the textarea reads `clipboardData.items`; an
  `image/*` item gives a `Blob` → base64 over the existing bridge to the host. The webview writes
  nothing to disk itself.
- **Showing it.** The CSP is `default-src 'none'` (`chatPage.ts:287`) and the panel has
  `localResourceRoots: []` (`chatPanel.ts:99`). Add `img-src data:` — and nothing wider: no remote
  host, no `file:`. A thumbnail in the composer, the image in the sent message.
- **Delivering it.** `send(text, …, attachments?)` — a list of `{ path, mime }` the adapter hands
  the CLI by the measured mechanism. The host writes the temp file, OWNS it (a ledger line beside
  `chatOrphans.ts`'s discipline so a force-killed VS Code leaves nothing in `%TEMP%`), and
  removes it after the turn.
- **Refusing.** If the chosen model cannot take images, the paste is refused IN THE PAGE with a
  sentence naming the model — the rule `strandedOption` (`panelView.ts:274`) already follows for
  models that cannot chat. A picture that silently does not arrive is the worst outcome.

## Build order

1. Phase 0 with a script beside `scripts/live-chat.mjs`; the table here; the decision line.
2. RED tests; the CSP and the paste listener; the bridge; the temp-file owner; one adapter; the
   refusal by name; the others.
3. GREEN; whole suite; a live paste per supporting adapter, recorded.

## Test plan

- `chatPage.test.ts`: the CSP carries `img-src data:` and no other source; a pasted image shows a
  thumbnail; a refusal renders the model's name.
- `chatWiring.test.ts`: a paste for a non-image model is refused before anything is written; a
  paste for an image model writes one temp file, sends it, and removes it after the turn; a stop
  mid-turn also removes it.
- `chatOrphans.test.ts`: an orphaned temp file from a dead window is cleaned on activation.

## Acceptance — one PR per plan, and the gate on both sides of it

This plan ships as **its own branch (`feat/a-picture-in-the-question`) and its own pull request**, and the PR is accepted
only when the whole ritual has run — not when the code works.

1. **Gate, before the first line of code.** `open` a coai session for this repository and the
   branch; `review_plan` with THIS file as the plan; `resolve` every finding (a rejection carries a
   reason); repeat until the verdict is `proceed`. `review_code` refuses without it.
2. **Tests first.** A RED test per defect, watched failing with the real symptom, then GREEN; every
   new behaviour with its happy path and the failure paths it introduces — `npm test` in
   `src_vs_code`, the whole suite, not the one file.
3. **Gate, after the code.** `review_code` with the scope = the *Definition of Done* below plus the
   goal, and the diff `main...feat/a-picture-in-the-question` — three dots; the two-dot moving-base trap is recorded in
   `todo/PLAN_the_gate_diffs_from_a_moving_base.md`. `resolve`; repeat until `proceed`.
4. **Documentation.** `research/module_extension.md` says what the code now does;
   `research/architecture.md` if a cross-module seam moved.
5. **Help.** Every new command and setting has an article in `helpContent.ts` —
   `helpCoverage.test.ts` fails the build otherwise; a lagging translation is marked as such.
6. **README and CHANGELOG.** `src_vs_code/README.md` if what a person sees changed;
   `src_vs_code/CHANGELOG.md` in the prose the file already uses — the sentence a person reads,
   not the commit subject.
7. **Manifest.** `package.json` contributions (settings, commands, keybindings, menus), and the
   version the release line expects (see the `chore(release)` history).
8. **Family checks.** `node .claude/rules/shared/tools/plan-lifecycle.mjs` and `pin-check.mjs`
   clean.
9. **Promote.** On merge, `/promote-plan` this file to `research/` with `IMPLEMENTED <date>` and
   every deviation recorded — what shipped differently is the most valuable line of the record.

**Specific to this plan:** the Phase 0 table is in the PR whatever it says; help sentence under the chat article naming which models take a picture; `module_extension.md` gains the attachment on the seam, the CSP change and the temp-file discipline; CHANGELOG in the person's words; no manifest change; the security note (CSP widened to `data:` images only) called out in the PR body for the gate.

## Definition of Done — PHASE 0 (this pull request)

- [x] The probe drives the REAL CLIs, importing the launch spec rather than retyping it.
- [x] **A run that exits 0 is not a yes.** The image carries a readable token, the prompt demands it
      back, and only the token in the answer counts as receipt.
- [x] **A "no" is distinguishable from "I could not find the way".** Seven named outcomes, of which
      `unavailable`, `failed`, `unspawnable` and `no-answer` are facts about this machine and are
      excluded from any conclusion about a vendor. `codex` landed in exactly that bucket, with its own
      sentence recorded, and is written down as untested rather than as refused.
- [x] **Both transmission vectors tried** — inline base64 and a path on disk — before Phase 1 designs
      an attachment shape, with "not representable" recorded where a vendor's turn has nowhere to put
      a block.
- [x] The table is in this file, with the environment and the payload sizes.
- [x] A decision line saying whether Phase 1 is worth building, and by what mechanism.

## Definition of Done — PHASE 1 (not this pull request)

- [ ] Phase 0's table is here with the mechanism per adapter.
- [ ] Where supported: paste → thumbnail → sent → answered; the temp file is owned and removed.
- [ ] Where not: refused by name in the page; nothing written.
- [ ] CSP allows `data:` images and nothing else new.
- [ ] `npm test` green; the acceptance ritual complete; promoted on merge.

## Parallelism

**Phase 0 has no conflicts** and can run in any lane. Phase 1 touches `chatPage.ts` (paste
listener, CSP) and the seam — queue it LAST in the chat-page lane, after streaming, and after
[PLAN_a_turn_nobody_can_stop.md](PLAN_a_turn_nobody_can_stop.md) on the seam.
