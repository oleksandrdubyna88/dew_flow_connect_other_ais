import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  CALLER_KINDS,
  CONSULTING_RUNTIMES,
  DEFAULT_CONSULT,
  consultSettingsFrom,
  consultableVendors,
  isDefaultConsult,
  sameCallers,
  sameVendorNote,
} from '../consultSettings';
import { consultPromptWrite } from '../consultPrompt';
import { consultantBody } from '../consultantView';
import { consultantRecordUpdate, settingWrite } from '../settingsShape';
import { Runtime } from '../models';
import { Vendor } from '../vendors';

/**
 * The Consultant section: who answers each kind of caller, the caps, and the prompt box.
 *
 * <p>Every assertion here is about a decision a PERSON will read off the screen and act on — which
 * vendor is about to be asked, why one is not offered, what an empty box means. The section is pure
 * markup from a value, so all of it is reachable without a running window.</p>
 */

function vendor(id: string, runtime = id as Runtime, enabled = true): Vendor {
  return {
    id,
    runtime,
    model: '',
    baseUrl: '',
    executablePath: '',
    enabled,
    plan: true,
    code: true,
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  };
}

/** A reader over a plain object, shaped like the one `settingsShape` hands the parsers. */
const reader = (stored: Record<string, unknown>) => (section: string): unknown => stored[section];

// ---------------------------------------------------------------------------------------------
// What the settings say

test('nothing configured is the shipped map, and the shipped map is four different vendors', () => {
  const settings = consultSettingsFrom(reader({}));

  assert.deepEqual(settings, DEFAULT_CONSULT);
  assert.ok(isDefaultConsult(settings));
  for (const { id } of CALLER_KINDS) {
    assert.notEqual(
      settings.byCaller[id]!.vendor,
      id,
      `${id} is shipped asking itself, and a model cannot see its own blind spot`,
    );
  }
});

test('one caller changed keeps the other three, because a record is not read whole', () => {
  const settings = consultSettingsFrom(reader({ consultants: { claude: { vendor: 'antigravity', model: 'x' } } }));

  assert.deepEqual(settings.byCaller['claude'], { vendor: 'antigravity', model: 'x' });
  assert.deepEqual(settings.byCaller['codex'], DEFAULT_CONSULT.byCaller['codex']);
  assert.deepEqual(settings.byCaller['gemini'], DEFAULT_CONSULT.byCaller['gemini']);
  assert.equal(isDefaultConsult(settings), false);
});

test('a row a person wrote by hand is trimmed, and a blank vendor is no choice at all', () => {
  const settings = consultSettingsFrom(reader({
    consultants: { codex: { vendor: '  claude  ', model: '  opus  ' }, gemini: { vendor: '   ' } },
  }));

  assert.deepEqual(settings.byCaller['codex'], { vendor: 'claude', model: 'opus' });
  assert.deepEqual(
    settings.byCaller['gemini'],
    DEFAULT_CONSULT.byCaller['gemini'],
    'a vendor made of spaces would be looked up, refused, and read as "not configured"',
  );
});

test('the caps take whole positive numbers and nothing else', () => {
  const junk = consultSettingsFrom(reader({
    consultTurns: 0, consultCallsPerSession: 2.5, consultIdleMinutes: 'thirty',
  }));

  assert.equal(junk.turns, DEFAULT_CONSULT.turns, 'a cap of zero refuses every call while the panel says it is on');
  assert.equal(junk.callsPerSession, DEFAULT_CONSULT.callsPerSession);
  assert.equal(junk.idleMinutes, DEFAULT_CONSULT.idleMinutes);

  const set = consultSettingsFrom(reader({ consultTurns: 3, consultCallsPerSession: 4, consultIdleMinutes: 30 }));
  assert.deepEqual([set.turns, set.callsPerSession, set.idleMinutes], [3, 4, 30]);
});

test('only a stored false switches the tool off — everything else leaves it available', () => {
  assert.equal(consultSettingsFrom(reader({ consultEnabled: false })).enabled, false);
  for (const value of [undefined, '', 'false', 0, 'no', true]) {
    assert.equal(
      consultSettingsFrom(reader({ consultEnabled: value })).enabled,
      true,
      `${JSON.stringify(value)} is not a person switching it off, and a consultant wrongly unavailable is a refusal in the one moment it was wanted`,
    );
  }
});

test('sameCallers compares the pairs, not the object', () => {
  assert.ok(sameCallers(DEFAULT_CONSULT.byCaller, { ...DEFAULT_CONSULT.byCaller, unknownKind: { vendor: 'x', model: '' } }));
  assert.equal(sameCallers(DEFAULT_CONSULT.byCaller, { ...DEFAULT_CONSULT.byCaller, codex: { vendor: 'codex', model: '' } }), false);
});

test('the same-vendor note is offered for thought rather than refused', () => {
  assert.match(sameVendorNote('claude', 'claude'), /stronger model/);
  assert.equal(sameVendorNote('claude', 'codex'), '');
});

// ---------------------------------------------------------------------------------------------
// Which rows may be consulted

test('a vendor that cannot hold a conversation is NAMED with its reason, never filtered away', () => {
  const { offered, refused } = consultableVendors([
    vendor('codex'),
    vendor('remsoftdev-claude', 'remote' as Runtime),
    vendor('gem', 'gemini' as Runtime),
    vendor('off', 'claude' as Runtime, false),
  ]);

  assert.deepEqual(offered.map((one) => one.id), ['codex']);
  assert.deepEqual(refused.map((one) => one.vendor.id), ['remsoftdev-claude', 'gem', 'off']);
  assert.match(refused[0]!.why, /remote/, 'the reason must name the runtime, or it explains nothing');
  assert.equal(refused[2]!.why, 'switched off');
});

test('the offered runtimes are the ones the server can actually resolve', () => {
  // The extension's copy of `ConsultantResolution.Consulting`. It decides what a picker OFFERS,
  // which has to be drawable before the server is installed — so it is a mirror, and this is the
  // list the agreement test holds against the C#.
  assert.deepEqual([...CONSULTING_RUNTIMES], ['codex', 'claude', 'antigravity', 'local']);
});

// ---------------------------------------------------------------------------------------------
// The section itself

const rows = [vendor('codex'), vendor('claude'), vendor('antigravity')];

test('every caller kind gets a row of its own, keyed by the caller', () => {
  const html = consultantBody(DEFAULT_CONSULT, { vendors: rows });

  for (const { id, label } of CALLER_KINDS) {
    assert.ok(html.includes(`data-caller="${id}"`), `${id} has no row`);
    assert.ok(html.includes(`${label} asks`), `${label} is not named in words`);
  }
  // Two controls per row, and both carry the caller: without it the provider would write whichever
  // of the four the document holds first.
  assert.equal((html.match(/data-setting="consultVendor" data-caller=/g) ?? []).length, CALLER_KINDS.length);
  assert.equal((html.match(/data-setting="consultModel" data-caller=/g) ?? []).length, CALLER_KINDS.length);
});

test('a saved vendor that no longer resolves is stranded in the list, not replaced', () => {
  const html = consultantBody(
    { ...DEFAULT_CONSULT, byCaller: { ...DEFAULT_CONSULT.byCaller, claude: { vendor: 'deepseek', model: '' } } },
    { vendors: rows },
  );

  assert.match(html, /deepseek — not configured any more/);
  // Falling through to the first offered row would show a pair nobody chose and offer it as valid.
  assert.match(html, /<option value="deepseek"[^>]*selected/);
});

test('a saved model is kept whatever the vendor lists, and both ways of saying so are on screen', () => {
  // A vendor that IS configured: the reviewers' own model list keeps a value it does not know and
  // marks it as yours — the same function, so the two sections cannot label one model two ways.
  const configured = consultantBody(
    { ...DEFAULT_CONSULT, byCaller: { ...DEFAULT_CONSULT.byCaller, claude: { vendor: 'codex', model: 'gpt-4' } } },
    { vendors: rows, codexModels: [{ id: 'gpt-5.6-luna', label: 'gpt-5.6-luna' }] },
  );
  assert.match(configured, /<option value="gpt-4" selected>gpt-4 \(yours\)/);

  // A vendor that is NOT configured any more has no model list at all, so the saved model would
  // vanish from the row that is asking about it. It is kept, and named for what it is.
  const stranded = consultantBody(
    { ...DEFAULT_CONSULT, byCaller: { ...DEFAULT_CONSULT.byCaller, claude: { vendor: 'deepseek', model: 'r1' } } },
    { vendors: rows },
  );
  assert.match(stranded, /r1 — not offered by this vendor/);
});

test('with nothing configured the section says so instead of drawing four empty pickers', () => {
  const html = consultantBody(DEFAULT_CONSULT, { vendors: [vendor('gem', 'gemini' as Runtime)] });

  assert.match(html, /No configured vendor can hold a consultation yet/);
  assert.ok(!html.includes('data-setting="consultVendor"'), 'a picker with nothing in it is worse than a sentence');
  // The vendor is still named with its reason — it is configured, and a person can see it is.
  assert.match(html, /gem cannot consult/);
});

test('a vendor id is escaped, because it is a name a person typed', () => {
  const html = consultantBody(
    { ...DEFAULT_CONSULT, byCaller: { ...DEFAULT_CONSULT.byCaller, other: { vendor: '<script>x</script>', model: '' } } },
    { vendors: rows },
  );

  assert.ok(!html.includes('<script>x</script>'));
});

test('the three caps are on screen with the numbers in force', () => {
  const html = consultantBody({ ...DEFAULT_CONSULT, turns: 3, callsPerSession: 4, idleMinutes: 30 }, { vendors: rows });

  assert.match(html, /data-setting="consultTurns" value="3"/);
  assert.match(html, /data-setting="consultCallsPerSession" value="4"/);
  assert.match(html, /data-setting="consultIdleMinutes" value="30"/);
  assert.match(html, /data-setting="consultEnabled" checked/);
});

test('the prompt box shows the override on disk, and says what empty means', () => {
  const shipped = consultantBody(DEFAULT_CONSULT, { vendors: rows });
  assert.match(shipped, /data-setting="consultPrompt" data-file="consult.md"/);
  assert.match(shipped, /Empty is the prompt this build ships with/);
  assert.match(shipped, /data-command="restoreConsultPrompt"/);

  const edited = consultantBody(DEFAULT_CONSULT, { vendors: rows, consultPrompt: 'Answer <b>briefly</b>.' });
  assert.match(edited, /Answer &lt;b&gt;briefly&lt;\/b&gt;\./, 'a prompt is text, and it is escaped like every other value');
});

// ---------------------------------------------------------------------------------------------
// What a changed control writes

test('a caller-keyed control is routed by its caller, not as a vendor called `claude`', () => {
  assert.deepEqual(
    settingWrite({ key: 'consultVendor', value: 'claude', caller: 'codex' }),
    { kind: 'caller', key: 'consultVendor', value: 'claude', caller: 'codex' },
  );
  // And the caller wins over a vendor that rode along: the consultant rows are the only controls
  // that carry one, and a row for the `claude` caller must never be looked up as the `claude` row.
  assert.equal(settingWrite({ key: 'consultVendor', value: 'x', vendor: 'claude', caller: 'claude' })?.kind, 'caller');
});

test('writing one caller keeps the other three, and a new vendor clears the model', () => {
  const stored = {
    claude: { vendor: 'codex', model: 'gpt-5.6-luna' },
    codex: { vendor: 'claude', model: 'opus' },
  };

  const changed = consultantRecordUpdate(stored, 'claude', 'consultVendor', 'antigravity');

  assert.deepEqual(changed['claude'], { vendor: 'antigravity', model: '' },
    'a model named for one vendor is not a model the next one offers');
  assert.deepEqual(changed['codex'], stored['codex'], 'the callers nobody touched must survive the write');

  const model = consultantRecordUpdate(changed, 'claude', 'consultModel', 'gemini-3.7-flash-high');
  assert.deepEqual(model['claude'], { vendor: 'antigravity', model: 'gemini-3.7-flash-high' });
});

test('an emptied prompt box removes the override rather than writing a prompt that says nothing', () => {
  assert.deepEqual(consultPromptWrite(''), { kind: 'remove' });
  assert.deepEqual(consultPromptWrite('  \n  '), { kind: 'remove' });
  assert.deepEqual(consultPromptWrite(undefined), { kind: 'remove' });
  // Kept verbatim: a prompt's trailing blank line is the author's, and the server composes its own
  // sections after it.
  assert.deepEqual(consultPromptWrite('Answer briefly.\n\n'), { kind: 'write', text: 'Answer briefly.\n\n' });
});
