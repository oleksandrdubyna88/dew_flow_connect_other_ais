# PLAN — a README that sells in five seconds, and a deep-dive that keeps the engineering

> Status: **plan only, nothing implemented yet.** Scope: `README.md`, a new `ARCHITECTURE.md`, one
> screenshot, and the badge row. No code.
>
> Related: [RESULTS_findings_that_are_worth_something.md](../research/RESULTS_findings_that_are_worth_something.md)
> — the Fast-mode measurement every claim below rests on. The overlap measurement that answers "why a
> second vendor" is `research/RESULTS_vendor_overlap_2026-09-06.md`, landing with the bench branch.

## The symptom

The README is an internal architecture manifesto: sixteen paragraphs deep before a reader learns
what pain it removes. A developer who opens the repository decides in about five seconds whether
this is for them, and today they are reading about `json_schema` versus `json_object` and about token
accounting in the Codex cache. That is the best engineering in the repository and it belongs
somewhere a reader chooses to go, not in the doorway.

## What must be true when this is done

1. **The first screen answers three questions**: what pain, why this is not another wrapper, and how
   to run it in sixty seconds.
2. **Every number is one we measured, with its sample size stated.** The repository's own
   `common/measurement.md` applies to its own marketing before it applies to anybody else's.
3. **Nothing in the README becomes false the day a shipping feature lands** — in particular the
   zero-ports claim, which the team server will qualify.
4. The full engineering text survives, whole, in `ARCHITECTURE.md`, linked prominently.
5. The install command is the real one — `code --install-extension remsoftdev.connect-other-ais` —
   verified against the Marketplace listing before it is published as an instruction.

## The structure

### Block 1 — Hero

- One punchline: *your primary AI cannot see its own assumptions; ConnectOtherAIs has other vendors'
  models read the plan and the diff before either reaches you.*
- The insight as a pull quote: **the value is not more review — it is review by a model that cannot
  see the author's reasoning.**
- Badges: CI, latest release, MIT, VS Code Marketplace version, test count.
- **A screenshot of the rounds page**: findings, which vendor found each, accepted or rejected, and
  the cost. PR-Agent's README proves with real output rather than with benchmarks, and this is output
  no competitor can show.

### Block 2 — The pain, as a contrast table

Four rows, each with a right-hand side that is measured or verifiable in one line of code:

| Reviewing with one vendor | With ConnectOtherAIs |
|---|---|
| The model that wrote it also judges it | Other vendors read it with no memory of the reasoning |
| Formatting nits count as findings | Only `Blocking` and `Major` gate — `Finding.IsGating` |
| Three bots raise one defect three times | Same file, lines within ±5, same remark → one finding (`FindingDedup.LineSlack = 5`) |
| A whole repository in the prompt | Diff only: **more** useful defects at two to three times fewer input tokens |

### Block 3 — What no competitor does

CodeRabbit, Greptile and Qodo all review a **pull request** — after the code exists. Three things
here are different, and they are the reason to read on:

1. **The plan is gated before the code is written.** `review_code` is refused until a plan round
   reached `proceed`: skipping a stage is impossible rather than discouraged.
2. **It lives in the agent's loop, not in GitHub.** Claude Code, Cursor and Codex call it themselves,
   before a commit exists.
3. **It records the blind spots.** Every finding, and every accept and reject with its reason, in a
   local SQLite database — by category, by role, by vendor. Nobody else hands you the shape of your
   own AI's blind spots.

### Block 4 — Proof, with sample sizes

- **Overlap 5–9 %.** Fourteen judged runs, three vendors, one repeat: the arms almost never name the
  same defect. That is the answer to "why pay for a second vendor", and it is a stronger answer than
  "we find more".
- **Fast mode.** Gemini 4 → 8, GPT-5.6-Luna 6 → 10, Claude Sonnet 6 → 7 useful findings, at 266k
  against 610k input tokens — **one commit, three hosted models, 19 findings**, said in the same
  breath, with the raw data linked.
- **Local models earn their place per stage**: 19 % useful on a plan, 3 % on code. A number that
  changes a setting is worth more than a number that flatters the tool.

### Block 5 — Quickstart, four steps

1. `code --install-extension remsoftdev.connect-other-ais`
2. Command Palette → **ConnectOtherAIs: Install the MCP Server…** (the native AOT binary for this
   platform, into the extension's own storage)
3. Paste the config block into `~/.claude.json`, `.mcp.json` or `.vscode/mcp.json`
4. Command Palette → **Copy the CLAUDE.md snippet** → paste it into your agent's instruction file

### Block 6 — Deep dive

One bold link: *"Want the benchmarks, the prompt-shape measurements, and why a diff beats a whole
checkout? Read the architecture and the research."* → `ARCHITECTURE.md`, with `research/` for the raw
campaigns.

## Build order

1. `git mv README.md ARCHITECTURE.md`, with a one-paragraph header saying what it is and linking back.
2. Write the new `README.md` to the structure above.
3. Take the screenshot: a real rounds page with two vendors' findings and the accept/reject split.
   Redact repository names if needed — a fabricated screenshot is not an option.
4. Verify every claim: the Marketplace id and listing, the two code citations, both measurements and
   their sample sizes, and the zero-ports wording against the team-server plan.
5. Sweep inbound links: every doc that points at `README.md` must still resolve, and fix the ones
   that do not.

## Test plan

- `plan-lifecycle` and the docs checks stay green — this plan, and the moved file's links.
- A link check over `README.md` and `ARCHITECTURE.md`: no dead relative link in either direction.
- Read the first screen cold, out loud, in five seconds: pain, differentiator, install. If one of the
  three is missing, the block order is wrong.

## Definition of Done

- [ ] `README.md` is the shop window and passes the five-second test.
- [ ] Every number carries its sample size; every code claim carries its file.
- [ ] The install command is the verified Marketplace id.
- [ ] `ARCHITECTURE.md` holds the full engineering text and the README links to it.
- [ ] No dead links either way; docs checks green.

## Open questions for the person

1. **Is the extension on the Marketplace right now?** The release workflow publishes only when
   `VSCE_PAT` exists. If it is not published, step 1 of the quickstart has to be the `.vsix` from the
   releases page instead — which changes the first impression, so it is worth knowing before writing.
2. **A GIF or a still?** A GIF of a round completing sells better and costs a recording; a still of
   the rounds page is honest and cheap. I would ship the still now and add the GIF later.
3. **Name the vendors by name?** "DeepSeek" reads well and is only true through an OpenAI-compatible
   endpoint. I plan to write "any OpenAI-compatible endpoint (DeepSeek, vLLM, Ollama)" unless you
   want the bolder wording.
