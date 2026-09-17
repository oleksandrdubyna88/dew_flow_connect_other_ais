import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Prove that an extraction was a MOVE: every line of the new modules came from the file they left.
 *
 * <p><b>Why this exists, precisely.</b> A refactor that carves one file into several deletes a region
 * and re-adds it elsewhere. When such a branch is rebased, git resolves delete-versus-modify in
 * favour of the DELETE — so any change main made to those lines in the meantime vanishes, with no
 * conflict and nothing to review. The first attempt at splitting `chatCommand.ts` did exactly that
 * on a base 28 commits stale: it would have re-inlined seven `vscode.window.show*` calls that main
 * had converted to the notifications ledger, and every check was green, because the ratchet that
 * counts those call sites did not exist on the base it was built from.</p>
 *
 * <p>The repository's own history records the same trap happening before this:
 * <i>"feat(notifications): the rebase re-opened the hole, and the ratchet said so"</i>.</p>
 *
 * <p>So the guard is not "does it compile" or "are the tests green" — both were. It is: <b>is every
 * line of the new module a line the original actually had?</b> That is cheap to answer and it is the
 * one question a silent revert cannot pass.</p>
 *
 * <p>It is a script rather than a test because it needs the ORIGINAL, which lives in git history: a
 * CI clone may be shallow, and once the split has landed there is no original left to compare
 * against. {@link movedVerbatim} — the decision — is pure and is tested on fixtures in
 * `src/test/proveMove.test.mjs`. Run this by hand while doing an extraction, and again after any
 * rebase of one.</p>
 *
 * <p>Usage: <code>node scripts/prove-move.mjs origin/main src/chatCommand.ts src/chatThread.ts …</code></p>
 */

/** A line as it is compared: trailing whitespace is not a difference anybody means. */
const asCompared = (line) => line.replace(/\s+$/u, '');

/**
 * An `export` keyword is the one edit a move may make, so a line matches if its un-exported form did.
 *
 * <p>Nothing else is forgiven. A renamed variable, a changed argument, a re-worded comment and a
 * dropped attribution all come back in the residue, which is the point — the caller reads them one
 * by one and says in the commit why each is there, or puts it back.</p>
 */
const unexported = (line) => line.replace(/^(\s*)export /u, '$1');

/**
 * The lines of the moved modules that the original did not have.
 *
 * <p>Order is deliberately NOT checked: a move may reorder whole functions, and a reader can see
 * that in the diff. What a reader cannot see is one line quietly differing in the middle of three
 * hundred that are identical.</p>
 *
 * @param original the file the modules were carved out of, as it was BEFORE the split
 * @param modules `{ name, text }` for each new module, each including its own new import block
 * @returns `{ checked, residue }` — how many body lines were compared, and the ones with no source
 */
export function movedVerbatim(original, modules) {
  const had = new Set(original.split('\n').filter((one) => one.trim().length > 0).map(asCompared));
  const residue = [];
  let checked = 0;
  for (const { name, text } of modules) {
    for (const line of bodyOf(text).split('\n')) {
      if (line.trim().length === 0) {
        continue;
      }
      checked += 1;
      const bare = asCompared(line);
      if (!had.has(bare) && !had.has(unexported(bare))) {
        residue.push({ name, line: line.trim() });
      }
    }
  }

  return { checked, residue };
}

/**
 * A module without its own import block and module header, which are new by definition.
 *
 * <p>Everything up to the end of the first block comment that follows the last import is the module
 * saying what it is; everything after it is the code that moved. A module with no imports at all is
 * compared whole.</p>
 */
export function bodyOf(text) {
  const lines = text.split('\n');
  // The LAST line that opens an import, counted from the start so that an import block beginning at
  // byte 0 is found — searching for a preceding newline misses it, which is how this read a module's
  // own header as moved code the first time it was written.
  let after = -1;
  let at = 0;
  for (const [index, line] of lines.entries()) {
    if (line.startsWith('import ') || line.startsWith('} from ')) {
      after = at + line.length + 1;
    }
    at += line.length + 1;
    void index;
  }
  if (after < 0) {
    return text;
  }
  const header = text.indexOf('/**', after);
  if (header < 0) {
    return text.slice(after);
  }
  const closes = text.indexOf('*/', header);

  return closes < 0 ? text.slice(header) : text.slice(closes + 2);
}

const [ref, originalPath, ...modulePaths] = process.argv.slice(2);
if (ref !== undefined && originalPath !== undefined) {
  const root = join(import.meta.dirname, '..', '..');
  const original = execFileSync('git', ['show', `${ref}:${join('src_vs_code', originalPath).replace(/\\/gu, '/')}`],
    { cwd: root, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  const modules = modulePaths.map((one) => ({
    name: one,
    text: readFileSync(join(import.meta.dirname, '..', one), 'utf8'),
  }));
  const { checked, residue } = movedVerbatim(original, modules);
  console.log(`${checked} body lines across ${modules.length} modules, against ${ref}:${originalPath}`);
  console.log(`${residue.length} not found verbatim in the original`);
  for (const one of residue) {
    console.log(`  ${one.name}: ${one.line.slice(0, 110)}`);
  }
  process.exitCode = residue.length === 0 ? 0 : 1;
}
