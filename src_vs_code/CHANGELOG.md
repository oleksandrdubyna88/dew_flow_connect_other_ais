# Changelog

## Unreleased

**Shared instructions work with Claude Code and Codex.** The panel recognizes the neutral
rules layout and still warns about an older local snippet. Distributed gate text is built
from the pinned canonical source; an interrupted generation recovers on the next build.

**A conversation says what it has cost.** Beside the model picker, updated as answers arrive — which
is where you decide whether to ask again or start fresh, and that decision is the one the number is
for: each question carries the whole conversation with it, so it is billed for the ones before it. A
figure worked out from tokens wears a tilde, because only one of the three vendors reports what it
actually charged.

**Paste a picture into a question.** A screenshot from the clipboard goes into the composer, shows
itself there, and travels with the next question you send. It works with the models that were
measured to read one — if the model you have chosen cannot, the tab says so by name instead of
quietly sending the words alone.

**Ask the other model the same thing.** Switch the model after an answer you did not like, and the
Send button becomes *Re-ask · <that model>* — press it, or press Enter on an empty box, and the
question goes to the new model with the conversation behind it and without the answer you rejected.

**Your prompts and your models, saved by name.** The single prompt in the sidebar has become a list
you build: give each one a name, tick the one to use when a capture sends by itself, and they appear
as buttons above the composer — models in their vendor's colour, prompts in another. **ConnectOtherAIs:
Edit chat presets** opens a tab with both lists and a prompt box big enough to read a prompt in;
everything there saves as you type. Whatever you had written in the old single prompt became your
first named preset the first time this version read it.

**`Ctrl+Alt+A` works in a WSL window.** It used to answer *copying the selection needs Windows* —
which was not true of the machine it said it on. The editor window is a Windows one; only the
extension lives inside the distro, and the helper that presses `Ctrl+C` for you is one hop away. It
is asked for now, and the round trip takes about a second. On macOS and on a Linux box that is not
WSL the keybinding still says so and points you at the right-click menu, which works everywhere.

**And when a copy does fail, the message says what actually happened.** Every failure used to read
*nothing was copied — select the text first*, including the ones where the helper never started or
never finished. A helper that could not be reached, one that was still running when we gave up, and
one that ended badly each say so in their own words now, with what the system itself said. The old
sentence is kept for the one case where it is true: the helper ran, and the selection was empty.

**With *Separate settings for each side* on, a chat uses the CLI that side has.** It was reading the
shared list, so a WSL window could launch a Windows shim — and on a machine where `codex` in WSL
resolves into the Windows npm folder, that is not a hypothetical. The same was true of the settings
file the review server reads, which decides what your *reviewers* are launched from.

**A vendor CLI left behind by a force-kill is cleaned up under WSL and on Linux.** The sweep that
runs at start-up only knew how to ask Windows; anywhere else it gave up and kept the record, so a
signed-in CLI could go on running with nobody to stop it and the same row was retried every time.
It asks this side now, re-checks that the process really is the one it wrote down, and leaves
anything it cannot prove alone.

**Choose the provider, then the model.** The list under a chat used to be your configured reviewers,
each showing the one model it happened to be set to — so picking a different model meant leaving the
conversation and editing a reviewer. There are two lists now: who answers, and which of their models.
Claude offers its three; a row whose model list has to be fetched still shows the one it is set to,
which is what the old list showed anyway.

**Every answer says which model gave it**, in that model's own colour — the same colour it has in the
rounds list and on its reviewer card. Switching models mid-conversation has always carried the
thread across, so a tab could hold answers from two of them looking identical; now the one you
switched away from and the one you switched to are told apart at a glance.

**A turn can be stopped.** While an answer is coming there is a *Stop* beside *Thinking…* — for the
question you saw was wrong the moment you sent it, and for the one that is taking far longer than it
should. It stops the turn it is showing and no other, so a press that lands late cannot end the
question you asked afterwards.

**What a conversation cost you is written down now.** Asking another AI has always spent real
money — a chat turn carries the whole conversation with it, so question five is billed for one
through four — and until now nothing recorded it anywhere. Every turn is written to a ledger of its
own: which model answered, its tokens, what it was billed, how long it took.

**And the review-rounds log shows them.** The table has a new **Kind** column and a matching filter:
*review* for a round, *conversation* for a chat turn, both priced through the same table, so the
question “what did today cost me” has one place to be answered. A conversation leaves the
repository, branch and stage columns empty, because a chat is not held against a branch. As
everywhere else here, `~$0.42` is what the tokens work out to and `$0.42` is what a vendor actually
charged.

**A turn you STOPPED is in the ledger too**, and so is one that failed. Those cost money as surely
as an answer does, and they are the ones worth finding. Where nobody told us what a turn cost, the
row says so with a dash rather than a zero — unreported and free are different things.

**Answers read like answers.** Headings were hashes, lists were dashes, code was backticks and a
link was its own address in brackets — everything a model wrote as Markdown arrived as Markdown. It
is rendered now: headings, numbered and bulleted lists that nest, code in a code face and in its own
scrolling box, tables, quotes.

**Links are blue and they work.** A web address opens in your browser; a file a model names opens in
the editor at the line it named, if that file is really in this workspace — and if it is not, the tab
says so instead of doing nothing. Anything else keeps the words and loses the link. An address a
model merely mentioned in a sentence is not turned into a link at all.

**You are on the right, the answer is on the left**, each behind an edge in its own colour, and a
line closes every answer — so finding where one ends no longer means reading to the end of it.

**Every answer can be copied as the Markdown it arrived as**, which is what you want when it goes
into a plan or an issue. Selecting the page still gives you what the page shows.

**The text is brighter and has room to breathe.** It was the colour of a button label, packed at the
editor's interface size.
**The box you type in stays where you put it.** It used to scroll away with the conversation, so
asking a second question meant scrolling back down to find it. The composer, the model picker and
the hint are pinned to the bottom of the tab now, the conversation scrolls above them, and there is
a **Send** button beside the box for anyone who would rather click than press Enter — locked at the
same moments the box is, because a second turn down the same pipe would interleave with the first.

**A new answer no longer drags you away from what you were reading.** The tab opens looking at the
last thing said rather than at the top, and an answer that arrives scrolls itself into view only if
you were already at the bottom. Scroll up to re-read something and the conversation stays where you
put it — including if the answer lands in the moment between your scroll and the screen redrawing.

**And when it stays put, it tells you something arrived.** A *Jump to newest* button appears over
the composer when an answer lands while you are reading further up, takes you there in one press,
and disappears again — either when you press it or when you scroll back down yourself.

**The box grows with your question.** It starts at three lines and stretches as you type, up to
about a third of the tab's height, and only then scrolls inside itself; it shrinks back when you send
or delete. If it grows while you are reading the last answer, the answer stays in view rather than
sliding behind the box.

**The captured text no longer has a scrollbar of its own.** There were two on the page. The passage
was boxed in because a long selection would otherwise push the composer off the screen when the tab
opened; with the composer pinned, nothing can push it anywhere.

**The chat tab had never used its own styles.** Its stylesheet began with a line that CSS does not
allow where it stood, and a browser reading it threw away the rule that followed — the tab's
margins, its font, its background and the text size you had chosen, all of it, on every open. The
size only appeared when something unrelated happened to push it. Fixed, with a test that fails the
build if any rule on that page is ever swallowed again.

**Reloading the window no longer throws away your conversations.** Every open chat tab comes back
with its questions and its answers, and a line saying it was closed by the reload. The model behind it
is genuinely gone, so nothing is running until you ask again — and that first question carries the
whole conversation across to a new session, which is said out loud because it is what it costs.

**The help describes the tab that shipped, in all five languages.** Everything above arrived over two
days, and *Chat with other AI* in the help still described the tab as it was before any of it. The
article now covers the picker, the presets, the re-ask, the picture, the running total, the rendered
answers and the Stop — in English, Русский, Українська, Deutsch and Español, rather than in English
with four translations quietly a version behind. The extension's README gained the section it never
had: the chat was the one thing this extension does that the README did not mention.

## Extension 0.31.21 — 2026-09-09

**A chat tab looks like a chat tab.** It wore the same generic icon as everything else in the editor,
which is no help at all when three conversations are open side by side. It carries this product's own
green glyph now, in a light and a dark version, because a tab icon cannot read your theme.


## Extension 0.31.20 — 2026-09-09

**The prompt you type into the sidebar now stays typed.** *What to ask about the selection* was
saving only when you clicked away, and the panel rebuilds itself whenever a probe, a version check or
any other setting moves — several times a minute, none of it your doing. Whatever you had typed died
with the page. It is written as you type now, and the panel will not rebuild itself underneath a box
you are working in.

## Extension 0.31.19 — 2026-09-09

**Ctrl+F works.** The chat tab, the rounds log and the help page each open with the editor's own find
bar now — a transcript of answers and a table built to be searched had both been pages nobody could
search, because a webview only gets that bar when it is asked for one.

## Extension 0.31.18 · Server 0.18.15 — 2026-09-09

**The log stopped sending every finding it has ever recorded, and started counting in SQL.** Reading
the rounds database answered **3.83 MB**, of which the rows themselves were 0.05 MB — the rest was
3 484 findings, shipped for every round although the page opens them one at a time. It now sends the
rounds and their counts, and fetches a round's findings when somebody opens its row.

**The table pages, two hundred rows at a time**, with Newer / Older under it and a line saying which
rows those are. Under that, what the database counted over the whole table: how many rounds, how many
findings, how many accepted, rejected and gating. Counted by SQL, not by the length of what was sent.

**An opened row is honest about all five things it can be.** It used to draw a blank for four of them,
and a blank reads as "this round was clean". Now it says whether it is reading, whether the read
failed and offers to try again, whether the round genuinely found nothing, and whether the database
has no record of it at all.

**Paging is keyed, not counted**, so a round finishing while you are on page two cannot make a row
appear twice or vanish — and the key is a pair, because two rounds can start in the same second.

**Either half can be older than the other.** A new extension asks a server that cannot page and gets
yesterday's answer — with the same three hundred rows it always had; a new server asked without the
flag answers yesterday's shape. Nothing goes silently empty in the field.

## Team server 0.5.6 — 2026-09-09

**Your spending page can now tell the gate apart from asking.** Reviews and conversations go to the
same vendors and cost the same money, so one total answered neither question. The *Team servers*
block shows both under its vendor rows — *reviews: 12 · 41k tokens · conversations: 3 · 2k tokens* —
and says nothing at all in a window that has only one of them, because "no conversations" is an
absence and a zero is a measurement.

**A conversation now says what it is, instead of being recognised by what it lacks.** Chat turns
have been telling this server apart from reviews by carrying no reviewer role, which worked and was
an accident: the day anybody tightened that check, every conversation on every machine would have
stopped with a message about roles. A turn carries `kind` now, the server reads it, and a client
too old to send one is still read as a review — which every one of them is.

**A review nobody is waiting for stops holding an account.** If the window that asked is killed —
the editor quits, the laptop closes — nothing can tell this server so, and the job went on sitting
in the queue, or ran to the end of its budget and answered into nothing. It is dropped now when
nobody has asked about it: three minutes for one still queued, which has cost nothing, and ten for
one already running, which has already been paid for and is not thrown away over a network blip. The
message says which, because "nobody was listening" and "the vendor was too slow" send you looking in
opposite directions.

**Pressing send twice after a lost connection no longer costs two slots.** When a request arrives and
its answer does not come back, the extension repeats it with the same name for that turn, and the
server hands back the review it already made rather than starting a second one on a shared account.

## Extension 0.31.17 — 2026-09-09

The client half of the above: every chat turn carries its own name so a retry is a retry, and the
*Team servers* spending block renders the conversations-against-reviews line when a server is new
enough to send it. A server that is not sends nothing, and the block looks exactly as it did.

## Extension 0.31.16 — 2026-09-09

**The Review rounds page has its findings back.** It had none — no list of what was found, no
accepted/rejected counts, and the Today/Week/Month buttons took eight seconds to do nothing. One
cause: the process fan-out rebuilt its whole buffer on every line nobody had subscribed to yet, so
reading the rounds database took 19.7 seconds for output that had arrived in 0.4, and the reader
gave up and threw the answer away. The buffer is appended to now. The read is 0.2 seconds, and the
page draws what every round found and what was decided about it.

## Extension 0.31.15 — 2026-09-09

**A Team server can answer a chat now.** Pick one of your Team server's models in *Chat other AIs*
and the passage goes to the company subscription instead of a CLI on this machine — the same queue
your reviews go through, the same accounting, and no vendor CLI to install.

It works differently from a local model and the page says so before you choose it. A server holds no
conversation: it answers one question and forgets it, so every follow-up carries the whole thread
across again and the bill for the third question is the bill for all three. **Three turns is the
limit**, and when you reach it the page offers the local model that does remember. While you wait,
it now tells you where you are — *waiting in the queue, 4 ahead* — because on a shared server most
of the wait is somebody else's round holding the vendor, and an unchanging spinner is the one thing
a busy server and a broken tab look identical as.

**Switching the model mid-conversation was carrying the wrong rules across.** Switching from a local
CLI to a Team server left the conversation thinking its model had a memory: the server was asked the
second question with none of the first behind it and answered as though the thread had restarted,
and the three-turn limit never applied at all. Switching back did the opposite — a local model that
remembers everything was refused a fourth question. Both are fixed, and the rules now travel with
the model rather than with the tab.

**A reviewer row saved before this spring could not chat at all.** Rows are named `<server>-<vendor>`
and the older ones do not record which half the server knows, so the whole name was being sent — and
the server answers *"'remsoftdev-codex' is not a vendor here"*, which reads exactly like a typo in a
setting you never typed. The name is now derived from the row, and older rows work.

**Closing a tab while your question was still being sent left the job running on the server**, using
a slot on the shared account to answer into a window that had gone. It is cancelled now, whichever
moment you close it in.


## Extension 0.31.14 — 2026-09-09

**The panel can tell when a Team server is older than it is.** The server has been putting its API
version on every response since the mechanism shipped — its own design note says the client's check
against that header is the right place to decide what to do about it — and the panel sent its own
number and threw the answer away. It reads it now, and says so in the *Team servers* row when a
server is behind what this extension needs.

It reports rather than refuses, and the asymmetry is deliberate: a server that keeps serving a client
it cannot satisfy corrupts state, so it refuses; a panel that stopped talking to a server it merely
suspects would turn a warning into an outage.

Three states, and the middle one is the reason this took care to get right. A number is what the
server said. Nothing said, on an answer that arrived, is a genuinely old server. Nothing known — no
answer at all, or an answer that is not a number — records nothing and stays quiet, so a dropped
connection cannot make a healthy server look ancient.

Nothing about this is visible while every server is current, which is the point: it exists so that
the day a response shape moves, the panels already installed can say what they are looking at.

## Extension 0.31.13 — 2026-09-09

**All three vendor CLIs can answer a chat now, not just one.** Set `coai.chatModel` — or pick in the
new *Chat other AIs* section — and the conversation goes to `claude`, `codex` or `antigravity`.

The one that could answer was the one whose protocol had been measured. The other two were written
off in a planning note as unable: *claude’s schema differs, codex has no multi-turn stdin*. Measured,
half of that was wrong. `codex` does hold a conversation — through a session it stores and resumes,
rather than a pipe it keeps open — and `claude` holds one exactly as `antigravity` does, and answers
faster than either: 4.5 seconds for the first turn and 1.6 for the next, against 8.0 and 1.4.

**And the panel has a place to set all this.** *Chat other AIs*, beside *Reviewers*: the prompt the
selection travels with as a box you can write a paragraph in (one word, `Explain`, until you change
it), the language the other AI answers in, who presses send, and which model. A reviewer on a runtime
the chat cannot speak to is listed underneath with the reason rather than quietly missing — and a
model you NAMED that cannot answer is shown as chosen and unable, instead of the panel quietly
reading back somebody else.

**Three things about other people’s programs, found by running them.** `claude` says nothing at all
until it is asked — given an empty input it exits without a word — so waiting for it to announce
itself reported a CLI that never started, for one that was working perfectly. A bare `codex` on
Windows means `codex.cmd`, which is not something a program can simply start: it now says *codex
could not be found* before a tab opens, instead of failing at the first question where it reads as
the model refusing. And `codex` conversations are resumed by their own id rather than by "the last
one" — which is the last one on the whole MACHINE, so two chat tabs would have answered each other.


## Server 0.18.14 — 2026-09-09

**A reviewer that waited out its deadline now tells you what to do about it.** A local reviewer that
never got the graphics card reported this:

```
local/Conventions FAILED after 290.0s: exit 69: l Qwen3.5-35B-A3B-Q5_vk128:latest, pid 52068)
```

A sentence beginning mid-word. Nothing had crashed — the reviewer waited its whole five-minute
deadline for an engine that was busy with somebody else's round, which is a normal thing to happen
and has three cures: give the reviewers more time, run fewer local roles per round, or point that
vendor at a second engine. All three were written, printed, and thrown away.

Three things were losing them. The shim writes its progress notes to the same place as its verdict,
and the note came first; the kept portion of that output was cut by character count, so it began in
the middle of a line; and the sentence a person reads was capped at 160 characters, which is shorter
than the verdict — so even when the right line won, everything you could act on was past the cut.
Our own sentences are no longer cut, the kept portion is whole lines, and a progress note is never
mistaken for a reason. A reviewer killed while it was still queuing now says it was still waiting,
instead of quoting a stack frame.

**A review that found nothing keeps its evidence.** An `ok` outcome with zero findings used to drop
the vendor's raw answer, so a round where eight reviewers all answered `{"findings": []}` on a diff
another reviewer found eleven things in could not be asked about afterwards. That answer is kept
now, the reviewer's audit line names the file, and both stages log what they assembled AND what each
reviewer was handed — two numbers that are the same only while nothing between them is broken.

## Extension 0.31.12 — 2026-09-08

**Switching the model in a chat tab now takes the conversation with it.** Both the questions and
the answers: the model you switch to carries on from where the last one stopped, instead of
starting again with nothing.

A vendor CLI keeps its context inside its own process — there is no transcript to hand over and no
session to resume — so the only way across is to say it all again. That happens once, inside your
next question, and never again: from there the new process remembers exactly as the old one did.
Nothing is sent at the moment you switch, so moving a conversation and then not continuing it costs
nothing at all.

What travels is bounded at 60 000 characters, newest first, and if a conversation is longer than
that the turn says so rather than being silently cut by the vendor. A single answer bigger than the
whole budget is cut with a marker instead of dropped. And because the conversation being carried is
another AI's text, it is fenced with a one-time delimiter and every line is indented under whoever
said it: a line inside an answer that reads like a new question cannot become one.

A switch that cannot be made says so instead of leaving you on the old model quietly, and a switch
asked for while an answer is still arriving tells you it is waiting for that answer first.

## Extension 0.31.11 — 2026-09-08

**The rounds log gets its findings back.** The *What it keeps missing* tab was empty and the table
had stopped showing how many findings were accepted and rejected. Nothing was wrong with the data,
and that was checked at every step: the server emitted 207 rounds, 24 blind spots and 205 decisions
in 143 ms, the parser read all of it, and the tab's HTML was built correctly from it — 2968
characters of it, going nowhere.

**One lost message did it, and it stayed lost.** The page is painted without reading the database on
purpose: reading it starts a process, and nobody should wait on one to see their log. The findings
arrive a moment later in a push — and that push was sent blind. VS Code answers *delivered* for a
page that merely EXISTS, which one does from the moment its HTML is set and before its script is
running, and the extension recorded the push as delivered before it had even made it. Every later
tick compared against that record, found nothing changed, and sent nothing. Closing and reopening the
tab was the only cure, and the same race could take that one too.

Now the page says when it is listening and is answered with everything; a push is recorded only when
it actually arrived, so a lost one is simply sent again on the next tick. If a page never speaks up,
the extension keeps pushing at it anyway rather than leaving it blank — and both tabs open on
*Reading the log…* rather than on nothing, so a section that never receives its data says so, on the
page, instead of looking like a section with nothing to show.

This one went through the product's own gate twice, and the code round is worth naming: fifteen
reviewers found that the fix had reintroduced its own defect in its fallback path — pushes at a page
that could not receive them were still being recorded as delivered. Four of them said so
independently, from three different roles.

**Ask another vendor’s model about a passage, without leaving the editor.** Select a paragraph in
your assistant’s answer, press `Ctrl+Alt+A`, and a tab opens — named after that assistant session —
where a different vendor’s model explains it in your language, in a conversation you can carry on.
The same thing sits in the panel’s right-click menu as **Chat with other AI**.

A dense English answer is not always a clear one, and asking the model that wrote it to explain
itself gets you the same words again. Doing this by hand is five steps — select, copy, switch to a
browser, new chat, paste — several times an hour.

**The two doors behave differently, and they have to.** From the keybinding the panel still holds
the keyboard, so the selection is copied for you and the question is asked at once. The menu cannot
do that — closing it takes the selection out of the panel — so it takes what you last copied and
puts it in the composer for you to send. `coai.chatAutoSend` overrules that in either direction, and
the passage is shown at the top of the tab so you can see what is about to be asked.

Four settings are yours: `coai.chatPrompt` (the prompt the passage travels with, one word `Explain`
by default), `coai.chatLanguage` (the language the other AI answers in — English by default, and
deliberately not the language of the help pages), `coai.chatAutoSend`, and `coai.chatModel`. They
are edited in `settings.json` for now; a section beside *Reviewers* comes next.

**What it will not do yet.** Only a reviewer on the `antigravity` runtime can answer a chat, and a
row on any other runtime is refused BY NAME rather than quietly missing from the picker — a model
you NAMED and which cannot answer is never silently replaced by another vendor’s. Copying the
selection needs Windows; elsewhere the keybinding says so and points at the menu, which works
everywhere. Picking a different model in an open tab takes the whole conversation with it — the
questions and the answers both — so the new model carries on rather than starting again; a vendor CLI
keeps its context inside its own process, so the only way across is to say it all again, which the
next question does once and then never again.

The whole thing went through this product’s own gate three times — a plan round and two code rounds,
eighteen reviewers between them — and what those rounds changed is worth naming: the copy helper
now WAITS for your own modifiers to come up instead of forcing them up under your fingers; a
clipboard it could not read (an image, a file) is never written to, because it could never be given
back; and the clipboard it borrows is put back only if nothing else wrote to it while you waited.

## Extension 0.31.9 — 2026-09-08

**A code-review role can be switched off.** Each of the four boxes — Conventions, Architecture,
Security & reliability, Performance & UX-DX — now carries a tick box on its own heading. Unticked,
that role takes no part in the round at all: no reviewer is launched for it, nothing it would have
found is counted, and it lends the stage neither its rounds nor its threshold.

Until now the only way to keep a role out was to lie to a control that means something else — set
its rounds low enough that it never gets a turn. That is not the same statement, it is not readable
as one, and it threw away the number you would want back. The switch keeps the role's rounds, its
threshold and its prompt picks, and returns them unchanged when you tick it again.

The last ticked role cannot be unticked. A code round with no reviewer in it is not an empty round —
a round nobody answered is counted as unresolved, so it would sit open and the next review would be
refused for the wrong reason. The server refuses that round too, because a Team server and a
hand-written config block have no checkbox to look at.

Plan review is not affected: it has one role and no tick box.

**Wants `coai-mcp` 0.18.13.** An older server does not look for the new setting and runs the role
anyway — a failure that reads backwards, because the box says off while the reviewer is the one thing
still working. The Code stage section says so out loud, naming the roles it would run, while that is
true.

## Server 0.18.13 — 2026-09-08

**A role switched off in the panel is a role the round does not launch.** `COAI_ENABLED_<ROLE>`
disables one code role; absent means on, and only the four spellings of false switch anything off —
absent, empty, `no`, a typo and a shell-mangled value all leave the reviewer working. A role wrongly
on costs one extra pass; a role wrongly off is a review nobody performed with nothing saying so.

A disabled role lends the stage neither its rounds nor its threshold, so it cannot keep a stage
running rounds nobody reviews or hold the gate open against a number no reviewer can bring down.
With every code role off, `review_code` is refused before the scope check and before any worktree,
naming the four boxes and the variable. `COAI_ENABLED_PLANCRITIQUE` is refused: this is code review
only.

## Extension 0.31.8 — 2026-09-08

**No two reviewers wear the same colour any more.** With six reviewers configured — three here and
one Team server's three — `local` and `remsoftdev-codex` had the same orange edge, and `claude` and
`antigravity` would have shared a purple the moment both were added.

That was not an unlucky hash, it was the shape of the answer. A name was hashed into six chart
colours; six names into six buckets collide more often than they do not, and a colour worked out
from ONE name cannot know what the other five took. So the promise moved to the list: the palette is
now decided for every configured reviewer at once.

There are twelve colours instead of six, contributed by the extension itself, so each carries a
dark, a light and two high-contrast variant rather than being whatever the theme happens to mean by
"orange". `codex`, `gemini` and `local` keep the blue, green and orange they already had — a mapping
you have learned is not worth invalidating — and `claude` and `antigravity` are pinned before anybody
learns something else. Those five hold their slots whether or not they are configured, so a stranger
never takes blue while `codex` is away.

Every view colours from the same list, the reviewers in your settings, rather than from whoever
happens to appear in the rounds it is drawing. That is what keeps a card, its running round, its
spending card and the log agreeing. A reviewer you have since removed still gets a colour, from its
own name, and it is the only one allowed to look like somebody else's.

Past twelve reviewers the thirteenth repeats. Twelve colours cannot dress thirteen people, and a
grey nobody can tell from an ordinary row would cost more than the repeat does.

**The rounds log names the model.** A reviewer row that has one now reads
`remsoftdev-claude/Architecture · claude-haiku-4-5 — done (3 findings)` instead of stopping at the
role. A reviewer launched without a model — a local engine with none configured — reads exactly as
it did. It is also searchable: the log page's filter reads these rows, so typing a model name finds
the rounds that used it.

**Read it as the model that was ASKED for.** For a local CLI that is the model that ran. For a Team
server it is what your panel requested — the server picks the account, and it does not yet report
back which model answered, so an escalation or a server-side substitution is not visible here. That
gap is the next piece of work, and this entry exists so the number is not read as more than it is.

Rounds recorded before this release name no model, which is the truth about them.


## Extension 0.31.7 — 2026-09-08

**Conventions is its own reviewer now.** There is a fourth box in the code stage, above
Architecture, with its own rounds and its own "passes at or under" — and `Conventions` is gone from
the other roles' dropdowns.

It used to be a prompt that borrowed somebody else's round: round 1 of every code role, and since
yesterday round 1 of Architecture alone. Borrowing was always the complaint — a role with one round
spent it on the written rules and never asked its own question, and whatever the conventions pass
found counted against that role's threshold. Now it has a budget of its own.

**Architecture drops to one round.** It had two because the first was the conventions pass and the
second its own question; take the pass away and the second is the only real round it had.

A repository that wrote no rules down — nothing in `CLAUDE.md`, `AGENTS.md`, `GEMINI.md`,
`.github/copilot-instructions.md`, `.claude/rules` or `.cursor/rules` — has these reviewers skipped,
and the server says so, naming those six so you know where to put some. A pass with nothing to judge against would
invent a standard, which is worse than the review it displaced.

**Wants `coai-mcp` 0.18.10.** An older server has four roles and does not know the fifth, so it
simply will not run it: your code rounds are three code roles instead of four, with the box still
drawn in the panel. Nothing fails — the server reads its gate from its own list of roles and ignores
a key naming one it has never heard of — but a conventions check you can see and cannot get is worth
a sentence, and the Prompts section carries it while it is true.

**Each reviewer now wears its own colour.** The card in *Reviewers* has a coloured left edge, and it
is the same colour that reviewer’s name has in *Active rounds* and in the rounds log — so you can
follow one vendor from where you set it up to where it is running without reading either.

**The gate you see is the gate that runs.** The panel writes a setting into the server's
configuration only where it differs from the default, so that putting a control back removes the
line instead of pinning a stale value. That quietly requires the panel's default and the server's
fallback to be the same number, and for one day they were not: yesterday's release moved every round
and threshold in the panel and left the server's own where they were. A fresh install read *1 round,
threshold 6* off the screen and ran three rounds at threshold 2.

They are the same number again — one round everywhere, six open findings allowed on a plan and five
on a diff. **If you never touched these sliders, your rounds change with this release**: they become
the ones the panel has been showing you. A test now reads the server's two constants out of its
source rather than copying them, so this cannot drift again without going red.

**If you are an admin on a Team server, its row now tells you what is published.** One line under
the status: the newest released `coai-server` version, and a `⬆` when it is newer than the one your
server is running.

Nothing to press. A Team server is deployed rather than downloaded, so updating it is somebody
going to that machine — this only tells you there is a reason to. Everybody who is not an admin
sees nothing new, and so does an admin whose server has no published release to compare against.

## Extension 0.31.6 — 2026-09-07

**New installs start on a budget somebody actually ran.** The shipped defaults were set before this
gate had reviewed much; they are now the ones this project runs on its own work.

| | was | now |
|---|---|---|
| Plan review — rounds | 3 | **1** |
| Plan review — passes at or under | 2 | **6** |
| Architecture — rounds · threshold | 2 · 3 | 2 · **5** |
| Security & reliability — rounds · threshold | 2 · 3 | **1** · **5** |
| Performance & UX-DX — rounds · threshold | 2 · 3 | **1** · **5** |

One plan round because the second and third mostly re-raise what the first found. Higher thresholds
because the old ones sat where a real change could not pass, and a gate that blocks everything is a
gate people learn to ignore.

**Round 1 is the Conventions pass for Architecture only.** It used to be round 1 of all three code
roles, which meant the same written rules read three times in one round — and now that two of those
roles get a single round, that round would have been spent on conventions instead of on security or
performance. Architecture keeps it because it has two rounds: the rules, then the broad question.
Every role can still be *given* the Conventions prompt; only the default changed. **This half needs
`coai-mcp` 0.18.9** — the panel and the server decide it together, and a mismatched pair would show
one thing and run another.

Nothing you had set changes: these are defaults, and your own choices in the panel win.

## Extension 0.31.5 — 2026-09-07

**A reviewer you added from a Team server now actually reviews.** It did not, for anybody, since
Team servers shipped: the row sat in *Reviewers* with both stage boxes ticked, and every round ran
without it. Nothing said so — the panel called it configured, which was true, and the round reported
"all 3 reviewers answered", which was true about what it asked.

Two things were wrong, and both are fixed.

The extension never sent the one field a Team server needs. A reviewer row is named
`<server>-<vendor>` so two servers offering `codex` cannot collide, and the name the SERVER knows it
by travels separately — that second name was missing from everything the extension wrote, so your
server was asked for a vendor called `remsoftdev-claude` and answered, truthfully, that it offers no
such thing.

And a second VS Code window could undo your settings. Every window writes the file the server reads,
including a window still running the build it was opened with — and an older build rewrites a
reviewer type it does not recognise. Two windows, 0.4 seconds apart, and the newer one lost. The
file now records which build wrote it, an older one stands down and tells you to reload that window,
and the write happens under a lock and by rename, so a crash cannot leave half a file behind.

**And when a reviewer cannot run, you are told.** Its card says so, with the reason — not signed in,
no key, no such vendor — and a round that leaves one out names it in the log and in what it hands
your AI. The reason comes from the server, so the panel never has a second opinion about it.

Smaller: a Team-server card no longer claims its models come from the Codex CLI, and a refusal says
which of the row's two names was tried instead of reading like a typo you never made.

## Extension 0.31.4 — 2026-09-07

**The *Server* section is now *MCP server*, and it is about that.** It carried the Team server's
address and the version answering on it as well, from 0.31.1 — the idea being that *what am I
talking to* is one question with two answers. Seen in the panel it was two subjects sharing a box.
The section is named for one thing and describes that thing; your Team server's address, its
version and the account you are signed in as are in **Team servers**, where you manage it, and
always were.

Nothing about signing in changed. A sign-in still belongs to one side of your machine and behaves
the way the side switch says.

## Extension 0.31.3 — 2026-09-07

**Nothing you can see changed.** Install it or don't; the panel behaves exactly as 0.31.2 did.
This release exists because the version string had to move: 0.31.2 is published, and the code
behind it has since gained one field — a successful server result now carries the HTTP status it
came back with, so a caller can tell `201 Created` from any other success. Two different builds
answering to one version string is how "fixed in 0.31.2" and "still broken in 0.31.2" become both
true.

Behind it: one test suite that drives this extension's real client against a real `coai-server` and
fails when the two disagree about a wire format — which is what shipped a sign-in nobody could use
in 0.31.1.

## Extension 0.31.2 — 2026-09-07

**Signing in to a Team server works.** It could not in 0.31.1, for anybody: the extension sent your
Microsoft token in the request *body*, and the server reads it from the `Authorization` header and
nowhere else. So the server answered `401` before it had looked at the token at all — which is why
the panel could only say *the server answered 401*, with no reason attached. The token now travels
in the header, which is where every other call already put it.

Nothing else changed, and no configuration needs changing: this was one line on the client side of
a contract whose two halves were each tested on their own.

## Extension 0.31.1 — 2026-09-06

**Team servers.** A company buys ONE subscription per vendor, installs the CLIs on ONE machine, and
everybody reviews through it with their work account. Nobody needs codex, claude or agy installed,
and nobody needs their own subscription.

The panel gains a **Team servers** section: add a server by name and address, sign in with Microsoft,
and it shows the account you signed in as, the server's version, and — per vendor — how many accounts
are free. That last line distinguishes the two states that need different people: *all signed out*
is the operator's job, *all rate-limited* comes back by itself.

**＋ Add a reviewer** then offers each signed-in server, and picking one lists the vendors that server
actually has, with their free accounts beside them. The models offered are the ones that server
allows — discovered from it, never a list shipped in this extension — and a model it stops allowing
is kept and marked rather than silently swapped. A Team-server row asks for no endpoint, no CLI path
and no price: all three are decided on the server.

Your spending page gains a block per server, beside this machine's own totals rather than added to
them — the two ledgers are separate and a combined figure would be a number neither of them holds.
An admin additionally sees the whole company's.

**Where your code goes is said on every row.** Being the company's own server makes it no less true
that the plan, the diffs and the file contents around them leave this machine, so the same disclosure
a remote local engine gets is shown here too.

The sign-in is your work Microsoft account, restricted to the domain the server allows; an account
outside it is told that, rather than told to try again. The session renews itself before it expires,
without asking. The token is kept in a file only you can read — never in `settings.json`, never in a
command line, never in a log.

**The Server section now says what this side is talking to — both halves of it.** Under the
`coai-mcp` lines it shows your Team server's address, read-only, and `coai-server <version> — signed
in as you@company` once it answers. Read-only because pointing a live session somewhere else is a
sign-out rather than a text edit; the section says where to do that instead.

**A sign-in belongs to one SIDE of your machine, and behaves the way the side switch says.** A
Windows window and each WSL distro keep their own token file — they always did — so the panel now
reports what THIS side actually holds rather than a record shared between them. With
*Separate settings for each side* off, a WSL window opened after a Windows sign-in signs itself in
with the same account, without a prompt and without the token ever leaving the side that minted it.
With it on, each side starts signed out and can hold a different account. Either way a sign-out
reaches the other sides: until now, signing out in Windows left a working token inside the distro.

If a side cannot sign itself in — no Microsoft session there, or the account active on it is a
different one — it says so and offers the button, instead of showing an account no review can use.
Signing yourself in there once is all it takes; nothing is minted behind your back for an account you
did not approve, and a session that comes back as somebody else is ended again rather than used.

*Requires a Team server: `coai-server`, deployed by whoever runs your company's subscriptions.*

## Server 0.18.4 — 2026-09-06

**Every `review_code` in every repository was failing.** It threw
`Value cannot be null. (Parameter 'first')` while `review_plan`, `status`, `resolve`, `open` and
`providers` all answered normally — which made it look like a regression in the code path. It was
data: the `usedPrompts` field arrived on 2026-09-01, a session file written before that has no such
member, and the deserializer does not run a property initializer for an absent one. So an old
session loaded with a null list and the next round over it died. New sessions were fine, which is
why the plan stage looked healthy — it had just created one.

Every collection that comes off disk is normalised where it is declared now, an absent member and an
explicit null alike, and the startup sweep — which reads the same files through its own
deserialisation — normalises them through the same method. That one would have taken down the sweep
in every window rather than one round.

If you hit this before updating: deleting the session file for the branch and calling `open` again
was the way through, because a new session is written with the field.

## 0.31.0 — 2026-09-06 (server 0.18.3)

**A reviewer can now be set per STAGE, and a stage nobody serves is refused.** Each vendor row has
two boxes — *reviews plans*, *reviews code* — ticked unless you untick one, with the master switch
above them still turning the vendor off everywhere. Asked for by a measurement rather than by taste:
over fourteen judged runs a local model was **19 % useful on a plan and 3 % on code**, while writing
more findings than codex and gemini together. So "on for the plan, off for the code" is a setting
somebody actually wants, and until now there was nowhere to say it.

An existing configuration keeps exactly the gate it had — absent means both, everywhere — and the
flags ride inside the vendor list, so a pristine configuration still writes no extra configuration
at all.

And the dangerous half: a stage no vendor serves used to build an empty work list, run no reviewer,
merge nothing and **pass the gate** — a round reporting `proceed` having reviewed nothing. It is
refused now, at both stages, with a sentence naming the stage.

**Each side of the machine can keep its own settings.** `coai.perSideSettings` (off by default) gives
a local window, and each WSL distro or remote host, its own vendors, models, proxies, CLI paths and
vault key — seeded from what that side had when you switched it on, so nothing changes until you edit
something. One machine, several companies, is what this is for. VS Code hands the same
`settings.json` to every extension host, which is why it could not be configured before. Your text
size and help language stay shared: they belong to you, not to the work.

**A round says whether anybody has decided about its findings.** The rounds log's Status column had
`done` next to a `good_enough` verdict while thirteen findings underneath were still open — both true,
and together they read as finished. `done` is about the reviewers having answered; whether the gate
was closed is a different fact, and the server already recorded it. The column now shows **awaiting
decisions** until a resolve lands, and `9 ✓ 4 ✗` once it has.

**`--log` no longer dies when the SQLite library is missing.** 0.18.2 claimed this and did not do it:
the real exception chain has a layer the fix did not know about (`TypeInitializationException` →
`TargetInvocationException` → `DllNotFoundException`), and the test had built the chain by hand
without it, so the code agreed with the fixture rather than with reality. Found by deleting the
library from the published archive and running it.

## 0.30.4 — 2026-09-06 (server 0.18.2)

**The server shipped unable to open its own database, on every platform.** 0.18.1 carried the
executable alone. Native AOT compiles managed code; the call into SQLite still resolves at run time
through the OS loader, which searches the directory the executable sits in — so the installed server
threw `DllNotFoundException` the first time anything touched the rounds database. That write is
best-effort by design, so it failed in silence: rounds ran, findings were answered, nothing was
recorded, and the log page had nothing to show.

It answered `--version`, `--help` and a full `tools/list` exchange throughout, which is why every
check passed. Found by running the installed build against a real round rather than by reading it.

What ships in the fix:

- **The release archive carries the native library**, the job fails when the publish output has none,
  and — the part the review gate added — the **archive itself** is listed after it is made and the
  library must be at its top level, beside the binary. A publish directory proves the library was
  built; an include filter sits between that and what ships.
- **The release smoke makes the published binary open a database** for every RID whose binary its
  runner can execute. That is the check that would have caught this.
- **The installer copies everything the archive brought**, rather than the one file it knows by name,
  so the next native dependency needs no change there. The SQLite library itself is **required**: one
  the archive brought and the installer could not place stops the install with a message naming the
  reason, because on an upgrade a running server holds the old file open, the copy fails, and a new
  binary beside an old library is this same incident again. Every copy gets one retry after 400 ms,
  since that failure is transient by nature.
- **`--log` no longer dies when the library is missing.** The fault arrives as a
  `TypeInitializationException` wrapping `DllNotFoundException`, thrown from SQLite's type
  initializer, which the handler's filter never saw — so the one command a person runs to find out
  what happened was the one that crashed. It answers with an empty log and names the missing library.

Also: the rounds log page had a guard against rendering an empty page, and that guard was green only
on the day it was written — the page opens on **today**, the fixture was dated, and the row fell out
of the default filter the next morning. The page's clock is frozen in the test now, so what is
asserted is the page rather than the calendar.

## 0.30.3 — 2026-09-05 (server 0.18.1)

**The antigravity model list is asked for, not remembered.** The dropdown offered Gemini 3.7 Flash
and nothing newer while `agy models` on the same machine listed **3.8 first** — along with 3.6 and a
Pro (Low) the panel had never heard of. The list was a hand-written constant, and the line under it
said *"what `agy models` lists for this subscription"*, which made a snapshot look like an answer.
The test that guarded that line asserted the sentence and carried a comment claiming the provenance
was admitted; the two disagreed, and the constant went a model generation stale behind them.

`agy models` is now read the way the CLI versions are — at most once an hour, only when a vendor is
actually set to that runtime, and never fatally: if it does not answer, the old list is offered and
the line says so instead of claiming the CLI said it.

## 0.30.2 — 2026-09-05 (server 0.18.1)

**A rate you typed in prices the round.** The Cost column works a round out from the usage ledger and
a public price list, and a local engine appears in no public list — so its rounds read as a floor,
whatever it actually costs. The per-vendor rate the panel has always had now reaches that
calculation, through the same rule the spending tab already used: **the specific statement wins over
the general one**, per field, so filling in only the input rate keeps it and lets the output rate
fall back rather than the vendor going dark.

## 0.30.1 — 2026-09-05 (server 0.18.1)

**The log shows the findings themselves, not only how many.** Expand a round and the sentences are
there — severity, file and line, what the reviewer said, what it proposed, and **what was decided
about it, with the reason a rejection carried**. A finding that repeats one already rejected is
marked *raised again*.

**A third tab: what it keeps missing.** The two questions this data exists to answer.
*What the caller ACCEPTS*, by category, by reviewer role and by vendor — an accepted finding is by
definition something the AI had not seen and then agreed was worth having, which is the blind-spot
corpus. Shown as accepted **over total**, because a category that produces eleven findings and gets
none taken says something quite different from one that produces nine and gets seven. And *rejected,
and raised again anyway*: the shorter, sharper list of disagreements the caller is defending.

**No SQLite in the extension.** The alternative was a WebAssembly build in the VSIX or a native
module per platform, to ask questions of a file the server already writes and whose schema it owns.
The server answers `--log` with JSON instead. A server older than the flag answers nothing, which
reads to the page exactly like a machine that has run no rounds — everything built from the session
files carries on as before.

## Server 0.18.3 — 2026-09-06

Per-stage vendor selection (the flags travel inside `COAI_VENDORS`, only when a vendor is narrowed),
a refusal when no reviewer serves the stage being run, and `--log` explaining a missing SQLite
library instead of crashing — which 0.18.2 promised and did not deliver.

## Server 0.18.2 — 2026-09-06

Ships `e_sqlite3` beside the binary, for win-x64/arm64, linux-x64/arm64 and osx-x64/arm64.

## Server 0.18.1 — 2026-09-05

`--log [--limit N]` prints the rounds database as JSON and leaves: rounds with their findings and
resolutions, what the caller accepted and rejected grouped three ways, and the findings raised again
over a standing rejection. Read-only, and an empty answer for a machine with no database rather than
an error.
## 0.30.0 — 2026-09-05 (server 0.18.0)

**The gate's own rule stopped being a paste, and the panel can see where it went.** The block that
teaches a repository's main AI to call this gate used to be copied into each repository's
`CLAUDE.md`, and a copy does not move when its source does: `dew_flow_creds_for_devs` was found
carrying **v2** while this extension handed out **v5** — three revisions behind, missing the whole
COMMANDS block, the "reject in round 1" rule and the enforced stop after `call_human`. It lives once
now, as `common/coai-review-gate.md` in `dew_flow_conventions`, and reaches six repositories through
the submodule they already mount.

So the panel reads two more places after the four instruction files: the mounted rule and its
unmounted twin. A repository whose only copy is the mount now reports **current** instead of the
*absent* it would have reported before. The instruction files stay FIRST on that list deliberately —
a paste in `CLAUDE.md` is what the AI there actually reads, so a stale one has to be the sentence
the panel says; answering with the mounted rule's version instead would be a green light over text
still being obeyed.

**A drift between the two is a red build.** A test asserts the mounted rule is byte-identical to
what the ⋯ menu hands out. It skips on a fresh clone without the submodule and **fails under CI**,
where the workflow now checks that one submodule out — a skip there is exactly how a changed snippet
would merge behind a green tick. `gate-snippet-check.mjs` fails the build of any consumer that keeps
its own copy of the block, which is the half a panel can never do: a panel only sees a repository
somebody has opened.

**Server 0.18.0** carries the other half — a round's worktree now actually contains the shared rules
it is meant to be judged against, instead of the empty directory git leaves in a linked worktree.

## 0.29.13 — 2026-09-05 (server 0.17.5)

**The Review rounds page came up empty again, saying `R is not defined`.** The same class of defect
as 0.29.10's `rowMatches is not defined`, and the guard written for that one could not see it: the
page embeds some functions by their SOURCE TEXT, and `cost3` — added in 0.29.12 — *called* `money`.
Minified, `money` becomes `R` inside the bundle, and the text of `cost3` goes to the page still
calling `R`, which the page has never heard of. The page defined `money` under its own name, so
nothing looked missing until a row asked for its cost.

So an embedded function now carries what it needs. And the check for it is no longer a runtime
error waiting to happen:

- the bundled-page test renders a page **with a row in it** — the old one rendered an empty page,
  and an empty page never calls anything that formats a row, which is precisely why it passed while
  the installed extension died;
- a new test reads each embedded function out of the shipped page and fails if it calls any name
  short enough to be a minifier's and not declared inside it. `cost3 calls a name that only exists
  inside the bundle` is what it says, before anybody installs anything.

## 0.29.12 — 2026-09-05 (server 0.17.5)

**The Cost column says three numbers: in / out / total.** It was empty on every row, because it
rendered the figure only a vendor that prices its own runs ever reports — and none of the three
here does. It is now worked out from the tokens each reviewer actually read and wrote and the same
two public price lists the spending section already reads. A `~` marks a total derived from a price
list rather than billed; a `+` marks one that is a floor because something in it could not be
priced; a `—` means nobody knows, which is a different claim from nothing. Hovering the cell spells
all of that out in a sentence.

**Priced from the usage ledger, at the price of the model that answered.** The first cut of this
priced each of the round's reviewer states, and the column stayed empty in the installed extension:
a reviewer state records `{provider, role, status, findings, note, seconds}` and no tokens at all.
The ledger has one line per reviewer run with its tokens AND its model, so a round is priced from
the lines that fall inside it — which also means a finished round keeps the cost it had when it ran,
instead of moving the next time somebody points a vendor at a different model. A round whose lines
do not add up to the tokens it recorded says so with the `+`, because two rounds of one stage
running at once can each match the other's lines by time alone. Found by this product's own gate,
nine reviewers over the diff.

**The date range takes a time, and the page opens on today.** The two pickers are date **and**
time now, and the log opens showing today from its first minute to its last — the question somebody
has when they open it is almost always what happened today. **All dates** clears the range in one
click. Storage stays UTC per the family rule; the conversion is the UI's, so the pickers speak your
wall clock and the filtering happens on instants.

**Long cells can be read.** The subject, the branch and the reviewer summary carry their full text
as a tooltip, so a column that is cut with an ellipsis no longer hides the rest of it.

## Server 0.17.5 — 2026-09-05

**A round no longer dies because a neighbour was writing the schema file.** Every round rewrote
`finding-schema.json` before launching its reviewers; the data directory belongs to every window on
the machine; on Windows two writers is an exception rather than a queue. Found by the seven-lane
matrix in its first minute — `the round failed: The process cannot access the file
'finding-schema.json' because it is being used by another process` — for a file whose content is a
compile-time constant and was already correct on disk. It is written now only when it is missing or
different, and a lost race is not an error: the neighbour is writing the same bytes. It fails open,
because a reviewer that cannot read the schema answers unshaped JSON, which a round already handles,
while a round that never launches over a locked constant is strictly worse.

**This is the release to have when several VS Code windows review at once.**

## Server 0.17.4 — 2026-09-05

**A local review cannot list findings until the token ceiling cuts it off.** The third way the local
model fails, found by the five-window campaign once the strings were bounded: forty-three findings in
385 lines, and `max_tokens` inside the forty-third's `why`. Every string was finite; the array was
not. The schema the local route sends now bounds `findings` at ten — the count at which a review with
every string at its bound still fits the ceiling, so a schema-valid answer can always finish. Ten is
also more than any local reviewer here has returned and been worth resolving; a review with forty
findings is a review nobody reads.

## 0.29.11 — 2026-09-05 (server 0.17.3)

**The Review rounds page draws its rows in the installed extension, not only on a developer's
machine.** 0.29.10's error trap reported it within a minute of release: *rowMatches is not
defined*. The page embeds its sort and filter functions by their source text so the tested
function is the one that runs — and the extension ships bundled and minified, where a function that
is not a top-level export is renamed: the page received `function m(a, b, c, d)` and called
`rowMatches()`. The unbundled build in node and in headless Chromium rendered every row, which is
why no test caught it. The functions are now bound by assignment, and a test refuses a bare
declaration.

## 0.29.10 — 2026-09-05 (server 0.17.3)

**The spending section is a tab of the Review rounds page, and Today means since midnight.** *What
each AI has used* is no longer in the sidebar: it is the second tab of *Show review rounds*, with the
same per-vendor rows, the same Today / Week / Month / Year buttons and the same ✕ to forget a vendor.
The default window is Today, and Today now starts at local midnight — "what did today cost" is a
question about the calendar day — while Week, Month and Year stay rolling. One section fewer in the
sidebar; one place for numbers.

**The Review rounds page says so when it fails, and filters by date.** The first release of the
page came up as a header row over nothing in VS Code's webview, while the same HTML rendered every
row in node and in headless Chromium — whatever failed, it said nothing. The page now traps its own
errors and writes them onto the page: what failed, where, and what to do. Two date inputs and a
*Today* button narrow the table to a range of days, inclusive; every column sorts, both ways.

**Server 0.17.3 — *Work autonomously* is six orders, not a mood.** Ruled by the operator over the
checkbox: an assistant told only to work autonomously fills in its own idea of the word, and the idea
that gets filled in is the cheapest one. The order it hands back now says what autonomous means:
every bug gets a red-green-red test; documentation, README, manifest and module docs are updated
with every change; ALL the tests run before a release; a release or pull request is made where the
repository has one, and a pull request's automatic comments are read five minutes later and fixed;
an automatic deploy is verified against dev, stage or test and its logs read; the code is re-read
against the repository's rules; and it says that it is working autonomously, and what it is writing
right now. The batching of questions — non-blocking ones at the end, blocking ones once and all
together — stays as it was.

## Server 0.17.2 — 2026-09-05

**A local reasoning model can no longer think inside a JSON string until the token ceiling.**
Observed twice on 2026-09-05: the answer opened as good JSON and then the `why` field became the
model's chain of thought — *"The plan *is* the instruction… Is there a violation? Maybe… No. Wait"*
— for thirty kilobytes, until `max_tokens` cut it mid-string; one repair launch, the same again, a
reviewer lost and three minutes of the GPU with it. The frequency penalty of 0.17.1 cannot touch
this: it is not a repeated sentence. The grammar can. The schema the LOCAL route sends now bounds
`title` (200), `why` (1000) and `fix` (1000) characters, and says so in each description.

Measured before release, on Ollama 0.33.3 with `Qwen3.5-35B-A3B-Q5`: a prompt demanding a
3000-character `why` came back in 16 s with `why` at exactly 1000 characters, `finish_reason: stop`,
valid JSON. The shared schema is untouched — OpenAI's strict structured outputs, which codex feeds,
reject `maxLength` with a 400 — and a test holds that line. The same measurement showed why the
local route defaults `reasoning_effort` to `none`: with the engine's own default, the same model
spent all 4096 tokens thinking and returned no content at all.

## 0.29.9 — 2026-09-05 (server 0.17.1)

**The rounds log is a page with a table.** *Show review rounds* used to write `rounds.md` under the
data directory and open it as a text document: fifty-three lines of markdown tables, one block per
session, each row one unwrapped line running off the right edge, nothing to sort, filter or search,
and a rewrite every five seconds while the tab was open that reloaded the editor each time. It now
opens a page with one table over every round of every session — when, repository, branch, stage,
round, what, status, verdict, gating, findings, duration, tokens in and out, cost, reviewers. Click a
column to sort it either way; narrow by repository, branch, stage, status, verdict or vendor; type
into the search box to match the subject, the branch, the repository or a reviewer line; click a row
to see its reviewers. Open questions stay at the top with their *Answer…* button.

The table advances by itself while a round runs, and your sort, filters, search text, scroll
position and expanded rows survive that — the page is only ever pushed rows, and only when they
changed. The markdown renderer and the tab-tracking that fed the rewrite are gone.

## 0.29.8 — 2026-09-05 (server 0.17.1)

**The sidebar shows what is running, and nothing else.** *Recent rounds* is now *Active rounds*: a
round in flight is shown whole — its reviewers, their durations, what each has found so far — because
that is what somebody is waiting on. A finished round is not in the sidebar at all. The history it used
to carry, 72 hours of disclosures, is a log, and a log wants a table with filters, sorting and search:
that page is the next release (`todo/PLAN_rounds_log_view.md`); until then *Show review rounds* still
opens `rounds.md`.

**And that is what stops the flicker.** Reported as "works on the first click, then shows what it
should and disappears" and, after 0.29.7 made the click fast, as "it flickers". One loop, two speeds:
the rounds list is replaced through `innerHTML` on every live patch, inserting a `<details open>` that
way fires `toggle` exactly as a click does, the document-level listener posted it to the provider, and
the provider answered with another patch. While a toggle cost a twenty-second repaint the loop crawled;
at a millisecond a patch it ran at the speed of a message round-trip. There are no disclosures, no
open-set policy and no toggle listener now, so there is nothing for the loop to be made of. The
open-set module and its nine tests are deleted rather than guarded.

**A live patch that changes nothing touches nothing.** The page compares each region's HTML with what
it showed last and skips identical markup — replacing it recreated every element and dropped the
scroll position on every five-second tick where nothing had happened, which is most of them.

**Server 0.17.1** carries the local reviewer's frequency penalty from 0.29.7's notes; its release was
held up two attempts by a test-harness race that only a two-core Windows runner could hit (the fake
reviewer read gemini's `-o json` as codex's `-o <file>` and three reviewers fought over a file called
`json`). Nothing in the product changed for it.

## 0.29.7 — 2026-09-04 (server 0.17.1)

**Opening a round no longer takes twenty seconds.** A card's reviewers are only built when it is
open, so the click had to repaint — and the repaint read the configuration, stat-ed the server
binary, ran every vendor CLI to ask its version, asked GitHub what was published and fetched two
public price tables. None of that can have changed because somebody clicked a triangle. The toggle
now redraws the rounds list alone, off a few small session files, and an opened card that has not
got its rows yet says *Reading this round…* instead of looking empty.

**Switching Today / Week / Month / Year is arithmetic again.** It was doing the same full repaint,
including both price fetches, to answer a question about rows the panel already had in hand. It now
patches the spending region and reuses the prices from the last real repaint — a list price does not
change because you asked about a different week.

**A card that cannot be opened no longer offers to open.** Rounds recorded before the server kept
per-reviewer detail have nothing to show and never will; they were disclosures all the same, with
the hand cursor and the marker, and clicking one opened a card containing a sentence apologising for
having nothing. They are plain lines now, with the same summary and no pointer. The ones that do
expand still show the hand.

**A local reviewer can no longer spend the GPU repeating one sentence** *(server 0.17.1)*. Greedy
decoding has no guard against a loop, and a schema is none either: a sentence repeated inside a
string value stays schema-valid right up to the token that runs out. Observed on 2026-09-04 — a
reviewer opened with a good finding, collapsed into *"The client retries again."* for forty
kilobytes, and spent 6.7 minutes of the one card while every other window queued behind it; the
round then reported "not the schema's JSON", which was true and nothing like the story. Requests now
carry a small, deterministic frequency penalty, so `temperature: 0` and the seed still mean what
they meant.

## 0.29.6 — 2026-09-04 (server 0.16.0)

**The vendor names in the rounds list are no longer struck through.** They were not struck through:
each one was wearing the border of a settings card. The colour added in 0.29.4 was hung on
`class="vendor"`, and `.vendor` already meant the reviewer's configuration box in this stylesheet —
a border, a radius and eight pixels of padding. On a single word inside a tight line, the top and
bottom edges of that box read as lines through the text. The word has its own class now, and a test
asks the general question rather than the specific one: whatever class the reviewer row puts on a
vendor, the stylesheet must not give it a border, padding, margin or a text decoration.

**A round that did not finish no longer claims to have run for hours.** Rounds were showing `361m 40s`
and `103m 44s` beside an *interrupted* badge on a machine whose reviewer timeout is ten
minutes. None of them ran that long: a round that dies is never written a completion time, so the
panel fell back to `now` and displayed how long ago it STARTED — and once a restart swept it, the
sweep stamped the moment it noticed, which measures how long nobody looked. An interrupted round now
shows no duration at all. The per-reviewer times beside it are real and stay.

## 0.29.5 — 2026-09-04 (server 0.16.0)

**The three switches in *The gate* now stay ticked.** They were never registered as settings, and VS
Code refuses to save a key no extension has declared — so the box lit up, nothing was written, and
the next honest read of your configuration found nothing there. The refusal was swallowed on top of
that, which is why it looked like the boxes were falling off by themselves rather than failing. They
are declared now, they default to off, and a setting that cannot be saved says so instead of going
quiet.

**A round no longer dies with "Access to the path is denied" — properly this time.** Six code rounds
were killed by it, one of them on the final save with every reviewer already answered: the findings
were in memory and were thrown away because a file could not be renamed. Two causes, and the first
is the one nobody looks for. Reading a session file *forbade writing it*, and five copies of the
server were running — one per window — each polling that directory. And the failure arrived as an
exception that the one catch written for exactly this case did not match, so it walked straight past
and took the round down.

Readers and writers now take turns through a lock the operating system releases even if a process is
killed. A missed repaint is dropped, the record of a finished round is best-effort, and the answer
comes back either way — a file that will not rename is not worth a review that has already happened.

## 0.29.4 — 2026-09-04 (server 0.16.0)

**A round that has finished closes itself again.** Rounds open while they run, which is what you want
to watch — and then they stayed open forever, so the list became a wall of expanded cards. Now:
running is open, finished is closed, and **a card you opened yourself is never touched**. Open one to
read it and it stays open when the round ends; close a running one and the five-second repaint leaves
it shut. The rule this replaces kept finished rounds open on the argument that this is the moment
their reviewers are worth reading — true of one round and wrong of a list.

**Each vendor's name now has its own colour, and it is the same colour everywhere.** `codex` is the
same blue in the round cards, in the live section and in the spending chart, this window and the
next, because the colour comes from the NAME rather than from the order rows happen to arrive in.
Only the vendor word is coloured; the rest of the row reads exactly as before. The colours are the
editor's own chart palette, so they hold in a light theme too.

**Split with Fable now works at all.** The order was withheld unless a Fable *reviewer* was
configured in the panel — and Fable is not a reviewer, it is a model of the assistant that calls the
gate. Nobody configures it as a vendor here and nobody should, so the switch did nothing on every
real machine, including the one it was built on. The box is the whole decision now: tick it if your
assistant can run Fable.

**A round no longer dies with "Access to the path is denied".** Two windows means two servers sharing
one data directory, and a nine-reviewer round saves its progress on every reviewer that moves. Every
one of those saves used a scratch file with the same fixed name, so two servers wrote the same
scratch path and both tried to move it — and the loser took its round down with it. Seen twice in one
morning, and once while this very change was being reviewed. Each save now has a scratch name of its
own, and the move is retried briefly for the case a unique name cannot fix: something else holding
the file for a moment.

## 0.29.3 — 2026-09-04 (server 0.15.0)

**The help now says that the split order is given once.** The tooltip on *Split the plan into epics
and stories*, and the article in all five languages, describe what an epic is told when it comes back
for its own plan review: that it is a piece of a split already under way, to be built as one unit and
closed properly, rather than split again. Behaviour that shipped in 0.29.2 with the help still
describing the version before it.

## 0.29.2 — 2026-09-04 (server 0.15.0)

**The order to split a plan is now given once, and an epic is told it is an epic.** With *Split the
plan* switched on, the assistant is told to break a plan into epics and stories — and each epic then
comes back for its own plan review, which is the right thing to do. Until now the gate had no memory
of the first order and told each epic to split into epics as well. Epics of epics, with no floor.
Reported by the operator before it could happen.

The gate now remembers which assistant it has already ordered to split, using the session id Claude
Code hands to the processes it starts, and tells a piece what it is: build it as one unit, review its
diff through this gate, fix, document, test and commit — and if it really is too big for one unit,
say so rather than starting a second round of splitting on your own. A different assistant, or the
same one a day later, is a new task and is owed its own order.

**A plan's split verdict was measured from the wrong plan.** It read the plan of the PREVIOUS round,
which on a first plan round is nothing at all — so the numbers that came back with the order read
`0 lines, 0 build step(s), 0 file(s) named` and a four-hundred-line plan was told it was small
enough to build as it stands. Found by a test that runs a real round rather than the function alone.

**Measured, not asserted.** 66 calls over eleven of this repository's own plans, two local models,
three arms — no orders, the split order, and the you-are-a-piece order. The full report, including
what the measurement got wrong about itself, is in `research/RESULTS_commands_campaign.md`.

## 0.29.1 — 2026-09-04 (server 0.14.0)

**The ⋯ menu names the snippet's version.** The snippet had been at v5 for a release while the menu
item still read *Copy the CLAUDE.md snippet* — so a repository carrying v4 had no way to learn there
was anything newer, and no reason to click. The item is now *Copy the CLAUDE.md snippet (v5)*, and a
test holds the manifest title to the version, so it cannot drift back out of the menu without a red
suite.

**The message after the click says what you have.** It compares the snippet you just took with the
copy in your repository: an older one is told which block to replace, a current one is told there is
nothing to replace, and a repository AHEAD of this build is told to keep what it has — the one case
where pasting is the wrong move.

## 0.29.0 — 2026-09-03 (server 0.14.0)

**The gate can now give orders.** It has always answered one question — are these findings gating,
may you proceed — and left everything else to the assistant: how the work is broken up, when you are
interrupted, which model does the expensive half. Those are your decisions, and the panel is where
you are. Three switches in *The gate*, all off until you turn them on:

- **Work autonomously.** A question that does not block is written down and asked at the END, all
  together; one that does block is asked at once — but only after the assistant has gathered every
  other blocking question it can foresee, so you are interrupted once instead of five times.
- **Split the plan into epics and stories.** After a plan passes, the assistant is told to break it
  into 2-4 epics and each into 2-4 logically complete stories, and to close every story properly:
  review its diff through this gate, fix what it accepts, update the documentation and the tests,
  commit — then start the next. A plan that did NOT pass is never told to go and build.
- **Split with Fable.** When a Fable reviewer is configured here, the split itself goes to Fable at
  its highest version — deciding what the epics and stories ARE is the judgement that shapes
  everything after it — and so do the stories where being wrong is expensive: payments, money,
  authentication, security, architecture, migrations. The ordinary ones go to Opus. Nothing is said
  about Fable when no Fable vendor is configured.

**Whether a plan needs splitting is measured, and the numbers come back with the verdict.** The gate
counts the plan's length, its build steps, the files it names and the subsystems it touches, and says
epics, stories or nothing — while saying out loud that this is a heuristic the assistant may argue
with. The rule was fitted to the 23 plans in this product's own repository, and to the one case they
can answer for: the plan that actually became six epics has no build order at all, so a rule that
only counted steps would have missed it.

**The commands reach your assistant because the CLAUDE.md snippet now tells it they exist** — that a
reply may carry them, that they come from the person who owns the gate, and that they outrank its own
habits. Copy the snippet again from the ⋯ menu; the panel already tells you when the copy in a
repository is older than this one.

**Every switch takes effect on the next call.** A box ticked one second before your assistant calls
the gate governs that call — in both directions, and the tests say so rather than assuming it.

## 0.28.0 — 2026-09-03 (server 0.13.0)

**A round is no longer a single line you cannot ask anything of, and one GPU is no longer shared by
every window you have open.**

- **Every round opens.** Click one and it lists the reviewers it ran: vendor and role, what it did,
  how many findings it filed, **how long it took** and what it read. A round is as slow as its
  slowest reviewer, so "11m 2s" across nine of them could not say which one cost the eleven minutes;
  now each says for itself. A round in flight opens itself once so you can watch it, and stays as you
  leave it afterwards — including when it finishes, which is the moment its reviewers are most worth
  reading. The list of rounds is twice as tall.
- **One caller on a local engine, across every process on the machine.** The previous release
  serialised the reviewers of one server; this machine runs several windows at once, each with a
  server of its own, and three requests on one card is what turned a 30-second reviewer into two
  cancelled at 590 s. The lease is now held by the process that talks to the engine, and the lock is
  the operating system's — released by the kernel even when a process is killed. Measured with five
  parallel processes: **5 of 5 answered, in 0.9 / 1.7 / 2.4 / 3.4 / 4.3 seconds**, no failures.
- **A queued reviewer says what it is waiting for**: how many callers are ahead on that engine and,
  once there are three runs of that model to average, roughly how long. Never a time it cannot
  support — the count alone is always true.
- **A local request now carries a token ceiling** (`COAI_LOCAL_MAX_TOKENS`, default 8192). Measured
  while testing the queue: with no ceiling this engine did not finish a one-line question in 90
  seconds and returned empty content with a full `reasoning` field — a reasoning model spending every
  budget it is given on thinking. The same review at the ceiling and at twelve times the ceiling
  produces the same six findings.
- **Two failures that used to read the same now read differently**: the engine was busy for your
  whole deadline and your question was never asked, or the engine had it and did not finish. They
  need different cures, and both carry their numbers.
- **"What a reviewer gets" was rendered twice**, and the two copies disagreed — two radio groups
  sharing a name are one group to a browser, so selecting in the first cleared the second.

## 0.27.0 — 2026-09-03 (server 0.12.3)

**The Server section said "0.12.2 is installed — you are up to date" on a side where 0.12.1 was
running, and hid the only button that could have fixed it.** One machine, one profile, a Windows
window and a `WSL: Ubuntu` window. `globalState` — where the version was remembered — is the
CLIENT's storage and is shared by both; `globalStorageUri`, where the binary actually goes, is a
path on the extension host that is running. One record, two disks: a WSL install wrote 0.12.1 into
the Windows-side database and a Windows press overwrote it with 0.12.2. The panel never looked at
either disk, and since the Update button only appears when the published version differs from the
remembered one, there was no way left, from inside the product, to update the server WSL was
running.

- **"Installed" is now the disk, and the version is the binary's own answer.** `coai-mcp --version`
  is new in server 0.12.3; the panel asks the file it is about to describe. A file that cannot
  answer — every release up to 0.12.2 — reads *"A coai-mcp is installed in WSL: Ubuntu, but it
  cannot report its version"* and offers the update, instead of claiming currency.
- **The Server section names the side** whenever there is one, because a machine with two of them
  had one sentence describing whichever pressed the button last. A local window is unchanged.
- **The record is per side**, folding the remote kind, the distro and the storage path — and it is
  only a fallback now, for a binary the OS refuses to spawn. The old shared key is never read: its
  value cannot be attributed to a side, which was the defect.
- **A release that misreports its own version fails.** The workflow stamps the tag into the binary
  and the smoke step compares the two.
- **Vendor CLI versions are readable again on Windows, and were worse than missing.** Every npm
  global is a `.cmd` shim, node refuses to launch one without a shell, and it refuses with a
  synchronous throw — which left `render` and stopped the panel repainting rather than showing the
  grey "could not be read" state. codex now reports 0.152.0 and gemini 0.57.0 where both showed
  nothing. A shim is the one case that gets a shell, on Windows only, resolved on the PATH first,
  with its path refused rather than escaped if it could break the quoting.
- A path pasted from Explorer's *Copy as Path* (wrapped in quotes) is accepted.

## 0.26.2 — 2026-09-03 (server 0.12.2)

**A local reviewer in WSL failed ten rounds in a row against an engine one hop away, and nothing said
so.** One machine, the same version installed on both sides, the two settings files byte-identical:
from Windows the local reviewer answered; from a VS Code attached to WSL every round died in zero
seconds with *"the local engine at http://127.0.0.1:11434/v1 could not be reached: Connection
refused"* — while a Windows Ollama sat there holding fifteen models. Two barriers, and fixing one is
not enough: a Windows engine binds `127.0.0.1` only, and a WSL distro's own `127.0.0.1` is not the
Windows host's.

- **The round carries a cure, not only a reason.** The panel had this sentence since 0.25.x; the
  round — the surface somebody actually watches during a review — had nothing. The diagnosis now
  answers for the server's own failure message, keeps the address that was already useful, and asks
  `/proc/version` rather than assuming Linux means WSL: a native Linux box was being handed
  instructions for a machine it is not.
- **The panel tells "you have no engine" apart from "your engine is one hop away".** When every
  candidate refuses, a WSL distro asks the Windows side of the same machine through interop — a
  Windows process asking `127.0.0.1` reaches the loopback this side cannot — and the note names what
  answered instead of showing an empty list.
- **`⇄` fixes it, and takes it back.** It merges `networkingMode=mirrored` into the Windows
  `.wslconfig`: inside `[wsl2]`, in the file's own line endings, keeping every other key, refusing a
  file that is not UTF-8 rather than corrupting one, and showing the whole merged file before writing
  it. The write is atomic and read back. It cannot restart WSL — that would terminate the distro the
  extension runs in — so it hands you `wsl --shutdown`, and pressing it again explains the restart
  instead of silently undoing the fix.

Measured after the change, from the distro against the Windows engine: `exit 0`, 249/142 tokens, a
valid findings object, 31 s. What this product's own gate removed from the plan before any of it was
written: probing WSL's default gateway for the engine — three reviewers, three separate reasons, all
correct.

## 0.25.2 — 2026-09-02 (server 0.11.2)

**Two rows on one runtime no longer kill the round.** The built-in runtimes hard-coded the name they
answered to — `CodexRuntime` said *codex*, `ClaudeRuntime` said *claude* — whatever row had selected
them. So `claude` beside `my-claude`, or `codex` beside a `local` row an older parser had rewritten to
codex, produced two reviewers with one provider/role key, and the round's dictionary threw on the
duplicate before any model was reached: *"every round dies on a duplicate reviewer key"*, from a
colleague's machine. A lone `my-claude` did not crash; it quietly filed its usage, its findings and
its vault-key lookup under `claude`, the name of a different row. Every runtime now carries the
VENDOR's id, the way the local and custom ones always did, and a settings file naming one id twice
keeps the first row rather than colliding.

**A local reviewer is told not to think, by default.** Measured: Gemma4 26B on Ollama answered the
planted-defect plan once in 171 s and, on the identical request, once spent 1056 s filling a 64k
context with 110 000 characters of `reasoning` and returned an empty `content`. Its thinking is
unbounded and not reproducible, and a reviewer that answers one time in two is worth less than one
that never runs. The escape had already been found in `dew_flow_rag_qln` three weeks earlier, against
the same model family: on Ollama's OpenAI route `think:false` is ignored and `reasoning_effort:"low"`
still burns the whole budget — only **`"none"`** returns an answer. Re-verified here (23 s, zero
reasoning characters, a valid findings object) and now the default; `COAI_LOCAL_REASONING_EFFORT`
sets a level, or `engine` to send nothing and take the engine's own behaviour. Measured on the same plan afterwards: **4 and 5 of eight planted defects in 31 s and 11 s**, both runs answering — against 4 and nothing in 171 s and 1056 s with thinking on. The thinking bought no defects and cost one review in two.

## 0.25.1 — 2026-09-02 (server 0.11.1)

**A configured local reviewer never ran, and nothing said so.** Found by running a local model
against the same baseline the thirteen hosted models were measured on: the round opened with
`0 reviewer(s)`, answered `call_human` with *"no reviewer answered — nothing was reviewed"*, and took
0.0 seconds. Meanwhile `providers` reported the same vendor as perfectly healthy, because it has its
own local arm — the panel said the reviewer was fine while every round silently ran without it.

Two copies of one mistake, both of them a hand-written list of "the runtimes this build knows":

- **The parser.** `RuntimeOf` mapped gemini, claude and antigravity to themselves and everything else
  to `codex`. A local vendor therefore arrived as a CODEX vendor carrying a base URL — which is the
  shape that means "a custom OpenAI endpoint needing a vault key". No key exists for a local engine,
  so the auth check answered `unavailable`, and unavailable vendors are dropped from the round.
- **The auth check itself.** Three methods decide what a vendor is from the same two fields, and two
  had already been taught that a local vendor is not a codex one — each with a comment saying "a
  local vendor IS a vendor with a base url". The third was never updated.

The 0.25.0 release fixed the extension's own copy of exactly this list, three hours earlier. The
server had the same one and nobody looked. Both sides now derive the set from where a vendor is
actually added, and a test fails if a fourth copy appears.

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
