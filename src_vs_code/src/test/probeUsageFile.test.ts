import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';

import { flushLedgers } from '../jsonlLedger';
import { PROBE_USAGE_FILE, probeLine, probeUsagePath, recordingRun } from '../probeUsageFile';
import { roundsLoader } from '../roundCountsRead';
import { parseUsage } from '../usage';

/**
 * The extension's Claude model probe writes what it spent (story 3.3 of todo/PLAN_every_round_is_counted.md, the
 * paid-path census row 8): up to four billed requests a week used to write nothing at all.
 *
 * <p>Its own ledger, `probe-usage.jsonl`, written by the extension's one append helper — the consultation of
 * 2026-10-11 chose it over a `kind` in the chat ledger (an older build reads every line there as a chat turn with real
 * money) and over a new `coai-mcp` mode (a release dependency that fixes nothing). The lines are `UsageEntry`-shaped,
 * so every reader of `usage.jsonl` parses them unchanged.</p>
 */

const NOW = Date.parse('2026-10-11T10:00:00.000Z');

/** The real `--output-format json` shape of an answered probe, cache reads included. */
const ANSWERED = JSON.stringify({
  type: 'result', subtype: 'success', is_error: false, result: 'Hi!', total_cost_usd: 0.0123,
  usage: { input_tokens: 4, cache_read_input_tokens: 9000, cache_creation_input_tokens: 200, output_tokens: 11 },
  modelUsage: { 'claude-haiku-5-5': { inputTokens: 4, outputTokens: 11 } },
});

function line(text: string): Record<string, unknown> {
  return JSON.parse(text) as Record<string, unknown>;
}

test('an answered probe is one probe line, with what Claude itself reported and billed', () => {
  const written = line(probeLine('haiku', { code: 0, output: ANSWERED }, NOW - 3_400, NOW));

  assert.equal(written['kind'], 'probe');
  assert.equal(written['provider'], 'claude');
  assert.equal(written['model'], 'haiku', 'the line names the candidate that was asked');
  assert.equal(written['tokensIn'], 9_204, 'every input field Claude bills, cache reads and writes included');
  assert.equal(written['tokensOut'], 11);
  assert.equal(written['costUsd'], 0.0123, 'the money Claude reported, never re-estimated');
  assert.equal(written['outcome'], 'ok');
  assert.equal(written['seconds'], 3.4);
  assert.equal(written['usageNote'] ?? '', '');
});

test('a probe that did not finish is "usage not captured" — never a free 0/0', () => {
  // `capture` answers -1 for a spawn error, a timeout and an unreadable exit alike: the request may well have been
  // read and billed (the consultation's point), so its usage is UNKNOWN, not zero.
  const written = line(probeLine('opus', { code: -1, output: '' }, NOW - 25_000, NOW));

  assert.equal(written['usageNote'], 'usage not captured');
  assert.equal(written['costUsd'], null);
  assert.notEqual(written['outcome'], 'ok');
});

test('the recording run writes one line per candidate it asked, failures included, and hands the answer back unchanged', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-probe-ledger-'));
  const answers = [{ code: 0, output: ANSWERED }, { code: -1, output: '' }];
  let call = 0;
  const run = recordingRun(async () => answers[call++]!, probeUsagePath(dir), () => NOW);

  const first = await run(['--model', 'haiku', '-p', 'hi', '--output-format', 'json']);
  await run(['--model', 'opus', '-p', 'hi', '--output-format', 'json']);
  await flushLedgers();

  assert.deepEqual(first, answers[0], 'the probe reads the same answer it read before');
  const lines = parseUsage(fs.readFileSync(path.join(dir, PROBE_USAGE_FILE), 'utf8'));
  assert.deepEqual(lines.map((one) => [one.model, one.kind]), [['haiku', 'probe'], ['opus', 'probe']],
    'a billed probe request wrote nothing, or a failed one was left out');
  fs.rmSync(dir, { recursive: true, force: true });
});

test('the Rounds block reads the probe ledger beside usage.jsonl', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-probe-ledger-'));
  fs.writeFileSync(path.join(dir, PROBE_USAGE_FILE), probeLine('haiku', { code: 0, output: ANSWERED }, NOW - 1_000, NOW));
  const load = roundsLoader(() => '', path.join(dir, 'usage.jsonl'), async () => ({ code: 64, output: '' }), [probeUsagePath(dir)]);

  const loaded = await load(NOW + 60_000);

  assert.deepEqual(loaded.ledger.map((one) => one.kind), ['probe'], 'the probe ledger was not read beside the review ledger');
  assert.equal(loaded.ledgerFailure, '', 'a missing usage.jsonl is still nothing run yet');
  fs.rmSync(dir, { recursive: true, force: true });
});
