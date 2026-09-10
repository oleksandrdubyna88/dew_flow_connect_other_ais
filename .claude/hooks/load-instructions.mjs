#!/usr/bin/env node
/**
 * Put this repository's instructions into a Claude Code session at its start.
 *
 * <p><b>Why this exists.</b> The shared layout is one source for two hosts, and it is enforced:
 * `CLAUDE.md` may contain only `@AGENTS.md`, and `.claude/rules` must be empty
 * (`tools/lib/rule-cli.mjs`, `validateInstructions`). Those are the two doors Claude Code loads
 * project instructions through on its own, so closing them left a session holding 561 bytes — an
 * instruction to go and read the rules — where it used to hold the rules themselves.</p>
 *
 * <p>That difference is not stylistic. The old arrangement was enforced by the HOST: every session
 * had the rules whether or not the model thought to fetch them. The new one is enforced by the
 * model's own diligence, and the two rules that suffer most from being optional are exactly this
 * project's own — `vendor-routing`, whose violation is invisible in the output and has already cost
 * three cells of a measurement, and `review-gate`, the contract for calling this product's gate.</p>
 *
 * <p>So this restores the guarantee WITHOUT touching either enforced door. It runs the canonical
 * resolver — the same one Codex is told to run, at the same pin — and prints what it emits.
 * `SessionStart` output becomes session context, so the always-core arrives before the first
 * decision rather than after somebody remembers to ask for it.</p>
 *
 * <p><b>What it deliberately does NOT do.</b> It does not replace the procedure in
 * `.agents/conventions/ENTRY.md`. `--task inspect` yields the rules that apply to every task; the
 * ones that depend on WHICH files are being changed still need `explain`/`read` for the real task,
 * and the notice below says so. A hook cannot know at session start what the session will touch.</p>
 *
 * <p>It never fails the session. A missing submodule or resolver is reported as INCOMPLETE, in the
 * words ENTRY.md uses, with the two commands that fix it — because a hook that exits non-zero on a
 * fresh clone teaches people to delete the hook.</p>
 */
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

/** The repository root, resolved the way ENTRY.md step 1 resolves it. */
function repoRoot() {
  return execFileSync('git', ['rev-parse', '--show-toplevel'], {
    encoding: 'utf8', timeout: 30_000, windowsHide: true, stdio: ['ignore', 'pipe', 'ignore'],
  }).trim();
}

function incomplete(reason) {
  process.stdout.write(
    'Instruction loading is INCOMPLETE — the shared resolver could not run.\n\n'
    + `${reason}\n\n`
    + 'Restore it inside the authorized task:\n'
    + '  git submodule update --init .agents/conventions\n'
    + '  npm ci --ignore-scripts --prefix .agents/conventions\n\n'
    + 'Until then, read .agents/PROJECT.md and .agents/conventions/ENTRY.md directly, and treat\n'
    + 'edits to affected files as blocked rather than defaulting to your own conventions.\n',
  );
}

try {
  const root = repoRoot();
  const resolver = path.join(root, '.agents/conventions/tools/rules.mjs');
  if (!fs.existsSync(resolver)) {
    incomplete(`No resolver at ${resolver} — the conventions submodule is not checked out.`);
    process.exit(0);
  }

  // `inspect` is the neutral task: what comes back is what applies to EVERY task, which is the only
  // selection a session start can honestly make.
  const emitted = execFileSync(process.execPath, [resolver, 'read', '--repo', root, '--task', 'inspect'], {
    encoding: 'utf8', timeout: 120_000, maxBuffer: 8 * 1024 * 1024, windowsHide: true,
    stdio: ['ignore', 'pipe', 'pipe'],
  });

  process.stdout.write(
    'Project instructions for this repository, loaded at session start by '
    + '.claude/hooks/load-instructions.mjs from the pinned canonical source.\n\n'
    + 'These are the rules that apply to EVERY task. Rules that depend on which files you change are\n'
    + 'not here: before editing, follow .agents/conventions/ENTRY.md and run\n'
    + '  node .agents/conventions/tools/rules.mjs explain --repo <root> --task <task> --file <path>\n'
    + 'then read what it selects. Re-run it after a compaction or when the scope changes.\n\n'
    + emitted,
  );
} catch (error) {
  incomplete(String(error?.stderr || error?.message || error).trim());
}
