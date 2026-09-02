# RECORD — 2026-09-02: what a day of measuring found

> One day, sixteen commits, four releases, and roughly 190 review cells across eleven campaigns.
> This is the consolidated record: what was run, what it found IN THE PRODUCT, and — the section
> that usually goes unwritten — what it found wrong in the MEASUREMENTS themselves.
>
> Each campaign has its own document; this one exists so the defects are in one place and so the
> measurement errors are recorded as carefully as the results, because four of them would have
> shipped a false number.

## 1. The campaigns

| campaign | cells | what it answered | document |
|---|---|---|---|
| Focused prompts, phase A | 72 | which wording of twelve narrow lenses — and that the SHAPE matters more | [RESULTS_focused_prompts.md](RESULTS_focused_prompts.md) |
| Focused vs universal, phase B | 32 | whether a lens finds what the broad prompt misses | [RESULTS_focused_vs_universal.md](RESULTS_focused_vs_universal.md) |
| Local models on the planted plan | 8 | a local model against thirteen hosted ones | [RESULTS_model_comparison.md](RESULTS_model_comparison.md) |
| Local models at 128k, plan + code | 8 | both local models, both stages | [RESULTS_local_models_128k.md](RESULTS_local_models_128k.md) |
| Five real plans × five models | 25 + 25 | agreement on real work, on the operator's own settings | [RESULTS_five_plans_five_models.md](RESULTS_five_plans_five_models.md) |
| Native Claude, plan + code | 10 | Claude run natively, never through another vendor's CLI | [RESULTS_model_comparison.md](RESULTS_model_comparison.md) |

Plus the engine probes: reasoning-effort behaviour, context ceilings, response shapes, prompt
token counts.

## 2. Defects found in the product, and fixed

Eight, all with a test that was watched fail. They fall into two families, and the families are the
interesting part.

### Family one: one decision written down in more than one place

Four defects, all the same shape — a set of names maintained by hand in two places, and one place
never updated.

| # | defect | how it showed | fixed in |
|---|---|---|---|
| 1 | The extension's `RUNTIMES` list never got `local` | every saved local reviewer came back as a CODEX reviewer: the row kept its name, listed GPT-5.6, offered codex's buttons | 0.25.0 |
| 2 | The server's `RuntimeOf` list never got `local` either | a local vendor parsed as codex-with-a-base-URL | 0.11.1 |
| 3 | The auth check was the third reader of those two fields and had never been told about `local` | it concluded a local engine needed a vault key, answered `unavailable`, and `BuildWork` dropped it — **the round opened with `0 reviewer(s)` while `providers` reported the vendor healthy** | 0.11.1 |
| 4 | Built-in runtimes hard-coded `Provider` | two rows on one runtime (`claude` + `my-claude`, or `codex` + a mis-parsed `local`) collided on the round's provider/role key and killed the round; a lone `my-claude` filed its usage under `claude` | 0.11.2 |

**The lesson these four teach together is not "check the other list".** Number 1 was fixed in the
morning with a comment saying the two must be kept in step, and number 2 — the identical defect on
the server — was found hours later because nobody went looking for a second copy. Both sides now
DERIVE the set from one declaration: the extension's type from its array, the server's parser from
`ReviewerRuntimeSelector.RuntimeNames`. There is nothing left to keep in step.

Number 3 is the worst of the four and deserves its own line: **the panel said the reviewer was fine
and every round silently ran without it.** A configuration that is reported healthy and does nothing
is worse than one that refuses.

### Family two: a promise the code did not keep

| # | defect | how it showed | fixed in |
|---|---|---|---|
| 5 | Settings were mirrored to the server from inside a webview | `PanelProvider.render()` returned early with no view, and the `onDidChangeConfiguration` subscription lived in `resolveWebviewView` — which VS Code calls LAZILY. In a window where nobody opened the panel, nothing watched the settings and nothing wrote the file | 0.24.0 |
| 6 | `call_human` did not stop anything | `BeginPlanRound`/`BeginCodeRound` never consulted the round budget — it was read only at COMPLETION, to choose a verdict — and `Resolve` cleared `HumanGate` unconditionally. So the loop was: round, `call_human`, resolve, round, forever | 0.11.0 |
| 7 | A reviewer killed by its timeout is killed by its PARENT | when `coai-mcp` itself dies, its in-flight reviewers are orphaned with nothing left to stop them. An Antigravity child started at 00:03 was alive at 10:00, its vendor removed from the configuration | 0.11.0 |
| 8 | A local reasoning model was never told not to think | Gemma answered once in 171 s and once spent 1056 s filling 64k with 110 000 characters of `reasoning`, returning an empty `content` | 0.11.2 |

**Number 6 is the expensive one.** On a colleague's machine a stage reached round TEN on a
three-round budget, and the AI running it judged its own work: rounds 1–3 found real defects, 4–9
chased *"progressively narrower crash windows"*, and round 10 **introduced a bug**. A gate that asks
for a person and then lets the AI carry on is not a gate.

**Number 8's answer already existed in another repository.** `dew_flow_rag_qln` had measured it three
weeks earlier against the same model family (`AiRuntimeOptions.ReasoningEffort`, 2026-08-11): on
Ollama's OpenAI route `think:false` and `chat_template_kwargs` are ignored and `"low"` still burns
the budget — only `"none"` answers. Finding it there cost one search; not finding it cost two failed
campaigns.

## 3. Defects found and NOT fixed

### The gate cannot see its own strongest signal on a plan round

`FindingDedup.SameDefect` merges two reviewers' findings when category, file and line match and the
titles are similar. A PLAN finding has no file and no line, so it falls to Jaccard ≥ 0.5 on titles
alone.

Measured, on five real plans across five models: **32 distinct defects, 31 raised by two models or
more when read by a person — and the rule detects essentially none of them.** So on a plan round the
same defect from two vendors counts TWICE against the threshold, and the "two vendors agreed" signal
the product calls its strongest never appears. Seen directly the same day: codex, gemini and a local
model each raised the delete-destroys-history defect, in three separate entries with one provider
each.

The code already records this lesson for the ANCHORED case, from the real run of 2026-08-31 — three
reviewers wording one path-traversal defect 0.43 apart. The fix made then lowered the bar only when
file, line and category agree. Plan findings have none of those and were left on the strict rule.

Not fixed here because it changes what passes a gate, which is the operator's call. The measurement
is the bar a fix has to clear.

### The pasted config block can be silently outranked

Claude Code reads `~/.claude.json` at two levels and a per-project `projects["…"].mcpServers` entry
wins over the top-level one, silently. Cost on this machine: a session ran an entire day against a
server binary from the previous evening while the extension had already installed the current one.
The paste instruction now names the shadowing (0.25.0), but nothing detects it.

## 4. What the measurements got wrong — four errors that would have shipped a false number

This section is the reason this document exists. Every one of these produced a plausible result that
was wrong, and each was caught by a different mechanism.

### 4.1 Five cells measured the conventions pass instead of the prompt under test

Round 1 of a code role IS the conventions pass when the checkout has written rules, and this one
does. The phase-B campaign's first five cells therefore measured `conventions` while believing they
measured `architecture`. Caught by reading the server's own log line — `codex/Architecture[conventions]`
— not by anything in the output, which looked entirely normal.

**This is the second time.** The hosted code campaign lost two whole runs to the identical mistake
and wrote it down; the note did not prevent the repeat, and the harness now pins round 1 explicitly.

### 4.2 The agreement matcher said 0–5 % where a reader says 55–76 %

A Jaccard-0.5 title comparison across five models on five plans found almost no agreement. Reading
the same findings found 31 of 32 defects raised by two models or more. Had the number been reported
as measured, the campaign's headline would have been "the models agree on nothing" — the opposite of
the truth, and it would have been believed because it came from code.

Caught by the discipline this repository already had written down: the hosted campaign threw away its
keyword scorer for crediting one finding as two defects. **The same rule is in the product**, which
is how §3's defect was found — the error in the measurement and the defect in the product were the
same rule.

### 4.3 A ground-truth matcher over-credited by keyword

In phase B, "endpoint credentials are exposed through process arguments" was scored as the
missing-key-path defect, and "very short reviewer timeouts erase the terminal error" as the
streaming-cancellation one. Both real findings; neither is the defect it was credited to. By keyword
each arm scored 3 of 4; by reading, universal found 1 and focused 2, **and two defects provably still
in that code were missed by twenty-two reviewer rounds.** That last sentence is the useful one and
the keyword pass had hidden it.

### 4.4 A hang that was not a hang, and a diagnosis stated too early

A matrix cell appeared to have run for two hours against a ten-minute timeout. It had run for two
minutes: the session records `startedUtc` and it was compared against local time, two hours ahead.
The word "hung" was said out loud before the clocks were checked.

The same day, a genuine hang WAS diagnosed — three concurrent reviewers against one local engine at
`num_ctx 131072` with `OLLAMA_NUM_PARALLEL=4`, where the engine reserves KV cache per slot and spills
to host RAM. The conclusion drawn from it — "a local vendor must be capped at one reviewer" — then
failed to reproduce under the operator's own limits, where all ten local cells passed at 3 per
vendor. **The mechanism is real and the rule drawn from it was too broad**; what actually varies is
headroom, and Qwen at 26.4 GB resident has less of it than Gemma at 22.3 GB on a 32 GB card.

## 5. What the local models turned out to be worth

Two models on this machine, at 128k, told not to think, on the same subjects as the hosted thirteen:

- **Plan, planted defects:** Qwen 5 and 5 of eight, Gemma 4 and 5 — between Gemini 3.7 Flash and
  GPT-5.5, and below Opus and Sonnet at 7. Both miss the same three, which are the three that need
  the plan imagined in motion rather than read.
- **Plan, five real plans:** Qwen 28 findings, Gemma 21 — bracketing Gemini Flash at 21, at a quarter
  of its wall clock and a fifth of its input tokens.
- **Code:** nine findings each, both runs, faster than most hosted models.
- **Agreement with the other four models:** Gemma 76 % (highest in the set), Qwen 64 %.
- **Reproducibility:** Qwen's two runs on one plan were byte-identical. Gemma's were not.

They cost nothing per token and hold a card for the duration. The honest recommendation is a local
model as the second or third reviewer, where its disagreement with a hosted one is the signal.

## 6. Still open

- The dedup rule on plan rounds (§3), which needs an operator decision.
- `COAI_LOCAL_REASONING_EFFORT` is env-only; the panel does not expose it.
- [PLAN_multi_repo_and_uncommitted.md](../todo/PLAN_multi_repo_and_uncommitted.md) — three gate
  rounds, 31 findings accepted, verdict `call_human`: the design survived, the size is the question.
- [PLAN_local_trust_and_vllm.md](../todo/PLAN_local_trust_and_vllm.md) — §1, §2, §4 remain; §5 was
  resolved the day it was written, by the answer another repository had already found.
- A fair hosted-vs-local comparison: the hosted CLIs explore the repository and spend ~200k tokens
  doing it, while a local reviewer is handed one prompt of ~25k. Measuring them on the SAME input
  means giving the hosted ones no checkout — which the repair launch already does deliberately.
