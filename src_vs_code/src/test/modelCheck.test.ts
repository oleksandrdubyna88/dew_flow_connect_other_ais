import assert from 'node:assert/strict';
import test from 'node:test';
import { checkArgs, type CheckRunPorts, isModelCheck, runConsultantCheck } from '../consultantCheckRun';
import { parseCheckDocument } from '../consultantHealth';
import { ConsultantHealthHost, confirmationOf, type HealthHostPorts } from '../consultantHealthHost';
import type { ConsultantHealthState } from '../consultantHealthState';
import { consultantTabShowing } from '../consultantHealthWatcher';
import { textCopier } from '../copyText';
import { checkInputOf } from '../modelCheckInput';
import { checkFactsOf, cliBadge, contractNote, offMachineNote } from '../modelCardWorld';
import { capture } from '../versionProbe';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';

/**
 * The Models card's ✓ Check and its world-facing parts (research/PLAN_one_model_catalog.md E3.3, D10): one paid turn of a
 * catalog row through `coai-mcp --check-model`, the row on stdin, asked first, its state read back from the durable
 * record `model-<id>` — and the CLI, endpoint and Team server lines each card shows.
 */

const row = (extra: Partial<Vendor> = {}): Vendor => ({ ...DEFAULT_VENDORS[0]!, ...extra });

test('a model\'s check runs --check-model; a caller kind\'s keeps --check-consultant --caller', () => {
  assert.deepEqual(checkArgs('model-codex'), ['--check-model']);
  assert.deepEqual(checkArgs('claude'), ['--check-consultant', '--caller', 'claude']);
  assert.equal(isModelCheck('model-'), false, 'a key with no row is no model check');
});

test('the row reaches the run as stdin — never argv', async () => {
  const asked: { args: readonly string[]; input: string }[] = [];
  const ports: CheckRunPorts = {
    run: (args, _cap, _stop, input) => {
      asked.push({ args, input });
      return Promise.resolve({ code: 0, output: JSON.stringify({ callerKind: 'model-codex', state: 'answered' }) });
    },
    readState: () => Promise.resolve(undefined),
    nowMs: () => 0,
    every: () => () => undefined,
  };

  await runConsultantCheck('model-codex', ports, '{"row":{"id":"codex"}}');

  assert.deepEqual(asked, [{ args: ['--check-model'], input: '{"row":{"id":"codex"}}' }]);
});

test('a child is handed what it is given on stdin', async () => {
  const echoed = await capture(process.execPath, ['-e', 'process.stdin.pipe(process.stdout)'], false, 20_000, undefined, undefined, '{"row":{}}');

  assert.equal(echoed.output.trim(), '{"row":{}}');
});

test('the row on stdin is the row a round would run — through the settings file\'s own wire, switched on', () => {
  const input = JSON.parse(checkInputOf(row({ enabled: false, model: 'gpt-6' }), '0.44.0', [])) as { row: Record<string, unknown> };

  assert.equal(input.row['id'], 'codex');
  assert.equal(input.row['model'], 'gpt-6');
  // The wire carries only switched-on rows and leaves `enabled` out (coai-mcp's default is on): a switched-off row
  // reaching stdin at all is the proof it is checked as it would run once switched on.
  assert.equal(input.row['enabled'], undefined);
});

function hostKit(): { host: ConsultantHealthHost; confirms: string[]; runs: string[] } {
  const confirms: string[] = [];
  const runs: string[] = [];
  const ports: HealthHostPorts = {
    confirm: (title) => { confirms.push(title); return Promise.resolve(true); },
    runCheck: (kind) => { runs.push(kind); return new Promise(() => undefined); },
    settled: () => undefined,
    announce: () => undefined,
    render: () => undefined,
    nowUtc: () => '2026-10-05T10:00:00.000Z',
    copier: textCopier({ writeText: () => Promise.resolve(), say: () => ({ dispose: () => undefined }) }),
  };

  return { host: new ConsultantHealthHost(ports), confirms, runs };
}

test('a model\'s check is asked first as one paid turn of THAT row, then run once', async () => {
  const kit = hostKit();

  await kit.host.check('model-qwen', { vendor: 'qwen', model: 'glm-5.3' });
  await kit.host.check('model-qwen', { vendor: 'qwen', model: 'glm-5.3' });

  assert.deepEqual(kit.confirms, ['Check qwen with one real, paid turn of qwen · glm-5.3?']);
  assert.deepEqual(kit.runs, ['model-qwen'], 'a second press while it runs starts nothing');
  assert.match(confirmationOf('model-qwen', { vendor: 'qwen', model: 'glm-5.3' }).go, /paid/u);
});

function health(over: Partial<ConsultantHealthState['thisSide']> = {}): ConsultantHealthState {
  return {
    thisSide: { label: 'Windows', probe: { kind: 'never' } as never, files: undefined, checking: [], runs: {}, ...over },
    otherSides: [],
    nowMs: Date.parse('2026-10-05T10:00:00.000Z'),
  };
}

test('the card reads the check from its durable record — and says "checking…" while this window runs one', () => {
  const answered = parseCheckDocument(JSON.stringify({ callerKind: 'model-codex', state: 'answered', finishedUtc: '2026-10-05T09:59:00.000Z' }))!;
  const files = { report: undefined, checks: { 'model-codex': { kind: 'found' as const, value: answered } } };

  assert.equal(checkFactsOf(health({ files }), 'codex').said, 'checked: it answered');
  assert.equal(checkFactsOf(health({ files, checking: ['model-codex'] }), 'codex').said, 'checking…');
  assert.equal(checkFactsOf(health({ files: { report: undefined, checks: {} } }), 'codex').said, 'not checked yet');
  assert.equal(checkFactsOf(undefined, 'codex').said, '', 'no badge while the health is not being read');
});

test('the Models tab is where the health is read too, beside the Consultant tab', () => {
  assert.equal(consultantTabShowing(true, 'models'), true);
  assert.equal(consultantTabShowing(true, 'consultants/consultant'), true);
  assert.equal(consultantTabShowing(true, 'reviews/gate'), false);
});

test('a CLI row shows its version and a newer one; a local row with an endpoint off this machine says where it goes', () => {
  assert.match(cliBadge(row(), { installed: '0.156.1', latest: '0.157.0' }), /codex 0\.156\.1 · 0\.157\.0 available/);
  assert.equal(cliBadge(row({ runtime: 'api' }), { installed: '', latest: '' }), '', 'no CLI, no badge');
  assert.match(offMachineNote(row({ runtime: 'local', baseUrl: 'http://gpu-box.lan:11434/v1' })), /gpu-box\.lan/);
  assert.equal(offMachineNote(row({ runtime: 'local', baseUrl: 'http://127.0.0.1:11434/v1' })), '');
});

test('a remote row on a contract-1 Team server is told that effort and its system prompt are not applied', () => {
  const remote = row({ id: 'srv-claude', runtime: 'remote', teamServerId: 's1' });
  const server = (contract: number | undefined) => [{ server: { id: 's1' }, contract } as never];

  assert.match(contractNote(remote, server(1)), /contract 1.*neither this row's effort nor its system prompt/);
  assert.equal(contractNote(remote, server(2)), '');
  assert.equal(contractNote(remote, server(undefined)), '', 'a server not heard from is not called old');
});
