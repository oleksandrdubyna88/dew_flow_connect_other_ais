import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { promptChosen } from '../promptsPerRound';
import { OVERLAID_SETTINGS } from '../settingsShape';

/**
 * The sidebar reads and writes THIS side's settings — the prompt-per-round picker included.
 *
 * <p>The defect: with *separate settings for each side* on, `PanelProvider` drew the page from the SHARED layer
 * (`settingsFrom` over a plain `config.get`), and the prompt-per-round picker, three reviewer-list writes and the Bugz
 * server box wrote straight to the global layer. So on a side with its own settings a pick landed on every side, and
 * the page showed values this side does not run with. Only the vendors went through the side-aware reader.</p>
 *
 * <p>The host half imports `vscode` and cannot be run here, so its WIRING is read — the precedent is
 * `settingRefusedWiring.test.ts` — and read as a SCAN over every host file, not a list of the sites somebody found:
 * a decision applied at some of its sites is the defect itself (plan round, session 1e7363d0). The scan is shown to
 * find the shapes it looks for, and each exemption is named with its reason. The decisions are values, tested as such:
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

/** A host read or write of one per-side setting by NAME — `.get('rounds')`, `.update(\n 'vendors', …)`, `.inspect<T>("x")`. */
const BY_NAME = new RegExp(
  String.raw`(?:config|getConfiguration\('coai'\))\s*\.\s*(?:get|update|inspect)\s*(?:<[^>()]*>)?\(\s*['"\x60](?:${OVERLAID_SETTINGS.join('|')})['"\x60]`,
  'gu',
);

/** A reader that answers every setting from the shared layer: `(section) => config.get(section)`. */
const SHARED_READER = /\(\s*section\s*\)\s*=>\s*config\.get\(\s*section\s*\)/gu;

/**
 * The exemptions, each with its reason. `sideConfig.ts` IS the door — `readerFor` and `saveSetting` live there. The
 * seeding read is the one place the shared layer is the point: turning the switch on copies what this side reads
 * TODAY, so nothing changes until something is edited.
 */
const EXEMPT: readonly { readonly file: string; readonly near: string }[] = [
  { file: 'sideConfig.ts', near: '' },
  { file: 'panelProvider.ts', near: 'seedIfEmpty(' },
];

interface Site { readonly file: string; readonly line: number; readonly text: string }

function sitesIn(file: string, text: string): readonly Site[] {
  return [...text.matchAll(BY_NAME), ...text.matchAll(SHARED_READER)].map((match) => ({
    file,
    line: text.slice(0, match.index).split('\n').length,
    text: text.slice(Math.max(0, (match.index ?? 0) - 120), (match.index ?? 0) + match[0].length),
  }));
}

function exempt(site: Site): boolean {
  return EXEMPT.some((one) => one.file === site.file && site.text.includes(one.near));
}

test('the scan finds every shape of a read or write that goes around this side', () => {
  for (const planted of [
    "await config.update('promptsPerRound', value, vscode.ConfigurationTarget.Global);",
    "await config.update(\n      'vendors',\n      kept,\n    );",
    "vscode.workspace.getConfiguration('coai').get<string>('bugzServer', '')",
    'const settings = settingsFrom((section) => config.get(section));',
  ]) {
    assert.ok(sitesIn('planted.ts', planted).length > 0, `the scan would miss: ${planted}`);
  }
  assert.equal(sitesIn('planted.ts', "config.update('teamServers', list, Global)").length, 0, 'teamServers is shared by design, not per side');
  assert.equal(sitesIn('planted.ts', "this.save(config, 'vendors', kept)").length, 0, 'the one save is the sanctioned road');
});

test('no host file reads or writes a per-side setting around this side', () => {
  const offenders = sourceFiles()
    .flatMap((file) => sitesIn(file, fs.readFileSync(path.join(SRC, file), 'utf8')))
    .filter((site) => !exempt(site))
    .map((site) => `${site.file}:${site.line}`);

  assert.deepEqual(offenders, [], 'these read or write a per-side setting from the shared layer — use readerFor / this.read and saveSetting / this.save');
});

test('the exemptions still name real sites, so none of them is a stale pass', () => {
  for (const one of EXEMPT) {
    const text = fs.readFileSync(path.join(SRC, one.file), 'utf8');
    assert.ok(sitesIn(one.file, text).some((site) => site.text.includes(one.near)), `${one.file} no longer has the exempt site near "${one.near}"`);
  }
});

test('the page reads through this side, and the prompt picker writes through the one save', () => {
  const panel = fs.readFileSync(path.join(SRC, 'panelProvider.ts'), 'utf8');

  assert.match(panel, /const settings = settingsFrom\(this\.read\(config\)\);/u, 'the render does not read through this side');
  assert.match(panel, /this\.save\(config, 'promptsPerRound', promptChosen\(/u, 'the prompt picker does not write through the one save');
});

test('choosing a round\'s prompt pads the earlier rounds with "not chosen" and keeps every other role', () => {
  const current = { plan: ['a'], code: ['x', 'y'] };

  assert.deepEqual(promptChosen(current, 'plan', 3, 'c'), { plan: ['a', '', 'c'], code: ['x', 'y'] });
  assert.deepEqual(promptChosen(current, 'code', 1, 'z'), { plan: ['a'], code: ['z', 'y'] });
  assert.deepEqual(promptChosen(current, 'code', 2, 'y'), current, 'the same pick again changes nothing');
  assert.deepEqual(promptChosen({}, 'feature', 1, 'f'), { feature: ['f'] });
  assert.deepEqual(current, { plan: ['a'], code: ['x', 'y'] }, 'the stored value is never changed in place');
});
