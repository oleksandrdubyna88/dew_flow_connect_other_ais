import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { execFileSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { consultantBody, gateBody, prepareGate, CONSULTANT_OUTPUT, CONSULTANT_SOURCE, OUTPUT } from '../../scripts/prepare-gate.mjs';

test('only leading metadata is removed and a later delimiter remains in the gate body', () => {
  const body = '<!-- coai-snippet v5 -->\n## Multi-model review gate (ConnectOtherAIs)\n\nFirst.\n---\nSecond.\n';
  assert.equal(gateBody('---\nid: common.gate\n---\n' + body), body);
  assert.equal(gateBody(('---\nid: common.gate\n---\n' + body).replaceAll('\n', '\r\n')), body);
  assert.throws(() => gateBody('---\nmissing closing delimiter'), /unterminated/);
  assert.throws(() => gateBody('---\nid: common.gate\n---\nempty'), /missing canonical/);
});

test('the ownership markers a rule carries are metadata too, and never reach the paste', () => {
  // The conventions release of 2026-09-15 armed an ownership check, and its `owns:` lines sit
  // between the frontmatter and the snippet marker. They are delivery metadata exactly as the
  // frontmatter is — they say which product names a shared rule is allowed to use — and pasting
  // them into somebody's CLAUDE.md would ship a lint annotation as an instruction. Until this they
  // were not stripped, so the body no longer STARTED with the marker and the whole build stopped
  // with `missing canonical marker`.
  const body = '<!-- coai-snippet v5 -->\n## Multi-model review gate (ConnectOtherAIs)\n\nFirst.\n';
  const owns = "<!-- owns: coai — the MCP server's own name -->\n<!-- owns: ConnectOtherAIs — the product -->\n";
  const commentary = '<!-- A token is matched EXACTLY, so one name does not cover another. -->\n';

  assert.equal(gateBody('---\nid: common.gate\n---\n' + owns + body), body);
  assert.equal(gateBody('---\nid: common.gate\n---\n' + commentary + owns + body), body);
  assert.equal(gateBody(('---\nid: common.gate\n---\n' + owns + body).replaceAll('\n', '\r\n')), body);

  // A comment INSIDE the instruction body is the author's and stays: only what precedes the marker
  // is metadata, so this must not become a licence to strip comments generally.
  const withInner = '<!-- coai-snippet v5 -->\n## Multi-model review gate (ConnectOtherAIs)\n\n<!-- keep me -->\nFirst.\n';
  assert.equal(gateBody('---\nid: common.gate\n---\n' + withInner), withInner);

  // And leading comments that never reach a marker are still a refused file, not an empty body.
  assert.throws(() => gateBody('---\nid: common.gate\n---\n' + owns + 'empty'), /missing canonical/);
});

test('the real pinned resolver rejects a dirty or wrong mount and leaves no stale build policy', async t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-pinned-gate-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const sourceRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
  const sourceMount = path.join(sourceRoot, '.agents/conventions');
  const git = (cwd, ...args) => execFileSync('git', ['-C', cwd, ...args], {
    encoding: 'utf8', timeout: 30_000, maxBuffer: 1024 * 1024, windowsHide: true,
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  const commit = cwd => git(cwd, '-c', 'user.name=Gate fixture', '-c', 'user.email=gate@example.invalid',
    '-c', 'commit.gpgsign=false', 'commit', '--allow-empty', '-qm', 'fixture');
  git(root, 'init', '-q');
  git(root, '-c', 'protocol.file.allow=always', 'submodule', 'add', '-q', sourceMount, '.agents/conventions');
  const mount = path.join(root, '.agents/conventions');
  // Keep a real ignored directory: Git's node_modules/ pattern does not ignore a POSIX symlink.
  fs.cpSync(path.join(sourceMount, 'node_modules'), path.join(mount, 'node_modules'), { recursive: true });
  fs.copyFileSync(path.join(sourceRoot, 'AGENTS.md'), path.join(root, 'AGENTS.md'));
  fs.writeFileSync(path.join(root, 'CLAUDE.md'), '@AGENTS.md\n');
  fs.writeFileSync(path.join(root, '.agents/PROJECT.md'), '# Fixture project\n');
  // The consultant half is a MOUNTED rule since story 5.2 of research/PLAN_consult_on_a_cadence.md: the
  // submodule above carries it, and the fixture needs nothing of this product's own.
  git(root, 'add', 'AGENTS.md', 'CLAUDE.md', '.agents/PROJECT.md');
  commit(root);
  const output = path.join(root, OUTPUT);
  const consultantOutput = path.join(root, CONSULTANT_OUTPUT);
  const body = prepareGate(root);
  // EXECUTED, not string-matched: generated code that is only searched for a substring can be
  // syntactically broken and still pass, which is what the shared rule on generated code forbids.
  // Both files are ES modules whose TypeScript is also valid JavaScript, so importing a copy runs
  // exactly what the build emitted. (codex, code round, Blocking.)
  assert.equal(await exported(consultantOutput, 'CONSULTANT_RULE'),
    consultantBody(fs.readFileSync(path.join(root, CONSULTANT_SOURCE), 'utf8')),
    'the generated consultant module exports the block');
  assert.equal(await exported(output, 'GATE_RULE'), body, 'the generated gate module exports the canonical body');
  fs.writeFileSync(output + '.tmp', 'interrupted generation');
  assert.equal(prepareGate(root), body, 'the next build recovers after interruption');
  assert.equal(fs.existsSync(output + '.tmp'), false);
  // Generated artifacts are ignored in the product; keep this fixture equivalent.
  fs.writeFileSync(path.join(root, '.gitignore'), 'src_vs_code/src/generated/\n');
  const rule = path.join(mount, 'common/coai-review-gate.md');
  const original = fs.readFileSync(rule);
  fs.appendFileSync(rule, '\nUnreviewed edit.\n');
  assert.throws(() => prepareGate(root), /uncommitted changes/i);
  assert.equal(fs.existsSync(output), false);
  fs.writeFileSync(rule, original);
  prepareGate(root);
  commit(mount);
  assert.throws(() => prepareGate(root), /pin|gitlink|mismatch/i);
  assert.equal(fs.existsSync(output), false);
});

/**
 * The value a generated module actually exports, by running it.
 *
 * <p>Copied to a `.mjs` beside it first: the emitted file is `.ts` by name, and Node will not import
 * that — but its contents are a plain ES module (`export const X = "…";`) with no TypeScript syntax
 * in them, which is the property this copy relies on and the reason it is worth asserting.</p>
 */
async function exported(file, name) {
  const asModule = file.replace(/\.ts$/, `.${Date.now()}.mjs`);
  fs.copyFileSync(file, asModule);
  try {
    return (await import(pathToFileURL(asModule).href))[name];
  } finally {
    fs.rmSync(asModule, { force: true });
  }
}

test('missing canonical checkout fails preparation and invalidates previous generated policy', t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-prepare-gate-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const output = path.join(root, OUTPUT);
  fs.mkdirSync(path.dirname(output), { recursive: true });
  fs.writeFileSync(output, 'previous generated policy');
  assert.throws(() => prepareGate(root), /git submodule update --init/);
  assert.equal(fs.existsSync(output), false);
});

/**
 * A consultant rule that is not ours by its marker is refused rather than shipped.
 *
 * <p>Asked of `consultantBody` directly since the source moved into the mount: corrupting the mounted
 * file made the mount DIRTY, and the resolver — which runs first — refused it for that instead, so the
 * old case went red for a reason that was not the one it named (the risk consultation for story 5.2,
 * point 1).</p>
 */
test('a consultant rule without its marker is refused, with and without delivery metadata', () => {
  const body = '<!-- coai-consultant v3 -->\n## When you are stuck, ask another vendor (ConnectOtherAIs)\n\nFirst.\n';
  const owns = "<!-- owns: coai — the MCP server's own name -->\n";

  assert.equal(consultantBody('---\nid: common.coai-consultant\n---\n' + owns + body), body);
  assert.throws(() => consultantBody('---\nid: common.coai-consultant\n---\n## Something else entirely\n'), /coai-consultant/);
  assert.throws(() => consultantBody('## Something else entirely\n'), /coai-consultant/);
});

/**
 * A clean, correctly pinned mount that LACKS the consultant rule fails the build naming it.
 *
 * <p>The case a consumer meets when its pin predates the move: nothing is dirty and nothing
 * mismatches, the file is simply not there — and a bare ENOENT would not say which half is missing
 * or why.</p>
 */
test('a pinned mount without the consultant rule fails naming it', async t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-gate-no-consultant-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const sourceRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
  const sourceMount = path.join(sourceRoot, '.agents/conventions');
  const git = (cwd, ...args) => execFileSync('git', ['-C', cwd, ...args], {
    encoding: 'utf8', timeout: 30_000, maxBuffer: 1024 * 1024, windowsHide: true,
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  const commit = (cwd, message) => git(cwd, '-c', 'user.name=Gate fixture', '-c', 'user.email=gate@example.invalid',
    '-c', 'commit.gpgsign=false', 'commit', '--allow-empty', '-qm', message);
  git(root, 'init', '-q');
  git(root, '-c', 'protocol.file.allow=always', 'submodule', 'add', '-q', sourceMount, '.agents/conventions');
  const mount = path.join(root, '.agents/conventions');
  fs.cpSync(path.join(sourceMount, 'node_modules'), path.join(mount, 'node_modules'), { recursive: true });
  // The rule and its body record leave together, as a conventions commit from before the move would.
  git(mount, 'rm', '-q', 'common/coai-consultant.md');
  const bodies = path.join(mount, 'research/rule-bodies.json');
  const manifest = JSON.parse(fs.readFileSync(bodies, 'utf8'));
  manifest.rules = manifest.rules.filter((rule) => rule.id !== 'common.coai-consultant');
  fs.writeFileSync(bodies, JSON.stringify(manifest, null, 2) + '\n');
  git(mount, 'add', 'research/rule-bodies.json');
  commit(mount, 'a conventions pin from before the consultant rule');
  fs.copyFileSync(path.join(sourceRoot, 'AGENTS.md'), path.join(root, 'AGENTS.md'));
  fs.writeFileSync(path.join(root, 'CLAUDE.md'), '@AGENTS.md\n');
  fs.writeFileSync(path.join(root, '.agents/PROJECT.md'), '# Fixture project\n');
  git(root, 'add', 'AGENTS.md', 'CLAUDE.md', '.agents/PROJECT.md', '.agents/conventions');
  commit(root, 'fixture');

  assert.throws(() => prepareGate(root), (error) => error instanceof Error
    && error.message.includes('.agents/conventions/common/coai-consultant.md') && /consultant/i.test(error.message));
  assert.equal(fs.existsSync(path.join(root, CONSULTANT_OUTPUT)), false, 'no consultant constant is left from a previous build');
});
