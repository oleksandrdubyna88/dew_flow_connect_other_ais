# The commands campaign — re-running it

What produced [`research/RESULTS_commands_campaign.md`](../../research/RESULTS_commands_campaign.md):
does an AI reading the gate's orders do anything differently, and does a piece of a split stay a
piece.

Three arms over the same real plans — no orders, the first-round orders, the you-are-a-piece order —
through the product's own `--ask-local` shim, strictly sequential on one GPU.

```bash
# 1. the command text, written by a TEST that calls GateCommands, never retyped here
dotnet build src_mcp/tests/CoaiMcp.Tests.csproj
COAI_WRITE_FIXTURES="$PWD/artifacts/commands" COAI_REPO_DIR="$PWD" \
  ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*CommandFixtures"

# 2. one run per model. NEVER two at once — one card, one caller
node tools/campaign/campaign.mjs \
  "$PWD/src_mcp/tests/bin/Debug/net10.0/coai-mcp.exe" \
  "Qwen3.5-35B-A3B-Q5_vk128:latest" "$PWD" A \
  PLAN_multi_repo_and_uncommitted PLAN_connect_other_ais PLAN_wsl_local_engine …

# 3. the tables
node tools/campaign/rescore.mjs "$PWD"   # only when a metric changes; answers are kept whole
node tools/campaign/report.mjs "$PWD"
```

**Write the predictions down before step 2.** `research/data/commands_campaign/predictions.md` is the
example: an expectation recorded after the numbers arrive measures nothing.

**`rescore.mjs` exists because a metric was wrong once.** The first pass looked for "at the end" while
the models write "deferred to the end of the summary", and read a working feature as broken. Every
answer is stored whole in the run files precisely so that costs a rescore rather than an hour of GPU.
