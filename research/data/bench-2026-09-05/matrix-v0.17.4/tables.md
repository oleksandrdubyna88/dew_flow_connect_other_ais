# Bench

## Per arm

| arm | stage | runs | verdicts | median time | median findings | gating | useful | tokens in / out | cost |
|---|---|---|---|---|---|---|---|---|---|
| `codex` | code | 4 | proceed | 78.7s | 3 | 8 | 4/9 | 893.5k / 23.7k | not reported |
| `codex` | plan-1 | 4 | proceed | 42.8s | 6 | 18 | 11/21 | 58.1k / 8.2k | not reported |
| `codex,gemini` | code | 4 | proceed×3, FAILED×1 | 72.2s | 5 | 13 | 11/13 | 856.3k / 40.9k | not reported |
| `codex,gemini` | plan-1 | 4 | good_enough×2, proceed×2 | 39s | 8 | 25 | 16/31 | 207.1k / 19.5k | not reported |
| `codex,gemini,local` | code | 4 | proceed | 306.9s | 10 | 31 | 9/44 | 1.4M / 69.3k | not reported |
| `codex,gemini,local` | plan-1 | 4 | good_enough | 94.8s | 14 | 43 | 23/51 | 153.2k / 21.3k | not reported |
| `codex,local` | code | 4 | proceed | 390.4s | 9 | 27 | 8/39 | 1M / 35k | not reported |
| `codex,local` | plan-1 | 4 | good_enough | 54.6s | 10 | 33 | 13/39 | 66.2k / 12.6k | not reported |
| `gemini` | code | 3 | proceed | 27.5s | 1 | 5 | 3/4 | 322.1k / 27.2k | not reported |
| `gemini` | plan-1 | 4 | proceed×3, FAILED×1 | 13.5s | 3 | 6 | 5/10 | 68.3k / 4.6k | not reported |
| `gemini,local` | code | 3 | proceed | 248.6s | 6 | 15 | 1/18 | 512.6k / 52.4k | not reported |
| `gemini,local` | plan-1 | 4 | good_enough×2, FAILED×1, proceed×1 | 177.8s | 9 | 21 | 9/28 | 78.1k / 11.4k | not reported |
| `local` | code | 4 | proceed | 496.6s | 7 | 18 | 1/28 | 188.9k / 16k | not reported |
| `local` | plan-1 | 4 | proceed | 140.4s | 6 | 18 | 6/22 | 8.4k / 5.3k | not reported |

## Who found it alone

| provider | findings written | distinct | also found by another | found by it alone | of those, worth having |
|---|---|---|---|---|---|
| `codex` | 111 | 85 | 4 (5%) | 81 (95%) | **48** |
| `gemini` | 69 | 51 | 2 (4%) | 49 (96%) | **25** |
| `local` | 186 | 38 | 2 (5%) | 36 (95%) | **4** |

## Per run

| arm | case | # | lane | stage | verdict | time | findings | gating | tokens in / out |
|---|---|---|---|---|---|---|---|---|---|
| `codex` | rounds-collapse | 1 | 1 | plan-1 | proceed | 38.8s | 4 | 3 | 15.3k / 1.8k |
| `codex` | rounds-collapse | 1 | 1 | code | proceed | 78.7s | 2 | 1 | 224.6k / 6k |
| `codex` | rounds-collapse | 2 | 2 | plan-1 | proceed | 42.8s | 5 | 4 | 15.3k / 2k |
| `codex` | rounds-collapse | 2 | 2 | code | proceed | 44.6s | 3 | 3 | 287.2k / 4.4k |
| `gemini` | rounds-collapse | 1 | 3 | plan-1 | proceed | 13.5s | 4 | 2 | 14.6k / 1.3k |
| `gemini` | rounds-collapse | 1 | 3 | code | proceed | 23.6s | 1 | 1 | 127.1k / 6.7k |
| `gemini` | rounds-collapse | 2 | 4 | plan-1 | **FAILED** | 0s | 0 | 0 | 0 / 0 |
| `local` | rounds-collapse | 1 | 7 | plan-1 | proceed | 47.5s | 6 | 5 | 2.8k / 1.3k |
| `local` | rounds-collapse | 1 | 7 | code | proceed | 62.3s | 4 | 3 | 69.1k / 2.9k |
| `local` | rounds-collapse | 2 | 5 | plan-1 | proceed | 385.3s | 6 | 5 | 2.8k / 1.3k |
| `local` | rounds-collapse | 2 | 5 | code | proceed | 74.9s | 5 | 4 | 69.1k / 3.5k |
| `codex,gemini` | rounds-collapse | 1 | 6 | plan-1 | good_enough | 39s | 9 | 7 | 50.5k / 3.5k |
| `codex,gemini` | rounds-collapse | 1 | 6 | code | **FAILED** | 0.1s | 0 | 0 | 0 / 0 |
| `codex,gemini` | rounds-collapse | 2 | 4 | plan-1 | proceed | 41.7s | 7 | 5 | 75.7k / 6.3k |
| `codex,gemini` | rounds-collapse | 2 | 4 | code | proceed | 63.6s | 5 | 4 | 361k / 15.4k |
| `codex,local` | rounds-collapse | 1 | 3 | plan-1 | good_enough | 39.3s | 10 | 8 | 17.8k / 3.3k |
| `codex,local` | rounds-collapse | 1 | 3 | code | proceed | 390.4s | 7 | 6 | 275.8k / 6.9k |
| `codex,local` | rounds-collapse | 2 | 6 | plan-1 | good_enough | 87.3s | 9 | 8 | 17.8k / 2.7k |
| `codex,local` | rounds-collapse | 2 | 6 | code | proceed | 185.4s | 9 | 6 | 346.3k / 9.1k |
| `gemini,local` | rounds-collapse | 1 | 2 | plan-1 | good_enough | 395.8s | 9 | 7 | 24.7k / 5.7k |
| `gemini,local` | rounds-collapse | 1 | 2 | code | proceed | 248.6s | 6 | 4 | 207.9k / 16.8k |
| `gemini,local` | rounds-collapse | 2 | 4 | plan-1 | good_enough | 37.5s | 11 | 8 | 28k / 3.5k |
| `gemini,local` | rounds-collapse | 2 | 4 | code | proceed | 389.4s | 6 | 5 | 199.2k / 17.2k |
| `codex,gemini,local` | rounds-collapse | 1 | 7 | plan-1 | good_enough | 49.7s | 14 | 11 | 40.6k / 4.4k |
| `codex,gemini,local` | rounds-collapse | 1 | 7 | code | proceed | 208.7s | 9 | 7 | 484.8k / 18.1k |
| `codex,gemini,local` | rounds-collapse | 2 | 1 | plan-1 | good_enough | 94.8s | 14 | 11 | 38.6k / 6.2k |
| `codex,gemini,local` | rounds-collapse | 2 | 1 | code | proceed | 306.9s | 9 | 8 | 393k / 22.6k |
| `codex` | split-once | 1 | 6 | plan-1 | proceed | 45s | 6 | 6 | 13.8k / 2.3k |
| `codex` | split-once | 1 | 6 | code | proceed | 62.1s | 1 | 1 | 168k / 5.3k |
| `codex` | split-once | 2 | 7 | plan-1 | proceed | 40.3s | 6 | 5 | 13.8k / 2.1k |
| `codex` | split-once | 2 | 7 | code | proceed | 80.4s | 3 | 3 | 213.7k / 8k |
| `gemini` | split-once | 1 | 6 | plan-1 | proceed | 8.3s | 3 | 2 | 19.8k / 919 |
| `gemini` | split-once | 1 | 6 | code | proceed | 34.6s | 1 | 1 | 104.7k / 11.9k |
| `gemini` | split-once | 2 | 5 | plan-1 | proceed | 15.6s | 3 | 2 | 33.9k / 2.4k |
| `gemini` | split-once | 2 | 5 | code | proceed | 27.5s | 3 | 3 | 90.3k / 8.5k |
| `local` | split-once | 1 | 6 | plan-1 | proceed | 140.4s | 5 | 4 | 1.4k / 1.3k |
| `local` | split-once | 1 | 6 | code | proceed | 584.3s | 13 | 6 | 25.4k / 3.7k |
| `local` | split-once | 2 | 3 | plan-1 | proceed | 32.7s | 5 | 4 | 1.4k / 1.3k |
| `local` | split-once | 2 | 3 | code | proceed | 496.6s | 7 | 5 | 25.4k / 5.9k |
| `codex,gemini` | split-once | 1 | 7 | plan-1 | proceed | 34.1s | 8 | 6 | 48.4k / 4.6k |
| `codex,gemini` | split-once | 1 | 7 | code | proceed | 80s | 7 | 6 | 210k / 13k |
| `codex,gemini` | split-once | 2 | 5 | plan-1 | good_enough | 31.1s | 7 | 7 | 32.6k / 5.1k |
| `codex,gemini` | split-once | 2 | 5 | code | proceed | 72.2s | 3 | 3 | 285.3k / 12.6k |
| `codex,local` | split-once | 1 | 1 | plan-1 | good_enough | 45.3s | 10 | 9 | 15.2k / 3.3k |
| `codex,local` | split-once | 1 | 1 | code | proceed | 231.5s | 15 | 8 | 192.4k / 8.5k |
| `codex,local` | split-once | 2 | 4 | plan-1 | good_enough | 54.6s | 10 | 8 | 15.2k / 3.2k |
| `codex,local` | split-once | 2 | 4 | code | proceed | 522.4s | 9 | 7 | 217.5k / 10.5k |
| `gemini,local` | split-once | 1 | 7 | plan-1 | **FAILED** | 0s | 0 | 0 | 0 / 0 |
| `gemini,local` | split-once | 2 | 7 | plan-1 | proceed | 177.8s | 8 | 6 | 25.4k / 2.3k |
| `gemini,local` | split-once | 2 | 7 | code | proceed | 200.4s | 8 | 6 | 105.6k / 18.4k |
| `codex,gemini,local` | split-once | 1 | 5 | plan-1 | good_enough | 59.2s | 12 | 11 | 40.4k / 4.7k |
| `codex,gemini,local` | split-once | 1 | 5 | code | proceed | 425.8s | 18 | 9 | 243.3k / 11.1k |
| `codex,gemini,local` | split-once | 2 | 2 | plan-1 | good_enough | 152.8s | 11 | 10 | 33.7k / 6k |
| `codex,gemini,local` | split-once | 2 | 2 | code | proceed | 283.9s | 10 | 7 | 250.7k / 17.5k |
