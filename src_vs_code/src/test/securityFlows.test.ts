import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DEFAULT_SECURITY, securityLaneFrom, securityLaneSave, type SecurityLane } from '../securityLane';
import { runSecurityCommand, type SecurityFlowHost } from '../securityFlows';
import type { SecurityTextState } from '../securityPromptFiles';
import { DEFAULT_VENDORS } from '../vendors';

// The Security lane tab's host flows with every effect faked (research/PLAN_the_security_tab_reads_at_a_glance.md, epic 3):
// these are the paths that delete a person's text, so the order and the refusals are pinned here, not trusted.

interface World {
  stored: unknown;
  text: Record<string, SecurityTextState>;
  dirty: Set<string>;
  /** Every effect, in the order it happened. */
  readonly log: string[];
  /** What the person answers: a name for the input box, true/false for a modal. */
  name?: string;
  confirm: boolean;
  /** A write the setting does not take — a workspace value shadowing it. */
  shadowed: boolean;
  /** A delete that fails. */
  removeFails: boolean;
  /** Run between the press and the queued step, as another window or a save in the editor would. */
  meanwhile?: () => void;
  readonly queued: (() => Promise<void>)[];
}

function world(lane: unknown, over: Partial<World> = {}): World {
  return { stored: lane, text: {}, dirty: new Set(), log: [], confirm: true, shadowed: false, removeFails: false, queued: [], ...over };
}

function hostOf(w: World): SecurityFlowHost {
  const lane = (): SecurityLane => securityLaneFrom(w.stored);
  return {
    lane,
    enqueue: (work) => { w.log.push('enqueue'); w.queued.push(work); },
    write: async (field, value) => {
      w.log.push(`write ${field}`);
      const next = securityLaneSave(w.stored, field, value, DEFAULT_VENDORS);
      if (next !== undefined && !w.shadowed) w.stored = JSON.parse(JSON.stringify(next));
    },
    promptsDir: '/data/prompts',
    fileOf: (id) => `/data/prompts/${id}.md`,
    textState: async (id) => w.text[id] ?? 'none',
    unsaved: (file) => w.dirty.has(file),
    remove: async (file) => {
      w.log.push(`remove ${file}`);
      if (w.removeFails) throw new Error('EBUSY: resource busy or locked');
    },
    askName: async (problem) => { w.log.push('ask name'); assert.equal(problem('redteam-ok'), ''); return w.name; },
    confirm: async (question) => { w.log.push(`confirm ${question}`); return w.confirm; },
    tell: (message) => { w.log.push(`tell ${message}`); },
    open: async (id) => { w.log.push(`open ${id}`); },
  };
}

/** Press a button, then let the queue run — after whatever happens in between. */
async function press(w: World, command: string, id?: string): Promise<void> {
  await runSecurityCommand(command, id, hostOf(w));
  w.meanwhile?.();
  while (w.queued.length > 0) await w.queued.shift()!();
}

const EDITED_SQL = { prompts: [{ id: 'redteam-sql', triggers: ['xss'], focus: ['sql'] }] };

test('Restore default asks over a file with text, writes the conditions first, then deletes the file', async () => {
  const w = world(EDITED_SQL, { text: { 'redteam-sql': 'written' } });
  await press(w, 'restoreSecurityPrompt', 'redteam-sql');
  const order = w.log.filter(line => !line.startsWith('enqueue'));
  assert.match(order[0]!, /^confirm Restore redteam-sql/);
  assert.deepEqual(order.slice(1), ['write prompt:redteam-sql:restore', 'remove /data/prompts/redteam-sql.md']);
  assert.deepEqual(securityLaneFrom(w.stored).prompts.find(p => p.id === 'redteam-sql')?.triggers, ['sql']);
});

test('Restore default asks before deleting an oversized or unreadable file too, and does nothing when told no', async () => {
  for (const text of ['oversized', 'unreadable', 'placeholder'] as const) {
    const w = world(DEFAULT_SECURITY, { text: { 'redteam-sql': text }, confirm: false });
    await press(w, 'restoreSecurityPrompt', 'redteam-sql');
    assert.equal(w.log.filter(line => line.startsWith('confirm')).length, 1, text);
    assert.equal(w.log.some(line => line.startsWith('remove') || line.startsWith('write')), false, `${text}: acted after "no"`);
  }
});

test('Restore default refuses over unsaved edits in an editor, deleting nothing', async () => {
  const w = world(EDITED_SQL, { text: { 'redteam-sql': 'written' }, dirty: new Set(['/data/prompts/redteam-sql.md']) });
  await press(w, 'restoreSecurityPrompt', 'redteam-sql');
  assert.ok(w.log.some(line => /unsaved changes/.test(line)));
  assert.equal(w.log.some(line => line.startsWith('remove') || line.startsWith('confirm')), false);
});

test('a file that gained text after the press is not deleted unasked, and one opened dirty meanwhile is kept too', async () => {
  // Pressed over an empty file (no question needed), then text was saved into it before the queued step ran.
  const gained = world(DEFAULT_SECURITY, { text: { 'redteam-sql': 'blank' } });
  gained.meanwhile = () => { gained.text['redteam-sql'] = 'written'; };
  await press(gained, 'restoreSecurityPrompt', 'redteam-sql');
  assert.equal(gained.log.some(line => line.startsWith('remove')), false);
  assert.ok(gained.log.some(line => /changed after you pressed Restore default/.test(line)));

  const dirty = world(EDITED_SQL, { text: { 'redteam-sql': 'written' } });
  dirty.meanwhile = () => { dirty.dirty.add('/data/prompts/redteam-sql.md'); };
  await press(dirty, 'restoreSecurityPrompt', 'redteam-sql');
  assert.equal(dirty.log.some(line => line.startsWith('remove')), false);
});

test('when the conditions write does not take, the file is kept and the person is told why', async () => {
  const w = world(EDITED_SQL, { text: { 'redteam-sql': 'written' }, shadowed: true });
  await press(w, 'restoreSecurityPrompt', 'redteam-sql');
  assert.equal(w.log.some(line => line.startsWith('remove')), false, 'the text was deleted while the restore did not land');
  assert.ok(w.log.some(line => /could not be restored — a workspace setting may override coai\.securityLane; .* was kept/.test(line)));
});

test('a delete that fails is reported, and pressing again is the retry: it deletes only', async () => {
  const w = world(EDITED_SQL, { text: { 'redteam-sql': 'written' }, removeFails: true });
  await press(w, 'restoreSecurityPrompt', 'redteam-sql');
  assert.ok(w.log.some(line => /could not be deleted \(EBUSY.*Press Restore default again to retry/.test(line)));
  w.removeFails = false;
  w.log.length = 0;
  await press(w, 'restoreSecurityPrompt', 'redteam-sql');
  assert.ok(w.log.includes('remove /data/prompts/redteam-sql.md'));
});

test('Remove custom prompt asks, names the pairs that go and the file that stays, and never deletes the file', async () => {
  const w = world({ prompts: [{ id: 'redteam-mine', triggers: [], focus: [] }], runs: [{ vendor: 'codex', prompt: 'redteam-mine' }] });
  await press(w, 'removeSecurityPrompt', 'redteam-mine');
  assert.ok(w.log.some(line => line === 'confirm Remove redteam-mine and its 1 pair? Its text stays in /data/prompts/redteam-mine.md.'));
  assert.equal(w.log.some(line => line.startsWith('remove')), false);
  assert.equal(securityLaneFrom(w.stored).prompts.some(p => p.id === 'redteam-mine'), false);
  assert.equal(securityLaneFrom(w.stored).runs.length, 0);
});

test('+ New custom prompt asks outside the queue, re-checks inside it, adds, and opens the file', async () => {
  const w = world(DEFAULT_SECURITY, { name: 'redteam-mine' });
  await press(w, 'newSecurityPrompt', '');
  assert.deepEqual(w.log, ['ask name', 'enqueue', 'write addPrompt', 'open redteam-mine']);
});

test('a new prompt whose name was taken meanwhile is not added twice, and a shadowed add opens no editor', async () => {
  const taken = world(DEFAULT_SECURITY, { name: 'redteam-mine' });
  taken.meanwhile = () => { taken.stored = { prompts: [{ id: 'redteam-mine', triggers: [], focus: [] }] }; };
  await press(taken, 'newSecurityPrompt', '');
  assert.equal(taken.log.includes('write addPrompt'), false);
  assert.ok(taken.log.some(line => /redteam-mine is already a prompt/.test(line)));

  const shadowed = world(DEFAULT_SECURITY, { name: 'redteam-mine', shadowed: true });
  await press(shadowed, 'newSecurityPrompt', '');
  assert.equal(shadowed.log.some(line => line.startsWith('open')), false);
  assert.ok(shadowed.log.some(line => /was not added/.test(line)));
});

test('a new prompt asked for by a pair is put on that pair, and a pair that moved is said, not guessed', async () => {
  const lane = { runs: [{ vendor: 'codex', prompt: 'redteam-authz' }] };
  const w = world(lane, { name: 'redteam-mine' });
  await press(w, 'newSecurityPrompt', JSON.stringify([0, 'codex', 'redteam-authz']));
  assert.equal(securityLaneFrom(w.stored).runs[0]?.prompt, 'redteam-mine');

  const moved = world(lane, { name: 'redteam-mine' });
  moved.meanwhile = () => { moved.stored = { runs: [{ vendor: 'codex', prompt: 'redteam-sql' }] }; };
  await press(moved, 'newSecurityPrompt', JSON.stringify([0, 'codex', 'redteam-authz']));
  assert.equal(securityLaneFrom(moved.stored).runs[0]?.prompt, 'redteam-sql');
  assert.ok(moved.log.some(line => /the pair changed while you typed — pair it from its card/.test(line)));
  assert.ok(moved.log.includes('open redteam-mine'), 'the prompt still exists and is opened');
});

test('a cancelled name box does nothing at all', async () => {
  const w = world(DEFAULT_SECURITY);
  await press(w, 'newSecurityPrompt', '');
  assert.deepEqual(w.log, ['ask name']);
});
