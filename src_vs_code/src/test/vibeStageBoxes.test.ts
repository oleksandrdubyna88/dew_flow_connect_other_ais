import assert from 'node:assert/strict';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import { cardContextFor, type PanelState } from '../panelView';
import { VIBE_ON_LINUX_SINCE } from '../runtimeSince';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { pageTree } from './pageTree';
import { panelState, runPanel } from './panelPageHarness';

/**
 * The Vibe card's "Use for" boxes follow the HELD runtime, not any note on the card (E3 of
 * research/PLAN_kimi_and_vibe_on_linux.md, code review).
 *
 * <p>`modelCard.useFor` switched the boxes off whenever `runtimeNote` was non-empty. That was one reason once — a server
 * that does not know the runtime — and it is two now: a note about the HOST (WSL2 on an old server, WSL1, a native Linux host)
 * rides the same field, and greying the boxes for it takes away the person's ticks on a row that is merely refused at
 * launch. The boxes are decided by `runtimeHeld`, which is `!runtimeOnServer(...)` and nothing else.</p>
 */

const VIBE: Vendor = {
  id: 'vibe', runtime: 'vibe', model: 'mistral-medium-3.5', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};
const KNOWN = (version: string) => ({ kind: 'known' as const, version, remembered: false, updateOffered: false });

function stateWith(overrides: Partial<PanelState>): PanelState {
  return { ...panelState('reviewers'), vendors: [DEFAULT_VENDORS[0]!, VIBE], ...overrides };
}

function stageBoxes(state: PanelState) {
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', 'models') });
  const card = pageTree(page.html).one((node) => node.dataset['modelCard'] === 'vibe', 'the Vibe card');

  return card.find((node) => node.tagName === 'INPUT' && ['plan', 'code', 'document'].includes(node.dataset['setting'] ?? ''));
}

test('a host note does not grey the Vibe stage boxes', () => {
  for (const [host, version] of [['wsl2', '0.57.0'], ['linux', VIBE_ON_LINUX_SINCE], ['wsl1', VIBE_ON_LINUX_SINCE]] as const) {
    const state = stateWith({ server: KNOWN(version), hostKind: host });
    const boxes = stageBoxes(state);

    assert.ok(cardContextFor(state)(VIBE).runtimeNote.length > 0, `the fixture: ${host} at ${version} carries a note`);
    assert.ok(boxes.length >= 3, `${host}: the stage boxes are there`);
    assert.ok(boxes.every((node) => !node.disabled), `${host} at ${version}: a note about the host switched the boxes off`);
  }
});

test('a server that does not know Vibe still switches the stage boxes off, on any host', () => {
  for (const host of ['windows', 'wsl2', 'linux'] as const) {
    const boxes = stageBoxes(stateWith({ server: KNOWN('0.52.0'), hostKind: host }));

    assert.ok(boxes.length >= 3, `${host}: the stage boxes are there`);
    assert.ok(boxes.every((node) => node.disabled), `${host}: a row the server refuses by name has live boxes`);
  }
});
