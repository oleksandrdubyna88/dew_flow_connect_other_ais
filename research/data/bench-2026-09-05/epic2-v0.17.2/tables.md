# Bench

## Per arm

| arm | stage | runs | verdicts | median time | median findings | gating | useful | tokens in / out | cost |
|---|---|---|---|---|---|---|---|---|---|
| `codex,gemini,local` | code | 6 | proceed×3, FAILED×2, good_enough×1 | 124s | 13 | 31 | — | 1.4M / 65k | not reported |
| `codex,gemini,local` | plan-1 | 6 | good_enough | 65.8s | 14 | 65 | — | 239.7k / 38.8k | not reported |

## Per run

| arm | case | # | lane | stage | verdict | time | findings | gating | tokens in / out |
|---|---|---|---|---|---|---|---|---|---|
| `codex,gemini,local` | rounds-collapse | 1 | 1 | plan-1 | good_enough | 82.7s | 14 | 10 | 39.3k / 7.5k |
| `codex,gemini,local` | rounds-collapse | 1 | 1 | code | proceed | 189.9s | 13 | 9 | 432.5k / 10.9k |
| `codex,gemini,local` | rounds-collapse | 2 | 2 | plan-1 | good_enough | 99.6s | 14 | 11 | 39k / 7.5k |
| `codex,gemini,local` | rounds-collapse | 2 | 2 | code | **FAILED** | 0s | 0 | 0 | 0 / 0 |
| `codex,gemini,local` | rounds-collapse | 3 | 3 | plan-1 | good_enough | 49s | 13 | 9 | 38k / 6.4k |
| `codex,gemini,local` | rounds-collapse | 3 | 3 | code | proceed | 156.9s | 13 | 8 | 399k / 17.1k |
| `codex,gemini,local` | split-once | 1 | 5 | plan-1 | good_enough | 65.8s | 13 | 12 | 47.7k / 4.3k |
| `codex,gemini,local` | split-once | 1 | 5 | code | **FAILED** | 0s | 0 | 0 | 0 / 0 |
| `codex,gemini,local` | split-once | 2 | 4 | plan-1 | good_enough | 46.9s | 14 | 12 | 42k / 6.9k |
| `codex,gemini,local` | split-once | 2 | 4 | code | proceed | 114.8s | 14 | 7 | 278.4k / 18.3k |
| `codex,gemini,local` | split-once | 3 | 5 | plan-1 | good_enough | 49.6s | 12 | 11 | 33.7k / 6.3k |
| `codex,gemini,local` | split-once | 3 | 5 | code | good_enough | 124s | 14 | 7 | 288.9k / 18.7k |
