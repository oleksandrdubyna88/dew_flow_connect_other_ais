import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  CONSULTANT_SINCE,
  KIMI_CONSULTANT_SINCE,
  KIMI_RUNTIME_SINCE,
  RUNTIME_SINCE,
  consultantOnServer,
  consultantRuntimeNote,
  runtimeOnServer,
} from '../runtimeSince';

/**
 * The consultant-runtime gate (E5.7 of todo/PLAN_vibe_and_kimi_runtimes_E5.md, decision 1): which `coai-mcp` first CONSULTS on a
 * runtime, beside the table of which one first KNOWS it. A server may review with a runtime for releases before it can hold a
 * consultation on it — Vibe reviewed from 0.53.0 and consulted from 0.54.0, Kimi reviews from 0.57.0 — so the two tables are two
 * facts, read by one rule.
 */

test('S5.7.1 consultantSinceNamesKimiAtTheNextMinor: vibe at the release that shipped its consultant, kimi at the next minor', () => {
  assert.deepEqual({ ...CONSULTANT_SINCE }, { vibe: '0.54.0', kimi: '0.58.0' });
  assert.equal(CONSULTANT_SINCE['kimi'], KIMI_CONSULTANT_SINCE, 'the table and the named constant say one thing');
});

test('a server consults on a runtime from its SINCE on — and an unknown server, or a runtime outside the table, is not old', () => {
  assert.equal(consultantOnServer('kimi', '0.57.0'), false, 'the release that reviews with kimi does not consult on it');
  assert.equal(consultantOnServer('kimi', '0.58.0'), true);
  assert.equal(consultantOnServer('kimi', '1.0.0'), true);
  assert.equal(consultantOnServer('kimi', ''), true, 'unknown is not old — the runtime gate’s rule');
  assert.equal(consultantOnServer('vibe', '0.53.0'), false, 'Vibe reviewed in 0.53.0 and consulted only from 0.54.0');
  assert.equal(consultantOnServer('codex', '0.1.0'), true, 'a runtime every server consults on is not in the table');
});

test('the two tables are read by ONE rule: a runtime known to the server is not therefore one it consults on', () => {
  // Kimi is known from KIMI_RUNTIME_SINCE; a server at exactly that release reviews with it and still cannot consult on it.
  assert.equal(RUNTIME_SINCE['kimi'], KIMI_RUNTIME_SINCE);
  assert.equal(runtimeOnServer('kimi', KIMI_RUNTIME_SINCE), true);
  assert.equal(consultantOnServer('kimi', KIMI_RUNTIME_SINCE), false);
});

test('the note names the installed version, the runtime and the release to update to — and is silent when there is nothing to say', () => {
  assert.equal(consultantRuntimeNote('0.57.0', 'kimi'),
    'The coai-mcp you have installed (0.57.0) cannot consult on kimi; update it to 0.58.0 or later — the MCP server tab.');
  assert.equal(consultantRuntimeNote('0.58.0', 'kimi'), '');
  assert.equal(consultantRuntimeNote('', 'kimi'), '', 'a server nobody has versioned is not behind');
  assert.equal(consultantRuntimeNote('0.57.0', 'codex'), '');
});
