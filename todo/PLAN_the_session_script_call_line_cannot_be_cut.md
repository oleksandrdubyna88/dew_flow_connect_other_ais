# PLAN — the session script's call line cannot be cut

> Status: **plan only, nothing implemented yet, 2026-11-19.** Scope: `src_vs_code` only — the last two lines of
> `REMOTE_SCRIPT` (`src/remoteScript.ts:428-429`), its doc comment, its two tests, and the text, line count and sha256
> of that script in its promoted spec
> ([PLAN_the_chat_opens_from_a_terminal_E2_S2_spec.md](../research/PLAN_the_chat_opens_from_a_terminal_E2_S2_spec.md),
> § S2.3) and in `research/module_extension.md`. A defect of shipped code (extension 0.87.0 and later), found by the
> E1.S2 spec of the accounts plan (its N2) and confirmed at that plan's gate G2
> ([PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_G2.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_G2.md),
> § 5). **Outside that plan, and no entry of its ledger.** Follow-up of
> [PLAN_the_chat_opens_from_a_terminal.md](../research/PLAN_the_chat_opens_from_a_terminal.md), whose E2 built the
> script.

## The symptom

The session script is sent whole to `sh -s` on a VM. It is one function, `coai_main`, and its last two lines are
(`src_vs_code/src/remoteScript.ts:428-429`):

```sh
coai_main "$@" </dev/null 2>/dev/null
printf 'COAI-END\n'
```

Its doc comment says: "a script cut on the way does nothing" (`src_vs_code/src/remoteScript.ts:130-131`), and its spec
says the same (§ S2.1, decision 4). At line 428 that is not true. When the text stops inside that line — the stream
ends, and `sh` reads its last line with no line end — the shell runs what it has:

| The text ends | What `sh` does |
|---|---|
| right after `coai_main`, or right after `coai_main "$@"` (with or without the space after it) | It RUNS the function, with the script's own pipe as its stdin and the caller's stderr as its stderr. The script's tools (`tmux`, `ps`) then have a stdin that is not `/dev/null`, and anything they write to stderr reaches ssh's stderr, which the runner logs |
| inside the path of `2>` — after `2>/d`, `2>/de`, `2>/dev/n`, `2>/dev/nu` or `2>/dev/nul` | It opens that path FOR WRITING and runs the function with its stderr in it. As a user who may write there — `root` — that CREATES `/d`, `/de`, `/dev/n`, `/dev/nu` or `/dev/nul` on the VM. The script is specified to write nothing |
| after the whole line 428, before line 429 | The function runs as intended; the end line is missing, and the parser says `cut`. This case is harmless |

**Probed 2026-11-19, with a TOY function and never the session script** (`f() { echo ran; }`, `dash -s` and
`bash --posix -s`, in an owned scratch folder that held `dev/null`; no VM): with the call written as line 428 has it
— `f "$@" <S/dev/null 2>S/dev/null` — four cuts ran `f` with the pipe as its stdin, and the cuts inside the `2>` path
created `d`, `de`, `dev/n`, `dev/nu` and `dev/nul` in the scratch folder, under both shells. The gate's judge saw the
same with `dash` (`printf 'f() { echo ran; }\n</dev/null 2>./nu' | dash -s` created `./nu`). With the two lines as ONE
brace group — `{ f "$@" <S/dev/null 2>S/dev/null; printf 'END\n'; }` — `f` ran in 1 of 290 cuts, the whole line, and
no entry was created: the shell parses a compound command whole before it runs any of it, so a cut before the `}` is
a syntax error.

**How much it matters.** The harm is small and the window is narrow. The script starts `tmux` and `ps`, not a vendor
program; its stdin is at its end when the function runs; and the stream has to stop inside about 25 bytes of a text of
7497. But one of the outcomes is a file created as root on a person's server by a script whose contract is that it
writes nothing, and the sentence that says "does nothing" is in the code, in the spec and in `research/`.

## What is not known

- **Whether a real ssh stream can stop there.** Not measured. The whole text is 7497 bytes and is written to the ssh
  child's stdin at once; a cut needs the child or the connection to die inside that write. `sh` then sees the end of
  its stdin and runs its last, unfinished line. No such run was observed on a VM.
- **What `tmux` and `ps` do with a stdin that is the script's pipe.** The spec's own measurement M-S3 saw the tools
  wait for and eat the script's stdin when ` </dev/null` was taken off the call line; here the pipe is already at its
  end.

## Build order

1. **RED first.** A new test in `src_vs_code/src/test/remoteScriptRuns.test.ts`, with the next free id of that file's
   series (`S2-T43` is the last today), for both shells: a text DERIVED from `REMOTE_SCRIPT` in which the two
   `/dev/null` of the call line are written as `<T>/dev/null`, where `<T>` is an `ownedTemp` folder that holds
   `dev/null` and nothing else. That text is cut at EVERY byte of its last two lines. Asserted: no line of the fakes'
   log (no tool was started — with any stdin); stdout is empty for every cut but the whole text; and after all the
   cuts `<T>` holds `dev`, `<T>/dev` holds `null`, and neither holds another entry. RED today: the function runs at the
   cuts of the table above, and `<T>/dev/nu` is created. No cut of the test names a path outside `<T>` for writing.
2. **The fix: the brace-group form the accounts script has** (the S2 spec of the accounts plan, § A2.1, decision 4).
   The last two lines become ONE:

   ```sh
   { </dev/null 2>/dev/null coai_main "$@"; printf 'COAI-END\n'; }
   ```

   The redirections first, as in the accounts script, so the two scripts end in one form. The script is then 292
   lines.
3. **Everything that holds the text moves in the same commit.** `REMOTE_SCRIPT` and its doc comment (the line count,
   the byte count, the sha256: `src_vs_code/src/remoteScript.ts:126-131`); the promoted spec — its block (§ S2.3, the
   last two lines), its line count, byte count and sha256 (`:190-191`), decisions 3 and 4 (`:117-121`), the rule at
   `:495`, the row of S2-T17 (`:1060`) and the brief's hash lines (`:1183`, `:1190`); `research/module_extension.md:13736`
   (the line count, the sha256, and rule (1)); the scan `S2-T17` in `src_vs_code/src/test/remoteScript.test.ts:124-137`,
   which asserts today's last two lines by their text, and its companion at `:157-158`. The new sha256 is computed
   from the spec's block and from the compiled constant, by the procedure of that spec's § S2.11, and the summary gives
   both commands and their output.
4. **Break-it.** With the braces taken off again, step 1's test is red with the path it created in its message.
5. **Decided at this plan's own gate, not here:** whether the session script gets a hash test, as the accounts script
   has (its A2-T2). The session spec chose none (its decision 14). The fix does not need one.

Order with the accounts plan: none. Its E1 does not touch `REMOTE_SCRIPT` or its tests (its S2 spec, § A2.13), and
this plan does not touch the accounts script. Either may land first.

## Test plan

- Step 1's cut test: its first RED recorded, then green, then red again with the braces off (step 4).
- `S2-T17` and its companions, changed only where they hold the last two lines; every other test of
  `remoteScript.test.ts`, `remoteScriptRuns.test.ts` and `remoteFrame.test.ts` green with no edit — S2-T12 and S2-T15
  among them, which hold that no command waits for the script's stdin.
- The whole suite and lint. No host scenario changes: nothing here imports `vscode`.
- Not covered: a real VM, and a real stream that stops (see *What is not known*).

## Definition of Done

- [ ] Step 1's test was seen RED on today's text, with the cut that ran the function and the path that was created.
- [ ] `REMOTE_SCRIPT` ends in the one brace group; a cut before its `}` starts no tool, creates no entry and prints no
      line, under `sh` and `bash --posix`.
- [ ] The constant's doc comment, the promoted spec (block, line count, byte count, sha256, decisions 3 and 4, S2-T17's
      row) and `research/module_extension.md` say the new text and the new hash, in the commit that changes the text.
- [ ] No sentence in the code, the spec or `research/` says "a script cut on the way does nothing" of a text for which
      it was not shown.
- [ ] The fix passed its own plan gate and code gate; the changelog entry of its extension release says what a cut
      could do and that it is closed.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean.
