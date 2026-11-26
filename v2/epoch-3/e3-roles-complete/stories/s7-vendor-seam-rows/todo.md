# s7 todo — what a coder could not meet as written (2026-11-26)

- **t3 d5 (this repository's instruction step): not declared, the lead decides.** The rule resolver's payload for the files of three past tasks of this repository
  (`t1-coder-vendor-seam`, `t3-instruction-step`, `t2-fix-loop-through-the-seam`, each with `--task implement`) is **334,362 bytes** for each (33 rules; `rules.mjs explain`),
  far over the 64 KiB an instruction step may print, and the resolver itself refuses to print more than 32 KiB in one `read` ("Selected payload exceeds 32 KiB per read", no text emitted).
  So `.coai/toolchain.yaml` declares no `instructions` step and says why in a comment; a test holds it. Options for the lead: raise the 64 KiB limit (a plan change of SPEC C9 item 4),
  declare a step that prints only the always-loaded core (a resolver mode that does not exist yet), or leave it as is (a coder of this repository is told that nothing was run).

- **t4 d4 (the codex and agy main-AI rows): not built, the owner's answer is missing.** The dod says "as the owner confirmed them (SPEC section D and section H, question 3)". SPEC section H records the failover entries as accepted (decision 35) and the main-AI rows as "not answered". `shared/role-defaults.json` keeps the codex and agy rows empty with their note; the measured names are in `RESULTS_model_lists.md`. The lead asks the owner which measured model takes which role; the rows are then a data edit (`shared/role-defaults.json`, `shared/role-model-names.json` when a name is new) and a line in `ARolesAnswerTests`.
- **t5 and t6 were not built in this run.** t5 (check units hide the coder folder) was set aside on purpose: the E2 stream fixes the same hole for claude on `feat/v2-ep3-e2-coder`, and t5 is done after that merges in. t6 (sign-in copies) depends on t5 and on the releases of s5 and s6; it was not started.
- **t2 d4 (no line names codex or agy):** the files of the road that had no such word still have none (a scan holds it, and the two files that already named a vendor name no more than they did). The tests of the road and of the route must name a vendor to build a route, so they are outside that scan.
