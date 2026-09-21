import {
  HeldTree, ListedTree, RemovalAnswer, RemovalRead, ReviewTreeAnswer,
  TOO_OLD_FOR_A_TREE, TOO_OLD_FOR_TREES, TreeRead, TreesAnswer, TreesRead,
} from './reviewTree';
import { Run, serverRun } from './roundsDbRead';

/**
 * Reading `--tree-at`: the mode that checks a commit out, and the shaping of its answer.
 *
 * <p><b>Why this is not in `roundsDbRead.ts` with its five siblings.</b> It was, and the file reached
 * 803 lines against the 800 the style rule allows. A code reviewer called the cap breached while the
 * file was still at 790 — which was wrong then, and became right two fixes later when the path guard
 * below was added for a different finding. The seam is clean: nothing in `roundsDbRead` reads a tree,
 * so the edge runs one way and this module imports the shared launcher rather than the reverse.</p>
 *
 * <p>It cannot live in `reviewTree.ts` either: that module is pure, free of `node:` and `vscode`,
 * because the page bundle is built from it. Spawning belongs on this side of that line.</p>
 */

/** A domain answer is never an exit code; 64 is ONLY "this binary never heard of the mode". */
const EX_USAGE = 64;

/** A checkout plus its submodules is minutes; the read cap would abandon a tree still being made. */
const TREE_CAP_MS = 10 * 60 * 1000;

/** A field as text, however the server spelled the absence of it. */
function textOf(one: Record<string, unknown>, key: string): string {
  const value = one[key];

  return typeof value === 'string' ? value : '';
}

function treeOf(raw: unknown, asked: AskedTree): ReviewTreeAnswer | undefined {
  if (raw === null || typeof raw !== 'object') {
    return undefined;
  }
  const one = raw as Record<string, unknown>;
  if (one['findingId'] !== asked.findingId) {
    return undefined;
  }

  // Same rule as a revision's answer, and for the same reason: an answer that CARRIES a tree is
  // checked against the commit that was asked about, because a pair recollected while the request
  // was in flight answers the same id at another commit — and this one would then open a window on
  // the wrong checkout. A reason-only answer has no tree to be wrong about.
  const carries = textOf(one, 'reason').length === 0;
  if (carries && (one['sha'] !== asked.headSha || one['repoPath'] !== asked.repoPath)) {
    return undefined;
  }

  // An answer that carries a tree is about to become a FOLDER this editor opens, so the one thing
  // worth insisting on is that it is an absolute path. The server builds it from its own root and a
  // digest, but a relative string would become a URI resolved against something nobody chose, and
  // the check is a line. (Code round, gemini.)
  if (carries && !absolute(textOf(one, 'path'))) {
    return undefined;
  }

  return {
    findingId: asked.findingId,
    sha: textOf(one, 'sha'),
    repoPath: textOf(one, 'repoPath'),
    path: textOf(one, 'path'),
    repository: textOf(one, 'repository'),
    reused: one['reused'] === true,
    reason: textOf(one, 'reason'),
    emptyMounts: textsOf(one['emptyMounts']),
    trees: heldOf(one['trees']),
  };
}

/** Absolute in the POSIX sense or the Windows one — the server may be either. */
function absolute(path: string): boolean {
  return path.startsWith('/') || /^[A-Za-z]:[/\\]/u.test(path);
}

function textsOf(raw: unknown): readonly string[] {
  return Array.isArray(raw) ? raw.filter((v): v is string => typeof v === 'string') : [];
}

function heldOf(raw: unknown): readonly HeldTree[] {
  if (!Array.isArray(raw)) {
    return [];
  }

  return raw
    .filter((v): v is Record<string, unknown> => v !== null && typeof v === 'object')
    .map((v) => ({
      repository: textOf(v, 'repository'),
      sha: textOf(v, 'sha'),
      path: textOf(v, 'path'),
      created: textOf(v, 'created'),
    }));
}

/**
 * The row a tree was asked about — all THREE coordinates, because the answer is checked against
 * every one of them.
 *
 * <p>The id and the commit alone are not an identity: a pair recollected while the request was in
 * flight can answer the same finding at the same commit for a DIFFERENT repository, and an answer
 * taken on trust would open the wrong one in a new window. (Code round, codex.)</p>
 */
export interface AskedTree {
  readonly findingId: number;
  readonly headSha: string;
  readonly repoPath: string;
}

/**
 * One pair's repository checked out at the commit the reviewers read — `--tree-at`, on stdout.
 *
 * <p>The third mode through this door and it obeys the same rule as the other two: 64 is "the server
 * is too old" and ONLY that, every other non-zero code is the server's own sentence, and a domain
 * outcome — a cap reached, another press already building it, a commit that is gone — is `ok: true`
 * with the reason on the answer.</p>
 *
 * <p>The cap is longer than a read's because this one checks a repository out and fills its
 * submodules; a large repository is minutes, not seconds.</p>
 */
export async function readTreeAt(
  executable: string,
  asked: AskedTree,
  run: Run = serverRun(executable),
): Promise<TreeRead> {
  const { code, output } = await run(['--tree-at', '--id', String(asked.findingId)], TREE_CAP_MS);
  if (code === EX_USAGE) {
    return { ok: false, tooOld: true, why: TOO_OLD_FOR_A_TREE };
  }
  if (code !== 0) {
    return { ok: false, tooOld: false, why: output.trim() || `the server exited ${code}` };
  }

  try {
    const tree = treeOf(JSON.parse(output), asked);

    return tree === undefined
      ? { ok: false, tooOld: false, why: 'the server answered something this panel does not understand' }
      : { ok: true, tree };
  } catch {
    return { ok: false, tooOld: false, why: 'the server answered something that is not JSON' };
  }
}

// --------------------------------------------------------------------------------------------
// Story 3.2b: what this machine holds, and giving one back.
// --------------------------------------------------------------------------------------------

/**
 * Every review tree this machine holds — `--trees`, on stdout.
 *
 * <p>Takes no id, so there is no 65 to meet here. 64 is "this server is too old" and ONLY that, which
 * is the one code a reader must never mistake for an answer: a fault wearing it would send a person
 * down the fallback and hide behind a version they cannot check. (Plan round, local, three times.)</p>
 */
export async function readTrees(
  executable: string,
  run: Run = serverRun(executable),
): Promise<TreesRead> {
  const { code, output } = await run(['--trees'], LIST_CAP_MS);
  if (code === EX_USAGE) {
    return { ok: false, tooOld: true, why: TOO_OLD_FOR_TREES };
  }
  if (code !== 0) {
    return { ok: false, tooOld: false, why: output.trim() || `the server exited ${code}` };
  }

  try {
    const answer = treesOf(JSON.parse(output));

    return answer === undefined
      ? { ok: false, tooOld: false, why: 'the server answered something this panel does not understand' }
      : { ok: true, answer };
  } catch {
    return { ok: false, tooOld: false, why: 'the server answered something that is not JSON' };
  }
}

/**
 * One tree given back — `--tree-remove --tree <name>`.
 *
 * <p><b>The name is echoed and checked.</b> A removal is the one call here whose answer must be about
 * the row the person pressed; an answer about another name would put another tree's sentence on it.</p>
 *
 * <p><b>65 is a request fault and must not read as an old server.</b> A missing `--tree` is the only
 * way to get one, and the reader says the server's own sentence rather than the fallback.</p>
 */
export async function removeTree(
  executable: string,
  name: string,
  withIgnored = false,
  run: Run = serverRun(executable),
): Promise<RemovalRead> {
  const args = withIgnored
    ? ['--tree-remove', '--tree', name, '--with-ignored']
    : ['--tree-remove', '--tree', name];
  const { code, output } = await run(args, REMOVE_CAP_MS);
  if (code === EX_USAGE) {
    return { ok: false, tooOld: true, why: TOO_OLD_FOR_TREES };
  }
  if (code !== 0) {
    return { ok: false, tooOld: false, why: output.trim() || `the server exited ${code}` };
  }

  try {
    const answer = removalOf(JSON.parse(output), name);

    return answer === undefined
      ? { ok: false, tooOld: false, why: 'the server answered something this panel does not understand' }
      : { ok: true, answer };
  } catch {
    return { ok: false, tooOld: false, why: 'the server answered something that is not JSON' };
  }
}

function treesOf(raw: unknown): TreesAnswer | undefined {
  if (raw === null || typeof raw !== 'object' || Array.isArray(raw)) {
    return undefined;
  }
  const one = raw as Record<string, unknown>;

  return {
    root: textOf(one, 'root'),
    trees: listedOf(one['trees']),
    reason: textOf(one, 'reason'),
  };
}

function listedOf(raw: unknown): readonly ListedTree[] {
  if (!Array.isArray(raw)) {
    return [];
  }

  return raw
    .filter((v): v is Record<string, unknown> => v !== null && typeof v === 'object')
    .map((v) => ({
      name: textOf(v, 'name'),
      repository: textOf(v, 'repository'),
      repoPath: textOf(v, 'repoPath'),
      sha: textOf(v, 'sha'),
      path: textOf(v, 'path'),
      created: textOf(v, 'created'),
      state: textOf(v, 'state'),
    }))
    .filter((t) => t.name.length > 0);
}

function removalOf(raw: unknown, asked: string): RemovalAnswer | undefined {
  if (raw === null || typeof raw !== 'object' || Array.isArray(raw)) {
    return undefined;
  }
  const one = raw as Record<string, unknown>;
  if (one['name'] !== asked || textOf(one, 'reason').length === 0) {
    return undefined;
  }

  return {
    name: asked,
    reason: textOf(one, 'reason'),
    inTheWay: textsOf(one['inTheWay']),
    ignored: typeof one['ignored'] === 'number' ? one['ignored'] : 0,
    ignoredSample: textsOf(one['ignoredSample']),
  };
}

/** Listing asks git once per repository; a removal checks out nothing but may wait on a big delete. */
const LIST_CAP_MS = 2 * 60 * 1000;
const REMOVE_CAP_MS = 5 * 60 * 1000;
