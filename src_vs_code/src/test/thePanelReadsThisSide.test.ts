import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
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
  'panelProvider.ts global write': {
    count: 3,
    why: 'coai.teamServers — one list every side shares, by design; and coai.planPrices (story 6.2) — what an account '
      + 'costs the person, the same on every side, never a per-side setting',
  },
  'panelProvider.ts by-name': {
    count: 1,
    why: 'coai.onExhausted inspected for the WORKSPACE layer only, to name a workspace value that wins over the choice in the gate section (E2.S1) — read to tell the person, never as the value this side runs with',
  },
  'chatPresetsHost.ts global write': { count: 1, why: 'coai.chatModelPresets — not a per-side setting' },
  'configTransferCommands.ts global write': { count: 1, why: 'an import writes the base layer; per-side overrides are never touched' },
  'helpPanel.ts global write': { count: 1, why: 'coai.helpLanguage — about the reader, not the work' },
  'skipShellHost.ts global write': { count: 1, why: 'terminal.integrated.commandsToSkipShell — a VS Code setting of the person, appended to after a press of Add (E3.S3), never a per-side setting' },
  'textToneHost.ts global write': { count: 1, why: 'coai.textTone — about the reader, not the work' },
  'uiScaleHost.ts global write': { count: 1, why: 'coai.uiScale — about the reader, not the work' },
  // The catalog migration moves EACH layer on its own (PLAN_one_model_catalog.md E1.3): the user layer is read raw
  // and written Global on purpose — a workspace value is not the person's to have migrated, and the merged reader
  // would hand it one — and a side's overlay reads the user layer's rows its keys fall back to (`sharedVendors`).
  // (The chat's three shared-layer readers left with E1.1: the chat reads its model keys through `userLayer`.)
  'catalogMigrationHost.ts by-name': { count: 5, why: 'what a side inherits from the user layer: sharedVendors — the rows an overlay falls back to; sharedConsultants and sharedQconsultRows — so the rows they refer to join its own list (C1); userChatRecord — whose row ids a side keeps (E4.6a); userChatModel — replaced when the side moved that preset elsewhere (R2)' },
  // Moved from catalogMigrationHost.ts into the one reader the move, Chat's conflicts and a choice share (R7's code
  // round, finding 6) — still the user layer, built from the configuration it is handed.
  'modelKeys.ts forwarding reader': { count: 1, why: 'userChatPresets — the chat presets as the move, Chat and a choice read them: the user layer, the shipped ones included (E4.6a, R7)' },
  'catalogMigrationHost.ts global write': { count: 1, why: 'the migration writes the user layer it read, and no other' },
  // The one-time reset of When the rounds run out to Auto (E2.S2, research/PLAN_auto_never_asks_a_person_E2_S2_spec.md D15): like the catalog
  // migration it reads the person's OWN layer raw — `inspect().globalValue`, never a workspace's — and writes only the Global target,
  // because a workspace value is not the person's to have reset (the owner's decision on Q1). The host half reads the three layers
  // once more, to say which value stays in force in this window — the same three reads `readerFor` makes.
  'onExhaustedMigration.ts by-name': { count: 1, why: 'userLayerOf: globalValue of coai.onExhausted and of its witness — the person\'s own layer, asked for again at every call' },
  'onExhaustedMigrationHost.ts by-name': { count: 1, why: 'inForce: coai.onExhausted inspected, to tell the person which value stays in force in this window' },
  'onExhaustedMigrationHost.ts global write': { count: 1, why: 'userLayerOf is handed the Global target and writes nothing else: the witness, its withdrawal and the removal' },
  // Not configuration at all: the shape matches a Map lookup too, and these two are exactly that.
  'chatPresets.ts forwarding reader': { count: 1, why: 'a Map lookup (byId), not a configuration read' },
  'configTransfer.ts forwarding reader': { count: 1, why: 'a Map lookup (FITS), not a configuration read' },
};

test('the scan finds every shape of a read or write that goes around this side', () => {
  for (const planted of [
    "await config.update('rounds', value);",
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

test('the page itself reads through this side', () => {
  const panel = source('panelProvider.ts');

  assert.match(panel, /const settings = settingsFrom\(this\.read\(config\)\);\s*const vendors = vendorsFrom\(this\.read\(config\)\('vendors'\)\);/u,
    'the render does not read its settings through this side');
});
