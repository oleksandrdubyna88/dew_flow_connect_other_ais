# ConnectOtherAIs

A multi-model review gate. Your main AI writes the plan and the code; **other vendors' models** —
Codex, Antigravity (Gemini/Claude/GPT-OSS), a second Claude, DeepSeek — review both in rounds,
until the findings that matter drop under a
threshold, or a human is called.

The value is not "more review". It is **review by a model that cannot see the author's reasoning**,
which is the only kind that catches the author's assumptions.

Two halves, the shape CredsForDevs proved:

| | What it is |
|---|---|
| `coai-mcp` | A Native-AOT MCP server over stdio. An MCP client starts it; it runs the rounds. |
| ConnectOtherAIs | A VS Code extension: settings, the install button, the rounds view. |

## The protocol

```
open → review_plan → resolve → (revise, repeat) → proceed
     → implement
     → review_code → resolve → (fix, repeat) → proceed
```

`review_code` **refuses** until a plan round has reached `proceed`. Skipped stages are impossible,
not discouraged — the honest limit of a design with no hooks: the server cannot make a model call
it, but it can make a skipped stage impossible to fake.

Seven tools, unprefixed (the client's `coai` id is the namespace): `providers`, `open`,
`review_plan`, `review_code`, `resolve`, `status`, `ask_human`.

## When it needs a person

`ask_human` — and a `call_human` verdict — put the question in front of you **in VS Code**: a
dialog, a status-bar item so a dismissed dialog loses nothing, and an open-questions section at the
top of the rounds view. The call blocks until you answer; after 30 minutes it comes back
`no_answer_yet` telling the AI to ask you in the chat instead, and the question stays open.

Still no port on either side: the server writes the question as a file into the data directory the
extension already reads, and your answer is a file beside it.

You answer with one of three buttons — **keep going** (another set of rounds), **stop and act on the
findings**, or **stop and talk to me** — and each says what it will cause. None of them ships a change
over open findings: an override meaning "ignore all this" is an off switch on a gate. The questions
are English; there used to be a translator, and three buttons removed the prose it existed for.

## What makes "fewer than 2 remarks" mean something

Without a rule, three verbose reviewers guarantee escalation forever. So:

1. Only `blocking` and `major` count; minors and nits are reported and never gate.
2. **De-duplication happens first** — same file, lines within ±5, same category, same remark → one
   finding listing every provider that raised it. Two vendors agreeing is stronger evidence, not
   twice the work.
3. A finding you rejected **with a reason**, re-raised with the same argument, does not count again.
   Re-raised with a genuinely new argument, it counts in full.
4. **Each ROLE has its own rounds and its own threshold**, and a finding is counted against the
   threshold of the role that raised it. Architecture may be worth two passes with different lenses
   while performance is worth one; a shared budget forces the cheapest role to pay for the most
   expensive. A stage passes when every role is at or under its own number — not when one total is
   small enough — and it revises only for roles that still have rounds to spend.

Defaults: the plan role gets 3 rounds at a threshold of 2; each code role 2 rounds at 3. When the
rounds run out there are four answers: ask a human, continue and say so, **good enough** (read the
findings, apply the ones that are true, move on), or climb the escalation ladder.

## Install

1. **The server**: *ConnectOtherAIs: Install the MCP Server…* downloads the release asset for your
   platform into the extension's storage (never onto `PATH`), verifies its `sha256`, and puts the
   `mcpServers` block on your clipboard. Paste it into `~/.claude.json`, a project `.mcp.json`, or
   `.vscode/mcp.json`, and restart the client.
2. **The instructions**: *ConnectOtherAIs: Copy the CLAUDE.md snippet* gives the text that teaches a
   repository's main AI when to call the tools.

Codex, Antigravity and Claude authenticate themselves — if their CLIs are signed in, no key is
needed. DeepSeek
rides the Codex CLI's custom-provider config and needs a key, which comes from **one CredsForDevs
`config` entry** read once at startup. That is the only key path: a `credential` entry cannot serve
it, because nothing in the vault's read routes returns a secret. A **local** reviewer needs neither:
there is no CLI to sign in and no account to bill.

## A model on your own machine

*＋ Add a reviewer → Local model (Ollama / vLLM)* adds a row called `local` whose model dropdown is
what THIS machine has installed — each with its parameter size, quantisation and disk size, read from
the engine rather than from a list shipped here. Nothing found says where it looked and why, because
an empty dropdown with no reason is indistinguishable from "you have no models".

It is **not** the Codex CLI pointed at a local endpoint. That was tried first and it answers, but
codex's own system prompt is 21k tokens before any review content — measured — so a small-context
model is refused outright and a large one pays for a prompt unrelated to the review. A local reviewer
is a direct call: `coai-mcp --ask-local` POSTs to the engine's OpenAI-compatible endpoint with the
finding schema, `temperature` and `seed` pinned in the request, and prints the answer where the
executor already looks. It is a process like every other reviewer, so the timeouts, the kill, the
usage parsing and the unparseable handling are the ones that were already there.

Three things worth knowing before using one:

- **Two structured-output modes exist and only one works.** `response_format: {"type":"json_schema"}`
  returns well-formed findings; the weaker `json_object` answers with a shape it invented. There is
  no fallback to it, because a fallback would buy a full generation and an unusable round.
- **An endpoint that is not on this machine is announced in the row**, naming the host and saying
  that the plan, the diffs and the file contents around them are sent to it. `localhost`, `::1` and
  the whole 127.0.0.0/8 block are this machine, decided by PARSING the host — `127.0.0.1.evil.test`
  is somebody else's.
- **Tokens are counted, money is a dash.** The engine reports `prompt_tokens` and
  `completion_tokens`, so a local round appears in the spending chart with real numbers. Cost stays
  null rather than 0, because free and unpriced are different facts: what a local run costs is
  electricity and a busy card, and this product can see neither.

## Fast or Full: what a reviewer is given

Every reviewer runs in an **empty directory** by default — plan stage and code stage alike. The
diff, the plan and this project's written rules are assembled **by the server** and handed over in
the prompt; what changes between the two positions is only whether there is a repository to explore.

| | what the reviewer gets | when |
|---|---|---|
| **Fast** *(default)* | the composed prompt, in an empty directory | almost always |
| **Full** | the same prompt, plus one read-only checkout of the commit | when the meaning of a change depends on callers the diff does not show |

**Fast is the default because it was measured, not preferred.** On one commit, taking the checkout
away made every hosted model find MORE useful defects — Gemini 3.7 Flash 4→8, GPT-5.6-Luna
6→10, Claude Sonnet 5 6→7 — at a half to a third of the input tokens, with no wrong
finding from any of the three. Three real defects surfaced that no run WITH a checkout had reached.
A reviewer given a repository spends its attention deciding where to look; a reviewer given a diff
reads the diff. The evidence is in
[RESULTS_findings_that_are_worth_something.md](research/RESULTS_findings_that_are_worth_something.md).

Full creates **one** detached `git worktree` pinned to a resolved SHA, outside your repository,
shared by every reviewer in that round: the main AI keeps editing while a review runs, and six
checkouts of a moving branch would be six different inputs to one comparison. Codex runs
`-s read-only --ephemeral`, Gemini `--approval-mode plan`. The tree is removed in a `finally`, and
an orphan from a killed session is pruned by the next `open`.

## Build and test

```bash
dotnet build dew_flow_connect_other_ais.slnx -c Debug
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe     # never `dotnet test` — MTP, no VSTest host
cd src_vs_code && npm ci && npm test
node .claude/rules/shared/tools/plan-lifecycle.mjs
```

The process-level suite drives a scriptable **fake CLI**, so CI touches no vendor and needs no
network. The wire contract is checked against the built binary and, in the release workflow,
against the **published** one.

## Documentation

`research/architecture.md` is the entry point; `module_core`, `module_runners`, `module_server` and
`module_extension` deep-dive each half. Plans live in `todo/` while open and move to `research/`
with an `IMPLEMENTED` status when they ship — a rule this repository's CI enforces.

## Prompts: a universal question, and twenty narrow lenses

Each reviewer role ships a **universal** prompt and five narrow ones, and the panel can pick which
prompt each ROUND uses.

**Round 1 of every code role is the conventions pass**: it judges the diff against the rules this
project has written down — `CLAUDE.md`, `AGENTS.md`, `GEMINI.md`, `.claude/rules`, read from the
commit under review **by the server** and put in the prompt, so the pass works in Fast too — and
nothing else. A finding there must quote the sentence it
breaks; a convention the reviewer believes in but the project never wrote is not a finding. Pick
something else for round 1 and that wins.

**Dealing the lenses** is a switch per stage, off by default. Off, every vendor answers every
question and two vendors agreeing on a finding is a fact the gate can use. On, the round's prompts
are dealt out one per vendor: every lens gets asked once at half the launches, and that agreement is
gone. It is a real trade and the default is the conservative half of it.

| Role | Universal | Lenses |
|---|---|---|
| Plan | the whole plan | assumptions & verification · the human path · data loss & recovery · operability · scope & budget |
| Architecture | boundaries + evolution | boundaries & duplication · cost of the next change · coupling & knowledge · names & the shape they imply · testability of the seams |
| Security & reliability | the whole surface | what it holds and leaves · attack surface · blast radius · two at once · what this change trusts |
| Performance & UX-DX | both | cost at scale · ergonomics & waiting · the first run and the empty case · work done twice · what cannot be taken back |

**The last twelve were measured before they shipped**, and the measurement found something better
than a winning sentence. Each was drafted three times, and the three drafts turned out to be three
SHAPES held constant across all twelve — a question list, a task to enact, a rule with exceptions.
Seventy-two runs later ([RESULTS_focused_prompts.md](research/RESULTS_focused_prompts.md)): the
shapes find the same AMOUNT (6.6–6.9 findings, 79–82 % gating, flat) and differ in whether they find
the same thing TWICE — **42 % against 32 %**. So a lens here is written as a task to perform wherever
its subject has a sequence to enact, and as a question list only where it does not. Five of the
twelve picks were decided by that measurement; seven were inside its noise and took the shape result
as a prior, which the document says rather than presenting twelve winners.

**What is still not claimed.** Whether a lens finds what the universal prompt MISSES is a different
question, and the campaign that asks it compares the two arms over one real change. Over three plans
the union of all lenses found roughly twice what any single one did — and that result does not
survive its own control: the SAME prompt on the SAME text three times produced 6, 4 and 5 findings
whose overlaps were 3, 1 and **0**. Run-to-run variance alone explains the spread, which is exactly
why repeatability, not yield, is what the shape measurement was scored on.

The measurement that matters for a gate is a different one: a finding raised by two vendors
independently is stronger evidence than one raised twice by the same prompt, and every finding
carries the `providers` that raised it.

## What each AI has used

The server appends one line per reviewer to `usage.jsonl` — vendor, model, role, stage, seconds,
tokens, cost, outcome — and the panel charts it per vendor over a day, week, month or year.
Failed reviewers are recorded too: a run that burned ninety seconds and answered nothing is
exactly what a spending record must not hide. A vendor that does not price its own runs shows a
dash rather than `$0.00`, because free and unreported are different facts.

Token accounting is each vendor's own, because a shared rule is wrong for at least one of them:
codex folds cached tokens INTO its input count, claude reports them BESIDE it, and antigravity's
thinking tokens sit inside its output count. Claude reports one run twice and the two disagree —
`usage` is the last message, `modelUsage` is the session — and the ledger reads the second.

## The help, in the panel

The yellow **?** in the panel's title bar opens a searchable help page. It carries one article per
control and per setting, plus the machinery you cannot see from the panel — where a reviewer
actually runs, what happens when one fails, how a setting reaches the server, and what the audit
trail holds.

The first four articles are the first four things a person does: install the server, choose the
reviewers, tell your AI to use the gate, and set the gate itself. Search runs over the full text,
the language switch and the ± text size are real settings so they sync, and **The prompts, in
full** prints every prompt verbatim — held byte-for-byte against the server's own files by a test,
so the page cannot describe a question the product no longer asks.

Two tests keep it honest. One fails the build when a command or a setting has nothing written
about it — a new button either gets an article, or an alias naming the words the help uses, or a
written reason why it needs none; there is no fourth way and no silent default. The other holds the
printed prompts against the shipped ones.
