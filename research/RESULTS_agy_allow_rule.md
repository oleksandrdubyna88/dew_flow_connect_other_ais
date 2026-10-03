# RESULTS — does a headless antigravity honour a `permissions.allow` rule for a shell command?

> Measured 2026-10-02, for [PLAN_the_consultant_works_on_every_vendor.md](PLAN_the_consultant_works_on_every_vendor.md)
> (its P0: the Consultant tab may offer an allow-rule snippet only if one is proven to work).
> Harness: [`scripts/probe-agy-allow-rule.mjs`](../scripts/probe-agy-allow-rule.mjs). Subject: agy **1.2.15**
> on Windows 11 (`%LOCALAPPDATA%\agy\bin\agy.exe`) and in WSL2 Ubuntu (`~/.local/bin/agy`), model
> `gemini-3.1-pro-high`, the consultant's own flags (`--print= --input-format stream-json --output-format
> stream-json --mode plan --add-dir <repo>`).

## Why it was measured

A consultant on antigravity answered nothing twice on 2026-10-02: headless agy auto-denies `run_command`
and ends the turn. Its own stderr says what would allow it — *"Add an allow-rule under permissions.allow in
settings.json (e.g. command(<target>))"* — and the repository had written down only that a WORKSPACE
`permissions.allow` is ignored (agy 1.2.10, [module_runners.md](module_runners.md)). Nobody had tried the
GLOBAL file, or which rule shapes it accepts.

## Method

- A scratch git repository with two files; `deep.txt` holds the marker `QUOKKA-7731`.
- The prompt asks for exactly one tool call: `run_command` with `git grep -n QUOKKA .`, and the file name
  as the only reply. Stdin is the consultant's NDJSON user message.
- Each arm writes ONE `permissions.allow` list into the GLOBAL `~/.gemini/antigravity-cli/settings.json`
  (keeping its other keys), runs one turn, and reads the stream's `result` event (`response`,
  `denied_actions`) and every `run_command` step.
- The settings file is copied aside first and restored after every run; the restore is verified by
  SHA-256 (`restoredOk: true` on every run below).
- **Prediction, written before the run:** no rule → denied and empty; `command(git grep)` → the command
  runs, if the global file is honoured at all.

## Results

| side | rule in `permissions.allow` | runs | outcome |
|---|---|---|---|
| Windows | *(none)* | 1 | **denied**, `response: ""`, `denied_actions: [{"action":"command"}]`, the `jetski: no output produced …` stderr, exit 0 |
| Windows | `command(git grep)` | **2** | **denied** both times — identical to no rule |
| Windows | `command(git)` | 1 | **denied** |
| Windows | `command(git grep -n QUOKKA .)` — the exact command line | 1 | **ran**, `response: "deep.txt"`, no `denied_actions` |
| WSL | *(none)* | 1 | **denied**, the same stderr, exit 0 |
| WSL | `command(git grep)` | **2** | **ran** both times, `response: "deep.txt"` |
| WSL | `command(git grep -n QUOKKA .)` | 1 | **ran** |

(The first attempt, 2026-10-02 17:07 UTC, produced nothing: the account was out of quota —
`AGY_ERROR … RESOURCE_EXHAUSTED (code 429): Individual quota reached … Resets in 33m52s`, exit 3. That
stream is itself useful: it is what the consultant's failure classification must read as `quota`.)

## What it shows, and what it does not

1. **The global settings file IS read by a headless turn** on both sides — an exact rule ran on both.
2. **On Linux (WSL) a prefix rule works**: `command(git grep)` allowed `git grep -n QUOKKA .`, 2 of 2.
3. **On Windows only the exact command line works**: `command(git grep)` was refused 2 of 2 and
   `command(git)` 1 of 1, while the exact line ran. The CLI's own changelog speaks of "strict exact-match
   verification for PowerShell scripts", and agy runs commands through PowerShell on Windows — a plausible
   mechanism, **not** measured here.
4. **One step `state` is not a denial signal.** In one Windows prefix run the `run_command` step reported
   `DONE` while `result.denied_actions` still named the command and the response was empty. Denial is read
   from `denied_actions` (and the stderr line), never from step state.
5. Not settled: other command shapes (pipes, redirections), other agy versions, macOS, and whether a
   prefix rule on Linux constrains arguments at all — `git grep -O<cmd>` runs a program and `git log
   --output=` writes a file, so a prefix rule is **not** a read-only rule.

## Consequence for the product

- **Windows**: an allow-rule snippet would need the exact command the model happens to choose, which
  nobody can know in advance. The Consultant tab offers **no snippet** on Windows; it says that agy
  allows only exact commands there and that coai continues the conversation without them.
- **Linux / WSL**: a prefix snippet works. It may be offered — as something the person pastes themselves,
  never written by coai — and only with the warning in point 5.
