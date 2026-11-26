#!/bin/bash
# usage: realsnap.sh <tag>   -> out/real_<tag>_{codex,agy}.tsv
S=${E3S4_SCRATCH:?set E3S4_SCRATCH to your scratch folder}
H=$HOME
node $S/tools/manifest.mjs $S/out/real_$1_codex.tsv "$H/.codex/auth.json,$H/.codex/config.toml,$H/.codex/installation_id,$H/.codex/history.jsonl" $H/.codex
node $S/tools/manifest.mjs $S/out/real_$1_agy.tsv "$H/.gemini/antigravity-cli/antigravity-oauth-token,$H/.gemini/jetski-standalone-oauth-token,$H/.gemini/antigravity-cli/settings.json,$H/.gemini/antigravity-cli/installation_id,$H/.gemini/config/config.json,$H/.gemini/antigravity-cli/cache/onboarding.json" $H/.gemini $H/.config/antigravity $H/.cache/antigravity
