# s7 todo — what a coder could not meet as written (2026-11-26)

- **t3 d5 (this repository's instruction step): not declared, the lead decides.** The rule resolver's payload for the files of three past tasks of this repository
  (`t1-coder-vendor-seam`, `t3-instruction-step`, `t2-fix-loop-through-the-seam`, each with `--task implement`) is **334,362 bytes** for each (33 rules; `rules.mjs explain`),
  far over the 64 KiB an instruction step may print, and the resolver itself refuses to print more than 32 KiB in one `read` ("Selected payload exceeds 32 KiB per read", no text emitted).
  So `.coai/toolchain.yaml` declares no `instructions` step and says why in a comment; a test holds it. Options for the lead: raise the 64 KiB limit (a plan change of SPEC C9 item 4),
  declare a step that prints only the always-loaded core (a resolver mode that does not exist yet), or leave it as is (a coder of this repository is told that nothing was run).
