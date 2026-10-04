import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DEFAULT_SECURITY, compactPrompts, securityLaneFrom, securityLaneSave, type SecurityLane, type SecurityPrompt } from '../securityLane';
import { conditionsSummary, newPromptProblem, promptState, promptsInOrder, repointWrite, restoreSteps, securityCommandWrite } from '../securityLaneState';
import { DEFAULT_VENDORS } from '../vendors';
import { SECURITY_SEED } from '../securityLane.generated';

/** Every trigger-capable signal, from the catalogue itself — so a sixteenth signal cannot leave "all ticked" short of all. */
const EVERY_TRIGGER: readonly string[] = SECURITY_SEED.signals.filter(s => s.trigger).map(s => s.id);

// The model the Security lane tab draws its cards from (todo/PLAN_the_security_tab_reads_at_a_glance.md, epic 2):
// which state a card is in, the order cards come in, what a collapsed block says, and what every button writes.

const sql = (over: Partial<SecurityPrompt> = {}): SecurityPrompt => ({ id: 'redteam-sql', triggers: ['sql'], focus: ['sql', 'entry-point'], ...over });

test('a shipped prompt is shipped until its text or its conditions change, and a custom one is always custom', () => {
  assert.equal(promptState(sql(), 'none'), 'shipped');
  assert.equal(promptState(sql(), 'blank'), 'shipped', 'Edit creates an empty file; that changes nothing');
  assert.equal(promptState(sql(), 'written'), 'edited');
  assert.equal(promptState(sql({ triggers: ['sql', 'xss'] }), 'none'), 'edited');
  assert.equal(promptState(sql({ focus: ['entry-point', 'sql'] }), 'none'), 'shipped', 'a set in another order is the same set');
  // The cast models a member a future version may add: SecurityPrompt names only the three members this one knows.
  assert.equal(promptState({ ...sql(), futureDetector: 'v2' } as SecurityPrompt, 'none'), 'shipped', 'an unknown member is not an edit');
  assert.equal(promptState(sql(), 'placeholder'), 'shipped', 'no usable text is not an edit; the card names the problem instead');
  assert.equal(promptState(sql(), 'oversized'), 'shipped');
  assert.equal(promptState({ id: 'redteam-custom', triggers: [], focus: [] }, 'written'), 'custom');
});

test('general is edited by stored triggers or by its text, never by a stored focus', () => {
  const general = { id: 'redteam-general', triggers: [] as string[], focus: [] as string[] };
  assert.equal(promptState(general, 'none'), 'shipped', 'every hand-added general was stored with focus []');
  assert.equal(promptState({ ...general, triggers: ['sql'] }, 'none'), 'edited');
  assert.equal(promptState(general, 'written'), 'edited');
});

test('cards come general first, then the shipped presets in catalogue order, then custom prompts as stored', () => {
  const lane = securityLaneFrom({ prompts: [{ id: 'redteam-mine', triggers: [], focus: [] }, { id: 'redteam-sql' }, { id: 'redteam-also', triggers: [], focus: [] }] });
  const order = promptsInOrder(lane).map(p => p.id);
  assert.equal(order[0], 'redteam-general');
  assert.deepEqual(order.slice(1, 13), DEFAULT_SECURITY.prompts.slice(1).map(p => p.id));
  assert.deepEqual(order.slice(13), ['redteam-mine', 'redteam-also']);
});

test('the collapsed block names what a prompt reacts to in one line', () => {
  assert.equal(conditionsSummary(sql()), 'Runs on: SQL and raw queries; focus: SQL and raw queries · Entry points and middleware');
  const every = DEFAULT_SECURITY.prompts.find(p => p.id === 'redteam-authz')!;
  const all = { ...every, triggers: EVERY_TRIGGER };
  assert.match(conditionsSummary(all), new RegExp(`^Runs on: any security signal \\(${EVERY_TRIGGER.length}\\);`));
  assert.match(conditionsSummary(sql({ triggers: [] })), /^Runs on: nothing/);
  assert.match(conditionsSummary(sql({ triggers: ['future-detector'] })), /future-detector/, 'an unknown tag is named, not dropped');
  assert.equal(conditionsSummary({ id: 'redteam-general', triggers: [], focus: [] }), 'Runs on every code change');
});

test('storage keeps only what the person changed, and the lane the server receives does not move', () => {
  const edited = securityLaneFrom({ prompts: [{ id: 'redteam-sql', triggers: ['sql', 'xss'] }, { id: 'redteam-mine', triggers: [], focus: [] },
    { id: 'redteam-authz', triggers: ['authz'], focus: ['sql', 'entry-point', 'authz'] }, { id: 'redteam-xss', triggers: ['xss'], focus: ['xss', 'entry-point'], future: 1 }] });
  const compact = compactPrompts(edited);
  assert.deepEqual(compact.prompts.map(p => p.id).sort(), ['redteam-mine', 'redteam-sql', 'redteam-xss'],
    'an untouched preset (authz, set in another order) is dropped; an edited one, a custom one and one with an unknown member stay');
  // Conditions are sets to the server (it ranks by a set of focus tags and matches any trigger), so the comparison
  // is of sets: compaction puts an untouched preset back in its shipped order, which means the same thing.
  const asSets = (lane: SecurityLane) => [...lane.prompts].sort((a, b) => a.id.localeCompare(b.id))
    .map(p => ({ ...p, triggers: [...p.triggers].sort(), focus: [...p.focus].sort() }));
  assert.deepEqual(asSets(securityLaneFrom(JSON.parse(JSON.stringify(compact)))), asSets(edited));
});

test('a legacy general stored with focus [] is compacted away, and the server still sees it with no conditions', () => {
  const legacy = securityLaneFrom({ prompts: [{ id: 'redteam-general', triggers: [], focus: [] }] });
  const compact = compactPrompts(legacy);
  assert.equal(compact.prompts.some(p => p.id === 'redteam-general'), false);
  assert.deepEqual(securityLaneFrom(JSON.parse(JSON.stringify(compact))).prompts.find(p => p.id === 'redteam-general')?.triggers, []);
});

test('every save stores the compacted lane, so a full snapshot from the previous extension shrinks on its next save', () => {
  const snapshot = JSON.parse(JSON.stringify(securityLaneFrom({ enabled: true })));
  assert.equal(snapshot.prompts.length, 13, 'the previous extension wrote every preset back');
  const saved = securityLaneSave(snapshot, 'threshold', 3, DEFAULT_VENDORS)!;
  assert.equal(saved.threshold, 3);
  assert.deepEqual(saved.prompts, []);
});

test('a new custom prompt name is checked before anything is written', () => {
  const lane = DEFAULT_SECURITY;
  assert.equal(newPromptProblem(lane, 'redteam-mine'), '');
  assert.match(newPromptProblem(lane, 'mine'), /redteam-/);
  assert.match(newPromptProblem(lane, 'redteam-Mine'), /lower-case/);
  assert.match(newPromptProblem(lane, 'redteam-' + 'a'.repeat(73)), /80/);
  assert.match(newPromptProblem(lane, 'redteam-sql'), /already/);
  let full: SecurityLane = lane;
  for (let i = 0; i < 19; i += 1) full = { ...full, prompts: [...full.prompts, { id: `redteam-c${i}`, triggers: [], focus: [] }] };
  assert.equal(full.prompts.length, 32);
  assert.match(newPromptProblem(full, 'redteam-twentieth'), /32 prompts, 13 of them shipped/);
});

test('every button maps to one write, and Remove pair refuses a row that moved', () => {
  const lane: SecurityLane = { ...DEFAULT_SECURITY, runs: [{ vendor: 'codex', prompt: 'redteam-authz' }, { vendor: 'codex', prompt: 'redteam-sql' }] };
  assert.deepEqual(securityCommandWrite(lane, 'addSecurityRun', undefined), { field: 'addRun', value: true });
  assert.deepEqual(securityCommandWrite(lane, 'removeSecurityRun', '[1,"codex","redteam-sql"]'), { field: 'run:1:remove', value: true });
  assert.deepEqual(securityCommandWrite(lane, 'removeSecurityRun', '[1,"CODEX","redteam-sql"]'), { field: 'run:1:remove', value: true }, 'vendors compare as the server compares them');
  assert.ok('refusal' in securityCommandWrite(lane, 'removeSecurityRun', '[0,"codex","redteam-sql"]'), 'row 0 holds another pair');
  assert.ok('refusal' in securityCommandWrite(lane, 'removeSecurityRun', '[1,"codex","redteam-SQL"]'), 'prompt ids are exact');
  for (const bad of [undefined, 'x', '[1]', '[1.5,"codex","redteam-sql"]', '["1","codex","redteam-sql"]', '[-1,"codex","redteam-sql"]', '[5,"codex","redteam-sql"]']) {
    assert.ok('refusal' in securityCommandWrite(lane, 'removeSecurityRun', bad), String(bad));
  }
  assert.deepEqual(securityCommandWrite(lane, 'restoreSecurityPrompt', 'redteam-sql'), { field: 'prompt:redteam-sql:restore', value: true });
  assert.deepEqual(securityCommandWrite(lane, 'clearSecurityConditions', 'redteam-general'), { field: 'prompt:redteam-general:restore', value: true });
  assert.ok('refusal' in securityCommandWrite(lane, 'removeSecurityPrompt', 'redteam-mine'), 'this lane holds no redteam-mine');
  assert.ok('refusal' in securityCommandWrite(lane, 'removeSecurityPrompt', '../escape'));
  assert.ok('refusal' in securityCommandWrite(lane, 'somethingElse', 'redteam-sql'));
});

test('a prompt command is refused where it would mean nothing, so no caller can take it as done', () => {
  // A restore on a custom card would delete the person's own text after a no-op write (own review, epic 2).
  const lane = securityLaneFrom({ prompts: [{ id: 'redteam-mine', triggers: [], focus: [] }] });
  assert.ok('refusal' in securityCommandWrite(lane, 'restoreSecurityPrompt', 'redteam-mine'), 'a custom prompt has nothing shipped to restore');
  assert.ok('refusal' in securityCommandWrite(lane, 'clearSecurityConditions', 'redteam-sql'), 'only an always prompt has leftover conditions to clear');
  assert.ok('refusal' in securityCommandWrite(lane, 'removeSecurityPrompt', 'redteam-sql'), 'a shipped prompt cannot be removed');
  assert.ok('refusal' in securityCommandWrite(lane, 'removeSecurityPrompt', 'redteam-unknown'), 'a prompt the lane does not hold');
  assert.deepEqual(securityCommandWrite(lane, 'removeSecurityPrompt', 'redteam-mine'), { field: 'prompt:redteam-mine:remove', value: true });
});

test('a shipped id stored twice is never compacted, so the server keeps reading the entry it read before', () => {
  // The server keeps the first entry of an id and complains about the second; dropping the untouched first would
  // silently promote the second (own review, epic 2).
  const twice = securityLaneFrom({ prompts: [{ id: 'redteam-sql' }, { id: 'redteam-sql', triggers: ['xss'] }] });
  assert.deepEqual(compactPrompts(twice).prompts.filter(p => p.id === 'redteam-sql').map(p => p.triggers), [['sql'], ['xss']]);
});

test('Restore default asks whenever the file holds anything, refuses over unsaved edits, and is safe to press again', () => {
  // An oversized or unreadable override is still the person's text: it is deleted only after they said yes (E3 plan round).
  for (const text of ['written', 'placeholder', 'oversized', 'unreadable'] as const) assert.equal(restoreSteps(text, false, false).confirm, true, text);
  assert.deepEqual(restoreSteps('blank', true, false), { refusal: '', confirm: false, writeConditions: true, deleteFile: true });
  assert.deepEqual(restoreSteps('none', false, false), { refusal: '', confirm: false, writeConditions: false, deleteFile: false },
    'a second press after a full restore does nothing');
  assert.deepEqual(restoreSteps('written', false, false), { refusal: '', confirm: true, writeConditions: false, deleteFile: true },
    'the retry after a failed delete deletes only — the conditions are already back');
  const refused = restoreSteps('written', true, true);
  assert.match(refused.refusal, /unsaved changes/);
  assert.equal(refused.deleteFile || refused.writeConditions || refused.confirm, false);
});

test('a new prompt is put on the pair that asked only while that pair is still where it was', () => {
  const lane: SecurityLane = { ...DEFAULT_SECURITY, runs: [{ vendor: 'codex', prompt: 'redteam-authz' }] };
  assert.deepEqual(repointWrite(lane, '[0,"codex","redteam-authz"]', 'redteam-mine'), { field: 'run:0:prompt', value: 'redteam-mine' });
  assert.deepEqual(repointWrite(lane, '[0,"codex","redteam-sql"]', 'redteam-mine'), { refusal: 'the pair changed while you typed' });
  assert.ok('refusal' in repointWrite(lane, undefined, 'redteam-mine'));
});

test('a custom prompt with no conditions is summarised as running on every change, not as one that never runs', () => {
  // Seen in the epic-4 screenshot: the summary told a person their own prompt would never run, while the server runs an
  // empty-trigger custom prompt on every change (SecuritySignals.Triggered).
  assert.equal(conditionsSummary({ id: 'redteam-mine', triggers: [], focus: [] }), 'Runs on every change: a custom prompt with no condition always runs; focus: none');
  assert.match(conditionsSummary(sql({ triggers: [] })), /^Runs on: nothing — a shipped preset with no condition does not run/);
});
