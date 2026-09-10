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

**The code gate.** When the branch is written, each vendor runs **three independent reviewers**:

| Reviewer | Reads for |
|---|---|
| Architecture | boundaries, abstractions doing two jobs, consistency with the code around them |
| Security & reliability | secrets, injection, swallowed errors, what a `kill -9` leaves behind |
| UX-DX & code performance | redundant re-renders and queries, blocking calls, the ergonomics of a new API |

Same loop: findings, decisions, fixes, another round.

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

Questions are shown **in your language** — English, Español, Deutsch, Русский, Українська. A question
already written in it is left exactly as it was; anything else is translated by a small fast model
first, and your answer is translated back for the AI that asked. If the translator cannot run, you
get the original text with the reason — never an error in its place.

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

The full description lives in the help — `⋯` → **Help** → *Chat with other AI*, in any of the five
languages. This section is the overview; that article is the one kept in step with the code.

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
  discovery. Any model can be typed in regardless.
- **Language** — who you are asked in, and which model translates.
- **The gate** — rounds per stage, the passing threshold, what happens when rounds run out.
- **Limits** — reviewers at once, per vendor (rate limits are per vendor: without that cap one
  throttled vendor holds every slot), timeouts, and how long a question waits for you.
- **Vendor keys** — and, first, whether you need any. With signed-in CLIs, you do not.
- **Recent rounds** — the verdicts, newest first.

Every setting carries a **?** that explains what it does and why it exists.

---

## Requirements

- **VS Code 1.85+**
- **An MCP client** — Claude Code, or anything that speaks MCP over stdio.
- **At least one reviewer CLI**, signed in: [Codex](https://developers.openai.com/codex/cli),
  [Gemini](https://github.com/google-gemini/gemini-cli), or
  [Claude Code](https://claude.com/claude-code). They authenticate themselves; no API key is needed
  for these.
- **git** — reviewers read a detached worktree, never your live checkout.
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
- **It sends nothing anywhere itself.** Reviewers are local CLIs you have already installed and
  signed in; what they send is between you and that vendor. The panel says so when you enable one.
- **It cannot edit your code.** Every reviewer runs read-only, in a worktree pinned to one commit,
  with the write tools explicitly denied.

---

## Privacy

Your plan and your diff are sent to the vendors you enable, by their own CLIs, under your own
accounts. Lock files, build output and binaries are excluded before anything is sent; an over-sized
diff names what it left out rather than silently truncating.

Nothing is sent to the authors of this extension. There is no telemetry.

---

## Licence

MIT. Source: [dew_flow_connect_other_ais](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais).
