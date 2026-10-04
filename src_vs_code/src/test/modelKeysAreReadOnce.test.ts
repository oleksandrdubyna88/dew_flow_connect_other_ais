import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { MODEL_KEYS } from '../modelKeys';

/**
 * The model-bearing settings are read through ONE door (PLAN_one_model_catalog.md, E1.1).
 *
 * <p>The rule in `modelKeys.ts` holds only while nothing goes around it: a single
 * `getConfiguration('coai').get('vendors')` somewhere else reads the workspace value again, and the
 * reader's own tests stay green. The plan round named exactly that — a measure applied at SOME sites —
 * so the absence of every other read is asserted, and the scan is shown to catch the shapes it looks for.</p>
 */

const SRC = path.join(__dirname, '..', '..', 'src');

/** Every production source file, recursively; `test` and `generated` skipped, as the wiring tests do. */
function sourceFiles(dir = ''): string[] {
  return fs.readdirSync(path.join(SRC, dir), { withFileTypes: true }).flatMap((entry) => {
    const here = dir === '' ? entry.name : `${dir}/${entry.name}`;
    if (entry.isDirectory()) {
      return ['test', 'generated'].includes(entry.name) ? [] : sourceFiles(here);
    }

    return entry.name.endsWith('.ts') ? [here] : [];
  });
}

/** A direct host read of one model key: `.get('vendors')`, `.get<unknown>('vendors')`, `.inspect("vendors")`. */
const DIRECT_READ = new RegExp(
  String.raw`\.(?:get|inspect)\s*(?:<[^>()]*>)?\s*\(\s*['"\x60](?:${MODEL_KEYS.join('|')})['"\x60]`,
  'u',
);

/**
 * The files that ARE the door — and the catalog migration, which reads the user layer RAW
 * (`inspect().globalValue`, no manifest default, never a workspace value), which is stricter than the door:
 * it must tell a key the person never set from one set to its default, or it would migrate a default.
 */
const DOOR = ['modelKeys.ts', 'sideConfig.ts', 'catalogMigrationHost.ts'];

test('the scan recognises every shape of a direct read it is there to refuse', () => {
  for (const planted of [
    "vscode.workspace.getConfiguration('coai').get('vendors')",
    'config.get<unknown>("consultants")',
    "config.inspect('chatModelPresets')",
    'config .get ( `securityLane` )',
  ]) {
    assert.match(planted, DIRECT_READ, `the scan would miss ${planted}`);
  }
  assert.doesNotMatch("config.get('maxConcurrency')", DIRECT_READ);
  assert.doesNotMatch("read('vendors')", DIRECT_READ, 'a read through the reader is the sanctioned road');
});

test('no source file outside the door reads a model-bearing setting from the host directly', () => {
  const offenders = sourceFiles()
    .filter((file) => !DOOR.includes(file))
    .filter((file) => DIRECT_READ.test(fs.readFileSync(path.join(SRC, file), 'utf8')));

  assert.deepEqual(
    offenders,
    [],
    'these files read a model-bearing setting around sideConfig.userLayer, so a workspace value reaches '
    + 'them again — read it through readerFor or userLayer',
  );
});
