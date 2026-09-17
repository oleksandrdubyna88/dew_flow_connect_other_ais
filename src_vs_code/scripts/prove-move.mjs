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
 * The moved modules, checked against the original IN ORDER.
 *
 * <p><b>Membership alone is not enough, and three reviewers said so in three ways.</b> The first
 * version of this asked only whether each line existed SOMEWHERE in the original. That accepts a
 * reordering: turn <code>await memory.remember(); await page.push();</code> around and every line is
 * still present, so the proof reports zero residue while the observable order of two writes has
 * changed. It also accepts a substitution whenever the replacement happens to occur elsewhere in a
 * four-thousand-line file, which is not a remote possibility in a file with repeated idioms.</p>
 *
 * <p>So the body is matched as a sequence of CONTIGUOUS RUNS of the original. A move produces a
 * handful of long runs — one per region cut — and every statement inside a run keeps its neighbours
 * and its order. A reordering inside a function breaks the run at exactly the line that moved, so it
 * shows up both as a jump in the run count and, when nothing downstream matches, as residue.</p>
 *
 * <p>The run count is returned rather than judged: only the caller knows how many regions it cut.
 * Fifteen modules assembled from twenty-two cut regions should report about twenty-two runs, and a
 * number far above that is the signal to read the diff rather than the summary.</p>
 *
 * @param original the file the modules were carved out of, as it was BEFORE the split
 * @param modules `{ name, text }` for each new module, each including its own new import block
 * @returns `{ checked, residue, runs }`
 */
export function movedVerbatim(original, modules) {
  const source = original.split('\n').map(asCompared).filter((one) => one.trim().length > 0);
  // Where each line of the original occurs, so the next line of a run is found without rescanning.
  const at = new Map();
  for (const [index, line] of source.entries()) {
    const seen = at.get(line);
    if (seen === undefined) {
      at.set(line, [index]);
    } else {
      seen.push(index);
    }
  }
  const whereverItIs = (line) => at.get(line) ?? at.get(unexported(line)) ?? [];

  const residue = [];
  let checked = 0;
  let runs = 0;
  for (const { name, text } of modules) {
    const body = bodyOf(text).split('\n').map(asCompared).filter((one) => one.trim().length > 0);
    let next = -1;
    for (const [index, line] of body.entries()) {
      const raw = line;
      checked += 1;
      if (next >= 0 && (source[next] === line || source[next] === unexported(line))) {
        next += 1;
        continue;
      }
      // The run broke, so a new one starts wherever this line occurs. WHICH occurrence matters: a
      // line like `}` appears hundreds of times, and restarting at the first one lands the walk in
      // an unrelated function, where the next line mismatches and the run breaks again. Chased
      // greedily that inflates the count into noise — measured at 52 runs for 27 real regions — and
      // a budget over a noisy number is not a check. So the occurrence that matches FURTHEST is
      // taken, which is the one the region actually came from.
      const starts = whereverItIs(line);
      if (starts.length === 0) {
        residue.push({ name, line: raw.trim() });
        next = -1;
        continue;
      }
      runs += 1;
      next = bestStart(source, starts, body, index) + 1;
    }
  }

  return { checked, residue, runs };
}

/**
 * Of the places this line occurs in the original, the one whose following lines match furthest.
 *
 * <p>Capped, because a line that occurs three hundred times against a body of three hundred lines is
 * the only shape that could make this slow, and the cap costs nothing else: past a few hundred
 * lines of agreement the candidate is obviously the right one.</p>
 */
function bestStart(source, starts, body, from) {
  let best = starts[0];
  let longest = -1;
  for (const candidate of starts) {
    let reach = 0;
    while (reach < 400
      && source[candidate + reach] !== undefined
      && body[from + reach] !== undefined
      && (source[candidate + reach] === body[from + reach]
        || source[candidate + reach] === unexported(body[from + reach]))) {
      reach += 1;
    }
    if (reach > longest) {
      longest = reach;
      best = candidate;
    }
  }

  return best;
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
  const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  // PINNED, and the SHA is printed. A branch name moves: `origin/main` advancing under a long
  // extraction can reject a faithful move because the reference gained unrelated edits, or accept a
  // changed line because the replacement now exists in the newer file. Neither says anything about
  // the code actually being refactored. (codex, the plan round.) Quote the printed SHA in the commit.
  const sha = git('rev-parse', ref).trim();
  const original = git('show', `${sha}:${join('src_vs_code', originalPath).replace(/\\/gu, '/')}`);
  const modules = modulePaths.map((one) => ({
    name: one,
    text: readFileSync(join(import.meta.dirname, '..', one), 'utf8'),
  }));
  const declared = Number(process.env.PROVE_MOVE_REGIONS ?? '0');
  const { checked, residue, runs } = movedVerbatim(original, modules);
  console.log(`against ${originalPath} at ${sha}${sha.startsWith(ref) ? '' : ` (${ref})`}`);
  console.log(`${checked} body lines across ${modules.length} modules, in ${runs} contiguous run(s)`);
  console.log(`${residue.length} not found in order in the original`);
  for (const one of residue) {
    console.log(`  ${one.name}: ${one.line.slice(0, 110)}`);
  }
  // THE RUN BUDGET is how order is enforced. Residue catches a line the original never had; the run
  // count catches a line it had somewhere ELSE — a reordering, or a statement borrowed from another
  // function. A move cuts a known number of regions, so the caller declares it and the walk has to
  // fit. Without a budget the tool would have to guess what "too fragmented" means, and a guess in
  // a guard is a guard nobody trusts.
  const tooMany = declared > 0 && runs > declared;
  if (tooMany) {
    console.log(`runs ${runs} exceeds the ${declared} regions declared in PROVE_MOVE_REGIONS —`
      + ' something was reordered, or cut into more pieces than the caller thinks');
  }
  process.exitCode = residue.length === 0 && !tooMany ? 0 : 1;
}
