import assert from 'node:assert/strict';
import { before, test } from 'node:test';

import { RoundCountsRead } from '../roundCounts';
import type { RoundsBlockInput } from '../roundsBlock';
import { BundledLog, bundledLogModule, runningDomPage } from './roundsLogPageHarness';
import { counted, hoursAgo, MERGED_JSON, NO_CHAT, snapshot, usageLine, WSL_STORE } from './roundCountsFixtures';

/**
 * The Rounds block over two stores, from the REAL bundle (E6.S4): minified the way the shipped extension is, handed to the
 * spending tab, and drawn by the page. Three shapes — both stores counted, one of them quiet, one of them not included — because
 * a minifier that renames a binding the store words reach for fails only when a store is actually drawn, and every other
 * test of this block draws the single-store answer.
 */

let bundle: BundledLog & { usageTabHtml?: (...args: unknown[]) => string; roundsBlock?: (block: RoundsBlockInput) => string };
before(() => { bundle = bundledLogModule({ 'roundsLog.ts': ['usageTabHtml'], 'roundsBlock.ts': ['roundsBlock'] }) as typeof bundle; });

type Shape = { windows: { rows: { parts: { store: string }[] }[] }[]; stores: Record<string, unknown>[]; storesNotIncluded: unknown[] };

function drawn(read: RoundCountsRead): string {
  const page = runningDomPage(bundle);
  const block = bundle.roundsBlock!({
    snapshot: snapshot(read, [usageLine()]), chat: NO_CHAT, rateOf: () => undefined, ranOn: () => '', store: { side: '', directory: 'C:\\coai', others: [] },
  });
  page.message({ type: 'usage', html: bundle.usageTabHtml!([], 'day', [], {}, { rounds: block }) });

  return page.element('usage-body').innerHTML;
}

function edited(edit: (answer: Shape) => void): RoundCountsRead {
  const answer = JSON.parse(MERGED_JSON) as Shape;
  edit(answer);

  return counted(JSON.stringify(answer));
}

test('the shipped page draws two stores counted together — the sentence, the parts in the tooltip and a row per store', () => {
  const html = drawn(counted(MERGED_JSON));

  assert.match(html, /Counted from two stores — Windows \(this machine\) and WSL \(Ubuntu, as of 3 min ago\)/u);
  assert.match(html, /title="Windows \(this machine\): 2 · WSL \(Ubuntu\): 1">3</u);
  assert.match(html, /<span class="indent">store · WSL \(Ubuntu\)<\/span>/u);
});

test('the shipped page draws a quiet store as 0 as of its last write — never as not included', () => {
  const html = drawn(edited((answer) => {
    answer.stores[1] = { ...answer.stores[1], quiet: true, asOf: hoursAgo(216) };
    for (const window of answer.windows) {
      window.rows[0]!.parts = window.rows[0]!.parts.filter((part) => part.store !== WSL_STORE);
    }
    answer.storesNotIncluded = [];
  }));

  assert.match(html, /WSL \(Ubuntu, 0 rounds as of 9 d ago\)/u);
  assert.doesNotMatch(html, /not included/u);
});

test('the shipped page draws a missing store as not included with the cure — and no zero for it', () => {
  const html = drawn(edited((answer) => {
    answer.stores = [answer.stores[0]!];
    answer.storesNotIncluded = [{ path: '\\\\wsl.localhost\\Ubuntu\\home\\me\\.local\\share\\coai-mcp\\round-counts-snapshot.json', reason: 'missing', detail: '' }];
  }));

  assert.match(html, /holds another installation's store; its rounds are not included: no snapshot yet — update coai-mcp on that side; the snapshot is written after the next round there\./u);
  assert.doesNotMatch(html, /<tr class="store">/u);
});
