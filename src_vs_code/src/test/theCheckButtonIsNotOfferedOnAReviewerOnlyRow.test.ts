import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import type { ConsultantHealthState } from '../consultantHealthState';
import { CONSULTING } from '../featureAvailability.generated';
import { checkNotOffered } from '../modelCardWorld';
import { RUNTIMES } from '../models';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { panelState } from './panelPageHarness';
import { bubbled, pageTree, selectorsOf, type PageNode } from './pageTree';
import { type Page, runPageHtml } from './pageScriptHarness';

/**
 * The Models card's ✓ Check is a CONSULTANT check, so it is offered only on a runtime that can hold a consultation
 * (todo/PLAN_a_spent_kimi_quota_is_not_a_lost_sign_in.md, D7 / S8).
 *
 * <p>Seen 2026-10-19: the owner pressed ✓ Check on the Kimi card, was asked to pay for one turn, and the server answered
 * that "the vendor 'kimi' runs on 'kimi', which cannot hold a consultation in this build". The button promised a paid
 * turn on every row; the server already refused by name. The page now says so BEFORE the press: a row outside the
 * consultant list (`shared/feature-availability.json`, read through the generated file) draws the button disabled,
 * with the reason as its title, and bound to nothing.</p>
 *
 * <p>Every case RUNS the page: the Models page is rendered by the real `catalogHtml` (the card path, never a helper-built
 * string), its own script is run over what it drew (`runPageHtml`), and the button is read off that running page and
 * pressed there. A button whose `disabled` is dropped in the real render path fails these cases.</p>
 */

const BASE = DEFAULT_VENDORS[0]!;
const KIMI: Vendor = { ...BASE, id: 'kimi-k3', runtime: 'kimi', model: 'kimi-code/k3' };
const GEMINI: Vendor = { ...BASE, id: 'gemini', runtime: 'gemini', model: '' };
const REMOTE: Vendor = { ...BASE, id: 'team-claude', runtime: 'remote', model: '', remoteVendor: 'claude', teamServerId: 'team' };
const CODEX: Vendor = { ...BASE, id: 'codex', runtime: 'codex', model: 'gpt-5.6' };

const PAID_TURN = 'One real, paid turn of this model in a scratch folder — you are asked first';
const AT_LOAD = ['[data-setting]', '[data-prompt]', '[data-command]', '[data-tab]', '[data-pane]'];

/** A consultant health in which this window is running a check for each of these rows. */
function checkingHealth(...ids: readonly string[]): ConsultantHealthState {
  return {
    thisSide: { label: 'Windows', probe: { kind: 'asking' }, files: undefined, checking: ids.map((id) => `model-${id}`), runs: {} },
    otherSides: [],
    nowMs: Date.parse('2026-10-05T10:00:00.000Z'),
  };
}

interface RunningModelsPage {
  readonly tree: PageNode;
  readonly page: Page;
}

/** The Models page for these rows, drawn by the real render path and then RUN — its script over what it drew. */
function runningModelsPage(rows: readonly Vendor[], health?: ConsultantHealthState): RunningModelsPage {
  const html = catalogHtml({ ...panelState('reviewers'), vendors: rows, catalogRows: [...rows], consultantHealth: health }, 'test-nonce', 'models');
  const tree = pageTree(html);

  return { tree, page: runPageHtml(html, selectorsOf(tree, AT_LOAD), undefined, { value: undefined }) };
}

function checkOf(tree: PageNode, id: string): PageNode {
  const card = tree.one((node) => node.tagName.toLowerCase() === 'article' && node.className.includes('card') && node.find((inner) => inner.id === `on-${id}`).length > 0, `card of ${id}`);

  return card.one((node) => node.tagName.toLowerCase() === 'button' && node.text() === '✓ Check', `✓ Check on ${id}`);
}

/** What the running page posted for a Check press — the page's handlers run, whatever the button's own state. */
function checksPostedBy(running: RunningModelsPage, button: PageNode): readonly Record<string, unknown>[] {
  bubbled(running.page, 'click', button);

  return running.page.posted.filter((one) => one['command'] === 'checkModel');
}

const ROWS = [KIMI, GEMINI, REMOTE, CODEX];

for (const row of [KIMI, GEMINI, REMOTE]) {
  test(`a ${row.runtime} card shows ✓ Check disabled, with the reason as its title, and pressing it posts no checkModel`, () => {
    const running = runningModelsPage(ROWS);
    const check = checkOf(running.tree, row.id);

    assert.equal(check.attrs['title'], `the consultant check is not offered for ${row.runtime} — this runtime cannot hold a consultation`);
    assert.equal('disabled' in check.attrs, true, 'the button is not disabled');
    assert.equal('data-command' in check.attrs, false, 'the button is still bound to a command');
    assert.equal('data-id' in check.attrs, false, 'the button still names a row to check');
    assert.deepEqual(checksPostedBy(running, check), [], 'a press posted a checkModel, which would open the paid-turn prompt');
  });
}

test('a codex card is unchanged: enabled, the paid-turn title, and its press posts checkModel for that row', () => {
  const running = runningModelsPage(ROWS);
  const check = checkOf(running.tree, CODEX.id);

  assert.equal(check.attrs['data-command'], 'checkModel');
  assert.equal(check.attrs['data-id'], 'codex');
  assert.equal(check.attrs['title'], PAID_TURN);
  assert.equal('disabled' in check.attrs, false);
  assert.equal(checksPostedBy(running, check).filter((one) => one['id'] === 'codex').length, 1, 'the offered Check no longer posts');
});

test('an offered row that is checking is still held, with the paid-turn title', () => {
  const running = runningModelsPage(ROWS, checkingHealth('codex'));
  const check = checkOf(running.tree, CODEX.id);

  assert.equal('disabled' in check.attrs, true, 'a row that is checking must be held');
  assert.equal(check.attrs['data-command'], 'checkModel');
  assert.equal(check.attrs['data-id'], 'codex');
  assert.equal(check.attrs['title'], PAID_TURN);
});

test('the rule is the generated list, not a hand list — read off a running page, one card per runtime', () => {
  const rows = RUNTIMES.map((runtime): Vendor => ({ ...BASE, id: `row-${runtime}`, runtime, model: '', remoteVendor: 'claude', teamServerId: 'team' }));
  const running = runningModelsPage(rows);

  for (const runtime of RUNTIMES) {
    const check = checkOf(running.tree, `row-${runtime}`);

    assert.equal('data-command' in check.attrs, CONSULTING.includes(runtime), `✓ Check on a ${runtime} row disagrees with features.consultant`);
    assert.equal('disabled' in check.attrs, !CONSULTING.includes(runtime), `✓ Check on a ${runtime} row is held wrongly`);
  }
});

test('the reason is one sentence, said in one place', () => {
  assert.equal(checkNotOffered('kimi'), 'the consultant check is not offered for kimi — this runtime cannot hold a consultation');
});
