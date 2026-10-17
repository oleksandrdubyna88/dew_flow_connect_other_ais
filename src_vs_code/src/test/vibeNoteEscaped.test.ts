import assert from 'node:assert/strict';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import type { HostKind } from '../hostSide';
import type { PanelState } from '../panelView';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { panelState, runPanel } from './panelPageHarness';

/**
 * The card note is rendered ESCAPED (E3 of todo/PLAN_kimi_and_vibe_on_linux.md, code gate, security).
 *
 * <p>`runtimeSkewNote` interpolates `vendor.id` into the sentence a card shows — and a row's id comes from settings a person
 * (or a file somebody else wrote) edits. The note is HTML-escaped where the card draws it (`modelCard.ignoredNote`); this
 * renders a card whose id is a script-carrying tag and holds that the page carries no raw tag from it. The escape is what
 * stops it, so the test names the shape the escape produces, and a render that dropped the escape would put the tag in the
 * page as markup.</p>
 */

const HOSTILE = '<img src=x onerror=alert(1)>';
const ESCAPED = '&lt;img src=x onerror=alert(1)&gt;';

const VIBE: Vendor = {
  id: HOSTILE, runtime: 'vibe', model: 'mistral-medium-3.5', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};

function pageFor(host: HostKind, version: string): string {
  const state: PanelState = {
    ...panelState('reviewers'),
    vendors: [DEFAULT_VENDORS[0]!, VIBE],
    hostKind: host,
    server: { kind: 'known', version, remembered: false, updateOffered: false },
  };

  return runPanel(state, { html: catalogHtml(state, 'test-nonce', 'models') }).html;
}

test('a vendor id that is a tag never reaches the page as markup, in any note a host can draw', () => {
  for (const [host, version, said] of [
    ['wsl2', '0.57.0', /Update coai-mcp to run Vibe here/u],
    ['linux', '0.58.0', /native Linux host/u],
    ['wsl1', '0.58.0', /This is WSL1/u],
  ] as const) {
    const html = pageFor(host, version);
    const note = /<p class="skew">([^]*?)<\/p>/u.exec(html);

    assert.ok(note !== null, `${host}: the card carries a note paragraph`);
    assert.match(note[1]!, said, `${host}: the note is the host's sentence`);
    assert.ok(note[1]!.includes(ESCAPED), `${host}: the id is in the note as escaped text: ${note[1]}`);
    assert.ok(!html.includes(HOSTILE), `${host}: the id is on the page as a raw tag`);
    assert.doesNotMatch(html, /<img\b/iu, `${host}: an <img> element reached the page`);
  }
});
