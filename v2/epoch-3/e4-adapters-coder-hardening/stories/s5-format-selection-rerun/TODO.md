# s5 - open items after the security verify (RESULTS_e4_s5_security_verify.md)

Done in the fix commits: M1, M2, M3, M4, M5, S1, S3, S4, S5, S8, S9, S10, S11, S12 (pass 2: the node note is in SPEC section E). What is left, by the verify's numbers:

- **S2 - what the format commit may hold.** Say in the `format` note when the formatter's command or configuration is among the head's changed definitions; refuse a mode change (a ` M` entry whose only change is the file mode); run the M6 proof (clean, empty `status --ignored`, no nested `.git`) before the formatter turn as well, because it runs on a tree that may hold the coder's ignored files.
- **S6 - the order of the records.** Cosmetic. After the fix a re-run's record follows its own first run; with several projects the order is by project, which the verify did not ask to change further.
- **S7 - two quiet failures.** A fact of the job that cannot be written (`full-failed`, `flaky-excused.txt`, `flaky-noticed`) is one warning, so the count of excused tests can be lost and a third excused; a `COAI_FLAKY_RERUNS` that is neither 0 nor 1 makes a note that is dropped (`FlakyReruns.Read(...).Count`). Decide: a failed write turns the re-run off for the rest of the job; carry the note into `caps` (s6 t3 d3).
- Not verified by Fable: anything inside a real unit; `CoderFormat` and `CoderFixLoop` at run time; the jest, vitest and node:test readers against M1 to M3 (only the platform runner's output was scripted).
- s5 t6 (this repository's `testFiles` and `testFor`, a recorded run) waits for the owner's answer to SPEC question 3.
