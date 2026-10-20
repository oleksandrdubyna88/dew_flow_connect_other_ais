import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';

import { cachedCliVersion, rememberCliStatus } from '../cliVersionMemory';

/**
 * E3.S3 of todo/PLAN_the_round_counter_tail_E3.md: a chat line's `cli` is the row's RUNTIME, and its `cliVersion` is the
 * catalog probe's LAST answer — read from memory, never a spawn on the turn's road (D7).
 *
 * <p>`chatTurn.ts` imports `vscode`, so the turn itself cannot run here (the house pattern: pin the source, as
 * `chatFreshWiring.test.ts` does). What runs is the memory the turn reads.</p>
 */

const source = (file: string): string => fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

test('AChatTurnWhoseCliWasNeverProbed_WritesAnEmptyVersion_AndSpawnsNothing — the memory answers empty for a row nobody probed', () => {
  rememberCliStatus({});

  assert.equal(cachedCliVersion('work-codex'), '', 'a row nobody probed must read as not recorded');
});

test('TheMemoryHoldsTheProbesLastAnswer_PerVendorRow', () => {
  rememberCliStatus({ 'work-codex': { installed: 'codex-cli 0.131.0', latest: '' }, claude: { installed: '', latest: '2.0.0' } });

  assert.equal(cachedCliVersion('work-codex'), 'codex-cli 0.131.0');
  assert.equal(cachedCliVersion('claude'), '', 'a CLI the probe could not read has no version, whatever is published');
  rememberCliStatus({});
});

test('TheTurnsLedgerWritesTheRowsRuntime_AndReadsTheMemory_NeverASpawn', () => {
  const turn = source('chatTurn.ts');
  const ledger = turn.slice(turn.indexOf('export function ledger('));

  assert.match(ledger, /cli: turn\.vendor\.runtime/u, 'the chat line does not write the row\'s runtime as its cli');
  assert.match(ledger, /cliVersion: cachedCliVersion\(turn\.vendor\.id\)/u, 'the version does not come from the probe\'s memory');
  assert.doesNotMatch(turn, /askVersion|versionProbe/u, 'the chat turn asks a binary its version — a spawn on the turn\'s road');
});

test('ThePanelFeedsTheMemory_WhenItFillsItsCliStatus', () => {
  const panel = source('panelProvider.ts');
  const filling = panel.slice(panel.indexOf('private async vendorCliStatus('), panel.indexOf('private async oneCliStatus('));

  assert.match(filling, /rememberCliStatus\(this\.cliStatus\)/u, 'the panel never hands its probe answer to the chat ledger');
});
