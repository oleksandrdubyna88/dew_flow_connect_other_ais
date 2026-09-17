import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { cyclesIn, graphOf, importsOf } from '../../scripts/import-graph.mjs';

/**
 * No module of the chat command may sit in an import cycle, and the rest may only improve.
 *
 * <p><b>Why this test exists, precisely.</b> A cycle between two ES modules bundles without
 * complaint — esbuild resolves the symbols and the build is green — and fails at RUNTIME with
 * <code>Cannot access 'X' before initialization</code>, or a call on <code>undefined</code>, when
 * the module still initialising reaches for a binding the other has not reached yet. Nothing in this
 * repository starts an extension host, so no other check here can see it.</p>
 *
 * <p>Splitting one 4 183-line file into fifteen is exactly the change that invents cycles, and three
 * reviewers said so at the plan round: the bundle succeeding is not evidence that the graph is
 * acyclic. It is measured now instead.</p>
 *
 * <p>A RATCHET rather than a ban. Nine cycles predate this check; banning cycles outright would mean
 * arguing with the author of each before anything could land, and a rule nobody can satisfy is a
 * rule that gets deleted. The nine are frozen by name so a TENTH is named the day it appears, and
 * the count may only fall.</p>
 */

const SOURCE = join(import.meta.dirname, '..', '..', 'src');

/**
 * The cycles this repository had when the check was written, 2026-09-17.
 *
 * <p>Every one is a pair of modules that reference each other's types or constants. None involves
 * the chat command or anything split out of it, which is the claim the test below makes separately
 * and the reason this list is frozen rather than fixed: untangling nine unrelated pairs is not part
 * of a refactor that promises no behaviour change.</p>
 */
const KNOWN = [
  'builtinRoles.generated ↔ prompts',
  'chatModels ↔ chatPage',
  'claudeModels ↔ models',
  'helpContent ↔ helpDe',
  'helpContent ↔ helpEs',
  'helpContent ↔ helpRu',
  'helpContent ↔ helpUk',
  'models ↔ vendors',
  'roundsCsv ↔ roundsDbRead ↔ roundsExport',
];

/** The fifteen the command file was split into, plus the command file itself. */
const SPLIT = [
  'chatCommand', 'chatThread', 'chatHost', 'chatRoots', 'chatConfig', 'chatCapture', 'chatPersist',
  'chatShow', 'chatSessionJoin', 'chatRegistry', 'chatFollow', 'chatArchive', 'chatLaunch',
  'chatTurn', 'chatHooks', 'chatConversationRestore',
];

test('no module of the chat command sits in an import cycle', () => {
  // The claim the split has to earn. `chatModels ↔ chatPage` is in the frozen list and neither is a
  // module this series created — the split IMPORTS both and adds no edge back.
  const tangled = cyclesIn(graphOf(SOURCE))
    .filter((ring) => ring.some((one) => SPLIT.includes(one)));

  assert.deepEqual(tangled, [],
    'a module of the chat command is in an import cycle — it will bundle, and then fail at runtime '
    + 'with "Cannot access X before initialization" the first time the extension activates');
});

test('the cycles this repository already had are frozen, and the count may only fall', () => {
  const rings = cyclesIn(graphOf(SOURCE)).map((ring) => ring.join(' ↔ '));
  const added = rings.filter((one) => !KNOWN.includes(one));

  assert.deepEqual(added, [], 'a NEW import cycle appeared; it will bundle and fail at runtime');
  assert.ok(rings.length <= KNOWN.length,
    `the cycle count rose from ${KNOWN.length} to ${rings.length}`);
  // Gone is good, and the list is meant to shrink — but it must shrink in the file too, or the next
  // person reads a frozen list that is partly fiction.
  const fixed = KNOWN.filter((one) => !rings.includes(one));
  assert.deepEqual(fixed, [],
    'a cycle in the frozen list is gone, which is good — take it out of KNOWN so the list stays true');
});

test('only the extension’s own modules count: node: and vscode are not cycles', () => {
  const found = importsOf([
    "import * as fs from 'node:fs';",
    "import * as vscode from 'vscode';",
    "import { a } from './alpha';",
    "import { b } from '../beta';",
  ].join('\n'));

  assert.deepEqual(found, ['./alpha', '../beta'], 'a built-in or a package was read as a local module');
});

test('a re-export and a dynamic import are edges too', () => {
  // `export … from` and `import('…')` both make the other module load, so both can close a ring.
  const found = importsOf("export { a } from './alpha';\nvoid import('./beta');");

  assert.deepEqual(found, ['./alpha', './beta'],
    'a re-export or a dynamic import was missed, so a cycle through one is invisible');
});

test('both quote styles are edges, because a guard must not depend on house style', () => {
  // The first version matched single quotes only — this repository's own style, and therefore
  // exactly the wrong thing to rely on. (codex, the code round.)
  const found = importsOf('import { a } from "./alpha";\nimport { b } from \'./beta\';');

  assert.deepEqual(found, ['./alpha', './beta'], 'a double-quoted import is invisible to the cycle check');
});

test('a module in a subfolder is scanned, and its relative imports resolve', () => {
  // `src/` is flat today. A check that assumed it would stay flat would report no cycle for two
  // modules put in a subfolder tomorrow — silence that reads exactly like safety.
  const here = mkdtempSync(join(tmpdir(), 'coai-graph-'));
  try {
    mkdirSync(join(here, 'inner'));
    writeFileSync(join(here, 'top.ts'), "import { a } from './inner/deep';\n");
    writeFileSync(join(here, 'inner', 'deep.ts'), "import { b } from '../top';\n");

    assert.deepEqual(cyclesIn(graphOf(here)), [['inner/deep', 'top']],
      'a cycle through a subfolder was not seen, so a future layout hides one');
  } finally {
    rmSync(here, { recursive: true, force: true });
  }
});

test('a two-module ring is found, and reported as a set rather than a rotation', () => {
  const rings = cyclesIn(new Map([['alpha', ['beta']], ['beta', ['alpha']]]));

  assert.deepEqual(rings, [['alpha', 'beta']], 'a plain A-B-A cycle was missed or reported twice');
});

test('a longer ring is found, and a diamond that is not a cycle is not', () => {
  const ring = cyclesIn(new Map([['a', ['b']], ['b', ['c']], ['c', ['a']]]));
  assert.deepEqual(ring, [['a', 'b', 'c']], 'a three-module cycle was missed');

  const diamond = cyclesIn(new Map([['top', ['left', 'right']], ['left', ['end']], ['right', ['end']], ['end', []]]));
  assert.deepEqual(diamond, [], 'two paths to one module were read as a cycle');
});
