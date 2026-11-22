import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import path from 'node:path';
import { CONVENTIONS_ROLE_SINCE } from '../prompts';
import { PanelState } from '../panelView';
import { everyPageHtml } from './panelPages';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';
import { SNIPPET_VERSION } from '../claudeSnippet';

/**
 * The panel and the server agree on the roles: the seed both read is loaded, and a panel ahead of its server says so.
 *
 * <p>The per-round prompt picker this file used to hold against the server's prompt catalog is gone with the server's
 * `PromptsPerRound` (v2 E3.S2): a role has one prompt now, so there is no round to pick for.</p>
 */

/** Enough panel to render the Prompts section; every field the version-skew test does not read. */
const baseState = (): PanelState => ({
  settings: DEFAULTS,
  vendors: DEFAULT_VENDORS,
  codexModels: [], agyModels: [],
  localEngines: {},
  server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
  hostKind: 'windows',
  side: '',
  perSide: false,
  questions: [],
  sessions: [],
  openSections: ['prompts'],
  usage: [],
  usageWindow: 'week',
  cliStatus: {},
  cardPrices: {},
  snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  latestServerVersion: '',
});

/** The seed (`shared/builtin-roles.json`), read as the server reads it — not the panel's generated copy of it. */
interface SeedRole {
  readonly id: string;
  readonly prompts: readonly { readonly id: string }[];
}

const SEED: readonly SeedRole[] = JSON.parse(
  fs.readFileSync(path.join(__dirname, '..', '..', '..', 'shared', 'builtin-roles.json'), 'utf8'),
).roles;

test('the seed actually loaded', () => {
  // The regular expression this replaced could return an empty map and leave every assertion below
  // passing over nothing — which is how the vacuous version of this file survived a day.
  assert.ok(SEED.length >= 5, `the seed has ${SEED.length} roles`);
});

/**
 * The one case the transcription above cannot cover: the two programs are versioned separately.
 *
 * <p>The two agree in the SOURCE, and are installed apart — an extension updates itself, a server
 * is a binary somebody presses a button to replace. Below {@link CONVENTIONS_ROLE_SINCE} the server
 * does not know `Conventions` is a role at all, so it does not run it: the box is in the panel and
 * the reviewer never appears. Raised by three reviewers as version skew when the warning was about
 * a wrong prompt — and all three, and this comment before them, called it a failed round. It is not:
 * the server reads its gate from its own list of roles and never parses a name the panel sends.</p>
 */
test('a panel ahead of its server says so, instead of showing a round the server will not run', () => {
  const withServer = (server: PanelState['server']): string =>
    everyPageHtml({ ...baseState(), server }, 'n0nce');

  const behind = withServer({ kind: 'known', version: '0.18.7', remembered: true, updateOffered: true });
  assert.ok(behind.includes('does not'), 'an older server is named');
  assert.ok(behind.includes('0.18.7'), 'and so is the version that is there');

  // 0.18.9 by name, because it is the one everybody has: it is yesterday's release and it has
  // four roles. This line is what would catch the constant sliding back onto it.
  const released = withServer({ kind: 'known', version: '0.18.9', remembered: true, updateOffered: true });
  assert.ok(released.includes('does not know'), '0.18.9 predates the role and must say so');

  const current = withServer({ kind: 'known', version: CONVENTIONS_ROLE_SINCE, remembered: true, updateOffered: false });
  assert.ok(!current.includes('does not know'), 'the server that agrees says nothing');

  const newer = withServer({ kind: 'known', version: '0.19.0', remembered: true, updateOffered: false });
  assert.ok(!newer.includes('does not know'), 'nor does a later one');

  const absent = withServer({ kind: 'absent', version: '', remembered: false, updateOffered: false });
  assert.ok(!absent.includes('does not know'), 'a server nobody has installed is not behind');

  // It counts ROLES, and the distinction is not pedantry: reviewers are vendors x roles, so with
  // two vendors an old server runs six reviewer calls, not three. The warning said "three
  // reviewers, not four" until two reviewers on the code round pointed at the panel's own fan-out
  // sentence, which multiplies by the vendor count three lines further down the same page.
  assert.ok(behind.includes('three code roles, not four'), 'the unit is roles');
  assert.ok(!behind.includes('three reviewers'), 'a reviewer count would contradict the fan-out line');
});
