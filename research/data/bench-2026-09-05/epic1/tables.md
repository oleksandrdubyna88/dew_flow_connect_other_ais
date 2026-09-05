# Bench

## Per arm

| arm | stage | runs | verdicts | median time | median findings | gating | useful | tokens in / out | cost |
|---|---|---|---|---|---|---|---|---|---|
| `codex,gemini,local` | code | 4 | proceed | 277.9s | 11 | 21 | — | 1.6M / 131.2k | not reported |
| `codex,gemini,local` | plan-1 | 4 | good_enough | 79.4s | 13 | 39 | — | 191.1k / 24.2k | not reported |

## Per run

| arm | case | # | lane | stage | verdict | time | findings | gating | tokens in / out |
|---|---|---|---|---|---|---|---|---|---|
| `codex,gemini,local` | rounds-collapse | 1 | 1 | plan-1 | good_enough | 79.4s | 11 | 7 | 39.9k / 8.2k |
| `codex,gemini,local` | rounds-collapse | 1 | 1 | code | proceed | 277.3s | 11 | 5 | 575.2k / 29.5k |
| `codex,gemini,local` | rounds-collapse | 2 | 1 | plan-1 | good_enough | 248.8s | 11 | 8 | 52.3k / 5.7k |
| `codex,gemini,local` | rounds-collapse | 2 | 1 | code | proceed | 211.5s | 10 | 5 | 409.4k / 27.2k |
| `codex,gemini,local` | split-once | 1 | 1 | plan-1 | good_enough | 46.7s | 13 | 12 | 50.1k / 5.2k |
| `codex,gemini,local` | split-once | 1 | 1 | code | proceed | 507.6s | 6 | 3 | 303.9k / 45.8k |
| `codex,gemini,local` | split-once | 2 | 1 | plan-1 | good_enough | 46.7s | 14 | 12 | 48.8k / 5.1k |
| `codex,gemini,local` | split-once | 2 | 1 | code | proceed | 277.9s | 14 | 8 | 289.6k / 28.7k |
