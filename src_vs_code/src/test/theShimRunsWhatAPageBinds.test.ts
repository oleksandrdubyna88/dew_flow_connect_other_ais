import assert from 'node:assert/strict';
import { test } from 'node:test';

import { Node, runPageHtml } from './rolesPageHarness';

/**
 * The shared DOM shim, widened for the text controls (`todo/PLAN_every_page_reads_alike.md`, S1): they bind
 * EACH button rather than one delegated listener, and the tone writes custom properties. Held here so a
 * shim that quietly stopped doing either cannot make a page's tests pass by not running what it binds.
 */

const page = (script: string): string => `<html><body><script nonce="n">${script}</script></body></html>`;

test('a click runs the listener the page bound to that very button, then bubbles to the document', () => {
  const button = new Node({ zoom: '1' }, 'BUTTON');
  const seen: string[] = [];
  const running = runPageHtml(page(`
    const vscode = acquireVsCodeApi();
    for (const b of document.querySelectorAll('button[data-zoom]')) {
      b.addEventListener('click', () => { vscode.postMessage({ type: 'own', delta: Number(b.dataset.zoom) }); });
    }
    document.addEventListener('click', () => { vscode.postMessage({ type: 'bubbled' }); });
  `), { 'button[data-zoom]': [button] });

  button.click();
  for (const message of running.posted) {
    seen.push(String(message['type']));
  }

  assert.deepEqual(seen, ['own', 'bubbled'], 'the button\'s own listener did not run, or ran after the document\'s');
});

test('a node the page never bound and never put in the document refuses a click', () => {
  runPageHtml(page('acquireVsCodeApi();'));

  assert.throws(() => { new Node({}, 'BUTTON').click(); }, /not in the running page/);
});

test('what the page writes to the body and the root is recorded, custom properties included', () => {
  const running = runPageHtml(page(`
    acquireVsCodeApi();
    document.body.style.fontSize = '20px';
    document.body.style.setProperty('--coai-text', 'red');
    document.body.style.color = 'var(--coai-text)';
    document.documentElement.style.fontSize = '20px';
  `));

  assert.equal(running.body.fontSize, '20px');
  assert.equal(running.body.custom['--coai-text'], 'red');
  assert.equal(running.body.color, 'var(--coai-text)');
  assert.equal(running.root.fontSize, '20px');
});
