import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { UNNAMED_VENDOR_COLOUR, VENDOR_COLOUR_IDS, VENDOR_PALETTE, vendorPalette } from '../vendorColour';
import { panelHtml, PanelState } from '../panelView';
import { RoundRecord, SessionFile } from '../rounds';
import { DEFAULTS } from '../settingsShape';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { Vendor } from '../vendors';

/**
 * A reviewer's colour is its own, and it is the same colour everywhere.
 *
 * <p>Reported first looking at a round card: in `codex/PlanCritique — running` the one word that says
 * WHO was the same grey as the rest of the row. Reported again on 2026-09-08, from a panel with six
 * reviewers configured: `local` and `remsoftdev-codex` wore the same orange edge. So the requirement
 * has two halves — one vendor is ONE colour in every view, and no two vendors are the SAME colour —
 * and the second half is a statement about the whole list, which is why the palette is built from
 * the list rather than from one name at a time.</p>
 */

const REPORTED = ['codex', 'gemini', 'local', 'remsoftdev-claude', 'remsoftdev-codex', 'remsoftdev-antigravity'];

test('one vendor keeps one colour', () => {
  const colour = vendorPalette(REPORTED);

  assert.equal(colour('codex'), colour('codex'));
});

/**
 * The configuration on somebody's screen on 2026-09-08 — three local reviewers and one Team server's
 * three. Before this file was rewritten it failed with `5 !== 6`, naming `local` and
 * `remsoftdev-codex` on the same `var(--vscode-charts-orange)`.
 */
test('every configured vendor gets its own colour', () => {
  const colour = vendorPalette(REPORTED);

  const colours = REPORTED.map((v) => colour(v));

  assert.strictEqual(
    new Set(colours).size,
    REPORTED.length,
    `two reviewers share an edge: ${REPORTED.map((v, at) => `${v}=${colours[at]}`).join(', ')}`,
  );
});

test('a full palette of unrelated names still collides nowhere', () => {
  // Twelve names with nothing in common and no anchor among them: the case where every reserved
  // slot has to be lent out rather than held, or four of these would be sharing.
  const names = Array.from({ length: VENDOR_PALETTE.length }, (_, at) => `reviewer-${at}-${at * 7}`);
  const colour = vendorPalette(names);

  assert.strictEqual(new Set(names.map((n) => colour(n))).size, names.length);
});

test('the thirteenth reviewer repeats a colour rather than losing one', () => {
  // Twelve colours cannot dress thirteen people. What must NOT happen is a blank edge or a grey
  // indistinguishable from every uncoloured row — a repeated hue still separates twelve groups.
  const names = Array.from({ length: VENDOR_PALETTE.length + 1 }, (_, at) => `reviewer-${at}-${at * 7}`);
  const colour = vendorPalette(names);

  const colours = names.map((n) => colour(n));

  assert.strictEqual(new Set(colours).size, VENDOR_PALETTE.length, 'twelve of the thirteen are still distinct');
  for (const one of colours) {
    assert.ok(VENDOR_PALETTE.includes(one), `${one} is outside the declared palette`);
  }
});

test('the assignment is decided by the list, not by the order it arrives in', () => {
  // The property that matters across views and across restarts: nothing about WHEN a vendor was
  // first seen can change its colour, because the list is sorted before anything is handed out.
  const forwards = vendorPalette(REPORTED);
  const backwards = vendorPalette([...REPORTED].reverse());
  const shuffled = vendorPalette([REPORTED[3]!, REPORTED[0]!, REPORTED[5]!, REPORTED[1]!, REPORTED[4]!, REPORTED[2]!]);

  for (const name of REPORTED) {
    assert.equal(backwards(name), forwards(name), `${name} moved when the list was reversed`);
    assert.equal(shuffled(name), forwards(name), `${name} moved when the list was shuffled`);
  }
});

test('case and stray space are the same vendor', () => {
  const colour = vendorPalette(REPORTED);

  assert.equal(colour(' Codex '), colour('codex'));
});

test('a vendor spelled with stray space in the LIST still claims its anchor', () => {
  // The anchor lookup normalises too. If it did not, ' Codex ' would be allocated as a new name and
  // could take a colour somebody else is wearing while blue sat unused.
  const colour = vendorPalette([' Codex ', 'GEMINI', 'remsoftdev-codex']);

  assert.equal(colour('codex'), vendorPalette(['codex'])('codex'));
  assert.equal(colour('gemini'), vendorPalette(['gemini'])('gemini'));
  assert.notEqual(colour('remsoftdev-codex'), colour('codex'));
});

/**
 * The five shipped kinds are pinned, and their slots are RESERVED whether or not they are
 * configured — placing them "first" is not enough. With only `claude` on the list, an unanchored
 * vendor would otherwise be free to take blue, and `codex` would come back to a colour somebody
 * else is already wearing.
 */
test('an anchored vendor wears one colour whatever else is configured', () => {
  const anchors = ['codex', 'gemini', 'local', 'claude', 'antigravity'];
  const alone = anchors.map((a) => vendorPalette([a])(a));

  for (const [at, anchor] of anchors.entries()) {
    assert.equal(vendorPalette(anchors)(anchor), alone[at], `${anchor} moved when the other anchors joined`);
    assert.equal(vendorPalette([anchor, ...REPORTED])(anchor), alone[at], `${anchor} moved when the report's list joined`);
    assert.equal(vendorPalette([anchor, 'a-a', 'b-b', 'c-c', 'd-d'])(anchor), alone[at], `${anchor} moved among strangers`);
  }

  assert.strictEqual(new Set(alone).size, anchors.length, 'two anchored kinds are pinned to one slot');
});

test('an absent anchor still holds its colour back', () => {
  const anchors = ['codex', 'gemini', 'local', 'claude', 'antigravity'];
  const reserved = new Set(anchors.map((a) => vendorPalette([a])(a)));
  // Seven unanchored reviewers, no anchor configured: there are exactly seven unreserved slots, so
  // none of these may reach into one an anchor is holding.
  const others = Array.from({ length: VENDOR_PALETTE.length - anchors.length }, (_, at) => `stranger-${at}`);
  const colour = vendorPalette(others);

  for (const one of others) {
    assert.ok(!reserved.has(colour(one)), `${one} took a colour reserved for an anchored kind`);
  }
});

test('past the unreserved slots a stranger may borrow an unclaimed anchor colour', () => {
  // The reservation is a preference, not a wall: with eight strangers and no anchor configured, a
  // borrowed blue is a better answer than a repeat.
  const others = Array.from({ length: VENDOR_PALETTE.length - 4 }, (_, at) => `stranger-${at}`);
  const colour = vendorPalette(others);

  assert.strictEqual(new Set(others.map((o) => colour(o))).size, others.length);
});

test('a vendor that is no longer configured is still coloured', () => {
  // An old round names a reviewer somebody has since removed. It gets a colour from its own name —
  // stable, and the only colour in the product allowed to coincide with a live reviewer's.
  const colour = vendorPalette(REPORTED);

  const stray = colour('a-vendor-nobody-has-any-more');

  assert.ok(VENDOR_PALETTE.includes(stray), 'a stray provider is coloured, never blank');
  assert.equal(stray, vendorPalette([])('a-vendor-nobody-has-any-more'));
});

test('a vendor with no name is the ordinary foreground', () => {
  assert.equal(vendorPalette(REPORTED)('   '), UNNAMED_VENDOR_COLOUR);
});

test('every colour is a contributed theme variable, never a bare hex', () => {
  const names = ['codex', 'gemini', 'local', 'deepseek', 'my-claude', 'antigravity', 'qwen'];
  const colour = vendorPalette(names);

  for (const name of names) {
    // The theme variable FIRST. The hex after the comma is the fallback for a build older than the
    // manifest that declares these ids, where the alternative is an uncoloured edge.
    assert.match(
      colour(name),
      /^var\(--vscode-coai-vendorColour(?:[1-9]|1[0-2]), #[0-9A-F]{6}\)$/,
      `${name} must use a contributed theme colour`,
    );
  }
});

test('the palette is what the function can return', () => {
  const names = Array.from({ length: 200 }, (_, i) => `vendor-${i}`);
  const colour = vendorPalette(names.slice(0, 8));

  for (const one of names.map((n) => colour(n))) {
    assert.ok(VENDOR_PALETTE.includes(one), `${one} is outside the declared palette`);
  }
});

/**
 * The one thing a unit test cannot infer and a webview fails at silently: whether the CSS variable
 * the code writes is the variable VS Code actually publishes. It is `--vscode-` plus the contributed
 * id with its dots turned into dashes and its case left alone — the spelling CredsForDevs has shipped
 * against since its dependency palette landed. Asserted against the manifest so a renamed
 * contribution is a red test rather than twelve invisible borders.
 */
test('every palette colour is declared in the manifest under the id it names', () => {
  const manifest = JSON.parse(readFileSync(join(__dirname, '..', '..', 'package.json'), 'utf8')) as {
    contributes: { colors?: readonly { id: string; description: string; defaults: Record<string, string> }[] };
  };
  const declared = manifest.contributes.colors ?? [];

  assert.deepEqual(declared.map((c) => c.id), [...VENDOR_COLOUR_IDS], 'the manifest declares exactly the palette, in order');

  for (const [at, id] of VENDOR_COLOUR_IDS.entries()) {
    assert.ok(
      VENDOR_PALETTE[at]!.startsWith(`var(--vscode-${id.replace('.', '-')}, `),
      `${VENDOR_PALETTE[at]} does not name ${id}`,
    );
  }

  for (const contributed of declared) {
    assert.deepEqual(
      Object.keys(contributed.defaults).sort(),
      ['dark', 'highContrast', 'highContrastLight', 'light'],
      `${contributed.id} must be readable in all four theme flavours`,
    );
  }
});

// ---------- through the page ----------

function vendor(id: string): Vendor {
  return {
    id, runtime: 'codex', model: '', enabled: true, plan: true, code: true,
    baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
  };
}

function running(): SessionFile {
  const round: RoundRecord = {
    stage: 'PlanReview',
    number: 1,
    verdict: '',
    gatingCount: 0,
    reviewers: '',
    status: 'running',
    startedUtc: new Date().toISOString(),
    completedUtc: '',
    reviewerStates: [
      { provider: 'codex', role: 'PlanCritique', status: 'running', findings: 0, note: '' },
      { provider: '<script>', role: 'PlanCritique', status: 'running', findings: 0, note: '' },
      { provider: '"><img src=x onerror=alert(1)>', role: 'PlanCritique', status: 'running', findings: 0, note: '' },
    ],
  };

  return {
    state: { sessionId: 'a1', repoPath: 'C:/repo', branch: 'main', stage: 'PlanReview', awaitingResolve: false },
    rounds: [round],
  };
}

function state(vendors: readonly Vendor[] = []): PanelState {
  return {
    settings: DEFAULTS,
    vendors,
    codexModels: [], agyModels: [],
    localEngines: {},
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '',
    perSide: false,
    latestServerVersion: '',
    questions: [],
    sessions: [running()],
    openSections: ['rounds'],
    usage: [],
    usageWindow: 'week',
    cliStatus: {},
    modelPrices: {},
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  };
}

test('the vendor word is coloured and the rest of the row is not', () => {
  const html = panelHtml(state(), 'n0nce', Date.now());

  assert.ok(
    html.includes(`<span class="who" style="color:${vendorPalette([])('codex')}">codex</span>/PlanCritique`),
    'the colour stops at the vendor name',
  );
});

test('the round card colours a reviewer as the configured list decided', () => {
  // The panel's rounds and the panel's cards read ONE list, so the running round agrees with the
  // reviewer card above it rather than with a list it inferred from the session file.
  const html = panelHtml(state(REPORTED.map((id) => vendor(id))), 'n0nce', Date.now());
  const colour = vendorPalette(REPORTED);

  assert.ok(
    html.includes(`<span class="who" style="color:${colour('codex')}">codex</span>/PlanCritique`),
    'the round card asks the configured palette',
  );
  assert.ok(
    html.includes(`<div class="vendor" style="border-left-color:${colour('remsoftdev-codex')}">`),
    'and the reviewer card wears the same answer',
  );
});

test('a vendor name from a session file is still escaped', () => {
  // The name comes out of JSON somebody else wrote. A colour is no reason to stop escaping it, and
  // a name carrying a quote must not be able to close the style attribute it sits beside.
  const html = panelHtml(state(REPORTED.map((id) => vendor(id))), 'n0nce', Date.now());

  assert.ok(!html.includes('<script>'), 'a tag-shaped vendor name never reaches the page as markup');
  assert.ok(!html.includes('<img src=x'), 'nor does an attribute-breaking one');
  assert.ok(html.includes('&lt;script&gt;</span>'), 'the name is escaped inside the coloured span');
  assert.ok(html.includes('&quot;&gt;&lt;img src=x'), 'the quote is escaped rather than closing an attribute');
});
