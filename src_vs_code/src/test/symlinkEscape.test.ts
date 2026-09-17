import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtempSync, mkdirSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { insideReally } from '../claudeSessions';

/**
 * A path is judged by WHERE IT LEADS, not by how it is spelled.
 *
 * <p><b>The defect this exists for.</b> `openWorkspaceFile` in `chatHooks.ts` decided containment
 * with `isInside(folder.uri.path, target.path)` — a comparison over the path AS WRITTEN — and then
 * called `vscode.workspace.fs.stat` and `vscode.window.showTextDocument`, both of which follow
 * links. A workspace holding `docs -> /home/me/.ssh` therefore passed the check and opened the file
 * outside it. The path arrives in a link inside a model's answer, which is the definition of an
 * untrusted input.</p>
 *
 * <p><b>Why the fix is a move rather than an invention.</b> `claudeSessions` had already been
 * through this, twice, at its own gate rounds: `staysInside` requires containment of the WRITTEN
 * pair and the CANONICAL pair, and `realOf` returns nothing rather than the path when `realpath`
 * throws, because a security check that answers "yes" because it could not run is worse than one
 * that is missing. The two were composed inline at one call site; `insideReally` is that composition
 * named, so the second caller does not get a second implementation. (`reuse-first.md`.)</p>
 *
 * <p><b>Why it hands back a path rather than a yes.</b> One caller needs the verdict and the other
 * needs the resolved file. A first draft made it a predicate and had the second caller return the
 * path as WRITTEN where it used to return the canonical one — a behaviour change smuggled in by a
 * refactor, which is the thing the series this came out of spent thirteen commits proving it had
 * not done. The last case below is what would have caught it.</p>
 *
 * <p><b>What this cannot prove.</b> `showTextDocument` re-opens BY PATH, so a local writer can still
 * replace the file between the check and the open. VS Code's API takes a `Uri` rather than a file
 * descriptor, so there is no no-follow handle to hand it; the race is bounded by the attacker
 * already being able to write inside the workspace, which is a far smaller set than "anything a
 * model can put in a link" — the set this removes.</p>
 */

/** A workspace, something outside it, and a handle to take it all away again. */
function aWorkspace(): { readonly root: string; readonly ws: string; readonly outside: string } {
  const root = mkdtempSync(join(tmpdir(), 'coai-escape-'));
  const ws = join(root, 'ws');
  const outside = join(root, 'outside');
  mkdirSync(ws, { recursive: true });
  mkdirSync(outside, { recursive: true });
  writeFileSync(join(outside, 'secret.txt'), 'the thing outside the workspace\n');
  writeFileSync(join(ws, 'honest.txt'), 'a file that is really here\n');

  return { root, ws, outside };
}

/** Windows refuses a directory symlink to an unprivileged process; a junction is the same trap, allowed. */
const linkDir = (target: string, at: string): void => {
  symlinkSync(target, at, process.platform === 'win32' ? 'junction' : 'dir');
};

test('a symlink pointing out of the workspace is refused, however it is spelled', async (t) => {
  const { root, ws, outside } = aWorkspace();
  t.after(() => rmSync(root, { recursive: true, force: true }));
  linkDir(outside, join(ws, 'docs'));

  // The path as WRITTEN is inside the workspace — that is the whole trap, and it is why the lexical
  // check passed it. Where it LEADS is the directory beside the workspace.
  assert.equal(await insideReally(ws, join(ws, 'docs', 'secret.txt')), undefined,
    'a link inside the workspace pointing outside it was accepted, so the file outside would open');
});

test('a real file inside the workspace still opens', async (t) => {
  const { root, ws } = aWorkspace();
  t.after(() => rmSync(root, { recursive: true, force: true }));

  // The companion the refusal needs. A containment check that refuses EVERYTHING passes the case
  // above while breaking the feature, and this is the case that says so.
  const answer = await insideReally(ws, join(ws, 'honest.txt'));

  assert.ok(answer !== undefined && answer.endsWith('honest.txt'),
    `an ordinary file in the workspace was refused, so the fix broke opening files: ${String(answer)}`);
});

test('a symlink that stays inside the workspace opens, and the CANONICAL path comes back', async (t) => {
  const { root, ws } = aWorkspace();
  t.after(() => rmSync(root, { recursive: true, force: true }));
  mkdirSync(join(ws, 'real'), { recursive: true });
  writeFileSync(join(ws, 'real', 'here.txt'), 'inside, reached through a link\n');
  linkDir(join(ws, 'real'), join(ws, 'linked'));

  // Two things at once, and the second is the one a predicate could not have said. A link is not the
  // defect — LEAVING is — so a link that stays inside is accepted; and what comes back is where the
  // file really is, not the way in. A version handing back the written path would end `linked/…`,
  // which is how a refactor changes behaviour while every other case stays green.
  const answer = await insideReally(ws, join(ws, 'linked', 'here.txt'));

  assert.ok(answer !== undefined, 'a link that never leaves the workspace was refused');
  assert.ok(answer.endsWith('here.txt'), `the answer is not the file that was asked for: ${answer}`);
  assert.ok(!answer.includes('linked'),
    `the path as WRITTEN came back instead of the canonical one, so a caller reads a path nothing verified: ${answer}`);
});

test('a sibling directory is not inside, and a prefix comparison would say it was', async (t) => {
  const { root, ws } = aWorkspace();
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const sibling = `${ws}-secret`;
  mkdirSync(sibling, { recursive: true });
  writeFileSync(join(sibling, 'keys.txt'), 'beside the workspace, not in it\n');

  // Written down because it is how this fix gets written wrong: a canonical comparison done with a
  // bare `startsWith` reintroduces exactly this, and `ws-secret` starts with `ws`.
  assert.equal(await insideReally(ws, join(sibling, 'keys.txt')), undefined,
    'a directory BESIDE the workspace was read as inside it');
});

test('a path that cannot be canonicalised is refused rather than guessed', async (t) => {
  const { root, ws } = aWorkspace();
  t.after(() => rmSync(root, { recursive: true, force: true }));

  // Failing CLOSED is the half that has been got wrong before in this repository: `realOf` used to
  // hand back the path unchanged when `realpath` threw, which is a check answering "yes" because it
  // could not run. Nothing is lost — a path that resolves nowhere could not have been opened.
  assert.equal(await insideReally(ws, join(ws, 'nothing', 'here.txt')), undefined,
    'a path that resolves nowhere was accepted');
});
