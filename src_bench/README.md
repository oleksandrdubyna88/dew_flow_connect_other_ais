# coai-bench — the measuring instrument, kept

Every run this project performs had been written by hand at least twice as a throwaway script: all
models three times each, one model alone, the same model local against hosted, five windows at once,
plans only, diffs only, both. The rewriting was not the cost — each rewrite measured something
slightly different from the last, and two campaigns could not be compared.

It drives the **published** `coai-mcp` over stdio, one server process per run. That is not tidiness:
five windows is five processes, and the failures worth measuring here — a shared data directory, a
lock held across processes, one GPU behind them all — do not exist inside a single one.

## Where it runs: THIS checkout

`--repo .` — the repository you are standing in, and nothing else is needed. The bench only ever
writes refs (`git branch -f bench/<arm>/<case>-r<n>`); the server makes its own worktree, detached
at a SHA, so no branch of yours is touched and no file in your tree moves. Afterwards:

```bash
git branch --list 'bench/*' | xargs -r git branch -D    # the refs a campaign leaves behind
```

The campaigns of 2026-09 ran from a temporary clone instead, and the reason was a defect rather than
a preference: one corpus case pointed at `artifacts/bench/plan-B.md`, and `artifacts/` is
git-ignored — so the file existed only in the folder where it had been written, and a fresh clone
could not run that case at all. Case texts live in `src_bench/cases/` now, which is what makes the
corpus portable.

## The runs it exists for

```bash
EXE="$APPDATA/Code/User/globalStorage/remsoftdev.connect-other-ais/coai-mcp.exe"
BENCH=./src_bench/CoaiBench/bin/Release/net10.0/coai-bench

# every vendor, three times each — the arms run in PARALLEL, one lane per arm
$BENCH run --exe "$EXE" --repo . --corpus src_bench/corpus.json \
      --arm codex --arm gemini --arm local --repeat 3

# one model on its own
$BENCH run --exe "$EXE" --repo . --corpus src_bench/corpus.json \
      --arm codex --model codex=gpt-5.6-sol

# the same job local against hosted
$BENCH run --exe "$EXE" --repo . --corpus src_bench/corpus.json \
      --arm local --arm codex --model local=Qwen3.5-35B-A3B-Q5_vk128:latest --model codex=gpt-5.6-sol

# five windows at once, sharing one data directory and one GPU
$BENCH run --exe "$EXE" --repo . --corpus src_bench/corpus.json --arm codex,gemini,local --parallel 5

# plans only / diffs only / both (both is the default)
$BENCH run … --stages plans
$BENCH run … --stages diffs

# a settings combination, handed to every server
$BENCH run … --set COAI_MAX_CONCURRENCY=9 --set COAI_SPLIT_PLAN=true
```

**A comma inside an arm is a SET, not a list of arms.** `--arm codex,gemini,local` is one round with
three vendors fanning out inside it; three separate `--arm` flags are three rounds compared against
each other. They are different measurements and the flag says which you meant.

**An id is not a vendor — the runtime and the model are.** An arm names IDS, and every one of them
is looked up in the settings file the panel writes (`--vendors-from` points elsewhere). The runtime,
the model, the base url and the executable come from there. An id nobody configured is refused by
name, with the list of what there is to choose from.

This is not a nicety. The first campaign passed bare ids, and the server did exactly as told: it
built a vendor called `gemini` on the RETIRED Gemini CLI — while the operator had configured that
same vendor to run `antigravity` days earlier — and a local vendor with no model at all. Six of nine
reviewers failed and the report blamed the release. A bench that rebuilds vendors from names is
measuring a machine nobody has.

**It runs against the INSTALLED server and writes to the REAL data directory.** `--exe` defaults to
the binary the panel spawns, and the rounds appear in the panel's *Recent rounds* while they happen —
which is where a person watches a campaign, and what a window actually does. `--isolate` gives each
run a directory of its own, for comparing two configurations that must not see each other.

**Every setting comes from the panel too** — thresholds, rounds per role, prompts, the exhausted
policy — with `--set` on top, and the effective set is printed before the first round and saved as
`settings.md` beside the runs.

**Nothing runs that was not named.** There is no default vendor and no default model, because a
default spends somebody's quota on a guess.

## What it records, and what it refuses to decide

`artifacts/bench/<stamp>/runs.json` holds every finding **whole** — text, file, severity, who raised
it — not a count. A bench that stores its own summary can only answer the question somebody thought
of first, and the one measurement that mattered most in this repository was rescued precisely because
the raw answers were still there when the metric turned out to be wrong.

It says nothing about which findings were **worth having**, and that is deliberate. Counting findings
ranks noise above insight: read one at a time against the code they name, the ranking by count
inverted outright ([RESULTS_findings_that_are_worth_something.md](../research/RESULTS_findings_that_are_worth_something.md)).
So worth is a second pass, over data already on disk — a change of mind about it costs a judgement,
not another evening of rounds.

```bash
# Fable reads each finding and the file it names, and says whether it was worth having
$BENCH judge --runs artifacts/bench/<stamp>/runs.json --repo .

# the tables again, from what is already recorded
$BENCH table --runs artifacts/bench/<stamp>/runs.json
```

An unjudged run prints `—` in the *useful* column rather than a zero. Zero is a measurement; nobody
having looked is not.

## Reading the tables

Medians, not means: one reviewer that hit a rate limit and took nine minutes moves a mean and says
nothing about the ordinary case. A failed run is **named** in the verdict column rather than averaged
away — three runs where one produced nothing is not "mostly fine".

## Running the tests

xUnit v3 on Microsoft Testing Platform, as everywhere in this repository — the executable, never
`dotnet test`:

```bash
dotnet build src_bench/CoaiBench.Tests/CoaiBench.Tests.csproj
./src_bench/CoaiBench.Tests/bin/Debug/net10.0/CoaiBench.Tests.exe
```
