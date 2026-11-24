import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { bundleText } from './shellHarness';

/**
 * The dead v1 is out of the bundle (v2 E4, story s6-dead-v1-out; owner decision 2026-11-23): the server has no held gates, cards or cadence any more, so the
 * extension carries no escalation, consultation or question WATCHER, no escalation answer, no cadence probe and no cadence or hold control.
 *
 * <p>There is no program to run for an absence, so this reads the manifest, the shipped source and the BUILT bundles. A scan that finds nothing proves
 * nothing, so each one first checks that it does read what it should (the entry and the panel are in the source; the legacy bundle is not empty).</p>
 */

const ROOT = process.cwd();

interface Manifest {
  readonly contributes: {
    readonly commands: readonly { readonly command: string }[];
    readonly menus: Record<string, readonly { readonly command?: string; readonly when?: string }[]>;
    readonly configuration: { readonly properties: Record<string, unknown> };
  };
}

const manifest = JSON.parse(readFileSync(join(ROOT, 'package.json'), 'utf8')) as Manifest;

/** Every shipped source file under `src` (tests excluded) and `shell`, as `relative path -> text`. */
function shipped(): ReadonlyMap<string, string> {
  const found = new Map<string, string>();
  const walk = (dir: string): void => {
    for (const entry of readdirSync(join(ROOT, dir))) {
      const relative = `${dir}/${entry}`;
      if (statSync(join(ROOT, relative)).isDirectory()) {
        if (entry !== 'test') {
          walk(relative);
        }
      } else if (entry.endsWith('.ts')) {
        found.set(relative, readFileSync(join(ROOT, relative), 'utf8'));
      }
    }
  };
  walk('src');
  walk('shell');

  return found;
}

test('the files that held the watchers, the answer flow and the cadence are gone', () => {
  for (const file of ['escalationWatcher', 'consultationWatcher', 'questionConsultWatcher', 'jsonDirectoryWatcher', 'jsonDirectory', 'escalations', 'escalationAnswer', 'cadenceProbe', 'cadenceLine', 'cadenceSettings']) {
    assert.equal(existsSync(join(ROOT, 'src', `${file}.ts`)), false, `src/${file}.ts must not exist`);
  }
});

test('the manifest declares no answer command, no cadence setting, no escalation wait and no "a question is waiting" context', () => {
  const commands = manifest.contributes.commands.map((one) => one.command);
  assert.ok(commands.includes('coai.showRounds'), 'the scan reads the manifest it should');
  assert.deepEqual(commands.filter((one) => /answerQuestion/u.test(one)), [], 'a command that answers a held question is declared');

  const menus = Object.values(manifest.contributes.menus).flat().map((one) => JSON.stringify(one));
  assert.deepEqual(menus.filter((one) => /answerQuestion|hasQuestions/u.test(one)), [], 'a menu still shows or hides something by a waiting question');

  const settings = Object.keys(manifest.contributes.configuration.properties);
  assert.ok(settings.includes('coai.maxConcurrency'), 'the scan reads the settings it should');
  assert.deepEqual(settings.filter((one) => /cadence|escalation/iu.test(one)), [], 'a cadence or escalation control is still a setting');
});

test('no shipped source names a watcher, the answer flow, the cadence probe or the context they fed', () => {
  const sources = shipped();
  assert.ok(sources.has('shell/entry.ts') && sources.has('src/panelProvider.ts'), 'the scan reads the shipped tree');
  const names = ['EscalationWatcher', 'ConsultationWatcher', 'QuestionConsultWatcher', 'JsonDirectoryWatcher', 'CadenceProbes', 'cadenceProbe', 'forgetCadence', 'answerQuestion', 'hasQuestions', 'COAI_CADENCE', 'COAI_ESCALATION_MINUTES'];
  const hits = [...sources].flatMap(([file, text]) => names.filter((name) => text.includes(name)).map((name) => `${file}: ${name}`));

  assert.deepEqual(hits, [], 'something still names the dead v1');
});

test('the legacy bundle, as built, carries no command, context or environment key of the dead v1', async () => {
  const legacy = await bundleText('legacy');
  assert.ok(legacy.length > 100_000, 'the legacy bundle is built, so the scan has something to read');

  for (const needle of ['coai.answerQuestion', 'coai.hasQuestions', 'COAI_CADENCE_MODE', 'COAI_ESCALATION_MINUTES', 'a-review-is-waiting']) {
    assert.equal(legacy.includes(needle), false, `the legacy bundle still carries ${needle}`);
  }
});
