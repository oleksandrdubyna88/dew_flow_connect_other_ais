import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { promptChosen } from '../promptsPerRound';
import { OVERLAID_SETTINGS } from '../settingsShape';

/**
 * Every host read and write of a per-side setting goes through THIS side — the sidebar's page, its prompt-per-round
 * picker, its reviewer list and its Bugz server box included.
 *
 * <p>The defect: with *separate settings for each side* on, `PanelProvider` drew the page from the SHARED layer, and
 * the prompt picker, three reviewer-list writes and the Bugz server box wrote straight to the global layer. So a pick
 * on one side landed on every side, and the page showed values this side does not run with.</p>
 *
 * <p>The host half imports `vscode` and cannot be run here, so its WIRING is read — the precedent is
 * `settingRefusedWiring.test.ts` — as a SCAN of every host file keyed on the OPERATION, not on what the next author
 * calls a variable (code round, session 1e7363d0): any `.get`/`.update`/`.inspect` of an overlaid name, in either
 * quote and with or without `coai.`; any one-parameter reader that forwards to `.get`; and every direct global write,
 * which must be on a named list. The scan is shown to find the shapes it looks for, and each exemption is pinned to a
 * COUNT, so a new site beside an old one is not waved through. The decisions are values, tested as such:
 * `readerFor`'s on/off behaviour in `sideSettings.test.ts`, the merge below.</p>
 */

const SRC = path.join(__dirname, '..', '..', 'src');

function sourceFiles(dir = ''): string[] {
  return fs.readdirSync(path.join(SRC, dir), { withFileTypes: true }).flatMap((entry) => {
    const here = dir === '' ? entry.name : `${dir}/${entry.name}`;
    if (entry.isDirectory()) {
      return ['test', 'generated'].includes(entry.name) ? [] : sourceFiles(here);
    }

    return entry.name.endsWith('.ts') ? [here] : [];
  });
}

const source = (file: string): string => fs.readFileSync(path.join(SRC, file), 'utf8');

/** A read or write of one per-side setting by name, whatever the receiver is called. */
const BY_NAME = new RegExp(
  String.raw`\.\s*(?:get|update|inspect)\s*(?:<[^>()]*>)?\(\s*['"\x60](?:coai\.)?(?:${OVERLAID_SETTINGS.join('|')})['"\x60]`,
  'gu',
);

/** A reader that answers every setting from wherever its receiver reads: `(section) => config.get(section)`. */
const FORWARDING_READER = /\(\s*(\w+)\s*\)\s*=>\s*\w+\s*\.\s*get\s*\(\s*\1\s*\)/gu;

/** A write to the global layer by any key, a dynamic one included. */
const GLOBAL_WRITE = /ConfigurationTarget\.Global/gu;

interface Kind { readonly name: string; readonly pattern: RegExp }

const KINDS: readonly Kind[] = [
  { name: 'by-name', pattern: BY_NAME },
  { name: 'forwarding reader', pattern: FORWARDING_READER },
  { name: 'global write', pattern: GLOBAL_WRITE },
];

/** Every hit of every kind in one file, keyed `file kind`, with its count. */
function hitsIn(file: string, text: string): ReadonlyMap<string, number> {
  return new Map(KINDS
    .map((kind) => [`${file} ${kind.name}`, [...text.matchAll(kind.pattern)].length] as const)
    .filter(([, count]) => count > 0));
}

/**
 * Every sanctioned site, by file and kind, with how many there are and why. A count, not a text window: one more
 * site in the same file is a red test that names it.
 */
const SANCTIONED: Readonly<Record<string, { readonly count: number; readonly why: string }>> = {
  // The door itself: readerFor builds on the merged reader, saveSetting writes Global when the switch is off, and
  // the storage choice reads where data lives before any side is known.
  'sideConfig.ts forwarding reader': { count: 2, why: 'readerFor / userLayer and the storage choice — the door' },
  'sideConfig.ts global write': { count: 1, why: 'saveSetting, when this side keeps no settings of its own' },
  'panelProvider.ts global write': { count: 2, why: 'coai.teamServers — one list every side shares, by design' },
  'chatPresetsHost.ts global write': { count: 1, why: 'coai.chatModelPresets — not a per-side setting' },
  'configTransferCommands.ts global write': { count: 1, why: 'an import writes the base layer; per-side overrides are never touched' },
  'helpPanel.ts global write': { count: 1, why: 'coai.helpLanguage — about the reader, not the work' },
  'textToneHost.ts global write': { count: 1, why: 'coai.textTone — about the reader, not the work' },
  'uiScaleHost.ts global write': { count: 1, why: 'coai.uiScale — about the reader, not the work' },
  'settingsPanel.ts global write': { count: 1, why: 'coai.settingsPreview — which Settings page the person reads; user scope, never per side (D5)' },
  // The catalog migration moves EACH layer on its own (PLAN_one_model_catalog.md E1.3): the user layer is read raw
  // and written Global on purpose — a workspace value is not the person's to have migrated, and the merged reader
  // would hand it one — and a side's overlay reads the user layer's rows its keys fall back to (`sharedVendors`).
  // (The chat's three shared-layer readers left with E1.1: the chat reads its model keys through `userLayer`.)
  'catalogMigrationHost.ts by-name': { count: 2, why: 'sharedVendors — the user rows an overlay falls back to; userChatRecord — the user layer’s chat record, whose row ids a side keeps (E4.6a)' },
  'catalogMigrationHost.ts forwarding reader': { count: 1, why: 'the chat presets as the chat reads them — the user layer, the shipped ones included — for every layer (E4.6a)' },
  'catalogMigrationHost.ts global write': { count: 1, why: 'the migration writes the user layer it read, and no other' },
  // Not configuration at all: the shape matches a Map lookup too, and these two are exactly that.
  'chatPresets.ts forwarding reader': { count: 1, why: 'a Map lookup (byId), not a configuration read' },
  'configTransfer.ts forwarding reader': { count: 1, why: 'a Map lookup (FITS), not a configuration read' },
};

test('the scan finds every shape of a read or write that goes around this side', () => {
  for (const planted of [
    "await config.update('promptsPerRound', value);",
    "await config.update(\n      'vendors',\n      kept,\n    );",
    "const cfg = vscode.workspace.getConfiguration('coai'); await cfg.update('rounds', 2);",
    'vscode.workspace.getConfiguration("coai").get("bugzServer")',
    "vscode.workspace.getConfiguration().get('coai.vendors')",
    'const settings = settingsFrom((section) => config.get(section));',
    'settingsFrom((key) => cfg.get(key))',
    'await ws.update(m.key, m.value, vscode.ConfigurationTarget.Global);',
  ]) {
    assert.ok(hitsIn('planted.ts', planted).size > 0, `the scan would miss: ${planted}`);
  }
  assert.equal(hitsIn('planted.ts', "this.save(config, 'vendors', kept)").size, 0, 'the one save is the sanctioned road');
  assert.equal(hitsIn('planted.ts', "config.get('teamServers')").size, 0, 'teamServers is shared by design, not per side');
});

test('no host file reads or writes a per-side setting around this side, beyond the counted, named sites', () => {
  const found = new Map(sourceFiles().flatMap((file) => [...hitsIn(file, source(file))]));
  const unexpected = [...found]
    .filter(([key, count]) => count !== (SANCTIONED[key]?.count ?? 0))
    .map(([key, count]) => `${key}: ${count} (sanctioned ${SANCTIONED[key]?.count ?? 0})`);

  assert.deepEqual(unexpected, [], 'a read or write of a per-side setting around this side — use readerFor / this.read and saveSetting / this.save, or name the site in SANCTIONED with its reason');
});

test('every sanctioned site still exists, so none of the counts is a stale pass', () => {
  const found = new Map(sourceFiles().flatMap((file) => [...hitsIn(file, source(file))]));
  const stale = Object.keys(SANCTIONED).filter((key) => !found.has(key));

  assert.deepEqual(stale, [], 'a sanctioned site is gone — lower its count or remove the entry');
});

test('the page itself reads through this side, and the prompt picker writes through the one save', () => {
  const panel = source('panelProvider.ts');

  // Pinned with the render's NEXT line: `choosePrompt` has the same first line, and must not satisfy this for it.
  assert.match(panel, /const settings = settingsFrom\(this\.read\(config\)\);\s*const vendors = vendorsFrom\(this\.read\(config\)\('vendors'\)\);/u,
    'the render does not read its settings through this side');
  assert.match(panel, /this\.save\(config, 'promptsPerRound', pick\.value\)/u, 'the prompt picker does not write through the one save');
});

test('choosing a round\'s prompt pads the earlier rounds with "not chosen" and keeps every other role', () => {
  const current = { plan: ['a'], code: ['x', 'y'] };

  assert.deepEqual(promptChosen(current, 'plan', 3, 'c', 3), { ok: true, value: { plan: ['a', '', 'c'], code: ['x', 'y'] } });
  assert.deepEqual(promptChosen(current, 'code', 1, 'z', 2), { ok: true, value: { plan: ['a'], code: ['z', 'y'] } });
  assert.deepEqual(promptChosen(current, 'code', 2, 'y', 2), { ok: true, value: current }, 'the same pick again changes nothing');
  assert.deepEqual(promptChosen({}, 'feature', 1, 'f', 2), { ok: true, value: { feature: ['f'] } });
  assert.deepEqual(current, { plan: ['a'], code: ['x', 'y'] }, 'the stored value is never changed in place');
});

test('a round the role does not have is refused, never written somewhere a save drops it', () => {
  for (const round of [0, -1, 1.5, Number.NaN, 4, 1e9]) {
    assert.deepEqual(promptChosen({ plan: ['a'] }, 'plan', round, 'p', 3), { ok: false }, `round ${round} was accepted`);
  }
});

test('a role is read only as its own key, so an inherited name is no role', () => {
  assert.deepEqual(promptChosen({}, 'constructor', 1, 'p', 1), { ok: true, value: { constructor: ['p'] } });
});
