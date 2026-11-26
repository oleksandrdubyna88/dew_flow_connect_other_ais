# e3s5 measure harness (epoch 3 E3 s5 t6, codex; s6 t7 stopped at d1)

Scripts and raw material that produced section 9 of `v2/spikes/RESULTS_codex_coder.md` and section 9 of `v2/spikes/RESULTS_agy_coder.md`. Measurement tools, not product code. No Python.
They reuse `../e3s4_measure/` (copy its scripts and these into `$E3S4_SCRATCH/tools/`; `E3S4_SCRATCH` and `E3S4_FIXTURES` point at private folders under `$HOME`, NOT under `/tmp`, because a codex
home under `/tmp` changes what codex makes). The sign-in is always a COPY in the scratch folder; the real file's sha256 and mtime are taken before and compared after (never printed); copies are deleted at the end.

- `PREDICTIONS.md` - written before the first paid call.
- `cx0.sh` - a model-free codex call with a scratch HOME and the scratch copy; `tools_probe.sh` - `codex debug prompt-input` with the CX1 flags and extra switches, tool names only.
- `cxrun5.sh` - ONE paid call in the unit (CX1 flags including `web_search="disabled"`), ledger line with tokens, tree diff; `CX_SAMPLE=1` samples the unit's processes (`cgsample.sh`), `arg0watch.sh` lists the helper folder.
- `latefiles.sh` - `.codex/config.toml`, hooks and rules written into the worktree after turn 0. `craft.mjs` - crafted copies of the sign-in (last_refresh 9 days; exp in 60 s; placeholder refresh token), from the scratch copy only.
- `render.sh`, `prompts/` - the prompts as sent; `canaries.sh`, `leakcheck.sh` - canary hashes and token-text hits (counts only); `doc_summary.mjs` - the sandbox facts of `codex doctor --json`.
- `out_codex/` - the ledger `calls.tsv`, the streams and stderr of every call, process samples, doctor reports, the JavaScript of d2 and its output, rollout call names. Paths are written `/home/<user>`.
The fixture `src_mcp/tests/fixtures/role-stops/codex-refresh-failed.json` was made with `../e3s4_measure/mkfixture.mjs`.
