# e3s4 measure harness (epoch 3 E3 s4)

Scripts that produced `v2/spikes/RESULTS_codex_coder.md` and `v2/spikes/RESULTS_agy_coder.md`. Measurement tools, not product code. No Python.
Set `E3S4_SCRATCH` (a private 0700 scratch folder; the sign-in COPIES live under `$E3S4_SCRATCH/copies/{codex,agy}`; delete them when done) and optionally
`E3S4_FIXTURES` (default `$HOME/coai-e3s4-fx`, fixtures only, no credential). Never print or commit a token: `authmeta.mjs` / `agyfp.mjs` print key names,
lengths, times and 8-hex fingerprints only. See the "Procedure" section of each results file for the order.
