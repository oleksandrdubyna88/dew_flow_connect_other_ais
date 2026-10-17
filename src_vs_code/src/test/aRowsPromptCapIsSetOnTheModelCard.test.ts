import assert from 'node:assert/strict';
import { test } from 'node:test';

import { FEATURES } from '../binaryFeatures';
import { catalogHtml } from '../catalogPage';
import { vendorFieldWrite } from '../cliPathSave';
import { ignoredSaid } from '../modelCard';
import { MAX_PROMPT_KIB } from '../promptCap';
import { settingMessageFrom, settingWrite } from '../settingsShape';
import { DEFAULT_VENDORS, vendorsFrom, type Vendor } from '../vendors';
import { vendorsEnv } from '../vendorsWire';
import { lastWrite, panelState, runPanel, type Page } from './panelPageHarness';
import { type PageNode, pageTree } from './pageTree';

/**
 * A row's `maxPromptKib` is set on its Models card (todo/PLAN_a_reviewer_is_not_sent_more_than_it_can_read.md S4, D1 and D6):
 * one number input, a setting and not an action — and the field crosses to coai-mcp only when the installed binary lists
 * `maxPromptKib` in `--features`, because an older one skips the member and would launch the reviewer the cap was meant to
 * leave out while the card said the row was capped.
 *
 * <p>The card is RUN (`runPanel`) and read back as the tree it drew; the typed value goes the way the panel routes it
 * (`settingMessageFrom` → `vendorFieldWrite` → `vendorsFrom`) and the card is drawn again from the stored row. Nothing
 * here asserts over the page's source text.</p>
 */

/** A stored row, through the parser — what a person typed is a raw value, so the overrides are not typed as a `Vendor`. */
const ROW = (over: Record<string, unknown> = {}): Vendor =>
  vendorsFrom([{ ...DEFAULT_VENDORS[0]!, id: 'codex-a', runtime: 'codex', model: 'gpt-6.1-sol', enabled: true, ...over }])[0]!;

function opened(rows: readonly Vendor[]): { readonly page: Page; readonly tree: PageNode } {
  const state = { ...panelState('reviewers'), vendors: rows, catalogRows: [...rows] };
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', 'models') });

  return { page, tree: pageTree(page.html) };
}

/** The cap box of one card — the one control a row has for `maxPromptKib`. */
function boxOf(tree: PageNode, id: string): PageNode {
  return tree.one((node) => node.tagName === 'INPUT' && node.dataset['setting'] === 'maxPromptKib' && node.dataset['vendor'] === id, `the prompt cap box of ${id}`);
}

const crossed = (rows: readonly Vendor[], features: readonly string[]): readonly unknown[] =>
  (JSON.parse(vendorsEnv(rows, '', () => undefined, features)) as Record<string, unknown>[]).map((row) => row['maxPromptKib']);

test('the card draws ONE number box for the cap, empty when the row has none and showing it when it does — beside, not instead of, the timeout', () => {
  const { tree } = opened([ROW(), ROW({ id: 'codex-b', maxPromptKib: 180 })]);

  const none = boxOf(tree, 'codex-a');
  assert.equal(none.attrs['type'], 'number');
  assert.equal(none.value, '', 'a row with no cap drew a number in the box');
  assert.equal(none.attrs['placeholder'], 'no cap');
  assert.equal(boxOf(tree, 'codex-b').value, '180');
  assert.equal(tree.find((node) => node.tagName === 'INPUT' && node.dataset['setting'] === 'maxPromptKib').length, 2, 'one box per card');
  assert.ok(tree.find((node) => node.dataset['setting'] === 'timeoutMinutes' && node.dataset['vendor'] === 'codex-a').length === 1, 'the timeout box is still there');
});

test('typing a number writes it through the one vendor write, and the card drawn again from the stored row shows it', () => {
  const before = ROW();
  const { page } = opened([before]);
  const box = page.controls.find((one) => one.dataset['setting'] === 'maxPromptKib' && one.dataset['vendor'] === 'codex-a');
  assert.ok(box !== undefined, 'the page draws no cap box to type into');
  box.value = '180';
  box.fire('change');

  const write = settingWrite(settingMessageFrom(lastWrite(page)));
  assert.ok(write?.kind === 'vendor', 'the box posted no vendor write');
  assert.equal(write.key, 'maxPromptKib');
  assert.equal(write.value, 180, 'a number box posts a number, as the timeout box does');
  const after = vendorsFrom([{ ...before, ...vendorFieldWrite(before, write.key, write.value, 'windows') }])[0]!;
  assert.equal(after.maxPromptKib, 180);
  assert.equal(boxOf(opened([after]).tree, 'codex-a').value, '180', 're-drawn, the box lost what was typed');
});

test('an emptied box, a negative, a fraction, a number past 4096 and a string are no cap — stored and read back as unset', () => {
  for (const raw of [0, -1, 2.5, MAX_PROMPT_KIB + 1, '180', null, true]) {
    assert.equal('maxPromptKib' in ROW({ maxPromptKib: raw }), false, `${String(raw)} was kept`);
  }
  assert.equal(ROW({ maxPromptKib: MAX_PROMPT_KIB }).maxPromptKib, MAX_PROMPT_KIB, 'the limit itself is a cap');
  assert.equal(ROW({ maxPromptKib: 1 }).maxPromptKib, 1);
});

test('the limit is the one coai-mcp pins: 4096 KiB', () => {
  assert.equal(MAX_PROMPT_KIB, 4096, 'CatalogLimits.MaxPromptKib says 4096 (ARowCarriesAPromptCapTests holds the other half)');
});

test('a binary that lists maxPromptKib is sent the cap — of a CLI row and of an api row alike — and one that does not is sent none', () => {
  const rows = [ROW({ maxPromptKib: 180 }), ROW({ id: 'qwen', runtime: 'api', model: 'qwen-max', baseUrl: 'https://q.example/v1', maxPromptKib: 150 })];

  assert.deepEqual(crossed(rows, [FEATURES.maxPromptKib]), [180, 150], 'the server leaves an over-cap reviewer out whatever the row runs on');
  assert.deepEqual(crossed(rows, []), [undefined, undefined], 'an older binary would skip the member and launch the reviewer anyway');
  assert.deepEqual(crossed([ROW()], [FEATURES.maxPromptKib]), [undefined], 'a row with no cap sends nothing');
});

test('the card says a binary that does not list the field ignores it — and nothing to a binary that does, or to a row with no cap', () => {
  const capped = ROW({ maxPromptKib: 180 });

  assert.match(ignoredSaid(capped, { installed: true, features: [] }).join(' '), /its prompt cap/u);
  assert.equal(ignoredSaid(capped, { installed: true, features: [FEATURES.maxPromptKib] }).length, 0);
  assert.equal(ignoredSaid(ROW(), { installed: true, features: [] }).length, 0, 'a row that sets no cap has nothing ignored');
});
