import assert from 'node:assert/strict';
import { test } from 'node:test';

import { cardContextFor, type PanelState } from '../panelView';
import type { Vendor } from '../vendors';
import { panelState } from './panelPageHarness';

/**
 * `PanelState.hostKind` is REQUIRED (E3 of research/PLAN_kimi_and_vibe_on_linux.md, code gate): there is no fallback path.
 *
 * <p>The fallback it replaces was `hostKindOfPlatform(hostPlatform())`, which maps every Linux platform to a NATIVE host —
 * so a state built without the kind, on a WSL2 window, drew the native-Linux refusal on a host that runs Vibe. Whoever builds
 * a state must now say what the window runs on. That is a property of the TYPE, held with `@ts-expect-error`: the directive is
 * itself an error (TS2578) the day the field becomes optional again, so `npm run typecheck` goes red.</p>
 */

const VIBE: Vendor = {
  id: 'vibe', runtime: 'vibe', model: 'mistral-medium-3.5', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};

test('a PanelState that names no host kind does not compile, so a WSL2 window cannot be drawn as a native host by omission', () => {
  const { hostKind: _named, ...without } = panelState('reviewers');
  // @ts-expect-error the host kind is required: a state without it is the bug this guards
  const omitted: PanelState = without;

  assert.equal(_named, 'windows', 'the fixture names a host kind, as every state must');
  assert.equal(omitted.hostKind, undefined, 'at run time the omission is still undefined: the guarantee is the compile error above');
});

test('the card note follows the kind the state names, with nothing to fall back to', () => {
  const state = { ...panelState('reviewers'), vendors: [VIBE], hostKind: 'wsl2' as const,
    server: { kind: 'known' as const, version: '0.57.0', remembered: false, updateOffered: false } };

  assert.match(cardContextFor(state)(VIBE).runtimeNote, /Update coai-mcp to run Vibe here/u);
});
