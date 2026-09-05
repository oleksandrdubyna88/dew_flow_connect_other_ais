# Bench

## Per arm

| arm | stage | runs | verdicts | median time | median findings | gating | useful | tokens in / out | cost |
|---|---|---|---|---|---|---|---|---|---|
| `codex,gemini,local` | code | 6 | proceed×4, good_enough×2 | 460.3s | 17 | 53 | 1/4 | 2.1M / 128.1k | not reported |
| `codex,gemini,local` | plan-1 | 6 | good_enough | 77.7s | 13 | 64 | 7/15 | 274.7k / 33.1k | not reported |

## Per run

| arm | case | # | lane | stage | verdict | time | findings | gating | tokens in / out |
|---|---|---|---|---|---|---|---|---|---|
| `codex,gemini,local` | rounds-collapse | 1 | 1 | plan-1 | good_enough | 77.7s | 13 | 11 | 38.7k / 5.9k |
| `codex,gemini,local` | rounds-collapse | 1 | 1 | code | proceed | 679.6s | 6 | 5 | 350.7k / 16.8k |
| `codex,gemini,local` | rounds-collapse | 2 | 4 | plan-1 | good_enough | 66.2s | 12 | 9 | 48.4k / 2.8k |
| `codex,gemini,local` | rounds-collapse | 2 | 4 | code | proceed | 749.6s | 9 | 6 | 430.6k / 26.4k |
| `codex,gemini,local` | rounds-collapse | 3 | 2 | plan-1 | good_enough | 95.4s | 13 | 11 | 39.1k / 6.6k |
| `codex,gemini,local` | rounds-collapse | 3 | 2 | code | proceed | 452.9s | 17 | 6 | 491.3k / 23.7k |
| `codex,gemini,local` | split-once | 1 | 3 | plan-1 | good_enough | 41.5s | 13 | 12 | 49.8k / 6.1k |
| `codex,gemini,local` | split-once | 1 | 3 | code | proceed | 124s | 17 | 10 | 242.7k / 15.4k |
| `codex,gemini,local` | split-once | 2 | 5 | plan-1 | good_enough | 60.3s | 14 | 11 | 49.2k / 5.9k |
| `codex,gemini,local` | split-once | 2 | 5 | code | good_enough | 460.3s | 18 | 13 | 305.6k / 28k |
| `codex,gemini,local` | split-once | 3 | 3 | plan-1 | good_enough | 535.4s | 11 | 10 | 49.5k / 5.7k |
| `codex,gemini,local` | split-once | 3 | 3 | code | good_enough | 172s | 16 | 13 | 263.3k / 17.8k |
