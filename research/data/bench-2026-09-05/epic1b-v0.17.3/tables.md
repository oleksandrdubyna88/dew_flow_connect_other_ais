# Bench

## Per arm

| arm | stage | runs | verdicts | median time | median findings | gating | useful | tokens in / out | cost |
|---|---|---|---|---|---|---|---|---|---|
| `codex,gemini,local` | code | 4 | proceed×3, good_enough×1 | 214.2s | 16 | 37 | — | 1.4M / 79.1k | not reported |
| `codex,gemini,local` | plan-1 | 4 | good_enough | 48.5s | 15 | 46 | — | 158.4k / 21.7k | not reported |

## Per run

| arm | case | # | lane | stage | verdict | time | findings | gating | tokens in / out |
|---|---|---|---|---|---|---|---|---|---|
| `codex,gemini,local` | rounds-collapse | 1 | 1 | plan-1 | good_enough | 48.5s | 15 | 11 | 39k / 7.3k |
| `codex,gemini,local` | rounds-collapse | 1 | 1 | code | proceed | 107.6s | 11 | 7 | 445.2k / 13.7k |
| `codex,gemini,local` | rounds-collapse | 2 | 1 | plan-1 | good_enough | 50.3s | 14 | 10 | 40.6k / 5.1k |
| `codex,gemini,local` | rounds-collapse | 2 | 1 | code | proceed | 214.2s | 11 | 8 | 404.8k / 24.3k |
| `codex,gemini,local` | split-once | 1 | 1 | plan-1 | good_enough | 35.5s | 15 | 13 | 39.4k / 4.4k |
| `codex,gemini,local` | split-once | 1 | 1 | code | proceed | 128.5s | 18 | 10 | 265.6k / 15.3k |
| `codex,gemini,local` | split-once | 2 | 1 | plan-1 | good_enough | 43.6s | 14 | 12 | 39.4k / 4.9k |
| `codex,gemini,local` | split-once | 2 | 1 | code | good_enough | 243.6s | 16 | 12 | 269.4k / 25.7k |
