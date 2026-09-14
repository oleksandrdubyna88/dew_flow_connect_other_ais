import * as assert from 'node:assert';
import { test } from 'node:test';
import { DEFAULTS } from '../settingsShape';
import { PanelState, panelHtml } from '../panelView';
import {
  documentSetting, pinnedDocument, reviewsDocuments, Vendor, vendorsEnv, vendorsFrom,
} from '../vendors';

/**
 * The third stage box: whether this reviewer reads documents, and — on a Team server — whether the
 * document leaves this machine at all.
 *
 * <p>Until plan 5 there were two boxes and three stages, so a document round rode the PLAN tick:
 * "this vendor is good at prose" was the nearest thing to a switch there was. That reading is fair
 * for a reviewer this machine launches and stops being fair for one that runs on a shared company
 * box, because the same tick then decides whether a file somebody was handed crosses the network.</p>
 *
 * <p>So an absent value answers differently on either side of that line, and the panel draws the
 * answer rather than the stored field — a box drawn from a field that can be absent would be
 * unticked while the round runs anyway, or ticked while it does not.</p>
 */

const LOCAL: Vendor = {
  id: 'local',
  runtime: 'local',
  model: 'qwen',
  enabled: true,
  plan: true,
  code: true,
  baseUrl: '',
  executablePath: '',
  pricePerMillionIn: 0,
  pricePerMillionOut: 0,
};

const REMOTE: Vendor = {
  ...LOCAL,
  id: 'remsoft-dev-codex',
  runtime: 'remote',
  remoteVendor: 'codex',
  baseUrl: 'https://coai.example.com',
};

test('a local reviewer with no document switch follows its plan tick', () => {
  assert.equal(reviewsDocuments(LOCAL), true);
  assert.equal(reviewsDocuments({ ...LOCAL, plan: false }), false);
});

test('a Team server with no document switch does NOT take documents', () => {
  // The consent rule. Ticking `plan` never meant "this file may go to the shared box", and every
  // configuration written before documents existed has that tick.
  assert.equal(reviewsDocuments(REMOTE), false);
  assert.equal(reviewsDocuments({ ...REMOTE, plan: true }), false, 'the plan tick does not grant it');
});

test('an explicit switch wins on either side', () => {
  assert.equal(reviewsDocuments({ ...REMOTE, document: true }), true);
  assert.equal(reviewsDocuments({ ...LOCAL, document: false }), false);
});

test('a vendor turned off entirely is still drawn by its own switch', () => {
  // `enabled` is the master switch and it is enforced where rounds are built, not here: this
  // function answers "which stages did they ask for", and a disabled vendor's boxes stay as they
  // were so turning it back on restores what it had.
  assert.equal(reviewsDocuments({ ...LOCAL, enabled: false }), true);
});

/**
 * The absent state ends at the first touch.
 *
 * <p>Otherwise somebody who unticks *plan* on a local reviewer loses its document rounds as an
 * invisible side effect of a decision about plans — and somebody who ticks it gains them. The panel
 * is where a person decides; the moment they decide anything on this card, what was inferred becomes
 * something they said.</p>
 */
test('changing the plan box pins what the document switch was silently meaning', () => {
  assert.deepEqual(pinnedDocument(LOCAL, 'plan'), { document: true });
  assert.deepEqual(pinnedDocument(REMOTE, 'plan'), { document: false });
});

test('nothing is pinned when the switch was already said, or when another box moved', () => {
  assert.deepEqual(pinnedDocument({ ...LOCAL, document: false }, 'plan'), {}, 'they already decided');
  assert.deepEqual(pinnedDocument(LOCAL, 'code'), {}, 'the code box never meant documents');
  assert.deepEqual(pinnedDocument(LOCAL, 'document'), {}, 'the write itself carries that value');
});

test('a stored vendor keeps an absent document switch absent', () => {
  const [read] = vendorsFrom([{ id: 'local', runtime: 'local', plan: true }]);

  assert.equal(read?.document, undefined, 'absent is a value here, not a missing default');
  assert.equal(reviewsDocuments(read as Vendor), true);
});

test('a stored FALSE is read as false rather than folded away', () => {
  const [read] = vendorsFrom([{ id: 'local', runtime: 'local', document: false }]);

  assert.equal(read?.document, false);
});

/**
 * The three states have names, because the interesting one is the absence.
 *
 * <p>`coai-mcp` carries them as `DocumentReviews.Unspecified | Yes | No` — a routing rule reading a
 * null is what its doctrine forbids. The stored field here stays `boolean | undefined` because that
 * is the wire format `coai.vendors` holds and the settings block sends verbatim; the name is for
 * reading and for saying which state a test is about.</p>
 */
test('the three states are named, and the absent one is not "no"', () => {
  assert.equal(documentSetting(LOCAL), 'unspecified');
  assert.equal(documentSetting({ ...LOCAL, document: true }), 'yes');
  assert.equal(documentSetting({ ...LOCAL, document: false }), 'no');
  assert.notEqual(documentSetting(REMOTE), 'no', 'a Team server nobody asked was never told no');
  assert.equal(reviewsDocuments(REMOTE), false, 'it is read AS no, which is a different statement');
});

test('the switch reaches coai-mcp whenever it was said, in either direction', () => {
  // Unlike `plan` and `code`, which are emitted only when narrowed: an absent `document` means
  // something on the other side, so emitting only `false` would throw away the half that says yes.
  assert.ok(vendorsEnv([{ ...LOCAL, document: true }]).includes('"document":true'));
  assert.ok(vendorsEnv([{ ...LOCAL, document: false }]).includes('"document":false'));
  assert.ok(!vendorsEnv([LOCAL]).includes('document'), 'and says nothing when nobody has');
});

function page(vendors: readonly Vendor[]): string {
  return panelHtml({
    settings: DEFAULTS,
    vendors,
    codexModels: [], agyModels: [],
    localEngines: {},
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '', perSide: false,
    questions: [], sessions: [], openSections: ['reviewers'],
    usage: [], usageWindow: 'day', latestServerVersion: '',
    cliStatus: {}, modelPrices: {},
    snippetStatus: { kind: 'absent', version: 0 },
    teamServers: [],
  } as unknown as PanelState, 'nonce');
}

test('every vendor card carries a document box, priced or not', () => {
  // A priced card puts plan and code on its two price rows and has nowhere to hang a third, so the
  // document box gets a line of its own; a Team-server card has no price rows and takes all three.
  // A box with no home is a decision nobody can make.
  const html = page([LOCAL, REMOTE]);

  assert.ok(html.includes('data-setting="document" data-vendor="local"'));
  assert.ok(html.includes('data-setting="document" data-vendor="remsoft-dev-codex"'));
});

test('the document box is drawn UNTICKED for a Team server nobody has asked', () => {
  const html = page([REMOTE]);
  const box = html.slice(html.indexOf('data-setting="document" data-vendor="remsoft-dev-codex"'));

  assert.ok(
    !box.slice(0, 120).includes('checked'),
    'a ticked box over a round that will not happen is the lie this whole switch exists to end',
  );
});

test('and TICKED for a local reviewer that reviews plans', () => {
  const html = page([LOCAL]);
  const box = html.slice(html.indexOf('data-setting="document" data-vendor="local"'));

  assert.ok(box.slice(0, 120).includes('checked'));
});
