import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { filledSidebarState, paintSidebarInStages, sidebarState, type SidebarReads } from '../firstPaint';
import { panelHtml, staticKey, type PanelState } from '../panelView';
import { NOT_LOOKED } from '../notificationsCount';
import { EMPTY_CORPUS, type BugCorpus } from '../roundsDb';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';
import { type Page, runPanel } from './panelPageHarness';

/**
 * The sidebar paints FIRST and fills its slow card after (v2 E2: M2 on Windows was 1602 / 1962 / 2106 ms against a limit of 2000, and the first
 * render awaited the published version, each CLI's models and the card prices before it wrote any html).
 *
 * <p><b>RUN, not read.</b> Each paint here is the sidebar's own page, run on the page harness every page test shares (`.agents/PROJECT.md`): the
 * test asks what the page POSTED and what its controls are, not what its source says. The order is run too — a fill that never answers is a fake
 * that holds its promise open, so a first paint that waited for it would leave the test with no page at all.</p>
 */

/** What the first render knows without waiting: the settings, the rows, no sessions, the caches — and no corpus yet. */
function reads(): SidebarReads {
  return {
    settings: DEFAULTS,
    vendors: DEFAULT_VENDORS,
    catalogRows: DEFAULT_VENDORS,
    sessions: [],
    openSections: ['bugz'],
    notifications: NOT_LOOKED,
    phrases: [],
    localEngines: {},
    rankByRuntime: false,
    hostKind: 'windows',
    bugz: undefined,
  };
}

/** A corpus the server read: pairs were collected, so Review has something to open. */
const READ: BugCorpus = { ...EMPTY_CORPUS, read: true, funnel: { ...EMPTY_CORPUS.funnel, collected: 3, unprocessed: 1 } };

const command = (page: Page, name: string) => page.commands.find((one) => one.dataset['command'] === name);

/** Lets every promise that can settle without a slow read settle, as the event loop would between two paints. */
const turn = (): Promise<void> => new Promise((resolve) => { setImmediate(resolve); });

/** The stages the provider runs, with the slow read a promise the test holds open and every paint a RUN of the sidebar's page. */
function held() {
  const pages: Page[] = [];
  let answer!: (corpus: BugCorpus) => void;
  const slow = new Promise<BugCorpus>((resolve) => { answer = resolve; });
  const paint = (state: PanelState): void => { pages.push(runPanel(state, { html: panelHtml(state, 'test-nonce') })); };
  const stages = {
    first: () => Promise.resolve(sidebarState(reads())),
    paintFirst: paint,
    fill: async (first: PanelState) => filledSidebarState(first, { bugz: await slow, localEngines: {}, rankByRuntime: false }),
    paintFill: paint,
  };

  return { pages, answer, stages };
}

test('the first paint is a page that says its corpus is loading, posts one ready, and offers nothing that waits on the corpus — before any slow read answers', async () => {
  const { pages, stages } = held();
  const running = paintSidebarInStages(stages);
  await turn();

  assert.equal(pages.length, 1, 'the slow read has not answered, so a first paint that waited for it would have painted nothing');
  const first = pages[0]!;
  const ready = first.posted.filter((one) => one['type'] === 'ready');
  assert.equal(ready.length, 1, 'one ready per document');
  assert.deepEqual(first.posted.at(-1), ready[0], 'ready is the last thing the page says');
  assert.match(first.html, /Loading the corpus…/u, 'the card does not say it is loading');
  for (const name of ['collectBugs', 'reviewBugs', 'sendBugs']) {
    assert.equal(command(first, name)?.disabled, true, `${name} is offered while the corpus is still being read`);
  }
  assert.equal(command(first, 'setBugsServer')?.disabled, false, 'a button that needs no corpus stays offered');
  void running;
});

test('when the slow read lands the card is filled: a second page, with the corpus drawn and Review offered, and its own single ready', async () => {
  const { pages, answer, stages } = held();
  const running = paintSidebarInStages(stages);
  await turn();
  answer(READ);
  await running;

  assert.equal(pages.length, 2, 'one paint from what is known, one when the corpus landed');
  const filled = pages[1]!;
  assert.doesNotMatch(filled.html, /Loading the corpus…/u, 'the card still says it is loading after the corpus landed');
  assert.equal(command(filled, 'collectBugs')?.disabled, false, 'Collect is offered again');
  assert.equal(command(filled, 'reviewBugs')?.disabled, false, 'Review has pairs to open');
  assert.equal(filled.posted.filter((one) => one['type'] === 'ready').length, 1, 'one ready per document');
});

test('the filled page is the page a render with every read would draw: what the first paint stands in for is never on the sidebar', () => {
  const filled = filledSidebarState(sidebarState(reads()), { bugz: READ, localEngines: {}, rankByRuntime: false });
  // What the Settings page draws and the sidebar does not: models, the server, prices, usage, versions, the side — all unlike the stand-ins.
  const everything: PanelState = {
    ...filled,
    codexModels: [{ id: 'gpt-x', label: 'GPT X' }],
    agyModels: [{ id: 'gem-y', label: 'Gem Y' }],
    server: { kind: 'known', version: '0.88.0', remembered: false, updateOffered: true },
    side: 'WSL: Ubuntu',
    perSide: true,
    cliStatus: { codex: { installed: '1.0.0', latest: '1.1.0' } },
    cardPrices: { 'gpt-x': { inPerMillion: 1, outPerMillion: 2, source: 'openrouter' } },
    latestServerVersion: '0.89.2',
  };

  assert.equal(panelHtml(everything, 'test-nonce'), panelHtml(filled, 'test-nonce'), 'the sidebar page changed with a field only the Settings page draws');
  assert.equal(staticKey(everything), staticKey(filled), 'the paint key moved, so a render with every read would reload a page already filled');
});

test('the loading card and the filled card differ in the key, so the fill is a repaint; a read that never lands leaves the loading card, not a wrong one', () => {
  const first = sidebarState(reads());

  assert.notEqual(staticKey(first), staticKey(filledSidebarState(first, { bugz: READ, localEngines: {}, rankByRuntime: false })));
  assert.equal(first.bugzLoading, true);
  assert.equal(first.bugz, undefined, 'the first state names no corpus, so none can be mistaken for the server\'s answer');
});
