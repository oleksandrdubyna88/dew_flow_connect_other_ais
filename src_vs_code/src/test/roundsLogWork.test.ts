import assert from 'node:assert/strict';
import { test } from 'node:test';

import { type LogCommand, type RoundsLogHooks, workFor } from '../roundsLogMessages';

/**
 * What each rounds-log command makes the host do — the work its busy mark is held for
 * (research/PLAN_busy_marks_on_every_webview.md, E1).
 *
 * <p>Extracted from `RoundsLogPanel.received`, which imports `vscode` and cannot be built here, so that the mapping a
 * busy mark depends on is RUN: a command whose work is not returned would settle at once and show no bar, and one
 * mapped to the wrong hook would mark the wrong work.</p>
 */

/** Hooks that write down which was called, with what, and finish only when the test says so. */
function recording(): { readonly hooks: RoundsLogHooks; readonly called: string[] } {
  const called: string[] = [];
  const note = (what: string) => (): Promise<void> => { called.push(what); return Promise.resolve(); };

  return {
    called,
    hooks: {
      onAnswer: (id) => note(`answer ${id}`)(),
      onUsageWindow: (window) => note(`usageWindow ${window}`)(),
      onSpotsPeriod: (period) => note(`spotsPeriod ${period}`)(),
      onForget: (provider) => note(`forget ${provider}`)(),
      onCloseConsultation: (id) => note(`closeConsultation ${id}`)(),
      onForgetChat: (provider, model) => note(`forgetChat ${provider} ${model}`)(),
      onExport: (rows) => note(`export ${rows.length}`)(),
      onFindings: (key, round) => note(`findings ${key} ${round.sessionId}/${round.stage}/${round.number}`)(),
      onTeamWindow: (id) => note(`teamWindow ${id}`)(),
      onTeamRefresh: (server) => note(`teamRefresh ${server}`)(),
      onTeamServer: (server) => note(`teamServer ${server}`)(),
      onTeamTab: (shown) => note(`teamTab ${String(shown)}`)(),
    },
  };
}

test('every command that asks the host for work maps to its own hook, and the work is what is awaited', async () => {
  const { hooks, called } = recording();
  const commands: readonly LogCommand[] = [
    { kind: 'answer', id: 'q1' },
    { kind: 'usageWindow', window: 'week' },
    { kind: 'spotsPeriod', period: 'month' },
    { kind: 'forget', provider: 'codex' },
    { kind: 'closeConsultation', id: 'c1' },
    { kind: 'forgetChat', provider: 'codex', model: 'gpt-6-astra' },
    { kind: 'export', rows: [] },
    { kind: 'findings', key: 'k1', sessionId: 's1', stage: 'CodeReview', number: 2 },
    { kind: 'teamWindow', id: 'acme|year' },
    { kind: 'teamRefresh', server: 'acme' },
    { kind: 'teamServer', server: 'acme' },
    { kind: 'teamTab', shown: true },
  ];

  for (const command of commands) {
    const work = workFor(command, hooks);
    assert.ok(work !== undefined, `${command.kind} has work to wait for`);
    await work();
  }

  assert.deepEqual(called, [
    'answer q1', 'usageWindow week', 'spotsPeriod month', 'forget codex', 'closeConsultation c1',
    'forgetChat codex gpt-6-astra', 'export 0', 'findings k1 s1/CodeReview/2',
    'teamWindow acme|year', 'teamRefresh acme', 'teamServer acme', 'teamTab true',
  ]);
});

test('ready and an ignored message ask the host for no work, so nothing is marked for them', () => {
  const { hooks, called } = recording();

  assert.equal(workFor({ kind: 'ready' }, hooks), undefined);
  assert.equal(workFor({ kind: 'ignore' }, hooks), undefined);
  assert.deepEqual(called, []);
});
