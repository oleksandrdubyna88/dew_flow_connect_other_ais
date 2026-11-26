import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { MANAGER_WORDS } from '../remoteAccountsReply';
import { INSTALL_JQ } from '../remoteAccountsRows';
import { accountsCommandOf, COPY_DONE, COPY_FAILED, PAGE_WORDS } from '../remoteAccountsMessages';
import { tableModel } from '../remoteAccountsTable';
import type { TableModel } from '../remoteAccountsTable';
import { claude, inputOf, memoryOf, reading, refused, seen } from './accountsTableFixtures';

/**
 * The check of a page's message against the model (E2.S3, E2S3-T9 and the half of T10 that is pure): a closed name with a known argument is a
 * command; anything else is nothing. The page is not trusted.
 */

const MODEL: TableModel = tableModel(inputOf({
  hosts: ['ok', 'no', 'jq', 'nojq', 'wsl'],
  memory: memoryOf(
    ['ok', seen(reading({ claude: claude() }), 1)],
    ['jq', seen(reading({ jqMissing: 'dnf' }), 1)],
    ['nojq', seen(reading({ jqMissing: 'none' }), 1)],
  ),
  standings: [refused('no')],
}));

test('E2S3-T9: each closed name with no argument is its command', () => {
  for (const kind of ['ready', 'refresh', 'stop', 'start', 'allow', 'withdraw', 'save-now'] as const) {
    assert.deepEqual(accountsCommandOf({ type: kind }, MODEL), { kind });
  }
  assert.deepEqual([...PAGE_WORDS].sort(), ['allow', 'copy-install', 'read-again', 'ready', 'refresh', 'refuse', 'save-now', 'start', 'stop', 'withdraw'].sort());
});

test('E2S3-T9: refuse and read-again name a host of the model that can take them', () => {
  assert.deepEqual(accountsCommandOf({ type: 'refuse', host: 'ok' }, MODEL), { kind: 'refuse', host: 'ok' });
  assert.deepEqual(accountsCommandOf({ type: 'read-again', host: 'no' }, MODEL), { kind: 'read-again', host: 'no' });
  assert.equal(accountsCommandOf({ type: 'refuse', host: 'no' }, MODEL), undefined, 'a refused host has no Not this host');
  assert.equal(accountsCommandOf({ type: 'read-again', host: 'ok' }, MODEL), undefined, 'only a refused host can be read again');
  assert.equal(accountsCommandOf({ type: 'refuse', host: 'wsl' }, MODEL), undefined);
  assert.equal(accountsCommandOf({ type: 'refuse', host: 'elsewhere' }, MODEL), undefined, 'a host that is not in the model');
  assert.equal(accountsCommandOf({ type: 'refuse' }, MODEL), undefined);
  assert.equal(accountsCommandOf({ type: 'refuse', host: 7 }, MODEL), undefined);
  assert.equal(accountsCommandOf({ type: 'refuse', host: '-oProxyCommand=x' }, MODEL), undefined);
});

test('E2S3-T9: copy-install hands the copier exactly INSTALL_JQ[word] of that place; a place with no manager word gets nothing', () => {
  const command = accountsCommandOf({ type: 'copy-install', place: 'jq' }, MODEL);

  assert.deepEqual(command, { kind: 'copy-install', decision: { kind: 'copy', text: INSTALL_JQ.dnf, done: COPY_DONE, failed: COPY_FAILED } });
  assert.equal(accountsCommandOf({ type: 'copy-install', place: 'nojq' }, MODEL), undefined, 'a host with no package manager has no command');
  assert.equal(accountsCommandOf({ type: 'copy-install', place: 'ok' }, MODEL), undefined);
  assert.equal(accountsCommandOf({ type: 'copy-install', place: 'zzz' }, MODEL), undefined);
  assert.equal(accountsCommandOf({ type: 'copy-install', place: 'jq', text: 'rm -rf /' }, MODEL) !== undefined, true);
  const sent = accountsCommandOf({ type: 'copy-install', place: 'jq', text: 'rm -rf /', command: 'rm -rf /' }, MODEL);
  assert.equal(sent?.kind === 'copy-install' && sent.decision.kind === 'copy' ? sent.decision.text : '', INSTALL_JQ.dnf, 'a text the page sends is never copied');
  for (const word of MANAGER_WORDS) {
    const model = tableModel(inputOf({ hosts: ['h'], memory: memoryOf(['h', seen(reading({ jqMissing: word }), 1)]) }));
    const one = accountsCommandOf({ type: 'copy-install', place: 'h' }, model);

    assert.equal(one !== undefined, INSTALL_JQ[word] !== undefined);
  }
});

test('E2S3-T9: an unknown name, a non-object and a missing model are nothing', () => {
  for (const one of [undefined, null, 'ready', 7, [], {}, { type: 'delete' }, { type: 7 }, { type: 'refresh ' }, { type: 'READY' }, { type: 'constructor' }, { type: '__proto__' }]) {
    assert.equal(accountsCommandOf(one, MODEL), undefined, JSON.stringify(one));
  }
  assert.equal(accountsCommandOf({ type: 'refuse', host: 'ok' }, undefined), undefined, 'with no model there is no host to name');
  assert.deepEqual(accountsCommandOf({ type: 'ready' }, undefined), { kind: 'ready' });
});

test('E2S2-F1 (message): refuse and read-again for a host of 250 characters carry the whole host, and the copy of its install command is found by it', () => {
  const host = `${'h'.repeat(249)}x`;
  const model = tableModel(inputOf({ hosts: [host], memory: memoryOf([host, seen(reading({ claude: claude() }), 1)]) }));

  assert.deepEqual(accountsCommandOf({ type: 'refuse', host }, model), { kind: 'refuse', host });
  assert.equal(accountsCommandOf({ type: 'refuse', host: model.blocks[0]?.shown }, model), undefined, 'the cut name names a block');
});
