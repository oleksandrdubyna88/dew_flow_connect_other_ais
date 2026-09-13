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
  // The consultant half is this PRODUCT's own file rather than a mounted rule, so the fixture
  // needs it too — and taking the real one keeps the fixture honest about what the build reads.
  fs.mkdirSync(path.dirname(path.join(root, CONSULTANT_SOURCE)), { recursive: true });
  fs.copyFileSync(path.join(sourceRoot, CONSULTANT_SOURCE), path.join(root, CONSULTANT_SOURCE));
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
  // A consultant source that is not ours by its marker fails the build rather than shipping
  // text nobody can recognise as the block.
  const consultantSource = path.join(root, CONSULTANT_SOURCE);
  const ourBlock = fs.readFileSync(consultantSource);
  fs.writeFileSync(consultantSource, '## Something else entirely\n');
  assert.throws(() => prepareGate(root), /coai-consultant/);
  fs.writeFileSync(consultantSource, ourBlock);
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
