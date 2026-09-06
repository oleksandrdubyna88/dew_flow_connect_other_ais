# Bench

## Per arm

| arm | stage | runs | verdicts | median time | median findings | gating | useful | tokens in / out | cost |
|---|---|---|---|---|---|---|---|---|---|
| `codex,gemini,local` | code | 4 | proceed | 475.4s | 13 | 27 | 10/41 | 1.6M / 147.1k | not reported |
| `codex,gemini,local` | plan-1 | 4 | good_enough | 39.2s | 13 | 44 | 29/51 | 177.1k / 21.3k | not reported |

## Per run

| arm | case | # | lane | stage | verdict | time | findings | gating | tokens in / out |
|---|---|---|---|---|---|---|---|---|---|
| `codex,gemini,local` | rounds-collapse | 1 | 1 | plan-1 | good_enough | 39.2s | 13 | 10 | 38.3k / 5.7k |
| `codex,gemini,local` | rounds-collapse | 1 | 1 | code | proceed | 505.9s | 14 | 8 | 489.1k / 40.9k |
| `codex,gemini,local` | rounds-collapse | 2 | 1 | plan-1 | good_enough | 54.3s | 15 | 12 | 40.7k / 5.6k |
| `codex,gemini,local` | rounds-collapse | 2 | 1 | code | proceed | 475.4s | 11 | 7 | 515.2k / 44.7k |
| `codex,gemini,local` | split-once | 1 | 1 | plan-1 | good_enough | 25s | 13 | 10 | 50.7k / 6k |
| `codex,gemini,local` | split-once | 1 | 1 | code | proceed | 386.5s | 12 | 6 | 296.8k / 31.2k |
| `codex,gemini,local` | split-once | 2 | 1 | plan-1 | good_enough | 33.2s | 13 | 12 | 47.6k / 3.9k |
| `codex,gemini,local` | split-once | 2 | 1 | code | proceed | 309.3s | 13 | 6 | 282.8k / 30.3k |
