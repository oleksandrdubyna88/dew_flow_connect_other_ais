import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import path from 'node:path';
import { isWellFormedAuthorsState, modelShown, writtenWith } from '../authorsLine';

/**
 * The round card's "written with" line (E5.S6 of `research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E5.md`): what the
 * session file's `authors` snapshot and `size` say, as one line — or nothing for a round that recorded neither.
 *
 * <p>The snapshot is JSON off a disk five processes write, so every parameter is `unknown` and no test uses a type assertion: a
 * snapshot is built as a plain object and handed over as it would arrive. The state rule and the model display are held to the
 * SAME vectors the server's own tests read (`shared/authors-state-vectors.json`, `shared/model-display-vectors.json`).</p>
 */

const SHARED = path.join(__dirname, '..', '..', '..', 'shared');

/** A vectors file section, every row checked to be an object — a malformed file fails naming the row. */
function vectors(file: string, section: string): readonly Record<string, unknown>[] {
  const parsed: unknown = JSON.parse(fs.readFileSync(path.join(SHARED, file), 'utf8'));
  assert.ok(typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed), `${file} is a JSON object`);
  const rows = Reflect.get(parsed, section);
  assert.ok(Array.isArray(rows), `${file} carries no \`${section}\` array`);

  return rows.map((row: unknown, at: number) => {
    assert.ok(typeof row === 'object' && row !== null && !Array.isArray(row), `${file} ${section}[${at}] is a vector row — an object`);

    return Object.fromEntries(Object.entries(row));
  });
}

const tokens = { input: 1, output: 1, cacheCreation: 0, cacheRead: 0 };

const row = (agent: string, model: string, over: Record<string, unknown> = {}): Record<string, unknown> => ({
  window: 'round', stage: 'CodeReview', number: 1,
  who: agent === 'main' ? 'main' : 'subagent', agent: { id: agent }, agentType: agent === 'main' ? '' : 'general-purpose',
  requested: 'opus', model, effort: 'high', association: agent === 'main' ? 'same-session' : 'wrote here',
  launches: 1, turns: 2, tokens, writeCalls: 1, ...over,
});

const snapshot = (rows: unknown, over: Record<string, unknown> = {}): Record<string, unknown> => ({
  stage: 'CodeReview', number: 1, session: '11111111-1111-1111-1111-111111111111', state: 'observed', reasons: [], rows,
  ambiguous: {}, elsewhere: {}, claimedByPeer: {}, unobservedLaunches: 0, windows: [], peerGates: [],
  idSource: 'registry', reader: 1, attribution: 1, submoduleWrites: 0, uncoveredWrites: 0, ...over,
});

const cell = (coverage: string, kind: string, files: number, added?: number, removed?: number): Record<string, unknown> =>
  ({ coverage, kind, files, ...(added === undefined ? {} : { added }), ...(removed === undefined ? {} : { removed }) });

const scope = (state: string, cells: readonly unknown[]): Record<string, unknown> => ({ state, from: 'abc123', cells });

const size = (whole: unknown, delta: unknown): Record<string, unknown> => ({ whole, delta, origin: 'live' });

const opusMain = row('main', 'claude-opus-5-5');
const sonnet = (id: string, over: Record<string, unknown> = {}): Record<string, unknown> => row(id, 'claude-sonnet-5-5', over);
const opus = (id: string): Record<string, unknown> => row(id, 'claude-opus-5-5');

const wholeOf412 = scope('recorded', [cell('shown', 'code', 5, 400, 80), cell('elided', 'code', 4, 12, 8), cell('excluded', 'config', 9, 900, 900)]);

test('an observed round says its main model, its coders and its size', () => {
  const line = writtenWith(snapshot([opusMain, sonnet('a1'), sonnet('a2'), sonnet('a3')]), size(wholeOf412, scope('unavailable: first round', [])));

  assert.equal(line, 'written with Opus 5.5 (main) · 3 Sonnet coders · +412 −88 in 9 files');
});

test('a model switch names both main models; a model seen twice is named once', () => {
  const switched = snapshot([opusMain, row('main', 'claude-sonnet-5-5'), row('main', 'claude-opus-5-5', { window: 'before-open' })]);

  assert.equal(writtenWith(switched, undefined), 'written with Opus 5.5 + Sonnet 5.5 (main) · main only');
});

test('the noun is coder for a total of one and coders for any other', () => {
  assert.equal(writtenWith(snapshot([opusMain, sonnet('a1')]), undefined), 'written with Opus 5.5 (main) · 1 Sonnet coder');
  assert.equal(writtenWith(snapshot([opusMain, sonnet('a1'), opus('a2')]), undefined), 'written with Opus 5.5 (main) · 1 Opus + 1 Sonnet coders', 'a tie in size: ordered by family NAME, so Opus before Sonnet');
});

test('two families: the larger group first, a tie by family name; three families are mixed', () => {
  assert.equal(writtenWith(snapshot([opusMain, sonnet('a1'), sonnet('a2'), opus('a3')]), undefined), 'written with Opus 5.5 (main) · 2 Sonnet + 1 Opus coders');
  assert.equal(writtenWith(snapshot([opusMain, sonnet('a1'), opus('a2'), row('a3', 'claude-haiku-4-5-20251001')]), undefined), 'written with Opus 5.5 (main) · 3 coders (mixed)');
});

test('an other-family and an unknown-model coder are groups of their own, as the report counts them', () => {
  assert.equal(writtenWith(snapshot([opusMain, row('a1', 'gpt-6-luna'), row('a2', 'gpt-6-luna')]), undefined), 'written with Opus 5.5 (main) · 2 other coders');
  assert.equal(writtenWith(snapshot([opusMain, sonnet('a1'), row('a2', 'unknown')]), undefined), 'written with Opus 5.5 (main) · 1 Sonnet + 1 unknown coders');
});

test('only wrote-here subagents are coders; a child-of agent and a same-session main are not', () => {
  const rows = [opusMain, sonnet('a1', { association: 'child of' }), sonnet('a2', { association: 'same-session' }), sonnet('a3')];

  assert.equal(writtenWith(snapshot(rows), undefined), 'written with Opus 5.5 (main) · 1 Sonnet coder');
  assert.equal(writtenWith(snapshot([opusMain, sonnet('a1', { association: 'child of' })]), undefined), 'written with Opus 5.5 (main) · main only');
});

test('a coder that straddles both windows is counted once; a before-open coder counts', () => {
  const rows = [opusMain, sonnet('a1'), sonnet('a1', { window: 'before-open' }), sonnet('a2', { window: 'before-open' })];

  assert.equal(writtenWith(snapshot(rows), undefined), 'written with Opus 5.5 (main) · 2 Sonnet coders');
});

test('a round with no main row does not invent a main model', () => {
  assert.equal(writtenWith(snapshot([sonnet('a1')]), undefined), 'written with main model unknown · 1 Sonnet coder');
});

test('the delta size is used when it is recorded, the whole size when only that is, and no size otherwise', () => {
  const delta = scope('recorded', [cell('shown', 'code', 2, 30, 4)]);

  assert.equal(writtenWith(snapshot([opusMain]), size(wholeOf412, delta)), 'written with Opus 5.5 (main) · main only · +30 −4 in 2 files');
  assert.equal(writtenWith(snapshot([opusMain]), size(wholeOf412, scope('unavailable: first round', []))), 'written with Opus 5.5 (main) · main only · +412 −88 in 9 files');
  assert.equal(writtenWith(snapshot([opusMain]), size(scope('unavailable: git failed', []), scope('unavailable: first round', []))), 'written with Opus 5.5 (main) · main only');
  assert.equal(writtenWith(snapshot([opusMain]), undefined), 'written with Opus 5.5 (main) · main only');
});

test('a measured zero is a size; a binary has files and no lines; one file is a file', () => {
  assert.equal(writtenWith(snapshot([opusMain]), size(scope('recorded', []), scope('recorded', []))), 'written with Opus 5.5 (main) · main only · +0 −0 in 0 files');
  assert.equal(writtenWith(snapshot([opusMain]), size(scope('recorded', [cell('binary', 'unknown', 2)]), scope('unavailable: first round', []))), 'written with Opus 5.5 (main) · main only · in 2 files');
  assert.equal(writtenWith(snapshot([opusMain]), size(scope('recorded', [cell('shown', 'code', 1, 3, 0), cell('binary', 'unknown', 1)]), scope('unavailable: first round', []))), 'written with Opus 5.5 (main) · main only · +3 −0 in 2 files', 'lines of the shown cell only — a binary adds a file, never a line');
});

test('a partial round says why, a provisional one says its peer', () => {
  assert.equal(writtenWith(snapshot([opusMain], { state: 'partial', reasons: ['read budget'] }), undefined), 'written with Opus 5.5 (main) · main only · partial — read budget');
  assert.equal(writtenWith(snapshot([opusMain], { state: 'partial', reasons: ['read budget', 'peer running'] }), undefined), 'written with Opus 5.5 (main) · main only · partial — read budget, peer running');
});

test('writes into another checkout say the size does not cover them; unknown says nothing', () => {
  assert.equal(writtenWith(snapshot([opusMain], { uncoveredWrites: 2 }), undefined), 'written with Opus 5.5 (main) · main only · size not covered');
  assert.equal(writtenWith(snapshot([opusMain], { uncoveredWrites: -1, state: 'partial', reasons: ['read budget'] }), undefined), 'written with Opus 5.5 (main) · main only · partial — read budget');
  assert.equal(writtenWith(snapshot([opusMain], { uncoveredWrites: 0 }), undefined), 'written with Opus 5.5 (main) · main only');
});

test('an unobserved round says why and nothing about size or coders', () => {
  const unobserved = snapshot([], { state: 'unobserved', reasons: ['transcript not found'], uncoveredWrites: -1 });

  assert.equal(writtenWith(unobserved, size(wholeOf412, scope('recorded', []))), 'authors not observed — transcript not found');
  assert.equal(writtenWith(snapshot([], { state: 'unobserved', reasons: ['invalid input', 'reader failed'] }), undefined), 'authors not observed — invalid input, reader failed');
});

test('a round with no snapshot says nothing at all', () => {
  assert.equal(writtenWith(undefined, size(wholeOf412, scope('recorded', []))), '');
  assert.equal(writtenWith(null, undefined), '');
  assert.equal(writtenWith('observed', undefined), '');
  assert.equal(writtenWith([], undefined), '');
});

test('a state the rule refuses is not recorded, whatever else the snapshot holds', () => {
  for (const [state, reasons] of [
    ['partial', []],
    ['observed', ['x']],
    ['done', []],
    ['partial', ['read budget ×2']],
    ['partial', ['read budget', 'read budget']],
    // A state that already carries its reasons is the database's rendered column, not the snapshot's kind.
    ['unobserved: transcript not found', []],
  ] as const) {
    assert.equal(writtenWith(snapshot([opusMain], { state, reasons }), undefined), '', `${state}: ${reasons.join(', ')}`);
  }
  assert.equal(writtenWith(snapshot([opusMain], { reasons: 'peer running', state: 'partial' }), undefined), '', 'reasons that is not a list');
});

test('a member of the wrong type makes the whole line unusable, never half a sentence', () => {
  assert.equal(writtenWith(snapshot([row('main', 'claude-opus-5-5', { turns: 'x' })]), undefined), '');
  assert.equal(writtenWith(snapshot([row('main', 'claude-opus-5-5', { agent: 'main' })]), undefined), '');
  assert.equal(writtenWith(snapshot([row('main', 'claude-opus-5-5', { model: 7 })]), undefined), '');
  assert.equal(writtenWith(snapshot([opusMain], { uncoveredWrites: 'many' }), undefined), '');
  assert.equal(writtenWith(snapshot('rows'), undefined), '');
  assert.equal(writtenWith(snapshot([null]), undefined), '');
});

test('a size of the wrong shape adds no size clause and does not spoil the line', () => {
  assert.equal(writtenWith(snapshot([opusMain]), 'big'), 'written with Opus 5.5 (main) · main only');
  assert.equal(writtenWith(snapshot([opusMain]), size(scope('recorded', [cell('shown', 'code', 1, 3, 0), 'x']), scope('unavailable: first round', []))), 'written with Opus 5.5 (main) · main only');
  assert.equal(writtenWith(snapshot([opusMain]), size(scope('recorded', [{ coverage: 'shown', kind: 'code', files: 'two', added: 1, removed: 1 }]), scope('unavailable: first round', []))), 'written with Opus 5.5 (main) · main only');
  // A coverage word the server never writes (RoundSize.cs: shown, elided, excluded, binary) is a cell of the wrong shape, not a counted one.
  assert.equal(writtenWith(snapshot([opusMain]), size(scope('recorded', [cell('bogus', 'code', 4, 10, 2)]), scope('unavailable: first round', []))), 'written with Opus 5.5 (main) · main only');
});

test('every model vector is shown and grouped as the shared file says', () => {
  const all = vectors('model-display-vectors.json', 'models');
  assert.ok(all.length > 10 && all.some((v) => v['family'] === 'other') && all.some((v) => v['family'] === 'unknown'), 'the file is read, with its three kinds');

  for (const vector of all) {
    const shown = modelShown(String(vector['model']));
    assert.equal(shown.display, vector['display'], `display of ${JSON.stringify(vector['model'])}`);
    assert.equal(shown.family, vector['family'], `family of ${JSON.stringify(vector['model'])}`);
  }
});

test('every state vector is judged as the shared file says', () => {
  const all = vectors('authors-state-vectors.json', 'states');
  assert.ok(all.some((v) => v['wellFormed'] === true) && all.some((v) => v['wellFormed'] === false), 'both kinds are in the file');

  for (const vector of all) {
    assert.equal(isWellFormedAuthorsState(String(vector['value'])), vector['wellFormed'], JSON.stringify(vector['value']));
  }
});
