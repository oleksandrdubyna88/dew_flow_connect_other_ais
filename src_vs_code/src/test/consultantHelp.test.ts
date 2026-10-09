import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import { CALLER_KINDS } from '../consultSettings';
import { HELP } from '../help';
import { panelState } from './panelPageHarness';
import { pageTree, type PageNode } from './pageTree';

// The census the other way round from helpTooltips.test.ts: there, every tooltip must sit on a control;
// here, every control of the Consultant tab and the consultation cadence must carry its "?"
// (research/PLAN_consult_limits_kinds_and_help.md, story 3). The operator's words: a "?" on every new setting.
// Read off the Settings page as it is DRAWN (todo/PLAN_one_model_catalog.md E5.3) — the tab a person opens.

const PANE = pageTree(catalogHtml(panelState('consultant'), 'test-nonce', 'consultants/consultant'))
  .one((node) => node.dataset['pane'] === 'consultants/consultant', 'Consultant pane');

/** A help mark: the "?" `help()` draws. */
const isHelp = (node: PageNode | undefined): boolean => node !== undefined && node.tagName === 'SPAN' && node.className === 'help';

/** The "?" a label carries — inside it, or right beside it as a checkbox's is — or none. */
function helpOf(label: PageNode, parent: PageNode): PageNode | undefined {
  const next = parent.children[parent.children.indexOf(label) + 1];

  return label.children.find(isHelp) ?? (isHelp(next) ? next : undefined);
}

/** Every label a person reads a setting by, with its "?" — the options of a segmented radio are one control, not three. */
const LABELS: readonly { readonly name: string; readonly words: string; readonly help: PageNode | undefined }[] = [PANE, ...PANE.all()]
  .flatMap((parent) => parent.children.filter((child) => child.tagName === 'LABEL').map((label) => ({ label, parent })))
  .filter(({ label }) => label.find((one) => one.tagName === 'INPUT' && one.type === 'radio').length === 0)
  .map(({ label, parent }) => ({ name: label.attrs['for'] ?? label.text().slice(0, 80), words: label.text(), help: helpOf(label, parent) }));

// Where a "?" sits relative to a checkbox is BEHAVIOUR — a "?" inside the label flips the setting when
// clicked — so it is measured in a real browser (scripts/measure-help-clicks.mjs), not asserted over this
// text: a new behavioural assertion over page text is refused (.agents/PROJECT.md).

test('every consultant and cadence setting has a "?" that explains it', () => {
  const bare = LABELS.filter((label) => label.help === undefined).map((label) => label.name);

  assert.deepEqual(bare, [], `settings with no "?": ${bare.join(', ')}`);
});

test('each caller\'s pick is named in words a person reads, and carries the "?" that says what a caller\'s consultant is', () => {
  for (const { id, label } of CALLER_KINDS) {
    const pick = LABELS.find((one) => one.name === `consult-row-${id}`);
    assert.ok(pick !== undefined, `the ${id} caller has no pick on the Consultant tab`);
    assert.ok(pick.words.includes(`${label} asks`), `the ${id} caller is not named in words: "${pick.words}"`);
    assert.equal(pick.help?.attrs['title'], HELP.consultCaller, `the ${id} caller's pick has no "?" explaining it`);
  }
});

test('the census covers every control it is about, so an empty list of labels cannot pass it', () => {
  for (const id of ['consultEnabled', 'consult-row-claude', 'consultTurns', 'consultCallsPerSession', 'consultIdleMinutes',
    'consultPrompt', 'cadenceEvery', 'cadenceRiskThreshold', 'cadenceRiskMax']) {
    assert.ok(LABELS.some((label) => label.name === id), `${id} has no label in the census`);
  }
});
