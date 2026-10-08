import assert from 'node:assert/strict';
import { test } from 'node:test';
import { bugzCollectRefusal, bugzPickOf } from '../bugzPick';
import type { CatalogUse } from '../catalogFields';
import type { LocalEngine } from '../localEngines';
import { panelHtml, type PanelState } from '../panelView';
import { DEFAULTS, envBlock } from '../settingsShape';
import { DEFAULT_VENDORS, LOCAL_PRESET, type Vendor } from '../vendors';
import { panelState } from './panelPageHarness';
import { pageTree, type PageNode } from './pageTree';

/**
 * E5.1 step 2 of todo/PLAN_one_model_catalog.md: the sidebar's Bugz picker lists the rows ticked Bugz on Models — the
 * one sidebar change of the switch-over. Before it, the picker listed every model of every local engine, and the
 * Models tab's Bugz tick (which writes `bugzModel` as `row/model`) was a second way to say the same thing.
 *
 * <p>What a person relied on stays: an install that never ticked Bugz keeps the model it had (the plan round's finding
 * 2); a pick whose row is unticked or gone is SHOWN as that pick, never quietly swapped for another, and a collect with
 * it — or with none — is refused by a sentence naming Models (finding 5), never run with an empty or stale model.</p>
 */

function engine(models: readonly string[]): LocalEngine {
  return {
    kind: 'ollama',
    probeUrl: 'http://localhost:11434',
    apiBaseUrl: 'http://localhost:11434/v1',
    reachable: true,
    status: '0.1.0',
    models: models.map((id) => ({ id, detail: '' })),
  };
}

function localRow(id: string, model: string, uses: readonly CatalogUse[] = []): Vendor {
  return { ...LOCAL_PRESET, id, model, ...(uses.length === 0 ? {} : { uses }) };
}

const ENGINES: Readonly<Record<string, LocalEngine>> = {
  local: engine(['qwen3.5', 'llama4:17b']),
  'local-2': engine(['gemma4:27b']),
};

function stateWith(rows: readonly Vendor[], bugzModel = ''): PanelState {
  return panelState('bugz', {
    settings: { ...DEFAULTS, bugzModel },
    catalogRows: [...DEFAULT_VENDORS, ...rows],
    localEngines: ENGINES,
    // A binary that ranks by runtime, so a row of any name on the local runtime may rank.
    rankByRuntime: true,
  });
}

/** The Bugz section as the sidebar draws it. */
function bugzSection(state: PanelState): PageNode {
  return pageTree(panelHtml(state, 'test-nonce')).one((node) => node.dataset.section === 'bugz', 'Bugz section');
}

function options(section: PageNode): readonly PageNode[] {
  return section.find((node) => node.tagName === 'OPTION' && node.parent?.dataset.setting === 'bugzModel');
}

/** The models a person may choose — every option that is not the drawn stranded pick or a placeholder. */
function choosable(section: PageNode): readonly string[] {
  return options(section).filter((node) => !node.disabled).map((node) => node.value);
}

function selected(section: PageNode): readonly string[] {
  return options(section).filter((node) => 'selected' in node.attrs).map((node) => node.value);
}

test('the picker lists the rows ticked Bugz on Models, each as its row/model — not every model of every engine', () => {
  const section = bugzSection(stateWith([localRow('local', 'qwen3.5'), localRow('local-2', 'gemma4:27b', ['bugz'])], 'local-2/gemma4:27b'));

  assert.deepEqual(choosable(section), ['local-2/gemma4:27b']);
  assert.deepEqual(selected(section), ['local-2/gemma4:27b']);
});

test('with no row ticked Bugz and no pick, the section says so and points to Models — no picker to choose from', () => {
  const section = bugzSection(stateWith([localRow('local', 'qwen3.5'), localRow('local-2', 'gemma4:27b')]));

  assert.deepEqual(choosable(section), []);
  assert.match(section.text(), /No model is ticked for Bugz/u);
  assert.match(section.text(), /Models/u);
});

test('an install that never ticked Bugz keeps the model it had: a saved pick naming a row and one of its models is that row\'s', () => {
  const section = bugzSection(stateWith([localRow('local', 'qwen3.5'), localRow('local-2', 'gemma4:27b')], 'local/llama4:17b'));

  assert.deepEqual(choosable(section), ['local/llama4:17b']);
  assert.deepEqual(selected(section), ['local/llama4:17b']);
  assert.doesNotMatch(section.text(), /no longer ticked Bugz/u);
});

test('a pick whose row is no longer ticked Bugz is drawn as that pick, stranded — never swapped for the ticked one', () => {
  const section = bugzSection(stateWith([localRow('local', 'qwen3.5'), localRow('local-2', 'gemma4:27b', ['bugz'])], 'local/qwen3.5'));
  const stranded = options(section).find((node) => node.value === 'local/qwen3.5');

  assert.ok(stranded !== undefined, 'the saved pick is not on screen');
  assert.equal(stranded.disabled, true, 'a stranded pick cannot be chosen again');
  assert.deepEqual(selected(section), ['local/qwen3.5'], 'what is configured is what is shown');
  assert.deepEqual(choosable(section), ['local-2/gemma4:27b']);
  assert.match(section.text(), /no longer ticked Bugz on Models/u);
});

test('a pick whose row was removed is stranded the same way', () => {
  const section = bugzSection(stateWith([localRow('local-2', 'gemma4:27b')], 'gone/qwen3.5'));

  assert.deepEqual(selected(section), ['gone/qwen3.5']);
  assert.match(section.text(), /no longer ticked Bugz on Models, or was removed/u);
});

test('a collect is refused with a sentence naming Models — for a stranded pick and for none; never for a pick that holds', () => {
  const modelsOf = (row: Vendor): readonly string[] => [row.model, ...(ENGINES[row.id]?.models ?? []).map((one) => one.id)];
  const ticked = [localRow('local', 'qwen3.5'), localRow('local-2', 'gemma4:27b', ['bugz'])];

  assert.match(bugzCollectRefusal(bugzPickOf(ticked, 'local/qwen3.5', modelsOf)), /local\/qwen3\.5[\s\S]*Models/u);
  assert.match(bugzCollectRefusal(bugzPickOf(ticked, '', modelsOf)), /No model is ticked for Bugz[\s\S]*Models/u);
  assert.match(bugzCollectRefusal(bugzPickOf([localRow('local', 'qwen3.5')], '', modelsOf)), /Models/u);
  assert.equal(bugzCollectRefusal(bugzPickOf(ticked, 'local-2/gemma4:27b', modelsOf)), '');
  assert.equal(bugzCollectRefusal(bugzPickOf([localRow('local', 'qwen3.5')], 'local/llama4:17b', modelsOf)), '', 'the never-ticked install collects as before');
});

test('T7: the Bugz model is not written into the settings file\'s environment block — nothing in coai-mcp reads it', () => {
  // The collect hands the model to coai-mcp as `--model`; `COAI_BUGZ_MODEL` was written and read by nothing.
  assert.equal(envBlock({ ...DEFAULTS, bugzModel: 'local/qwen3.5' }, DEFAULT_VENDORS)['COAI_BUGZ_MODEL'], undefined);
});
