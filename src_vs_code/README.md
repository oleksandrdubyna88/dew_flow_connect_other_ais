# ConnectOtherAIs

**Your AI writes the code. Other vendors' models review it — before it is written, and after.**

One model planning and implementing alone has a blind spot it cannot see past: its own reasoning.
ConnectOtherAIs puts that work in front of models from *other* vendors — Codex, Gemini, a second
Claude, DeepSeek, anything with an OpenAI-compatible endpoint — and holds a gate until they broadly
agree, or until you decide.

They see the plan and the diff. They never see the conversation that produced them. That is the
whole point.

---

## What it actually does

Two gates, both run by the AI you are already working with, through an MCP server this extension
installs.

**The plan gate.** Before implementation, the plan goes to every enabled vendor. Each answers with
findings — a severity, a category, what breaks and the smallest fix. Your AI records a decision for
every one of them, revises, and asks again. When the gating count drops to your threshold, it may
implement.

**The code gate.** When the branch is written, each vendor runs **four independent reviewers**:

| Reviewer | Reads for |
|---|---|
| Architecture | boundaries, abstractions doing two jobs, consistency with the code around them |
| Security & reliability | secrets, injection, swallowed errors, what a `kill -9` leaves behind |
| UX-DX & code performance | redundant re-renders and queries, blocking calls, the ergonomics of a new API |
| Conventions | only the rules this project wrote down — `CLAUDE.md`, `AGENTS.md`, `GEMINI.md`, `.claude/rules`. A convention the reviewer believes in but the project never wrote is not a finding |

Each role has its own rounds, its own threshold and a tick box on its heading, so a role you do not
want takes no part and costs nothing. You can add roles of your own beside these.

Same loop: findings, decisions, fixes, another round.

**The document gate.** `review_document` is the third way in: a DOCUMENT rather than a diff — a
plan, a spec, a piece of prose — with no plan round before it and its own session per document. Two
roles ship for it: *The document*, which reads for whether a reader could act on it, and a summary
role that writes an account of the whole thing rather than findings. A summary gates nothing.

**When the rounds run out**, your policy decides: ask a human, continue and say what is still open,
or climb a ladder — reviewer effort, then reviewer model, then the arbiter.

---

## What makes the count trustworthy

Three vendors producing five remarks each would make any threshold unreachable. So:

- **Only `blocking` and `major` gate.** Minor and nit findings are reported and never counted.
- **The same defect from two vendors is ONE finding**, with both names on it. Two vendors agreeing
  is stronger evidence, not twice the work — and when they disagree on severity, the merge resolves
  toward caution.
- **A finding you rejected with a reason stops counting** — unless a reviewer raises it again with a
  genuinely new argument. Disagreeing honestly is cheap; disagreeing silently is impossible, because
  a rejection without a reason is refused.
- **A partial round says so.** If four of six reviewers answered, the verdict carries that sentence
  and names who timed out or hit a rate limit. Silence is never counted as agreement.

---

## When a decision is yours

Some verdicts are not the AI's to make. The question appears **in VS Code** — a dialog, a status-bar
item so a dismissed dialog loses nothing, and at the top of the panel — together with the findings
that are still gating, so you are not deciding from a summary.

The call blocks until you answer. After your timeout it comes back `no_answer_yet` and tells the AI
to ask you in the chat instead; the question stays open either way. **Nothing is decided by your
silence.**

The question reaches you as **one fixed English sentence and three buttons**, and your answer goes
back exactly as you gave it. There used to be a translator here — the question was prose an AI had
written, and your words were rendered into another language by a third model before the asker saw
them. Three buttons removed the need for it, along with the `COAI_LANGUAGE` and `COAI_TRANSLATOR_*`
settings behind it: free text you type on a button reaches the AI unmediated, which is worth more
than the same words translated.

The **Help** pages are a separate choice and still yours — English, Español, Deutsch, Русский,
Українська, switched at the top of any help page and stored as `coai.helpLanguage`.

---

## Ask a second model about a passage

The gates read a plan and a diff. **Chat with other AI** does the same thing at the scale of a
paragraph: select a dense passage in your assistant's answer, press `Ctrl+Alt+A`, and a tab opens
where another vendor's model explains it — in the answer language you chose, English by default, in
a conversation you can carry on. The right-click menu carries the same item for where a keybinding
cannot reach.

The tab is a conversation, not a viewer:

- **A provider, then one of its models.** A model list belongs to a provider. Switching
  mid-conversation is expected — the whole thread goes across — so every answer is captioned with
  the model that gave it, in that model's colour.
- **Named prompts and named models**, two rows of buttons above the box, edited in a tab of their
  own (**Edit chat presets**) that saves as you type.
- **An empty box with a different model chosen re-asks**: the same question goes to the other model,
  carrying the conversation minus the answer you did not want.
- **Paste a screenshot** and it goes with the next question. A provider that cannot take one is
  refused by name rather than dropping it silently.
- **What the conversation has cost**, beside the picker, where the decision to ask again is taken.
- Answers are rendered — headings, lists, tables, code — with blue links whose reach is bounded: a
  path inside the workspace opens in the editor, an `http(s)` address in your browser, and anything
  else stays text. A **Copy** gives you the Markdown, and a **Stop** ends the turn it was drawn for
  and never the next one.

Reloading the window keeps what was said: every open chat tab comes back with its questions and
answers, and a line saying the reload closed the conversation.

**CoAI: switch conversations…** (`Ctrl+Shift+Alt+G`, or either right-click menu) is the list of all
of them — **Open** at the top, what this window has in a tab right now; **Recent** below, everything
else, newest first. Typing filters by the title, by the model that answered and by the last thing
said, so a conversation you remember by one word of its answer is one word away. Choosing an open one
brings its tab to the front; choosing a closed one opens it again where it left off, with nothing
running until you ask something. A trash button on every closed row forgets that conversation and
`Alt+Delete` (`Cmd+Delete` on a Mac) forgets the one under the cursor — the list stays open either way — and the globe in the
title widens it from this folder to every folder.

Forgetting is not destroying: the row goes at once, but the conversation itself is set aside on
disk and only really deleted after the same ninety days as everything else. A conversation open in
ANOTHER VS Code window is listed and says so, and cannot be opened or forgotten from here — one
window cannot raise another, and a second tab onto one conversation would split it in two.

**CoAI: go to conversation** (`Ctrl+Alt+G`, or either right-click menu) is the same thing from the
other end, and usually the one you want: pressed on the tab you are working in rather than on a list.
If this window already has that conversation open it comes to the front; if it is only saved it opens
again, bound to that tab, so every later press lands in the same place. If nothing is saved for the
tab, the list opens with *New conversation for it* under the cursor — and nothing is created until you
press it; press it and the ordinary chat opens on that tab, from whatever you have selected there. From a terminal or anywhere a conversation cannot belong, it opens the full list rather than
doing nothing. Where it cannot be sure — two Claude sessions with one name, or a conversation about
this file filed under another folder — it shows those and lets you choose, with a title saying why and
the tab's own name already in the search box. If exactly one saved conversation was opened from a tab
of that name, and only one tab open right now carries it, it opens that one rather than asking.

**New chat** in the tab's header starts again in the same tab: the conversation you were in is
archived — whole, under the same name, in **CoAI: switch conversations…** — and the tab is yours
again, empty, with the quotation that started the old one cleared away. The model, the prompt and the
tab's own name stay as they were, and the next question reaches a model that has never heard any of
it. A turn still running is ended first and waited for, so the answer you were waiting on is kept in
the conversation that is archived. The button in the *this conversation is full* notice is the same
one.

The full description lives in the help — `⋯` → **Help** → *Chat with other AI*, in any of the five
languages. This section is the overview; that article is the one kept in step with the code.

---

## When your AI is the one who is stuck

The chat is a person asking another vendor about a passage. **The consultant** is the same idea from
the other end: `consult` is a tool your assistant calls itself, when it has been going in circles,
to ask another vendor's model about the working tree **as it stands** — uncommitted edits included,
which is the state no diff and no review round can see.

It gates nothing and blocks nothing; it comes back as an answer, not a verdict. Its cost is bounded
by caps you set in the panel — turns per consultation, calls per session, and an idle time after
which one is closed — because the failure mode of a tool an AI can call on its own judgement is
calling it forty times. The turns and the idle time bound every consultation; calls per session counts
only the calls an AI makes because it is stuck — its first question and every follow-up alike. The idle close is checked every minute while the server
runs.

**It is also called when nobody is stuck.** After a plan is split into epics, every group of three
owes one consultation before its first code round — is this group right, where is it weak, what did it
forget — and from five epics the assistant is asked which epics and stories carry the most risk, each
of which gets one of its own. *Consultation cadence* in the same section sets the numbers and what the
gate does: *Remind* puts the order in every review reply, *Require* also holds the group's code round
until it is taken. *Active rounds* says where each plan stands — `epics closed 4/14 · consultation for
epics 4-6: due · branch feat/x`. An ordered consultation spends none of the calls-per-session budget: the
gate bounds it instead — one open or answered consultation per group of epics or risky piece, and
another only after one failed or lapsed without a verdict.

**Every consultation says what kind it is** — `stuck`, `cadence` or `risk` — on its card in the sidebar,
in the server's log, and in the *Kind* column of *Consultations* in **Show review rounds**, beside *For*:
the epics or story and the plan an ordered one covered.

---

## A company box, if you have one

A **Team server** reviews for everybody without anybody installing a CLI. Add it under *Team
servers*, press **Sign in**, and its reviewers appear beside your local ones; your plan and your
diffs are sent to that machine over HTTPS, behind your own sign-in, and what it is authenticated as
is shown on the row.

Documents are deliberately held back. A reviewer row on a Team server has a third box, **reviews
documents**, and on a Team server it starts **off** — a diff belongs to a repository that machine
already has, but a document you were handed may be somebody else's to release. The box is what
decides whether it leaves your machine, and it says so rather than meaning it silently.

---

## The log, and taking it with you

Finished rounds go to **Show review rounds** on the panel's `⋯` menu: a table of everything that has
ever run, sorted, filtered and searchable, holding your chat conversations as well as your review
rounds. The **Took** column now reports two figures — how long the reviewers ran, and how long the
*deciding* took, from the round finishing to its last decision, which is the half that actually takes
somebody's afternoon.

**And it leaves as a file.** Every row ends with **Export**, which writes that round to CSV: its
verdict, both times, the tokens, the three cost figures, a line per reviewer — and a line per
finding, carrying what you decided about each one in the same words the page uses, with a declined
finding's reason beside it. Tick several rows, or the box in the header to take every round the
filters currently match across pages, and the toolbar exports them together, cancellably.

Two things the file is careful about, because a log that lies is worse than no log: an absent
measurement stays an **empty cell** rather than a zero, and a round whose findings could not be read
is marked as such in a `findings_read` column rather than written as a round that found nothing.
Nothing in it can execute when a spreadsheet opens it, either.

---

## Getting started

1. **Install the server.** The `⋯` menu in the panel → *Install the MCP server…*. It downloads the
   published binary into this extension's own storage — never onto your `PATH` — verifies its
   checksum, and puts the client configuration on your clipboard.
2. **Paste that configuration** into your MCP client (`~/.claude.json`, a project's `.mcp.json`, or
   `.vscode/mcp.json`) and restart it. This is a **one-time paste**: everything you change in the
   panel afterwards is saved for the server itself.
3. **Teach your AI when to call it.** `⋯` → *Copy the CLAUDE.md snippet*, and paste it into the
   `CLAUDE.md` of the repository you want reviewed. The server can refuse an out-of-order call, but
   it cannot make a model call it — that snippet is what does.

Then work as usual. Your AI opens a session, submits its plan, and the gate does the rest.

---

## The panel

Everything in the sidebar, most of it folded away because it is configured once:

- **Reviewers** — add a vendor, remove one, switch one off, choose its model. Codex's models come
  from the CLI's own cache, so the list is what this machine can actually reach today; Gemini's and
  Claude's are curated, and the panel says which is which rather than passing curation off as
  discovery. Any model can be typed in regardless. **Add a reviewer** offers the whole catalogue
  whether or not you already have one of each: pick a vendor you already have and it adds a second
  row under the next free name — `claude-2` beside `claude`, which is how you run one on haiku for
  the cheap passes and one on opus for the hard rounds — and the entry tells you which name that
  will be. The filter box searches what each entry SAYS, not only its name.
- **Chat other AIs** — ask a second model about a passage without leaving VS Code, and find that
  conversation again afterwards.
- **Phrases** — the sentences you stopped wanting to retype, one button each. Press one and it is
  on the clipboard; paste it where you were about to type it, usually the Claude Code box. Edit
  them in a tab of their own (**Edit phrases**) that saves as you type. It copies rather than
  typing into the box for you on purpose: no Claude Code command accepts arbitrary text, and the
  only alternative was a synthetic keystroke through the Windows API — one `Ctrl+V` is a better
  price than a mechanism that can fail silently on somebody else's machine.
- **Consultant** — the same idea from the other end: `consult` lets an AI ask another vendor's
  model about your working tree as it stands — when it is stuck, or when the consultation cadence
  orders one — with its own caps on turns, calls per session and idle time. Every setting in it has a
  `?` that says what it does.
- **Prompts per round** — which lens each role is asked through, and the full text of every prompt.
- **The gate** — rounds and a passing threshold **per role**, each role with a tick box on its own
  heading, and what happens when the rounds run out.
- **Limits** — reviewers at once, per vendor (rate limits are per vendor: without that cap one
  throttled vendor holds every slot), timeouts, and how long a question waits for you.
- **Vendor keys** — and, first, whether you need any. With signed-in CLIs, you do not.
- **Team servers** — a company box that reviews for you, with nothing installed here. Sign in, and
  its reviewers appear beside your local ones; the *reviews documents* box on such a row starts off.
- **This side** — a local window, or each WSL distro and remote host, can keep its own settings and
  its own data directory.
- **MCP server** — install or update it, and see where this window keeps its data.
- **Active rounds** — what is running right now, whole: the stage, the branch, and every reviewer
  the round launched. Finished rounds live in the log — **Show review rounds** on the ⋯ menu —
  which sorts, filters, and now **exports to CSV**, one round or a selection of them.

Every setting carries a **?** that explains what it does and why it exists.

---

## Requirements

- **VS Code 1.85+**
- **An MCP client** — Claude Code, or anything that speaks MCP over stdio.
- **At least one reviewer CLI**, signed in: [Codex](https://developers.openai.com/codex/cli),
  [Antigravity](https://antigravity.google) (which fronts Gemini, Claude and GPT-OSS), or
  [Claude Code](https://claude.com/claude-code). They authenticate themselves; no API key is needed
  for these. A fresh install starts with Codex and Antigravity enabled. The standalone
  [Gemini CLI](https://github.com/google-gemini/gemini-cli) is still selectable but Google **retired
  Code Assist for individual accounts**, so it refuses before reaching a model — it is kept only for
  a Workspace account that still has it, and the panel labels it retired rather than letting you
  find out from a failed round.
- **git** — a reviewer never touches your live checkout. By default it is given no checkout at all;
  set *Full* and it reads a detached worktree pinned to the commit under review.
- **Nothing at all, for a Team server reviewer** — that machine has the CLIs, you only sign in.
- **An API key only for a vendor without a CLI** (DeepSeek, OpenRouter, any endpoint you add). Those
  live in one [CredsForDevs](https://marketplace.visualstudio.com/search?term=CredsForDevs) entry of
  kind `config`; the extension never stores a secret itself.

Published server builds: `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`. macOS is not built yet
and the installer says so rather than downloading something that cannot run.

---

## What it does not do

- **It opens no port.** The extension and the server talk through files in a directory they both
  already use — sessions, escalations, settings.
- **It stores no secret.** The vault key you paste is a pass to one entry, revocable, and useless
  while VS Code is closed.
- **It sends nothing anywhere itself — until you add a Team server.** Reviewers are local CLIs you
  have already installed and signed in; what they send is between you and that vendor, and the panel
  says so when you enable one. A **Team server** is the one exception and it is opt-in: configure one
  and the extension itself POSTs your plan or diff over HTTPS to that machine, behind your own
  sign-in. Nothing reaches it until you add it.
- **It cannot edit your code.** Every reviewer runs read-only with the write tools explicitly denied.
  By default it is given no checkout at all — just the composed prompt, which measured better on
  every hosted model at a fraction of the input tokens. Switch **What a code reviewer is given** to
  *Full* and it also gets a worktree pinned to the commit under review.

---

## Privacy

Your plan and your diff are sent to the vendors you enable, by their own CLIs, under your own
accounts. Lock files, build output and binaries are excluded before anything is sent; an over-sized
diff names what it left out rather than silently truncating.

If you configure a **Team server**, that work is also sent over HTTPS to the machine you named, by
this extension rather than by a CLI. A **document** review is the one case held back by a switch of
its own: on a Team server the *reviews documents* box starts **off**, because a document you were
handed is not a diff of a repository the server already has, and the box is what decides whether it
leaves your machine at all.

Your saved phrases and chat prompts are never sent to a review server or mirrored to a Team server.

Nothing is sent to the authors of this extension. There is no telemetry.

---

## Licence

MIT. Source: [dew_flow_connect_other_ais](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais).
