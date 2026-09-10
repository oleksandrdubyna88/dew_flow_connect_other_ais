import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

/**
 * The Claude host adapter loads this repository's instructions, and does it the sanctioned way.
 *
 * <p>The shared layout is one source for two hosts, and it is enforced: `validateInstructions` in
 * `.agents/conventions/tools/lib/rule-cli.mjs` refuses a `CLAUDE.md` that is anything but
 * `@AGENTS.md`, and refuses a non-empty `.claude/rules`. Those are the two doors Claude Code loads
 * project instructions through by itself, so closing them left a session holding an instruction to
 * go and read the rules instead of the rules.</p>
 *
 * <p>The `SessionStart` hook is the third door, and the only one the resolver does not police. That
 * is precisely why it needs a test: nothing outside this file would notice if the wiring were
 * dropped, and the failure is silent — a session that simply does not have the rules behaves like a
 * session that has them and ignores them.</p>
 */

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const HOOK = '.claude/hooks/load-instructions.mjs';

/** What must arrive before the first decision, whatever the session turns out to be about. */
const ALWAYS = [
  'common.coding-style',
  'common.knowledge-base',
  'common.security',
  // This project's own, and the reason the guarantee matters rather than a nicety: breaking
  // vendor-routing is invisible in the output and has already cost three cells of a measurement.
  'local.common.review-gate',
  'local.common.vendor-routing',
];

test('the session-start hook is wired, and points at a file that exists', () => {
  const settings = JSON.parse(fs.readFileSync(path.join(root, '.claude/settings.json'), 'utf8'));
  // Command AND args, because the wiring is in exec form: the command is `node` and the script is
  // an argument, addressed through ${CLAUDE_PROJECT_DIR}. A relative path was the first version and
  // CodeRabbit was right about it — a session started outside the project root would have failed to
  // find the file, and the hook's own careful degradation cannot run when node cannot load it.
  const commands = (settings.hooks?.SessionStart ?? [])
    .flatMap((entry) => entry.hooks ?? [])
    .map((hook) => [hook.command ?? '', ...(hook.args ?? [])].join(' '));

  assert.ok(
    commands.some((command) => command.includes(`\${CLAUDE_PROJECT_DIR}/${HOOK}`)),
    `no SessionStart hook runs ${HOOK} by an absolute path — a session started outside the project `
      + 'root would not find it, and one started inside would get 561 bytes of instructions',
  );
  assert.ok(fs.existsSync(path.join(root, HOOK)), `${HOOK} is wired but missing`);
});

test('the enforced doors are left shut, so the shared resolver still calls this repository resolved', () => {
  // Not a formality. Restoring instructions the OBVIOUS way — @-imports in CLAUDE.md, or files
  // under .claude/rules — makes `rules check` report INCOMPLETE, which by ENTRY.md means edits are
  // blocked. The hook exists because both of those are refused, and this asserts nobody undid that.
  assert.equal(fs.readFileSync(path.join(root, 'CLAUDE.md'), 'utf8').trim(), '@AGENTS.md');

  const legacy = path.join(root, '.claude/rules');
  assert.ok(
    !fs.existsSync(legacy) || fs.readdirSync(legacy).length === 0,
    '.claude/rules is populated again; the resolver refuses that as a second source',
  );
});

test('the hook emits every always-rule, from the pinned canonical source', (t) => {
  const resolver = path.join(root, '.agents/conventions/tools/rules.mjs');
  if (!fs.existsSync(resolver) || !fs.existsSync(path.join(root, '.agents/conventions/node_modules'))) {
    const message = 'the conventions submodule is not prepared — run git submodule update --init '
      + '.agents/conventions && npm ci --ignore-scripts --prefix .agents/conventions';
    // Under CI both are present, and a skip there would hide exactly what this test is for.
    assert.ok(process.env.CI === undefined, `${message} (CI must not skip this)`);
    t.skip(message);

    return;
  }

  const emitted = execFileSync(process.execPath, [path.join(root, HOOK)], {
    cwd: root, encoding: 'utf8', timeout: 120_000, maxBuffer: 8 * 1024 * 1024, windowsHide: true,
  });

  // Matched as a literal line rather than through a pattern built from the id. Escaping a string
  // into a regex is a thing to get subtly wrong — CodeQL said so about the first version of this
  // line, and it was right in general even though these ids are a constant list — and `startsWith`
  // cannot be got wrong at all.
  const opened = emitted.split('\n').map((line) => line.trimEnd());
  for (const id of ALWAYS) {
    assert.ok(
      opened.some((line) => line.startsWith(`BEGIN RULE ${id} sha256:`)),
      `${id} did not reach the session`,
    );
  }
  assert.match(emitted, /rules\.mjs explain/,
    'the hook must still send the reader to explain/read for the rules its neutral task cannot select');
});

test('a session without the resolver is told loading is INCOMPLETE, and is not killed for it', (t) => {
  const resolver = path.join(root, '.agents/conventions/tools/rules.mjs');
  if (!fs.existsSync(resolver)) {
    t.skip('nothing to move aside');

    return;
  }
  const parked = `${resolver}.parked-by-test`;
  fs.renameSync(resolver, parked);
  t.after(() => fs.renameSync(parked, resolver));

  // Exit 0 on purpose: a hook that fails a fresh clone is a hook somebody deletes.
  const said = execFileSync(process.execPath, [path.join(root, HOOK)], {
    cwd: root, encoding: 'utf8', timeout: 60_000, windowsHide: true,
  });

  assert.match(said, /INCOMPLETE/);
  assert.match(said, /git submodule update --init \.agents\/conventions/);
  assert.match(said, /npm ci --ignore-scripts --prefix \.agents\/conventions/);
});
