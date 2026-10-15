import assert from 'node:assert/strict';
import { test } from 'node:test';

import { catalogHtml } from '../catalogPage';
import { planPricesHtml } from '../planPricesView';
import type { TeamServerState } from '../teamServerView';
import { panelState } from './panelPageHarness';
import { bubbled, pageTree } from './pageTree';
import { runPageHtml } from './pageScriptHarness';

/**
 * Where a plan price is typed (story 6.2 of todo/PLAN_every_round_is_counted.md): Setup › Team servers, once per vendor
 * account the server serves — one box and one Save price button each (every action a panel button). A refused price is
 * said AT ITS CARD, with what was typed kept in the box, and nothing is stored.
 */

function server(id: string, email: string, vendors: readonly string[]): TeamServerState {
  return {
    server: { id, name: `Server ${id}`, url: `https://${id}.example.com` },
    email,
    problem: '',
    stale: false,
    catalog: {
      serverVersion: '0.12.0', isAdmin: false, error: '',
      vendors: vendors.map((vendor) => ({ id: vendor, runtime: vendor, models: [], slots: { total: 1, ready: 1, coolingDown: 0, needsSignIn: 0 } })),
    },
  };
}

const SERVERS = [server('acme', 'me@example.com', ['codex', 'claude']), server('other', '', ['codex'])];

test('one box per vendor of a server this side is signed in to, holding the stored price', () => {
  const tree = pageTree(planPricesHtml(SERVERS, { 'acme/codex': 200 }, undefined));

  const boxes = tree.find((node) => node.tagName === 'INPUT');
  assert.deepEqual(boxes.map((box) => box.value), ['200', ''], 'the stored price is not in its box, or a signed-out server drew boxes');
  const buttons = tree.find((node) => node.dataset.planPrice !== undefined);
  assert.deepEqual(buttons.map((button) => [button.dataset.planPrice, button.text()]), [['acme/codex', 'Save price'], ['acme/claude', 'Save price']]);
  assert.ok(buttons.every((button) => button.attrs['type'] === 'button'), 'a Save price that would submit a form');
});

test('a refused price is said at its own card, and what was typed stays in the box', () => {
  const tree = pageTree(planPricesHtml(SERVERS, { 'acme/codex': 200 }, { key: 'acme/codex', typed: '-5', reason: 'a price cannot be negative' }));

  const box = tree.find((node) => node.tagName === 'INPUT')[0]!;
  assert.equal(box.value, '-5', 'the refused text was replaced by the stored price, so the person cannot see what was refused');
  const alert = tree.one((node) => node.attrs['role'] === 'alert', 'the refusal');
  assert.match(alert.text(), /a price cannot be negative/);
  assert.equal(box.attrs['aria-describedby'], alert.id, 'the box does not point at its refusal');
  assert.equal(tree.find((node) => node.attrs['role'] === 'alert').length, 1, 'the refusal is drawn at more than its own card');
});

test('no server signed in: one sentence, no box', () => {
  const tree = pageTree(planPricesHtml([server('other', '', ['codex'])], {}, undefined));

  assert.equal(tree.find((node) => node.tagName === 'INPUT').length, 0);
  assert.match(tree.text(), /Sign in to a Team server to type what its subscriptions cost/);
});

test('every word from a server is escaped', () => {
  const html = planPricesHtml([server('acme', 'me@example.com', ['<b>x</b>'])], {}, undefined);

  assert.doesNotMatch(html, /<b>x<\/b>/);
});

test('on the Settings page, Save price posts the account and what was typed — the host decides', () => {
  const state = panelState('reviewers', { teamServers: SERVERS, planPrices: {} });
  const html = catalogHtml(state, 'test-nonce', 'setup/team');
  const tree = pageTree(html);
  const pane = tree.one((node) => node.dataset.pane === 'setup/team', 'the Team servers pane');
  const button = pane.find((node) => node.dataset.planPrice === 'acme/codex')[0];
  assert.ok(button !== undefined, 'Setup › Team servers draws no Save price for codex on acme');
  const box = tree.one((node) => node.id === button.dataset.planPriceBox, 'the box beside it');
  box.value = '19.99';
  const page = runPageHtml(html, { [`#${box.id}`]: [box] });

  bubbled(page, 'click', button);

  const posted = page.posted.filter((one) => one['type'] === 'command').map(({ command, id }) => ({ command, id }));
  assert.deepEqual(posted, [{ command: 'savePlanPrice', id: 'acme/codex|19.99' }]);
});
