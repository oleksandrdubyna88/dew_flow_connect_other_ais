# E3 agy coder: two real runs with no model (pass 7)

Date: 2026-11-27. Branch `feat/v2-ep3-e3-agy`. Paid agy calls: 0. `launchable` for agy: still OFF.

The test that makes these runs is `src_mcp/tests/TheRealAgyRunsTests.cs`. It runs only with `COAI_REAL_AGY=1` (a skip is not a pass), with a results file and a made-up jobs folder named by the person. It uses a temp job folder and the product's own sign-in copy. The real sign-in file is only read: the product makes a byte copy through a read handle. The test compares the sha256 of the real file before and after each part and writes only "equal" or "changed". No token, no environment and no file content is written. The made-up jobs folder, with the sign-in copy in it, is removed at the end (it is removed in a `finally`).

## What the runs showed

1. (a) One real `agy models` on a stale copy that coai made: **done, it refreshed.** The product's `SignInRefresh.RunAsync` copied the real file into `<job>/refresh`, set the expiry of that COPY two hours into the past, and ran `agy models` in the refresh unit. The scratch copy had an expiry 60 minutes ahead afterwards. The refresh unit exit code was 0 (about 1.6 s). The real file was not changed (sha256 equal after (a)). The refresh folder was gone afterwards.
2. (b) The product's own road before a launch, in the real unit, with the real `agy` as the program and no launch of agy: sign-in copy made and gated, guard folder written and read back, preflight **23 of 23 rows held, ADMITTED.** The three rows of M12 (`keys-folder-hidden`, `sign-in-not-in-unit`, `other-vendor-folder-hidden`), the read arm of the home row and the per-vendor temp folders are among them.
3. **The first real run found a bug that no test with made-up data had found.** The real file spells the expiry as `2026-11-26T23:04:58.302489051+01:00` (nine fraction digits, offset `+01:00`). The copy is written as JSON, and JSON spells the plus as `\u002B` (six characters). The row `sign-in-copy-has-no-refresh-token-and-the-margin` reads the time with `date -d` inside the unit and could not read it, so EVERY real agy launch would have been refused ("the expiry of the sign-in copy cannot be read"). All test fixtures used a `Z` time. Fix: the row decodes `\u002B` back to a plus before it asks `date`; the new test `TheSignInInTheShapeOfTheRealFile_NineFractionDigitsAndAPlusOffset_PassesTheCopyRowAndTheLaunchIsAdmitted` is red without it (1 of 25) and green with it. The host side (`SignInCopy.ExpiryOfFile`) reads JSON and was not affected.
4. The first try also showed a limit of the test setup, not of the product: a job folder under `/tmp` cannot be covered (`JobCover` rule), so the road refused the refresh there ("the folder of the jobs cannot be hidden from the refresh unit"). The runs below use a made-up jobs folder outside `/tmp`.
5. The real file is shared with other agy processes of this machine (consultations of other coai sessions run the real `agy`, which refreshes an expired real token at its own start). An earlier try compared the hash from outside the lock and saw "changed" while other agy processes were running; the runs below measure inside the lock around the run, and the in-test checks are the evidence. In the second try the real token was already 5 hours past its expiry (so the road itself ran one refresh, counted in the envelope as `refreshes`, on a scratch copy; the real file stayed as it was).

## The final run (all of it measured inside the shared lock)

Real sign-in file, measured around the run by a script outside the test: **equal**, and its mtime was in the same second. The test measured the same inside: equal after (a) and equal after (b).

Rules for the run, as the coordinator set them: a temp HOME (the job's home) and the product's sign-in copy only; never the key folder or any `*.key` file; no environment printed; the argv below has secrets redacted (none were in it).

```
program problem: none
real file: expiry readable = true; minutes left = 52
(a) refresh unit: finished; the scratch copy's expiry is in the FUTURE, 60 minutes (agy refreshed the stale copy)
(a) the refresh folder is gone afterwards: true
real sign-in file after (a): equal
(b) sign-in copy for the job: made and gated
(b) refreshes counted by the road: 0
(b) guard folder: written and read back
(b) admission: ADMITTED
(b) rows: 23 held, 0 not held, of 23
  - ok  user-manager: systemd-run --user started the preflight unit
  - ok  worktree-write: a file was written in the worktree
  - ok  main-hidden: the main checkout is an empty tmpfs with only its .git bound back, read-only
  - ok  coder-root-hidden: the coder root is an empty tmpfs with only the job's worktree bound back
  - ok  git-readonly: writes to the .git pointer and to the main .git folder were refused
  - ok  no-user-manager-inside: systemd-run --user cannot start a unit from inside
  - ok  windows-exe: a Windows program in the worktree did not run
  - ok  no-sockets-no-interop: /run/user is empty, /mnt holds nothing but the resolver file at most, and /run/WSL is closed
  - ok  home-is-the-jobs: HOME is the job's own folder and the real HOME can be neither written nor read
  - ok  tmp-is-the-jobs: /tmp and /var/tmp are the job's own folders
  - ok  environment-is-the-allow-list: the environment holds exactly the names of the vendor's allow-list
  - ok  other-job-folder-hidden-from-the-coder: a stand-in folder of another job cannot be seen, read or written from inside the coder's unit
  - ok  program-cannot-be-written: the program of the launch is there, can be run and cannot be written
  - ok  cwd-is-the-guard-folder: the unit starts in the guard folder, and the worktree is no working directory
  - ok  guard-folder-readonly: a write into the guard folder and its .agents folder was refused
  - ok  sign-in-is-the-jobs-copy: the sign-in is the job's copy, mode 0600, the only one there, and the real ~/.gemini is not seen
  - ok  sign-in-copy-has-no-refresh-token-and-the-margin: the copy holds no refresh token and its access token outlives the unit by the margin
  - ok  guard-answers-each-case: the handler allowed the write inside the worktree and denied every other case, through the exact command of hooks.json
  - ok  guard-answers-empty-and-garbage: an empty input and text that is not JSON are each answered with an explicit deny
  - ok  guard-files-are-the-expected-bytes: hooks.json, the agent file, the hook scripts and the guard's own file are the expected bytes, read back inside the unit
  - ok  keys-folder-hidden: no key folder or keys file is named for this job, so there is none to hide
  - ok  sign-in-not-in-unit: the person's sign-in file is not in the check's unit
  - ok  other-vendor-folder-hidden: the folders of the other vendors of the job cannot be seen from inside the unit
real sign-in file after (b): equal
main checkout stand-in untouched by the preflight marker: true
the made-up jobs folder (with the sign-in copy) is removed: true
```


Processes the road started (argv cut at 360 characters, secrets redacted; the long preflight script is not repeated):

- `systemd-run` (1.6 s, exit 0): --wait --pipe --user --collect --quiet --expand-environment=no --unit coai-check-t4-f222a30536a1-refresh-real --working-directory /home/jinx/coai-t3/runs/real-agy-jobs/t4-f222a30536a1/refresh -p ProtectSystem=strict -p ProtectHome=tmpfs -p TemporaryFileSystem=/run/user -p TemporaryFileSystem=/mnt -p TemporaryFileSystem=/dev/s …
- `systemctl` (0.0 s, exit 0): --user show --property=ActiveState --value <redacted>
- `systemd-run` (1.6 s, exit 0): --wait --pipe --user --collect --quiet --expand-environment=no --unit coai-coder-t4-f222a30536a1-pre --working-directory /home/jinx/coai-t3/runs/real-agy-jobs/t4-f222a30536a1/coder/agy/guard -p ProtectSystem=strict -p ProtectHome=tmpfs -p TemporaryFileSystem=/run/user -p TemporaryFileSystem=/mnt -p TemporaryFileSystem=/dev/sh …
- `systemctl` (0.0 s, exit 0): --user show --property=ActiveState --value coai-coder-t4-f222a30536a1-pre.service

## Again after pass 10 (2026-11-27): the refresh unit with its new lists

The refresh unit now hides the key pool and every real sign-in file of the person at their real paths, and asks the home rule for its program (pass 10: S26, R-S7, S54). Its argv and binds changed, so the two runs were repeated once with that build, with the same rules (a temp job folder outside `/tmp`, the product's sign-in copy, no model, only "equal" or "changed" about the real file).

- (a) `agy models` on a stale copy that coai made: the refresh unit finished and the scratch copy's expiry was in the future (60 minutes). The refresh folder was gone afterwards.
- (b) The product's road before a launch: the sign-in copy was made and gated (the real token had been expired 268 minutes, so the road itself ran one refresh, counted 1), the guard folder was written and read back, and the preflight held **23 of 23 rows: ADMITTED**.
- Real sign-in file, measured inside the shared lock around the run: **equal**, mtime in the same second. The made-up jobs folder was removed.
