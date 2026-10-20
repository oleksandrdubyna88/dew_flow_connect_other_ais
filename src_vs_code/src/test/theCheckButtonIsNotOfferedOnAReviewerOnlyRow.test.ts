import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import { CONSULTING } from '../featureAvailability.generated';
import { checkButton, checkNotOffered } from '../modelCardWorld';
import { RUNTIMES } from '../models';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { panelState } from './panelPageHarness';
import { bubbled, pageTree, selectorsOf, type PageNode } from './pageTree';
import { runPageHtml } from './pageScriptHarness';

/**
 * The Models card's ✓ Check is a CONSULTANT check, so it is offered only on a runtime that can hold a consultation
 * (todo/PLAN_a_spent_kimi_quota_is_not_a_lost_sign_in.md, D7 / S8).
 *
 * <p>Seen 2026-10-19: the owner pressed ✓ Check on the Kimi card, was asked to pay for one turn, and the server answered
 * that "the vendor 'kimi' runs on 'kimi', which cannot hold a consultation in this build". The button promised a paid
 * turn on every row; the server already refused by name. The page now says so BEFORE the press: a row outside the
 * consultant list (`shared/feature-availability.json`, read through the generated file) draws the button disabled,
 * with the reason as its title, and bound to nothing.</p>
 */

const BASE = DEFAULT_VENDORS[0]!;
const KIMI: Vendor = { ...BASE, id: 'kimi-k3', runtime: 'kimi', model: 'kimi-code/k3' };
const GEMINI: Vendor = { ...BASE, id: 'gemini', runtime: 'gemini', model: '' };
const REMOTE: Vendor = { ...BASE, id: 'team-claude', runtime: 'remote', model: '', remoteVendor: 'claude', teamServerId: 'team' };
const CODEX: Vendor = { ...BASE, id: 'codex', runtime: 'codex', model: 'gpt-5.6' };

const PAID_TURN = 'One real, paid turn of this model in a scratch folder — you are asked first';
const AT_LOAD = ['[data-setting]', '[data-prompt]', '[data-command]', '[data-tab]', '[data-pane]'];
const IDLE = { checking: false, said: '', tone: 'warn' } as const;

/** The Models page for these rows: its tree, and the Check button of one card. */
function modelsPage(rows: readonly Vendor[]): { html: string; page: PageNode } {
  const html = catalogHtml({ ...panelState('reviewers'), vendors: rows, catalogRows: [...rows] }, 'test-nonce', 'models');

  return { html, page: pageTree(html) };
}

function checkOf(page: PageNode, id: string): PageNode {
  const card = page.one((node) => node.tagName.toLowerCase() === 'article' && node.className.includes('card') && node.find((inner) => inner.id === `on-${id}`).length > 0, `card of ${id}`);

  return card.one((node) => node.tagName.toLowerCase() === 'button' && node.text() === '✓ Check', `✓ Check on ${id}`);
}

const ROWS = [KIMI, GEMINI, REMOTE, CODEX];

for (const row of [KIMI, GEMINI, REMOTE]) {
  test(`a ${row.runtime} card shows ✓ Check disabled, with the reason as its title`, () => {
    const check = checkOf(modelsPage(ROWS).page, row.id);

    assert.equal(check.attrs['title'], `the consultant check is not offered for ${row.runtime} — this row reviews plans and code only`);
    assert.equal('disabled' in check.attrs, true, 'the button is not disabled');
    assert.equal('data-command' in check.attrs, false, 'the button is still bound to a command');
    assert.equal('data-id' in check.attrs, false, 'the button still names a row to check');
  });
}

test('pressing it sends nothing', () => {
  const { html, page } = modelsPage(ROWS);
  const posts = runPageHtml(html, selectorsOf(page, AT_LOAD), undefined, { value: undefined });
  bubbled(posts, 'click', checkOf(page, KIMI.id));

  assert.deepEqual(posts.posted.filter((one) => one['command'] === 'checkModel'), []);
});

test('a codex card is unchanged', () => {
  const { html, page } = modelsPage(ROWS);
  const check = checkOf(page, CODEX.id);

  assert.equal(check.attrs['data-command'], 'checkModel');
  assert.equal(check.attrs['data-id'], 'codex');
  assert.equal(check.attrs['title'], PAID_TURN);
  assert.equal('disabled' in check.attrs, false);

  const posts = runPageHtml(html, selectorsOf(page, AT_LOAD), undefined, { value: undefined });
  bubbled(posts, 'click', check);
  assert.equal(posts.posted.filter((one) => one['command'] === 'checkModel' && one['id'] === 'codex').length, 1, 'the offered Check no longer posts');
});

test('an offered row that is checking is still held, with the paid-turn title', () => {
  const button = checkButton('codex', 'codex', { ...IDLE, checking: true });

  assert.match(button, / disabled>/);
  assert.match(button, /data-command="checkModel" data-id="codex"/);
  assert.ok(button.includes(`title="${PAID_TURN}"`));
});

test('the rule is the generated list, not a hand list', () => {
  for (const runtime of RUNTIMES) {
    assert.equal(
      checkButton('x', runtime, IDLE).includes('data-command="checkModel"'),
      CONSULTING.includes(runtime),
      `✓ Check on a ${runtime} row disagrees with features.consultant`,
    );
  }
});

test('the reason is one sentence, said in one place', () => {
  assert.equal(checkNotOffered('kimi'), 'the consultant check is not offered for kimi — this row reviews plans and code only');
});
