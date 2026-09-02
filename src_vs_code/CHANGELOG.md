# Changelog

## 0.25.0 — 2026-09-02

**A local reviewer was silently running as a codex one.** The `Runtime` type gained `'local'` and
the list `vendorsFrom` validates against did not — and an unknown runtime is deliberately rewritten
to `codex`, because that is the one that takes a base URL. So every saved local reviewer came back
as codex: the row kept the name `local`, listed GPT-5.6 in its model dropdown, offered codex's run
and install buttons, and a round would have gone through the Codex CLI — the one thing the local
runtime exists to avoid, and the thing measured as spending 21k tokens of someone else's system
prompt before any review content.

The comment beside that check already said the two lists had to be kept in step. That is the
argument against fixing it with a longer comment: the type is now DERIVED from the list, so there
is nothing left to keep in step, and a test walks every runtime and every preset through a save and
a read.

**`call_human` now stops the review, which it did not.** A round budget was never a budget: nothing
asked how many rounds had been spent before opening one — the number was read only at the END, to
choose between `revise` and `call_human` — and recording decisions cleared the human gate
unconditionally. So the loop after exhaustion was: run a full round, be told to ask a person,
resolve, run a full round, be told to ask a person, forever.

It is not hypothetical. On a three-round budget a stage reached round **ten**, and the AI running it
judged its own work: rounds 1–3 found real defects, 4–9 chased "progressively narrower crash
windows", and round 10 **introduced a bug**. Now `review_plan` and `review_code` refuse after
`call_human` until a person answers, and the answers are the ones the panel already offered — *keep
going* and *stop and act on the findings* grant a fresh set of rounds, *stop and talk to me*
advances nothing, and shipping with the findings open is still `humanDecision: "proceed"`. The
snippet says so too (v4), so a pasted copy that predates this reports itself as behind.

**Reviewers left running by a server that died are collected.** The timeout kill is performed by the
parent, so when `coai-mcp` goes away — which is what happens every time an MCP client restarts —
its in-flight reviewers keep running with nothing left to stop them. Reported from a macOS checkout:
an Antigravity child started at 00:03 was still alive at 10:00, its vendor removed from the
configuration, its server long gone.

Every reviewer launch is now recorded with the process that owns it, and a later server collects the
orphans at startup. The care is all in the refusals, because the vendor CLIs are programs a person
also runs by hand: a process is killed only when this product recorded starting it, its recorded
start time still matches so the PID cannot have been reused, and the owning server is provably gone.
A live second server's reviewers are never touched.

**Pasting into `~/.claude.json` says which entry wins.** Claude Code reads that file at two levels,
and a per-project `projects["…"].mcpServers` entry silently outranks the top-level one. Somebody who
pastes at the top level and restarts gets no signal at all that their paste was read and overruled.

## 0.24.0 — 2026-09-02

**Settings reach the server even if you never open the panel.** This one came from a colleague on
macOS, and the report was precise: they set `onExhausted` to `good_enough`, restarted everything,
and the server launched by Claude Code went on answering `call_human` an hour later — ten third
rounds in a row, every one rubber-stamped by a person who had already decided otherwise.

The mechanism that was supposed to carry the setting has existed since `mcp-v0.3.1`: the extension
writes `settings.json` into the data directory and the server reads it underneath the environment,
which is what makes the pasted config block a one-time paste. **It simply never ran.** The write sat
in the panel's `render()`, behind `if (this.view === undefined) return`, and the configuration
listener was registered inside `resolveWebviewView` — which VS Code calls LAZILY, only when somebody
first opens the view. In a window where nobody had opened the ConnectOtherAIs panel, nothing watched
the settings and nothing mirrored them, so the server kept running on an `env` block pasted months
earlier.

The fix is not a bigger guard. Mirroring settings to the server was never the panel's job — the
server needs them whether a person is looking at a webview or not — so it moved to activation, holds
no VS Code type, and has tests that fail if it goes back. It also stopped rewriting the file with
identical content on every repaint, which was asking the server to reload its settings several times
a minute for nothing, and it no longer registers a second configuration listener each time the view
is closed and reopened.

**The pasted instructions gained the rule that keeps a review loop converging** (snippet v3, so an
older copy in a repository now says so). Reject a finding that is wrong, out of scope or already
covered the FIRST time it appears, not only when the rounds run out. Accepting everything to be
agreeable is what stops the count falling: each accepted finding rewrites the plan, and the next
round is handed fresh text with new things to find in it. Also from the same colleague, who worked
it out from ten rounds that never converged.

## 0.23.0 — 2026-09-02

**A model on your own machine can be a reviewer.** *＋ Add a reviewer → Local model (Ollama / vLLM)*
adds a row called `local` whose dropdown is what THIS machine has installed — each with its parameter
size, quantisation and disk size, read from the engine rather than from a list shipped here. Nothing
found says where it looked and why, because an empty dropdown with no reason is indistinguishable
from “you have no models”.

It is deliberately **not** the Codex CLI pointed at a local endpoint. That was tried first and it
answers — but codex's own system prompt is 21k tokens before any review content, measured, so a small
model is refused outright and a large one pays for a prompt unrelated to the review. A local reviewer
is a direct call to the engine's OpenAI-compatible endpoint with the finding schema, `temperature`
and `seed` pinned, run as a process like every other reviewer so the timeouts, the kill and the usage
parsing are the ones that were already there.

**An endpoint that is not on this machine says so in the row**, naming the host and what is sent to
it — the plan, the diffs, and the file contents around them. `localhost`, `::1` and the whole
127.0.0.0/8 block are this machine, decided by parsing the host, so `127.0.0.1.evil.test` is
somebody else's.

**Local tokens are real; local money is a dash.** The engine reports what it used, so a local round
appears in the spending chart with real numbers. Cost stays null rather than 0, because free and
unpriced are different facts: what a local run costs is electricity and a busy card.

**A setting value the server does not understand now says so at startup** instead of quietly doing
something else. This one came from a bug report that was not one — “I set this and it still keeps
asking me” — where the setting was applied, the value was read, and the running server was a build
from the day before that value existed. The fallback stays; what changed is that it is audible, and
that the message says which half to update.

## 0.22.0 — 2026-09-01

**The pasted CLAUDE.md snippet carries a version, and the panel says when a copy has fallen behind.**
Handing somebody text to paste means the source moves and the copy does not — and the copy is the one
being obeyed. That is not hypothetical: a block pasted into one repository here predated the SCOPE
rule, so the AI following it would call `review_code` with a commit subject and meet a refusal that
nothing in its instructions explained.

The snippet now emits a marker, and the Server section reads it back out of this workspace's
`CLAUDE.md`, `AGENTS.md`, `GEMINI.md` or `.github/copilot-instructions.md` — the same four files the
server reads for its conventions pass. Older says both numbers and what to do; a copy from before
versioning says so without inventing a number for it; a copy NEWER than this extension says to update
the extension rather than paste over the repository. Current and absent say nothing, because a
repository that has not adopted the gate is entitled not to.

**The version cannot silently go stale**, which is the part that makes it worth having: a test pins
it to the snippet's own hash, so editing the text fails the build until the number moves with it —
and the failure message carries the next number and the new hash, ready to paste.

## 0.21.0 — 2026-09-01

**The review gate says out loud that it is ADDITIONAL.** The `feature-dev` plugin's quality phase
launches three Claude reviewers in parallel at exactly the moment the CLAUDE.md snippet says to call
`review_code` — and nothing in that snippet forbade the phase or protected it. Between a numbered
CONTRACT that "the server enforces" and one phase of a workflow, the emphatic text wins, so this one
now says what it means: run your own reviewers exactly as you would have, start them and this gate at
the same time, and neither replaces the other. Your reviewers read the whole change with the
repository in context; this gate asks a different vendor's model the questions your own model is
worst placed to answer. The same sentence is in the server's advertised instructions, which reach
every client whether or not anybody pasted the snippet. A `call_human` verdict stops the SHIPPING,
not the task.

**The snippet also shipped one paragraph twice**, verbatim. Everybody who pasted it got it twice. A
test now fails when any paragraph appears more than once.

**A model's price is looked up instead of typed.** The two rate fields were empty on every machine
this shipped to, so the money column was dashes — and both public price sources turn out to carry
every model this build offers: OpenRouter's model list, and LiteLLM's price file for the models
OpenRouter does not list. The published rate shows as the field's placeholder and feeds the money
when the field is empty; anything typed wins over it, per field. It is a LIST price, not a bill —
reviews run on your subscription — so it keeps the tilde that already means "worked out, not
charged", and the tooltip names which list it came from.

**`agy update` exists, and this product said it did not.** `agy --help` lists four subcommands and
update is not among them; the command works. It went into a comment, a changelog, a plan and a module
doc as "no update subcommand at all" — inferred from an incomplete list instead of run. The update
button now uses each vendor's own command, verified one at a time: `claude update`, `agy update`,
and re-installing for codex and gemini, which is what their own docs prescribe.

**Two defects in the update button, reported within the hour of 0.20.0.** It looked green on an
up-to-date CLI — not the state logic but the HOVER: `.upd` inherits `.run`, whose hover paints a
green border, and the grey rule sat earlier at equal specificity. Hovering is how you read a tooltip,
so the wrong colour was the only colour anybody saw. And codex reported "could not be read" on a
machine where `codex --version` answers: on Windows an npm global is a `.cmd` shim and `spawn`
without a shell does no PATHEXT resolution.

**Recent rounds is a 72-hour window that scrolls**, rather than the six newest whatever their age —
a quiet week left last month on screen looking current, and a busy afternoon hid the morning. A round
still running is always shown. The rounds markdown file gained a **When** column and sorts newest
first; an undated round sorts last rather than floating to the top.

**A vendor's spending row can be forgotten**, after a confirmation. It clears the counters without
touching the ledger on disk — a watermark on this side, not a rewrite of a file the server is
appending to — so nothing is destroyed and the row returns the next time that vendor runs.

## 0.20.0 — 2026-09-01

**Every reviewer row has an update button, and it says by its colour whether there is anything to
update.** Green when the vendor publishes a version newer than the one on this machine, grey when you
are on the newest — and grey again when either number could not be read, because a button that
lights up on a failed fetch is worse than one that never lights up. Both versions are in the tooltip
and in the accessible label, so the colour is the fast signal and never the only one.

Pressing it opens a terminal with the same command the install button uses. **That is the vendors'
own answer, not a shortcut**: OpenAI's quickstart prints the identical `curl … install.sh | sh` under
*Install Codex* and under *Update Codex*, Anthropic's native install is the same script, and `agy`
has no `update` subcommand at all. Re-running the installer IS the update for every CLI here.

Where the published version is read from, checked at each vendor's own site rather than recalled:
npm's registry for `@openai/codex`, `@google/gemini-cli` and `@anthropic-ai/claude-code`; for
Antigravity, the release manifest Google's own `install.sh` reads. A vendor this build has no
official source for gets no guess — its button stays grey.

**The collapsible headers carry the panel's colours.** Eight identical grey words were a column you
had to read; each one now has its own tone from the same palette the role boxes use, and the chevron
follows for free. Every tone is a `--vscode-charts-*` token with a hex fallback, so a theme that
defines the charts palette moves these with it.

## 0.19.1 — 2026-09-01

**A role's Rounds number would not stick, and the prompt pickers would not follow it.** Type 3 into
Architecture's *Rounds*, switch to another view and back, and the old number was there again — and the
round pickers never changed count either. Two symptoms of one defect: the input travelled as
`data-vendor="Architecture"`, and `data-vendor` means A VENDOR. The provider looked for a vendor with
that id, found none, wrote the vendor list back unchanged, and never touched `coai.rounds` at all.

The rendering had been right the whole time — it sizes the pickers from that role's own rounds — and it
was reading a value nothing could change. What was wrong is that one attribute carried two different
KINDS of key with nothing to tell them apart, so the routing is now a decision with three named
outcomes (a plain setting, one vendor's property, one role's entry) that is tested without VS Code,
and the record is merged rather than replaced so writing one role keeps the other three.

Two tests in this repository had pinned the broken markup in place — they asserted the control existed,
which it did, while it could not save anything. A test that copies markup can only confirm it; the new
one asks where the value LANDS.

**Two translator leftovers went with it**, found by the compiler on the way through: a
`customModel` branch still writing `coai.translator.model`, a setting the manifest no longer has, and a
webview id that still nominated `__translator__` for a model box with no vendor. Neither was reachable.

## 0.19.0 — 2026-09-01

**Rounds and a threshold PER ROLE.** They were per stage, and one number for both before that — the
same discovery each time: a budget shared by things that are not alike makes the cheapest of them pay
for the most expensive. Architecture may be worth two passes with different lenses while performance
is worth one. Each role now carries its own two numbers, beside its own prompts, in one box; a finding
counts against the threshold of the role that RAISED it, so a noisy role cannot spend another role's
tolerance; and a stage passes when every role is at or under its own number rather than when one total
is small enough. A role whose rounds are spent simply stops being asked.

A threshold of **zero** survives the trip to the server now. The panel had always accepted it and had a
test saying so, while the server required a positive number and silently substituted its own default —
the two halves disagreeing about a number somebody had deliberately set to nothing.

**Deal the prompts across vendors** — one switch per stage, off by default. Off, every vendor answers
every question, and two vendors filing the same finding is a fact the gate can use. On, the round's
prompts are dealt out one per vendor: a code round costs three launches instead of six, and that
agreement is gone. Measured on a real commit: 3 reviewers against 6, 39 % of the tokens, 59 % of the
wall clock. It is a real trade and the default is the conservative half of it.

**A fourth answer when the rounds run out: *Good enough — take what's true and move on*.** Between
"ask a human" and "continue anyway", which touches nothing: the AI reads what is still open, applies
the findings that are true and useful, rejects the rest with reasons, and proceeds. Observed end to
end in the pre-delivery campaign.

**The prompt picker no longer names a prompt the server will not run.** It passed the DEAL switch into
the mirror function's ROTATING slot, so ticking *Deal the lenses across vendors* displayed
`arch-boundaries` for round 2 of Architecture while the server ran `architecture`. The server's
rotation read only `COAI_ROTATE_PROMPTS`, which this extension stopped writing when the Prompts and
Gate sections were merged — so rotation had no way in from the product at all, and its only surviving
effect was that lie in a dropdown. Rotation is removed from both halves; two different lenses on one
change are still available by picking them on two rounds. `COAI_ROTATE_PROMPTS` keeps working as the
alias for the two dealing switches. Nothing was lost: rotation was measured worse than asking the
universal question twice — 17 distinct findings against 25, for less money.

**The translator is gone.** A `call_human` question is one fixed English sentence and three buttons,
so there was nothing left to translate. `Ask and answer in`, `Translated by` and the settings behind
them are removed, along with three tooltips describing controls that no longer existed — now caught by
a test that fails when help describes a control the panel does not render. **The help's own five
languages are untouched**: that is the reading side, and every article that changed in this release was
rewritten in all five.

**Help brought level with the product**, in English, Русский, Українська, Deutsch and Español: the
per-role gate, the dealing switch, the conventions pass that owns round 1 of every code role, money and
the tilde that separates a billed figure from a computed one, and the ⤤ button that installs a
vendor's CLI with the command for the OS the terminal will actually run in.

## 0.18.0 — 2026-09-01

**Money, for the vendors that do not report any.** Only Claude prices its own runs, so every other
row read a dash — true, and useless against the question you actually have. Each reviewer now takes
two rates, `$ / 1M in` and `$ / 1M out`, and the spending section shows what a vendor cost. The rates
come from YOU, never from a table shipped here: a price list would be wrong for anyone on a flat
subscription, wrong the first time a vendor changes a price, and wrong silently both times.

What is worked out from a rate is marked with a tilde — `~$0.42` — and what a vendor actually billed
is not. The totals keep the two apart for the same reason. The rates never leave the panel: the
ledger records tokens, which are facts, so correcting a rate re-prices your whole history.

**Rounding fixed while in there.** There were two `money` functions, and the one the spending section
used rounded to cents — so a round costing $0.0004 displayed as `$0.00`, which reads as free. Its
twin, four lines away in another file, carried a comment warning about exactly that. One now.

**The markdown rounds view renders as a table again.** Its delimiter row had eight cells against a
nine-column header after the `What` column was added, and markdown answers a mismatch by not
treating the block as a table at all — hence a preview full of pipes. The columns are declared once
and the header, the delimiter and every row are built from them; cells are flattened, so a reviewer
sentence carrying a newline can no longer end the table mid-row.

**The code stage says its own arithmetic.** "Three reviewers per vendor" made a reader ask whether
each reviewer runs six times. It does not — six is the number of reviewers in a round — so the
section now multiplies it out in your own numbers: *2 vendors × 3 roles = 6 reviewers per round, each
runs once per round, up to 2 rounds*.

**And the panel no longer lies about round 1.** It showed `Universal` for a round the server would
run `Conventions` in, because the conventions rule had been added on the server side only.

## 0.17.0 — 2026-09-01

**WSL works, and this is the release that makes it possible.** Three separate blockers, measured
rather than assumed:

- **Every reviewer row now has a CLI path field.** It had none, and the environment variable that
  used to serve the purpose was read only in a branch the panel never uses — so from the moment
  anybody opened this panel, saying WHERE a CLI lives was impossible. In WSL that is fatal: `codex`
  and `gemini` resolve there to the WINDOWS npm shims through the interop PATH, which run Linux node
  against a Windows install and die. Empty still means "look it up on PATH", which is right almost
  everywhere.
- **The install button offers only what a vendor itself publishes.** Codex, Gemini and Claude have
  official npm packages and the button gives you the exact line. Antigravity does not: `agy` ships
  with the Antigravity app and npm has no package for it. There IS a convenient `antigravity-cli`
  snap at Google's own version — published by a third party — and it is deliberately NOT offered. A
  button that installs software gets pressed without reading, so it may only ever offer an official
  source, and a test now holds every command against that rule.
- **On Linux, an Antigravity reviewer says so plainly** instead of reporting a missing file: Google
  publishes no Linux CLI, so use codex or claude there — or, on WSL, point the reviewer's new CLI
  path field at a Windows `agy.exe`, which does run through interop (measured).
- **A round now tells you when a CLI is installed but not signed in.** A fresh codex answers with
  five reconnect attempts and two 401s; nothing in that wall says to run its login. Same for a
  directory the CLI has never been trusted in, which every review worktree is.

## 0.16.0 — 2026-09-01

**The gate reads your project's own rules, and spends the first code round on nothing else.** Every
repository carries written conventions — `CLAUDE.md`, `AGENTS.md`, `GEMINI.md`, `.claude/rules` — and
the reviewers had never been shown a line of them, so the gate could call a change well written by
its own standards while it broke four rules the project enforces on its humans. Round 1 of all three
code reviewers now judges the diff against those rules and nothing else, quoting the sentence it
breaks. Pick something else for round 1 and that still wins.

The prompt for it was chosen by measurement — three variants, two vendors, plus a variance control —
and the measurement decided nothing: they were indistinguishable, and across 56 findings not one
cited a rule that did not exist. What mattered was putting the rules in the prompt at all. The record,
including the violation all eight cells missed, is in `research/RESULTS_conventions_prompt.md`.

**Rounds and threshold are set per stage.** Plan review defaults to 3 rounds and 2 findings; code
review to 3 and 3. One number for both was strict on a page of text and impossible on a diff of a
dozen files — measured here, where the plan stage passed at two and the code stage never passed.

**A `call_human` verdict is answered with three buttons**, not a text box: keep going with another
set of rounds, stop and act on the findings, or stop and talk to me. Each says what it will cause.
And they now DO something — a typed answer to that card used to be written to a file nothing read, so
you could decide, watch the card disappear, and have changed nothing. None of the three ships a
change over open findings; an override meaning "ignore all this" is an off switch on the gate.

**The Prompts and Gate sections are reframed**: the plan role in its own frame, the three code roles
in one, each with a coloured edge from the sibling product's palette — and each showing only the
rounds its own stage will actually run.

## 0.15.0 — 2026-09-01

**Install a reviewer's CLI from its own row.** The new `⤓` button beside `▶` opens a terminal with
the install command typed and waiting. A fresh WSL box has none of these CLIs and the panel is where
you are standing when you find that out; the answer living on somebody else's docs page is why a
reviewer never gets added. The command is the same in PowerShell and bash — npm does not care — so
what is chosen per shell is how to get node first, read from your terminal profile rather than from
the platform. The Antigravity CLI ships with its app rather than through npm, so that row opens the
instructions instead of a command that would fail.

**`▶` opens the right CLI.** Its executable came from a two-step chain that fell through to
`codex`, so an Antigravity row started a different vendor's CLI under that vendor's name — on the
button whose whole purpose is signing that vendor in.

**An update from the ⋯ menu repaints the panel too.** Only the panel's own button did, so an update
started from the menu left the Server section showing the version it had just replaced — the very
symptom the button was fixed for. Both doors repaint now.

**A blocked update tells the truth about which failure it was.** A sharing violation (something has
the file open) and an access denial (a read-only attribute, an ACL) are different problems with
different cures, and collapsing them sent people to close a program that was never the reason. They
are classified by error code now, and the ambiguous one names both possibilities instead of
asserting one.

**Two clicks are one install**, and a failed one leaves the next click free to retry.

Every fix above except the first came out of a real review round through the gate itself, on one of
this extension's own commits. Two of them were findings both vendors raised independently.

## 0.14.3 — 2026-09-01

**The spending section is legible.** It looked switched off, and nothing was broken: `.usage` was
defined twice in the panel's stylesheet — once for the per-round line in Recent rounds, once for
the spending card. CSS does not care which was meant, so every card rendered at 70% opacity and the
lines inside it at 45%.

The card is `.spend` now, with the vendor and its cost at opposite ends of the row instead of
reading as `antigravity—`, and with the tokens at full strength: they are what the section exists
to show, so they are no longer styled as a hint. The window tabs got a hover, so a live control
stops looking like a label. A test walks the emitted CSS and fails on any selector defined twice —
dimming has no other symptom, and nobody goes looking for a stylesheet collision.

## 0.14.2 — 2026-09-01

**The Update button works.** It posted `installServer`; the panel's message handler had no case for
that name, so every click fell into `default: return` — no error, no notification, nothing in a
log. A button wired to nothing looks exactly like a button whose work failed silently, which is why
it took somebody reporting it rather than a test.

The panel's command vocabulary is now declared once, beside the markup that emits it, and the
handler switches over it with an exhaustiveness check: a command without a case no longer
compiles. A test covers the other direction — a button posting a name nobody declared.

**An update blocked by the running server says what to close.** Windows refuses to overwrite a
binary that is executing, and the MCP client holding `coai-mcp.exe` open is the normal case at the
moment you press Update, because that client is what started it. The message names the cure
instead of an errno.

## 0.14.1 — 2026-09-01

**The rounds view refreshes while it is open behind another tab.** "Open" was asked of
`workspace.textDocuments`, which is the editor's own cache — VS Code is free to drop an entry for
a file nobody is currently looking at, and it does. So a rounds view left open behind another tab
quietly stopped being rewritten, and the only symptom was a number that would not move. It now
asks about a TAB, which is what the person actually sees. A loaded document with unsaved edits
still wins: an automatic rewrite must never discard something somebody typed.

## 0.14.0 — 2026-09-01

**Antigravity is the reviewer you get, not one you have to know about.** The adapter shipped a day
earlier and nothing used it: no preset offered it, every default still named `gemini`, and a saved
reviewer list therefore went on naming a CLI that Google had closed. Supporting a vendor and
DEFAULTING to it turn out to be different changes, and only the first had been made.

Antigravity is now a preset and a shipped default. A reviewer saved with `runtime: "gemini"` is
migrated to it, keeping its id — the id names the row, its usage history and its vault key, so
renaming it would orphan all three. A vendor with its own base URL is never touched: that is not
Google's CLI at all. Gemini remains in the list, marked retired, for a Workspace account that still
has Code Assist.

**A `call_human` verdict now reaches you** — see the server's 0.7.0 notes; the panel showed *No
ConnectOtherAIs review is waiting on an answer* while a gate sat blocked, twice in one day.

**The spending window buttons work.** Today, Month and Year recorded the choice and repainted
nothing, so the section sat on Week for good. The panel repaints on a key over its state, and
anything missing from that key is a control that can never change; `usageWindow` is in it now. The
spending rows became a live region instead, so they advance mid-round without closing a dropdown —
and the tabs deliberately stay outside that region, because a button inside a patched one loses its
click listener on the next tick.

**`rounds.md` refreshes for a restored tab too.** "Is it open" compared two Windows paths exactly,
and VS Code answers a lower-case drive letter for a tab it restored and an upper-case one for a tab
the extension opened — so a restored tab silently stopped being refreshed and the file went stale
while rounds kept running.

**The help speaks five languages.** English, Русский, Українська, Deutsch and Español, one module
per language, with a visible English fallback for anything not yet translated. Two new tests keep
it that way: one fails when an article exists in no translation, one fails when a "translation" is
the English text pasted across.

## 0.13.1 — 2026-09-01

**macOS is a supported platform.** The release now builds `osx-arm64` and `osx-x64` beside the
Windows and Linux ones, and the extension maps node's `darwin` onto .NET's `osx` — the missing
line that told a Mac there was no build while the runtime had supported one all along.

The "no build for your platform" message is now built from the RID list instead of being typed,
so it cannot name a matrix that has moved on, and a test holds the extension's list against the
workflow's own matrix.

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
