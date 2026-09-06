# SCOPE — the split order needs a floor, and the caller is what supplies it

With *Split the plan* on, a plan that passes is told to split into 2–4 epics and each into stories.
Each epic then comes back for its own plan review — correctly — and the gate, with no memory, told
each one to split into epics as well. Epics of epics, with no floor.

Our session cannot hold that memory: it is keyed repo+branch and its plan stage happens once, so an
epic returns as a DIFFERENT session on its own branch. The caller is what crosses them, and Claude
Code exports `CLAUDE_CODE_SESSION_ID` to every child it spawns.

## What must be true

1. A caller is ordered to split ONCE. A plan that is already a piece is told so instead: build as one
   unit, review its diff, fix, document, test, commit — and say so if it is genuinely too big.
2. A piece's shape is not re-measured and the Fable order is not issued to it; the autonomy order is.
3. Switch off → no command at all. A plan that did not pass → no command at all.
4. The claim is atomic across processes: two servers sharing a data directory cannot both give it.
5. An unwritable store fails OPEN with a warning rather than silently disabling the feature.
6. A client that exports no session id is still followed across the branches of one checkout.
7. The split verdict is measured from the plan THIS round reviewed.

## Constraints

- No change to what the gate decides: verdicts, thresholds and the resolve loop are untouched.
- The condition that issues the order and the condition that claims it are ONE question asked once.
- A caller id is somebody else's string and reaches a file name: it must not escape the directory.
- Everything off by default; an empty command list stays the behaviour of every earlier release.
- Nothing may be added that requires the calling AI to remember and pass an identifier.
