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

/**
 * A reader that hands ANY key to the merged configuration — `(key) => config.get(key)`, typed or not, on a variable
 * or on `getConfiguration('coai')` itself. A helper given one reads `chatModel` or `vendors` from a workspace again
 * without a model key ever being spelled at the call site, which `DIRECT_READ` cannot see (PR #681's review).
 */
const FORWARDING_READER =
  /\(\s*(\w+)\s*(?::\s*string)?\s*\)\s*=>\s*[\w.]+(?:\s*\([^()]*\))?\s*\.\s*get\s*(?:<[^>()]*>)?\s*\(\s*\1\s*\)/u;

test('the scan recognises a reader that forwards any key to the merged configuration', () => {
  for (const planted of [
    'chatSettingsFrom((key) => config.get(key))',
    'chatSettingsFrom((key: string) => config.get(key))',
    "chatSettingsFrom((key) => vscode.workspace.getConfiguration('coai').get(key))",
    'seedIfEmpty(store, side, (section) => config.get<unknown>(section))',
  ]) {
    assert.match(planted, FORWARDING_READER, `the scan would miss ${planted}`);
  }
  assert.doesNotMatch('chatSettingsFrom(userLayer(config))', FORWARDING_READER, 'the door is the sanctioned road');
  assert.doesNotMatch('(key) => byId.get(other)', FORWARDING_READER, 'a lookup of a DIFFERENT name forwards nothing');
  assert.ok(MEMENTO.test('get: <T,>(key: string) => this.context.globalState.get<T>(key),'), 'extension state is not configuration');
});

test('no source file outside the door hands the merged configuration to a settings helper', () => {
  const offenders = sourceFiles()
    .filter((file) => !DOOR.includes(file))
    .flatMap((file) => fs.readFileSync(path.join(SRC, file), 'utf8').split(/\r?\n/u)
      .flatMap((line, index) => (forwards(file, line) ? [`${file}:${index + 1}`] : [])));

  assert.deepEqual(
    offenders,
    [],
    'these hand every key — the model keys among them — to the merged configuration; pass userLayer(config) '
    + '(or readerFor, for a per-side setting) instead',
  );
});

/** The same arrow shape over a Map, not a configuration: `(id) => byId.get(id)`. */
const MAP_LOOKUPS = ['chatPresets.ts', 'configTransfer.ts'];

/** ...and over the extension's own state (a Memento), which holds no setting at all. */
const MEMENTO = /\b(?:globalState|workspaceState)\s*\.\s*get\b/u;

function forwards(file: string, line: string): boolean {
  return FORWARDING_READER.test(line) && !MAP_LOOKUPS.includes(file) && !MEMENTO.test(line);
}
