# Bench

## Per arm

| arm | stage | runs | verdicts | median time | median findings | gating | useful | tokens in / out | cost |
|---|---|---|---|---|---|---|---|---|---|
| `codex` | code | 2 | proceed | 94.7s | 6 | 7 | 4/10 | 545.7k / 12k | not reported |
| `codex` | plan-1 | 2 | proceed | 50.8s | 5 | 6 | 3/8 | 29.1k / 3.5k | not reported |
| `codex,gemini` | code | 2 | proceed | 87s | 12 | 12 | 7/17 | 801.4k / 20.3k | not reported |
| `codex,gemini` | plan-1 | 2 | good_enough | 48.8s | 9 | 14 | 10/17 | 76.4k / 10.2k | not reported |
| `codex,gemini,local` | code | 2 | proceed | 197.1s | 20 | 22 | 7/35 | 1M / 39k | not reported |
| `codex,gemini,local` | plan-1 | 2 | good_enough | 42.4s | 14 | 23 | 10/28 | 96.9k / 12.5k | not reported |
| `codex,local` | code | 2 | proceed | 169.6s | 16 | 15 | 2/25 | 653.5k / 18.1k | not reported |
| `codex,local` | plan-1 | 2 | good_enough | 60.5s | 11 | 18 | 8/21 | 33.1k / 6.3k | not reported |
| `gemini` | code | 2 | proceed | 19.6s | 3 | 3 | 2/3 | 263.7k / 13.9k | not reported |
| `gemini` | plan-1 | 2 | proceed | 16.2s | 4 | 6 | 3/7 | 46.5k / 5.6k | not reported |
| `gemini,local` | code | 2 | proceed | 166.6s | 11 | 12 | 2/18 | 440k / 27.8k | not reported |
| `gemini,local` | plan-1 | 2 | good_enough×1, proceed×1 | 20.3s | 10 | 13 | 7/18 | 53.9k / 7.2k | not reported |
| `local` | code | 2 | proceed | 150.6s | 15 | 10 | 0/10 | 137k / 8.6k | not reported |
| `local` | plan-1 | 2 | proceed | 50.1s | 7 | 9 | 2/7 | 4.2k / 2.7k | not reported |

## Who found it alone

| provider | findings written | distinct | also found by another | found by it alone | of those, worth having |
|---|---|---|---|---|---|
| `codex` | 75 | 60 | 5 (8%) | 55 (92%) | **22** |
| `gemini` | 52 | 43 | 4 (9%) | 39 (91%) | **19** |
| `local` | 118 | 59 | 3 (5%) | 56 (95%) | **5** |

## Per run

| arm | case | # | lane | stage | verdict | time | findings | gating | tokens in / out |
|---|---|---|---|---|---|---|---|---|---|
| `codex` | rounds-collapse | 1 | 1 | plan-1 | proceed | 50.8s | 3 | 2 | 15.3k / 2.3k |
| `codex` | rounds-collapse | 1 | 1 | code | proceed | 88.7s | 6 | 3 | 378.8k / 6.5k |
| `gemini` | rounds-collapse | 1 | 1 | plan-1 | proceed | 16.2s | 4 | 3 | 20.7k / 3k |
| `gemini` | rounds-collapse | 1 | 1 | code | proceed | 19.4s | 3 | 2 | 159.1k / 5.5k |
| `local` | rounds-collapse | 1 | 1 | plan-1 | proceed | 50.1s | 7 | 5 | 2.8k / 1.5k |
| `local` | rounds-collapse | 1 | 1 | code | proceed | 150.6s | 7 | 6 | 90.4k / 5.5k |
| `codex,gemini` | rounds-collapse | 1 | 1 | plan-1 | good_enough | 48.8s | 9 | 7 | 38.5k / 7.8k |
| `codex,gemini` | rounds-collapse | 1 | 1 | code | proceed | 61.4s | 12 | 8 | 451.2k / 8.3k |
| `codex,local` | rounds-collapse | 1 | 1 | plan-1 | good_enough | 60.5s | 11 | 9 | 17.9k / 3.9k |
| `codex,local` | rounds-collapse | 1 | 1 | code | proceed | 169.6s | 9 | 7 | 381.6k / 9.7k |
| `gemini,local` | rounds-collapse | 1 | 1 | plan-1 | good_enough | 20.3s | 10 | 7 | 25.5k / 2.5k |
| `gemini,local` | rounds-collapse | 1 | 1 | code | proceed | 166.6s | 11 | 7 | 288.2k / 18.2k |
| `codex,gemini,local` | rounds-collapse | 1 | 1 | plan-1 | good_enough | 38.2s | 14 | 11 | 47.7k / 6.8k |
| `codex,gemini,local` | rounds-collapse | 1 | 1 | code | proceed | 197.1s | 20 | 12 | 623.3k / 16.5k |
| `codex` | split-once | 1 | 1 | plan-1 | proceed | 25.2s | 5 | 4 | 13.8k / 1.2k |
| `codex` | split-once | 1 | 1 | code | proceed | 94.7s | 4 | 4 | 167k / 5.5k |
| `gemini` | split-once | 1 | 1 | plan-1 | proceed | 15.6s | 3 | 3 | 25.8k / 2.7k |
| `gemini` | split-once | 1 | 1 | code | proceed | 19.6s | 1 | 1 | 104.6k / 8.4k |
| `local` | split-once | 1 | 1 | plan-1 | proceed | 27.9s | 5 | 4 | 1.4k / 1.2k |
| `local` | split-once | 1 | 1 | code | proceed | 72.3s | 15 | 4 | 46.7k / 3.1k |
| `codex,gemini` | split-once | 1 | 1 | plan-1 | good_enough | 39.5s | 8 | 7 | 37.9k / 2.4k |
| `codex,gemini` | split-once | 1 | 1 | code | proceed | 87s | 5 | 4 | 350.1k / 12k |
| `codex,local` | split-once | 1 | 1 | plan-1 | good_enough | 30.1s | 10 | 9 | 15.2k / 2.4k |
| `codex,local` | split-once | 1 | 1 | code | proceed | 105.9s | 16 | 8 | 271.9k / 8.4k |
| `gemini,local` | split-once | 1 | 1 | plan-1 | proceed | 17.7s | 8 | 6 | 28.4k / 4.7k |
| `gemini,local` | split-once | 1 | 1 | code | proceed | 86.8s | 9 | 5 | 151.8k / 9.6k |
| `codex,gemini,local` | split-once | 1 | 1 | plan-1 | good_enough | 42.4s | 14 | 12 | 49.2k / 5.7k |
| `codex,gemini,local` | split-once | 1 | 1 | code | proceed | 157.9s | 16 | 10 | 414.5k / 22.5k |
