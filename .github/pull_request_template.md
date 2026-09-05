<!-- The title is the changelog line: `type: what changed` — feat, fix, docs, test, chore, refactor, perf, ci, build. -->

## What changed, and why

<!-- The symptom or the ask first, then what shipped. A reader who sees only this text should know both. -->

## Evidence

- [ ] **Red-green:** every bug or problem spot here has a test that failed before the fix and passes after — name it.
- [ ] **All the tests ran** (not only the ones near the change), and this is what they printed:

```
<paste the test runner's summary lines>
```

## Documentation

- [ ] The docs that describe what changed are updated in this PR — `research/module_*.md`, README, CHANGELOG, the manifest (`package.json` / `.csproj`) where a version or a contribution changed.
- [ ] If this finishes a plan in `todo/`, it is promoted to `research/` here, with what shipped differently.

## The reviewer

<!-- Filled in about five minutes after opening: what CodeRabbit said, what was fixed, and what was NOT acted on — with the reason, citing the code or the rule. -->

## Release

- [ ] No release needed / a release tag will be cut on `main` after the merge: `<tag>`.
