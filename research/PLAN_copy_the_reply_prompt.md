# PLAN — Copy the reply prompt

> Status: **IMPLEMENTED, 2026-09-15.** Both stories shipped in #273. Scope: the chat page's answer
> rendering and its per-message controls — `src_vs_code/src/renderAnswer.ts`, `chatPage.ts`,
> `chatMessages.ts`, `chatPanel.ts`, `chatCommand.ts`, plus two modules the plan did not foresee,
> `answerCopy.ts` and `copyText.ts`.
>
> Related docs: [module_extension.md](module_extension.md),
> [PLAN_carry_nothing_above.md](PLAN_carry_nothing_above.md),
> [PLAN_chat_with_other_ais.md](PLAN_chat_with_other_ais.md).

## What shipped differently

Everything the plan set out shipped. Four things arrived differently, and each is written into the
sections below rather than only here, because they are the design now.

1. **"No new module" was wrong, and the gate proved it before a line was written.** The plan put the
   host decision in `chatCommand.ts`. Nothing in this repository imports that file — it closes over
   `vscode` and there is no stub — so the end-to-end test the plan specified *could not have been
   run at all*. The decision lives in `answerCopy.ts`, pure, and the hook is two delegations.
2. **`copyText.ts` exists because reuse was taken rather than described.** `phraseCopy.ts` already
   solved two problems this path has — writes chained so two presses land in the order made, and one
   status line disposed before the next. Its machinery was extracted; `phraseCopier` delegates to it
   with its public shape and its nine tests unchanged.
3. **`at` became optional rather than defaulting to `0`.** With a default, story 1.1 alone would have
   put live controls under every fence on the page before anything could act on them.
4. **A quote is numbered on the way OUT, not IN.** The plan said the opposite and the code said this;
   three reviewers caught the contradiction in story 1.1's code round, and the cost would have landed
   in story 2.1, whose seam test would have been written to the plan and expected the quote first.

Two more the code rounds added, neither in the plan: **the write has a ceiling** (a clipboard that
never settles would otherwise wedge every later press for the life of the session, and one abandoned
at the ceiling can still land, so the newer text is put back), and **the whole-answer control stopped
failing silently** — it wrote with a bare `void` and no catch, which is one site of a protective
decision guarded and the other left.

## The open tail

- **The `reply` tag is only as good as the prompt that asks for it.** The product recognises the tag
  and never requests it; a model that ignores it gets a plain *Copy block*, and nothing here can tell
  "the model ignored the instruction" from "there was no reply to give". That is the deliberate
  trade — the measurement below is the argument against guessing.
- **Every ordinary blockquote carries a control.** Chosen by the operator with the cost stated and
  measured at 9 rows across 39 answers. If it reads as noise in use, narrowing to fenced blocks is a
  one-line change.
- **A collection-only mode for `answerBlocks` was refused**, and the reason is worth keeping: two
  traversal modes is the divergence this whole design exists to prevent. The hot-path half of that
  objection was taken instead, as a lazy signature.
- **CodeRabbit never reviewed #273.** Its check reported *pass* with "Review rate limited", which is
  not an approval; the change had four multi-model gate rounds and a green CI instead.

## The symptom

An answer in the chat tab often ends with a suggested **reply prompt** — the words the person is
meant to send onward to the AI they are working with. It arrives as a grey block at the foot of the
answer, and there is no way to take just that block.

The only copy control on an answer copies the **whole** answer. What looks like the grey block's own
footer is not one: `chatMessagesHtml` renders a second copy of the per-MESSAGE control pair under
every answer (`chatPage.ts:389-394`, the `.afterRow` div, added because an answer can be a page and
a half), and when the answer ends in a fence that row simply lands under it. Its button posts
`copyAnswer` with the message index and the host writes `said.text` — the entire answer —
to the clipboard (`chatCommand.ts:3074-3083`).

So the person copies a page and a half, then deletes everything except the last ten lines, every
time.

## What the product owns today, and what it does not

The convention the block follows is **not the product's**. `chatPrompt.ts` composes exactly four
things into an outgoing turn: the role half, the task half, `Answer in <Language>.`, and the
material fence (`chatPrompt.ts:58-75`, `:330-332`). Nothing anywhere asks a model to end with a
reply prompt, to use a heading, or to use a fence.

The instruction lives in the operator's own **model preset**, as the role half — for
`Gemini 3.8 Flash (Med)`: *"Если в тексте есть вопросы, или нужно мнения - вконце должен идти
краткий промт что ответить"*. The extension has no contract with that sentence and cannot recognise
what it produces.

## The measurement, before any solution

Taken 2026-09-15 over the live store, `C:\Users\strug\AppData\Local\coai-mcp\chat-conversations`:
22 conversations, 39 model answers, **every one of them `gemini-3.8-flash-medium`** — so this
describes that model and licenses nothing about the others.

| | |
|---|---|
| answers ending in a reply prompt | **20 of 39** |
| distinct spellings of the announcing heading | **14** |
| the most common one, `**Краткий промт для ответа:**` | 5 of 20 |
| wrapped in a fenced block | 16 |
| wrapped in a **blockquote** instead | **4** |
| fences tagged `text` that ARE the reply prompt | 14 |
| fences tagged `text` that are an **ordinary** block | **2** |
| answers carrying more than one fence | 1 (four of them) |

**What it settles.** A heuristic over today's output cannot be trusted. The heading has fourteen
spellings, a fifth of the answers use no fence at all, `text` is already ambiguous, and "the last
fence" is not unique. Any detector written against this corpus would be reading a convention nobody
owns, and would be wrong roughly a fifth of the time while looking right.

**What it does not settle.** One model, one operator, one month. Nothing here says how another
vendor frames a reply prompt, and the design must not depend on it.

### How many buttons this actually adds

The objection to "a row under every block" is noise, so it was measured rather than argued, over the
same 39 answers, counting every `code` and `blockquote` the renderer actually draws — which is what
this plan numbers:

| | |
|---|---|
| copy rows added, in total | **31** — 22 under fences, 9 under quotes |
| mean rows per answer | **0.79** |
| answers getting none | 12 |
| answers getting exactly one | **25** |
| answers getting two | 1 |
| the worst answer | **4** |

So the common answer gains one button, and no answer in a month gained more than four. Including
blockquotes costs 9 rows across 39 answers, which is what makes the operator's choice cheap rather
than merely defensible.

### The two-enumerator bug, demonstrated on the corpus rather than predicted

A control has to say WHICH block it belongs to, and the obvious coordinate is an ordinal. The page
gets its blocks from `renderAnswer`/`marked`; a host resolving "block N" from the stored markdown
would count its own. That is one list with two enumerators, and
`.agents/conventions/common/testing.md` names the failure: both suites stay green while the button
copies the wrong text.

It is not hypothetical here. Counting top-level `code`/`blockquote` tokens against the `<pre>` and
`<blockquote>` elements `renderAnswer` actually emits, over the same 39 answers:

| | |
|---|---|
| answers where the two counts agree | 38 |
| answers where they **differ** | **1** — `e51854d6`: 3 top-level fences, **4** emitted |

The fourth is a fence nested inside a list item. So "the Nth block of the top-level markdown" and
"the Nth block the renderer actually drew" are already different numbers, today, on a real answer —
and the difference would have shipped as a button that copies the wrong text from the one answer in
the month that has a fence inside a list.

`MAX_DEPTH` is the same hazard from the other end: `blocks()` stops recursing past depth 8 and emits
the rest as escaped text (`renderAnswer.ts:214-219`), so a deeply nested answer genuinely contains
FEWER drawn blocks than a re-lex would count. Any separate host-side counter would count blocks the
page never drew, and every ordinal after them would be off by one.

**So the walk that DRAWS is the walk that COUNTS.** There is one function, it emits the HTML and
appends to a block list in the same pass, and the ordinal written into the button is the index the
push returned. The two cannot disagree without the walk disagreeing with itself — which is not a
thing a test has to check for, because there is nothing there to diverge.

## What this will do

Three things, decided by the operator on 2026-09-15.

1. **Every fenced block in an answer gets its own copy control**, as a small row underneath that
   block.
2. **Every blockquote gets one too.** Chosen with the cost stated: an ordinary quotation will also
   carry a button. It is what covers the 4 answers in the measurement that used `>` rather than a
   fence, without a second detector that has to guess which quote was meant.
3. **The product reserves one fence tag, `reply`.** A block opened as ```` ```reply ```` renders its
   row as **"Copy the reply prompt"** rather than **"Copy"**. Nothing else about it changes.

The extension does **not** touch the outgoing turn. The operator asks for the tag in their own role
preset, and the help says how. That keeps `chatPrompt.ts`'s four-part composition and its
`serviceLines` contract exactly as they are, and means a person who wants none of this is unaffected.

The tag is free: ```` ```reply ```` occurs **zero** times in the 22 stored conversations, and
`reply` has no meaning anywhere in `src_vs_code/src`.

### What it deliberately does NOT do

- **It does not detect a reply prompt.** Without the tag there is no "reply prompt" — only blocks,
  each with a plain Copy. That is the whole point of the measurement above: the product either owns
  the marker or it guesses, and it will not guess.
- **It does not change what the existing controls do.** The whole-answer `Copy` and
  `Carry nothing above` stay exactly as they are, in both rows.
- **It does not add a control to a block the page never drew.** A block nested past `MAX_DEPTH` is
  rendered as escaped text and is not a block; it gets nothing, and it does not consume an ordinal.

## The seam

**One walk, two exports.** `renderAnswer.ts` gains a private `walk(markdown, at): { html, blocks }`.
As the existing recursive descent reaches `case 'code'` or `case 'blockquote'`, it appends one
`AnswerBlock` to a local list and emits the block's copy row using **`blocks.length - 1` after the
append**. Nothing computes an ordinal twice.

> **Never the return value of `push`.** It is the new LENGTH, not the index — so the first block
> would be numbered 1, every button would copy the block after its own, and the last would error out
> as missing. Caught on the plan round by gemini, before a line was written; the first test below
> pins the first block at `0` so the language cannot drift back.

```ts
export interface AnswerBlock {
  readonly kind: 'code' | 'quote';
  readonly reply: boolean;
  readonly text: string;
}

export function renderAnswer(markdown: string, at = 0): string;      // the page:  walk(...).html
export function answerBlocks(markdown: string): readonly AnswerBlock[]; // the host: walk(...).blocks
```

- `at` is the message index, so the button can name both coordinates on itself. It defaults to `0`,
  so nothing else that calls `renderAnswer` changes.
- **What a block IS** is `token.text` for both kinds, read back from `marked` 18.0.12 rather than
  assumed: a fence's `text` is its body without the ``` lines and without the info string; a
  blockquote's `text` is its content with one level of `>` markers already stripped. That is exactly
  what a person wants on the clipboard.
- The `catch` path returns the escaped paragraph **and an empty block list**, so an answer that
  failed to tokenize offers no controls and the host agrees, because it is the same walk.

**The row**, emitted immediately after `</pre>` or `</blockquote>`:

```html
<p class="blockRow"><button type="button" class="copy blockCopy" data-block="N" data-at="M" data-sig="S">Copy block</button></p>
```

`reply` is recognised by lowercasing the language already computed at `renderAnswer.ts:182`; it
changes the button's words to **Copy the reply prompt** and nothing else.

> **"Nothing else" is compared against a TAGGED fence, not a plain one.** The plan first said the
> `<pre>` output stays byte-identical to a plain fence, and that is false — measured on marked
> 18.0.12: an untagged fence emits `<pre><code>`, while any tagged one emits
> `<pre><code class="language-…">`. So `reply` and `text` must come out identical **but for the class
> name**, and a companion assertion says so. Written down because the obvious way to make the
> original claim true is to strip the class, which would change `<pre>` output for every tagged fence
> in the product.

**`at` is optional, not defaulted to `0`.** A row is drawn only when the renderer is told which
message it is drawing. With a `0` default, the renderer alone would put live rows reading
`data-at="0"` under every fence on the page before anything could act on them — a visible control
that does nothing, which is the half-wired seam this build order exists to avoid. `renderAnswer(md)`
with one argument therefore stays byte-identical to today.

### Three labels, because two of them used to be the same word

An answer ending in an untagged block would otherwise show **two adjacent buttons both reading
"Copy"** — the block's row, and immediately under it the message's `.afterRow`, whose button copies
the entire page-and-a-half answer. That is the symptom this plan opens with, re-created one line
lower, and a reviewer caught it on the plan round.

So the words are made to say the scope, and the message-level control is renamed in the same task:

| Control | Was | Is |
|---|---|---|
| the message row, above and below an answer | `Copy` | **`Copy answer`** |
| a block's own row, untagged | — | **`Copy block`** |
| a block's own row, fence tagged `reply` | — | **`Copy the reply prompt`** |

Renaming the existing control is a deliberate widening of this task: it is the cheapest fix for the
collision, and it also removes the original confusion — the operator read that button as belonging to
the block above it, which is exactly what its name invited.

### The signature, so a rewritten answer cannot be copied as the old one

The ordinal is positional, and two reviewers independently raised the same defect: if a message's
stored text were ever rewritten in place while keeping the same number of blocks, the ordinal would
stay in range and the host would copy the NEW block under a button drawn for the OLD one — silently.
It cannot happen today (`thread.messages` is only appended to, or truncated from the end), so this is
a guard against a change somebody makes later, and it is what turns a caveat in the risks section
into something structural.

`walk` computes one short signature of the markdown it was given and writes it into every row as
`data-sig`. The page echoes it back untouched. Before resolving an ordinal the host recomputes the
signature **with the same function, over its own stored text**, and refuses on a mismatch with the
same words as an out-of-range block. One function, one definition, and the page is still only an
echo — it cannot make the host copy anything by lying, because a wrong signature only ever refuses.

**Both coordinates sit on the button itself**, never on an ancestor. That is a testability decision:
`bundledPage.test.ts`'s `clickIn` stub resolves `closest` against the real selector but hands back
one dataset, so an ancestor lookup would be satisfied by the stub even if the ancestry were wrong in
a real DOM — a hollow test of exactly the kind this repository has already been bitten by.

**The wire** is a command of its own, not a flag on `copyAnswer`:

```
page → { type: 'command', command: 'copyBlock', index: <message>, block: <ordinal>, sig: <signature> }
```

Overloading `copyAnswer` with an optional `block` would make an unreadable ordinal ambiguous between
*ignore* and *fall back to the whole answer*, and the fall-back reading is a silently-wrong-clipboard
bug of the very family this design exists to prevent. A distinct kind makes "unreadable → nothing
happens" the only possible reading. It costs one property on `ChatPanelHooks`.

| Step | Where |
|---|---|
| parse + validate (both integers, safe, ≥ 0; `sig` a string under a length cap; else `ignore`) | `chatMessages.ts`, beside `copyAnswer` at `:335-341` |
| hook | `ChatPanelHooks.onCopyBlock`, `chatPanel.ts` after `:111` |
| dispatch | `chatPanel.ts` `handle`, after `:341` |
| **the decision** | **a new `vscode`-free module with injected ports** — see below |
| the wiring | `chatCommand.ts` `conversationHooks` (`:2819`): two one-line delegations |

### The host decision cannot live in `chatCommand.ts`, and that is measured

The plan first put the handler "in `chatCommand.ts` after `:3083`" and put the end-to-end seam test
there too. That is not buildable: `onCopyAnswer` sits inside `conversationHooks(panels)`, a
non-exported function closing over `import * as vscode`, and **no test in this repository imports
`chatCommand.ts` or `chatPanel.ts`** — there is no `vscode` stub, and the existing wiring tests read
those files as TEXT. The seam test as first written could not have been run at all.

So the decision moves into its own module, `vscode`-free, with the clipboard write and the message
injected — which is not an invention: **`phraseCopy.ts` is exactly this pattern**, and its own header
records why, in those words: *"the interesting paths here are the ones that FAIL, and a rule living
inside the panel host is a rule no test can reach."* `conversationHooks` then holds two one-line
delegations, and the pure module IS the "real host handler" the seam test dispatches through.

**And the shared half is extracted rather than copied.** `phraseCopier` already solves two problems
this path has and the plan had not noticed: two presses start two asynchronous writes and *whichever
resolves last is what the clipboard keeps*, so the writes are chained; and repeated confirmations
would otherwise stack on the status bar, so one live line is disposed before the next is set.
Re-implementing either beside it would be a second copier that drifts. The ports, the chaining and
the one-line rule move into a small shared unit that `phraseCopier` composes and the block path
composes — `phraseCopier`'s public shape is unchanged, so its own tests are untouched.

**One consequence, stated because it is a change of mechanism from what the plan said above:**
failures on this path are said the way `phraseCopy` says them — a transient **status-bar line** —
rather than through `showWarningMessage`. Two mechanisms for one kind of failure is the thing to
avoid, and the existing one has the precedent and the gentler interruption for a copy that did not
land.

The host re-derives the blocks from **its own stored markdown**, the same string the page was drawn
from. Nothing about the text crosses the wire, so a retained or tampered webview cannot dictate what
is copied — `renderAnswer.ts:23` already says the page is a surface and the host is the boundary.

**Every failure on this path is said out loud, and the mechanism is named.** All three go to the
**status bar**, through the injected `say` port — the mechanism `phraseCopy.ts` already uses for a
copy that did not land, so this adds no second channel for one kind of failure:

| What went wrong | What the person sees |
|---|---|
| the ordinal is past the end of the answer | *That block is no longer part of this answer.* |
| the signature does not match the stored text | the same sentence — from the person's side it is the same fact |
| `clipboard.writeText` **rejected** | *Copying to the clipboard failed.* |

The clipboard rejection is the one the plan originally left out, and it is not hypothetical: an
unfocused window or a denied permission rejects. Today `onCopyAnswer` writes with a bare `void` and
no catch (`chatCommand.ts:3082`), so a failed copy of a whole answer is already silent — **that site
is fixed in the same task**, because a protective decision applied at one of its two sites is the
defect this family keeps writing, and there would be exactly two of them the moment this lands.

### The one honest cost: `button` joins the emitted-tag allowlist

`renderAnswer` may currently emit twenty-four elements and the hostile-input test asserts exactly
that set (`renderAnswer.test.ts:27-30`). This adds a twenty-fifth. Model text can never BECOME a
button — a raw `html` token is escaped and shown, so it has no `<` left — but the net is one element
wider, so it is re-sharpened rather than merely widened: every `<button …>` in the output must match
the exact shape above, asserted by attribute, so a button with any other attribute fails.

## Build order

Each step compiles and is testable on its own.

**Two epics, one story each**, decided on Fable per the gate's standing order. Every story ends the
way those orders require: its own `review_code` round, every finding resolved, the tests and the
documentation updated, and a commit.

The order's own heuristic asked for 2–4 epics of 2–4 stories, and it says to say so when that is
wrong for a plan. It is wrong here, for one structural reason: **every cut inside the chain leaves a
visible control that does nothing.** `ChatPanelHooks.onCopyBlock` is a required interface member, so
a story adding the hook without the handler cannot typecheck without a stub; a story adding the page
branch without the parser leaves a button posting a message the host drops. The only seam with no
inert half in it is between the renderer and everything that acts on it — which is exactly one
boundary, and therefore two stories.

**Epic 1 — the enumerator, and what it draws** (`renderAnswer.ts` alone)

**Story 1.1 — one walk, two exports, the rows, the signature.** True when done: `renderAnswer(md, at)`
draws a copy row under every fence and blockquote it actually emits, numbered `blocks.length - 1`
after the append by the same walk `answerBlocks(md)` returns; every row carries
`data-block`/`data-at`/`data-sig` in the exact shape; a `reply` fence's row reads *Copy the reply
prompt* and every other *Copy block*; **`renderAnswer(md)` with one argument is byte-identical to
today's output**, so nothing is visible on the page yet and no control is inert; the catch path
returns the escaped paragraph and an empty list; `button` joins the allowlist behind the exact-shape
assertion and its known-instance companion. Docs: the tag list in the module header
(`renderAnswer.ts:29-31`) and a dated section in `research/module_extension.md`.

**Epic 2 — the control, from the page to the clipboard**

**Story 2.1 — the page names the block, the host copies it, every failure is said, and the words are
updated.** True when done: pressing a block's row on the *shipped bundle* posts `copyBlock` with the
triple; `chatCommandOf` accepts only a well-formed one and answers `ignore` to everything else; the
pure handler resolves the block from the host's own stored markdown, refuses on an out-of-range
ordinal or a signature mismatch, and says so when the write rejects; `onCopyAnswer` goes through the
same shared copier, so the whole-answer path stops failing silently; the message-level control reads
*Copy answer*; `chatPage.test.ts:1287` counts `data-copy=` rather than `class="copy"`; the help in
all five languages says what the `reply` tag is; `research/module_extension.md` records the wire and
the three refusals. The end-to-end seam test lives here, because this is the first commit at which it
can exist.

## Test plan

**The seam test — the one that answers the central risk.**

> `the control the page rendered for a block copies that block and no other`

One answer holding, in order: a fence, a blockquote, a fence **inside a list item**, a fence
**inside a blockquote**, a ```` ```reply ```` fence, and a nest deeper than `MAX_DEPTH` containing a
fence that must therefore be drawn as text and counted by nobody. Render it through
`chatMessagesHtml`, scrape every `data-at`/`data-block`/`data-sig` triple out of the HTML **as
data**, build the message the page would post for each, run it through `chatCommandOf`, and then
**dispatch it through the real host handler with a fake clipboard**, asserting on what
`writeText` was actually handed — compared against **hand-written literal expectations**, never
against the same array indexed a second time, which would pass under any consistent-but-wrong
numbering.

**It goes through the handler, not to `answerBlocks` directly.** The Definition of Done promises
what reaches the CLIPBOARD, and a test that stops at `answerBlocks(md)[block].text` proves the
enumerator and nothing else: the hook could fail to forward, the dispatch could miss its case, the
write could never happen, and the suite would stay green while nothing was ever copied. Raised on the
plan round by two vendors independently, which is what moved it from a unit test to this one.

Watched RED first, twice: (a) emit `ordinal + 1` in the row — the `push`-returns-length trap, which
must fail naming the wrong TEXT rather than a length; (b) make `answerBlocks` skip blockquotes.

> **What this test cannot reach, said rather than pretended.** A third red case — deleting
> `case 'copyBlock'` from `chatPanel.ts`'s dispatch — is not runnable: `handle` is unexported and no
> test in this repository imports `chatPanel.ts`. What guards that wiring is the type system
> (`onCopyBlock` is a required member of `ChatPanelHooks`, so an unimplemented one fails the
> typecheck) plus a source-text assertion in the style the existing wiring tests already use. That is
> a wiring check and not a behavioural one, which is why `PROJECT.md`'s refusal of new behavioural
> source assertions does not cover it — and saying which of the two it is, is the point.

**The ordinal of a quote that CONTAINS a fence is assigned on the way OUT** — after its children, so
the inner fence is numbered first and the numbers run in the same order as the rows. The quote's own
row is emitted after `</blockquote>`, therefore after the inner fence's row, and numbering on the way
out is what keeps the two orders together instead of against each other.

> Written the other way round in the first draft of this plan, and caught by three reviewers
> independently in story 1.1's code round — the danger being precisely that story 2.1's seam test
> would have been written to the plan, expected the quote at ordinal 0, and either failed or provoked
> a "fix" that reversed the renderer.

Measured on marked 18.0.12: such a quote's `text` is `"quoted\n```js\ninner()\n```"`, fence lines
included, so the two controls overlap on purpose — the outer copies the quote whole, the inner copies
just the code. The fixture pins both, and the help says it in one sentence.

> **The first block is `0`.** Asserted on its own, because the whole `push`-returns-length defect
> shows up as every button being one out, and a test that only compares texts pairwise can be fooled
> by a consistently shifted list.

The rest, each named by its guarantee, RED-first where marked:

- **RED** `a fenced block and a blockquote each get their own copy row underneath it`.
- **RED** `a block tagged reply says so on its button, and nothing else about it changes` — red by
  removing the tag check; a companion assertion pins the `<pre>` output byte-identical to a plain
  fence.
- **RED** `a fence deeper than the renderer will draw gets no control, and does not shift the
  ordinals after it`. The fixture must genuinely cross depth 8 — a quote AT depth 8 is still drawn
  and still gets a row, and a fixture that stops short passes without exercising the cut-off, which
  is the hollow shape this repository has already been bitten by.
- `model text cannot become a button, and the scan that says so still finds one` — the exact-shape
  assertion gets its companion: a known good `<button>` the scan is proven to match, so it cannot
  quietly start matching nothing after a reformat.
- **RED** `the shipped page asks the host for the block it names` — in `bundledPage.test.ts` via
  `clickIn('messages', { block: '2', at: '3' })`, made red by removing `[data-block]` from the
  selector. It lives there and not beside `chatPage.test.ts:1325`, whose `closest` ignores the
  selector entirely.
- **RED** `copying a block that is no longer in the answer says so rather than copying something
  else` — clipboard NOT written, warning raised.
- **RED** `a button drawn for an answer that has since been rewritten refuses rather than copying the
  new text` — the signature guard, exercised by rewriting the stored message between render and
  dispatch while keeping the block COUNT the same. Red without the guard by copying the new block.
- **RED** `a clipboard that refuses the write says so` — `writeText` rejects; the person is warned
  and nothing reports success. Asserted for the block path **and** for the whole-answer path, so the
  sweep of the existing site has a test of its own.
- **RED** `the two copy controls on an answer say which scope they copy` — the message rows read
  `Copy answer`, a block's row reads `Copy block`, and no two adjacent controls share a label.
- `a copyBlock naming no readable position is not obeyed` — missing, negative, fractional, `NaN`,
  string; and a missing or non-string `sig`.
- `a block control never appears on the person's own message` — their text goes through
  `markedTurn`, not `renderAnswer`.
- `model text cannot become a button` — `assertOnlyAllowedTags` gains `button`, plus the exact-shape
  assertion that keeps the net sharp.
- `an answer still carries exactly one whole-answer Copy above it and one below` — moved from
  counting `class="copy"` to counting `data-copy=`. The existing count at `chatPage.test.ts:1287`
  stays green by luck (`class="copy blockCopy"` does not contain `class="copy"`), and the next
  person to put a fence in that fixture would meet a confusing failure; it is corrected in this task.

## What the operator adds to their own preset

The product recognises the tag; asking for it stays the person's sentence. Appended to the role half
of the `Gemini 3.8 Flash (Med)` model preset, replacing the clause that is there now:

> Если в тексте есть вопросы, или нужно мнение — в конце дай краткий промт что ответить, и помести
> его в блок, открытый тремя обратными кавычками со словом `reply`.

The help page says the same thing in each of its five languages.

## Growth surfaces

**None.** Nothing new is stored, no file is written, no table gains a row, no process is spawned.
The blocks are derived on demand from a string the host already holds, and the derivation is
discarded. Said explicitly because a plan that creates nothing still has to answer the question.

## Risks and the open tail

- **The ordinal is positional — and that is now guarded rather than merely documented.** It is
  stable for the same reason `data-copy` has been since it shipped: a message's text is written once
  and messages are appended, never reordered. The plan first recorded the in-place-rewrite case as a
  caveat; two reviewers raised it independently on the plan round, and they were right that a caveat
  is not a guard. The `data-sig` comparison above closes it, including the hard half — a rewrite that
  keeps the same block COUNT, which no range check can see. The residual is only what a short
  signature always leaves: a collision, which refuses nothing and copies text that hashes the same as
  what was drawn.
- **The tag is only as good as the preset.** A model that ignores it and writes ```` ```text ````
  gets a plain *Copy*, and nothing in the product can tell "the model ignored the instruction" from
  "there was no reply to give". Accepted: the alternative is the guessing the measurement refuted.
- **Every ordinary blockquote gets a button.** The operator chose this with the cost stated, and the
  measurement puts it at 9 rows across 39 answers. If it reads as noise in use, the cheapest retreat
  is a CSS change, not a structural one.
- **This was designed against ONE architecture round, not two.** The second architect was lost to an
  API spend limit rather than to a judgement, so the alternative shape — a pure enumerator module
  beside `chatCarry.ts`, with the renderer asking it — was never written down. Recorded so nobody
  reads the single design as a comparison that happened.

## What the plan round changed

One round, `good_enough` at 7 gating findings against a threshold of 6, all three reviewers
answering. Seven findings taken, three rejected with reasons. Each accepted one is folded into the
sections above rather than listed here, because they are part of the design now:

- **the `push` trap** — the plan said the ordinal was "the index that append returned", which is the
  new LENGTH; every button would have copied the block after its own (gemini);
- **the second "Copy"** — an answer ending in an untagged block would have shown two adjacent buttons
  reading the same word with different scopes, which is this plan's own symptom one line lower
  (gemini);
- **the seam test stopped short of the clipboard**, proving the enumerator while the hook, the
  dispatch and the write went unexercised (gemini and codex, independently);
- **the in-place rewrite** — a caveat where a guard belonged (local and codex, independently);
- **a rejected clipboard write had no path at all**, on the new site or the existing one (codex);
- **the refusal mechanism was unnamed** (local).

Rejected: that the page and host could hold different `marked` or `MAX_DEPTH` versions — they are one
module in one process, since `renderAnswer` runs in the extension host and the webview never parses
markdown; that a "copy the block plus its context" control was needed — that is the whole-answer Copy
that already exists twice; and that a fallback detector should find an untagged reply prompt — the
measurement in this plan is the argument against exactly that.

## What the split round changed

The epic/story split was decided on Fable, per the gate's standing order, and it did not merely cut
the plan up — it found four things wrong with it, each verified against the code before being taken:

1. **The seam test as written could not run.** No test here imports `chatCommand.ts` or
   `chatPanel.ts`, and there is no `vscode` stub. The decision moves to a ports module on the
   `phraseCopy.ts` pattern, and the shared copier is extracted rather than copied.
2. **`at = 0` would have shipped inert controls** at the end of story 1.1. `at` is optional instead.
3. **"byte-identical to a plain fence" is false** — verified on marked 18.0.12: a tagged fence emits
   `class="language-…"` and an untagged one does not, so the comparison is against `text`.
4. **A quote containing a fence yields two overlapping controls**, and its `text` includes the fence
   lines — so the ordinal's assignment order had to be decided rather than discovered by a test.

Six stories became two, because every other cut leaves a control that does nothing.

## Definition of Done

- [ ] Every fenced block and every blockquote the renderer DRAWS carries a copy row beneath it —
      including one nested in a list, excluding one past `MAX_DEPTH` that was drawn as text.
- [ ] A ```` ```reply ```` block's row reads **Copy the reply prompt**; every other block row reads
      **Copy block**, and the message-level control reads **Copy answer** — no two adjacent controls
      share a label.
- [ ] The first block of an answer is numbered `0`, asserted on its own.
- [ ] What reaches the clipboard is the block's own source text, out of the host's stored markdown —
      asserted by dispatching through the real host handler with a fake clipboard, never by calling
      the enumerator and comparing it with itself.
- [ ] A button drawn for an answer whose stored text has since changed REFUSES, even when the block
      count is unchanged.
- [ ] A rejected clipboard write is surfaced — on the block path AND on the whole-answer path, whose
      existing silent `void` is fixed in the same task.
- [ ] One enumerator decides what "block N" is; a disagreement between the page and the host is
      structurally impossible, and a test exercises both sides against the same answers.
- [ ] The outgoing turn is byte-for-byte unchanged — `chatPrompt.test.ts` untouched and green.
- [ ] The page's `closest(...)` selector was widened, and the bundled page test proves the new
      control is reachable through it.
- [ ] New behaviour is tested by RUNNING the page, not by asserting over its source text.
- [ ] The help says what the `reply` tag is, in all five languages, in the same commit.
- [ ] `npm run typecheck` clean; the whole suite green against the 2718/2717 baseline.
