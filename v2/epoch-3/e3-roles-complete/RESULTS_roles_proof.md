# Epic e3-roles-complete, story s8 — the paid proofs: t3 (the real scout job and the real judge job, claude haiku)

This file holds numbers, ids, hashes and command results only. It quotes no key, no token, no environment and no file content of the key folder. t4 (the coder resume proof) appends its own section below and starts from the budget that t3 left.

## t3 — the money rules, written BEFORE the first call (2026-11-27)

### Price table (d3: for each model that will run)

The only model that runs is the one the alias `haiku` names in a claude route (`claude/haiku`).

| Model | Input USD per MTok | Output USD per MTok | Context window | Largest output | Source | Date |
|---|---|---|---|---|---|---|
| claude-haiku-5-5, prompt up to 100,000 tokens | 0.10 | 0.50 | 1M tokens | 128K tokens | https://platform.claude.com/docs/en/about-claude/pricing (prices); https://platform.claude.com/docs/en/models/overview (window, output) | 2026-11-27 |
| claude-haiku-5-5, prompt over 100,000 tokens | 0.50 | 2.50 | 1M tokens | 128K tokens | the same two pages | 2026-11-27 |
| claude-haiku-4-5 (kept as a second row: the dearest of the older Haiku) | 1.00 | 5.00 | 200K tokens | 64K tokens | the same two pages | 2026-11-27 |

Cache prices (read 0.01 / write 0.125 for the first row) are not used: the formula of the reservation prices every input token at the base input price, as the task says.

Which model the alias runs: the session files of the earlier job `r20261126012559-2183e184` (route `claude/haiku`, claude CLI 2.1.332) hold 10 responses with `"model":"claude-haiku-5-5"`. So the alias ran Haiku 5.5 that day. The reservation does not depend on this: it takes the dearest row of the table for each turn.

### Budget

Budget left before the first call: **30.00 USD** (the owner's cap of 2026-11-26 for t3 and t4 together). The coordinator's word of 2026-11-27: t3 may RESERVE up to 5 USD of it; the real spend should stay far below.

### The reservation of one call (d3)

The claude CLI has no flag that bounds the tokens of a call, and coai passes none (searched `max-budget`, `max-turns`, `max_output` in `src_mcp`: only the vibe runner has one). So the bound is the context window and the largest output for EVERY turn the job may make. A scout or judge job on a list of one model makes turn 0 and at most 2 repair turns (`ReadOnlyRoleWork.RepairTurns` = 2), so 3 turns.

- One turn at claude-haiku-5-5 over 100k: 1,000,000 x 0.50 / 1e6 + 128,000 x 2.50 / 1e6 = 0.50 + 0.32 = 0.82 USD.
- One turn at claude-haiku-4-5: 200,000 x 1.00 / 1e6 + 64,000 x 5.00 / 1e6 = 0.20 + 0.32 = 0.52 USD.
- The reservation takes the dearest: **0.82 USD per turn x 3 turns = 2.46 USD per call.**
- Two calls (one scout, one judge): 4.92 USD reserved, under the 5 USD of t3.
- A call whose reservation is more than the budget left is not launched; a call whose bound or price cannot be named is not launched. The reservation is made by the harness before `start_role` (no server is started when it refuses). Budget left after a call = budget before it - the actual cost of the call from the ledger.
- Limit of the bound, said plainly: one claude turn can make several model requests inside the CLI (it reads files with its tools); each request can carry up to the window. The formula bounds a turn as ONE request, as the task asks. As a second stop the harness reads the ledger while the job runs and cancels the job when the ledger sum reaches the reservation. That is a guard, not the cap.

### Dry run below one reservation (d3)

`node driver.mjs dry 1.0`: budget left 1.00 USD, reservation 2.46 USD, `launched` false, refusal "reservation 2.4600 USD is more than the budget left 1.0000 USD", server started false, job folders in the data folder 3 before and 3 after. The same through the run road (`driver.mjs run scout 1.0 …`): "REFUSED … (no job started)", exit 3, job folders still 3. At 2.4599 USD it refuses; at 30 USD it would launch.

### What was run, and how

- Binary: published native-AOT build of head ae855982 (branch `feat/v2-ep3-e3-roles`, no edit of mine in it), `/home/jinx/coai-t3/scratch-s8t3/bin/coai-mcp`, built under the shared lock; `--version` says `0.0.0` (not stamped).
- Repository of the jobs: a scratch clone of this repository at ae855982 (`/home/jinx/coai-t3/scratch-s8t3/clone`, outside /tmp). Nobody edited it while a job ran.
- Data folder: `/home/jinx/coai-t3/data` (the folder whose pool the E2 proof used; the key folder was not opened, listed or copied, and no `--keys` verb was run). Its `settings.json` was copied to a backup first (sha256 beginning `5ed01e97ad8c62a4`); for the run the lists `scout` and `important` were set to `claude/haiku` only and `COAI_FIX_ROUNDS` to 0 (it belongs to the coder; the scout and judge repair cap is a constant of 2). The original is restored byte for byte at the end.
- The server got a small environment (PATH, a temp HOME inside the scratch folder, the user bus, the data folder); nothing of any environment was printed. Person sign-in switch: not set, so the jobs run on a pool key, never on the person's sign-in file.

## t3 — the two real jobs (2026-11-27, 14:49 UTC)

**Short answer.** One scout job and one judge job, both route `claude/haiku`, both `succeeded` on the first turn (0 repair turns), every piece of evidence verified. Real spend of t3: **0.171380 USD** (ledger). Budget left for t4: **29.828620 USD**. One finding the owner must see: the session files of both jobs say the model that answered was `claude-sonnet-5-5`, NOT haiku (see "The alias `haiku` ran Sonnet 5.5", below). The reservation was priced at Haiku, so at the price of the model that really ran the reservation would have been 9.84 USD per call and the calls would NOT have been launched under d3.

### The calls

| | Scout | Judge |
|---|---|---|
| Job id | r20261127144904-7c875b05 | r20261127144943-039737ec |
| Route (as answered by `start_role`) | `claude/haiku` | `claude/haiku` |
| Brief | which source file chooses the models a role is routed to, and where the scout default list is written | is it sound that the scout list and the important list are two lists |
| State, stop | succeeded, no stop | succeeded, no stop |
| Attempts | 2: (1) key k-48f7cfb0c731, stop `limit`, 4.5 s, 0 tokens, cost null (the vendor's spend-limit answer, nothing billed); (2) key k-2b88f1669d85, no stop | 1: key k-2b88f1669d85, no stop |
| Repair turns | 0 | 0 |
| Envelope | status ok, commit ae8559824046f0ca792a09eb4152abb98742107e (the clone's head) | status ok, verdict `approve`, same commit |
| Counts of verified evidence | facts 4, evidence 9, verified 9, filesMissing 0 (4 files named, all exist) | findings 1 (f1, nit), evidence 2, verified 2 |
| Ledger line of the paying attempt | 19.1 s, tokens in 92,837 (cached 76,552), out 2,467, **0.0974412 USD**, key k-2b88f1669d85 | 12.1 s, tokens in 55,793 (cached 41,589), out 1,298, **0.0739389 USD**, key k-2b88f1669d85 |
| Wall time of the job | 25 s | 13 s |
| Reservation (before the call) | 2.46 USD (3 turns x 0.82) | 2.46 USD |
| Budget before the call | 30.000000 USD | 29.902559 USD |
| Actual cost from the ledger | 0.097441 USD | 0.073939 USD |
| Budget left after the call (before minus actual) | 29.902559 USD | **29.828620 USD** |

The reservations are not added to the spend: a reservation is released when the call ends. The total of both reservations, 4.92 USD, is under the 5 USD the coordinator gave t3.

The first attempt of the scout ended on a limit: the pool key k-48f7cfb0c731 answered the vendor's spend limit (the same account limit as in the E2 proof), the product switched to the next key and ran the same model again. That is the pool's key switch working; it cost nothing.

### Spend of t3 and what is left for t4 (d2)

- Spend of t3 (sum of the ledger lines of the two jobs): 0.0974412 + 0.0739389 = **0.171380 USD**. The first scout attempt's line has cost null (0 tokens).
- Left of the 30 USD for t4: **29.828620 USD**.
- No role was run a second time. No file of the scratch clone was changed while a job ran. The dry run (above) and the refusal road were run before the first call.

### The alias `haiku` ran Sonnet 5.5 (found after the calls, from the session files)

- The session files of the attempts (`attempts/<n>/claude-home/.claude/projects/…jsonl` of each job) hold 21 assistant responses (scout) and 12 (judge), ALL with `"model":"claude-sonnet-5-5"`; no response names a Haiku. The claude CLI was launched with `--model haiku` (the route says `claude/haiku`, the ledger says `haiku`), CLI 2.1.332.
- The earlier job r20261126012559-2183e184 (route `claude/haiku`, same CLI version) has 10 responses with `claude-haiku-5-5` and cost 0.00236 USD for 38,092 tokens in. So the alias answered Haiku on 2026-11-26 and Sonnet on 2026-11-27, on the same CLI version. The cause is not found. The cost agrees with the finding: Sonnet 5.5 prices ($2 / $10 per MTok, cache read 0.10) give about 0.07 and 0.045 USD for the token counts above, Haiku 5.5 would give about 0.01 USD or less.
- What this means for the money rule (d3): the reservation must be priced for the model that RUNS. At claude-sonnet-5-5 one turn is 1,000,000 x 2.00 / 1e6 + 128,000 x 10.00 / 1e6 = 2.00 + 1.28 = 3.28 USD, so a call of 3 turns is 9.84 USD. That is more than the 5 USD the coordinator gave t3 for reservations (both calls: 19.68 USD). The calls were made on the Haiku price and the real cost (0.17 USD) is far below any of these numbers, but the rule was kept only for the model the route NAMED. The next paid call (t4 or a second run here) must either name a model that is shown to answer as itself (the full id `claude-haiku-5-5` is NOT in the list of verified ids of `shared/role-model-names.json` at head ae855982, which holds only the aliases and `claude-haiku-4-5-20251001` for Haiku, so the router would refuse it until it is added), or price the reservation at the Sonnet 5.5 row.
- Not done, and not allowed by d2 without a recorded change: a second run of either role to find out why the alias answered Sonnet.

### Checks after the calls

- `settings.json` of the data folder restored from the backup: sha256 of the restored file and of the backup are **equal** (printed only equal or changed). The data folder holds the 2 new job folders (`roles/jobs/<id>`); they stay under the product's retention.
- The key folder was not opened, listed or copied; no `--keys` verb was run, so no key scan was made (the E2 proof's scans were not repeated). The jobs' own folders hold the key id only.
- The person's sign-in file: not bound, not read (the jobs ran on pool keys).
- Scratch folders (the clone, the build, the temp HOME and the harness output) are deleted after this file is committed.

### The harness (not committed)

A node script (`driver.mjs`, about 100 lines) did the reservation arithmetic before each `start_role` (the two rows of the price table, 3 turns, the budget left given on the command line, the 5 USD cap of t3), refused with exit 3 and started no server when the reservation did not fit, spoke stdio to `coai-mcp` with a small environment, polled `role_status`, and read `usage.jsonl` of the data folder for the actual cost (and as a second stop: a cancel when the ledger sum reached the reservation, which did not fire).
