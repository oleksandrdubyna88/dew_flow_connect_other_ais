import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  CALLER_KINDS,
  CHOICE_FIELDS,
  CONSULTING_RUNTIMES,
  ConsultantChoice,
  DEFAULT_CONSULT,
  ResolvedConsultant,
  consultSettingsFrom,
  consultableVendors,
  isDefaultConsult,
  resolveConsultant,
  sameCallers,
  sameVendorNote,
} from '../consultSettings';
import { consultPromptWrite } from '../consultPrompt';
import { consultantBody } from '../consultantView';
import { consultantRecordUpdate, envBlock, settingWrite, settingsFrom } from '../settingsShape';
import { Runtime } from '../models';
import { Vendor, vendorsFrom } from '../vendors';

/**
 * The Consultant section: who answers each kind of caller, the caps, and the prompt box.
 *
 * <p>Every assertion here is about a decision a PERSON will read off the screen and act on — which
 * vendor is about to be asked, why one is not offered, what an empty box means. The section is pure
 * markup from a value, so all of it is reachable without a running window.</p>
 */

function vendor(id: string, runtime: Runtime = 'codex', enabled = true): Vendor {
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

/** A legacy reference, as every `coai.consultants` written before 2026-09-14 holds one: an id, maybe a model, no runtime. */
function legacy(vendor: string, model = ''): ConsultantChoice {
  return { vendor, runtime: '', model, baseUrl: '', executablePath: '' };
}

/** A definition: the consultant's own runtime, model, endpoint and CLI path. */
function definition(vendor: string, runtime: Runtime, model = '', baseUrl = '', executablePath = ''): ConsultantChoice {
  return { vendor, runtime, model, baseUrl, executablePath };
}

/** What the reader hands back for a definition: the definition, marked as the rule's answer. */
function resolvedDefinition(vendor: string, runtime: Runtime, model = '', baseUrl = '', executablePath = ''): ResolvedConsultant {
  return { kind: 'definition', vendor, runtime, model, baseUrl, executablePath };
}

/** A reader over a plain object, shaped like the one `settingsShape` hands the parsers. */
const reader = (stored: Record<string, unknown>) => (section: string): unknown => stored[section];

// ---------------------------------------------------------------------------------------------
// What the settings say

test('nothing configured is the shipped map, and the shipped map is four different vendors', () => {
  const settings = consultSettingsFrom(reader({}));

  assert.deepEqual(settings.stored, DEFAULT_CONSULT.stored);
  assert.deepEqual(
    [settings.turns, settings.callsPerSession, settings.idleMinutes, settings.enabled],
    [DEFAULT_CONSULT.turns, DEFAULT_CONSULT.callsPerSession, DEFAULT_CONSULT.idleMinutes, DEFAULT_CONSULT.enabled],
  );
  assert.ok(isDefaultConsult(settings));
  for (const { id } of CALLER_KINDS) {
    assert.notEqual(
      settings.byCaller[id]!.vendor,
      id,
      `${id} is shipped asking itself, and a model cannot see its own blind spot`,
    );
    // And each shipped pair RESOLVES with no reviewer row configured at all: a fresh install has no
    // `claude` row, and `codex → claude` was dead for exactly that reason.
    assert.equal(settings.byCaller[id]!.kind, 'definition', `${id}'s shipped consultant does not resolve on a pristine install`);
  }
  // And what it reads is exactly the default VALUE: the shipped `codex` reviewer row carries no
  // model, endpoint or path that a bare runtime would not, so rule (a) against it and rule (b)
  // against nothing answer the same four definitions. `DEFAULT_CONSULT`'s docblock makes that
  // claim; this is what holds it true.
  assert.deepEqual(settings.byCaller, DEFAULT_CONSULT.byCaller, 'a fresh install reads something other than the default value');
});

test('the shipped pairs are legacy references — byte-for-byte the pairs the server ships', () => {
  // A runtime on a shipped pair would make it a definition the C# has nothing to agree with, and the
  // VALUES belong to the sibling plan (`PLAN_consultant_defaults_from_phase_0.md`), which this shape
  // change leaves alone.
  for (const { id } of CALLER_KINDS) {
    const pair = DEFAULT_CONSULT.stored[id]!;
    assert.deepEqual(pair, legacy(pair.vendor), `${id}'s shipped pair is not a bare legacy pair`);
    // The default's RESOLVED side is honest about what such a pair means with no rows in hand:
    // rule (b) — a definition on the runtime the id names, borrowing nothing — and it is the answer
    // the rule itself gives, not a second statement of it.
    assert.deepEqual(DEFAULT_CONSULT.byCaller[id], resolveConsultant(pair, []));
    assert.deepEqual(
      DEFAULT_CONSULT.byCaller[id],
      { kind: 'definition', vendor: pair.vendor, runtime: pair.vendor, model: '', baseUrl: '', executablePath: '' },
    );
  }
});

test('one caller changed keeps the other three, because a record is not read whole', () => {
  const settings = consultSettingsFrom(reader({ consultants: { claude: { vendor: 'antigravity', model: 'x' } } }));

  assert.deepEqual(settings.stored['claude'], legacy('antigravity', 'x'));
  // Resolved against the shipped reviewer rows, which include an `antigravity` one; the entry's own
  // model wins over the row's.
  assert.deepEqual(settings.byCaller['claude'], resolvedDefinition('antigravity', 'antigravity', 'x'));
  assert.deepEqual(settings.stored['codex'], DEFAULT_CONSULT.stored['codex']);
  assert.deepEqual(settings.stored['gemini'], DEFAULT_CONSULT.stored['gemini']);
  assert.equal(isDefaultConsult(settings), false);
});

test('a row a person wrote by hand is trimmed, and a blank vendor is no choice at all', () => {
  const settings = consultSettingsFrom(reader({
    consultants: { codex: { vendor: '  claude  ', model: '  opus  ' }, gemini: { vendor: '   ' } },
  }));

  assert.deepEqual(settings.stored['codex'], legacy('claude', 'opus'));
  assert.deepEqual(
    settings.stored['gemini'],
    DEFAULT_CONSULT.stored['gemini'],
    'a vendor made of spaces would be looked up, refused, and read as "not configured"',
  );
});

test('the three new fields are read trimmed, and junk of any type reads as empty — never a throw', () => {
  // `settings.json` is a file a person edits by hand: a number, a null or an array where a string
  // belongs must not leave the panel on its previous paint with nothing saying why.
  const settings = consultSettingsFrom(reader({
    consultants: { other: { vendor: ' codex ', runtime: ' codex ', model: 42, baseUrl: null, executablePath: ['x'] } },
  }));

  assert.deepEqual(settings.stored['other'], definition('codex', 'codex'));
});

test('a definition is stored as one: every field comes back trimmed, and it resolves to itself', () => {
  const settings = consultSettingsFrom(reader({
    consultants: {
      gemini: { vendor: 'deepseek', runtime: 'codex', model: ' deepseek-chat ', baseUrl: ' https://api.deepseek.com/v1 ', executablePath: ' /opt/codex ' },
    },
    vendors: [],
  }));

  const expected = definition('deepseek', 'codex', 'deepseek-chat', 'https://api.deepseek.com/v1', '/opt/codex');
  assert.deepEqual(settings.stored['gemini'], expected);
  assert.deepEqual(settings.byCaller['gemini'], { kind: 'definition', ...expected }, 'a definition borrows nothing — no row is consulted');
});

test('a runtime this build does not know reads as a legacy reference, and the id still resolves', () => {
  const settings = consultSettingsFrom(reader({ consultants: { other: { vendor: 'claude', runtime: 'llama.cpp' } }, vendors: [] }));

  assert.equal(settings.stored['other']!.runtime, '');
  assert.deepEqual(settings.byCaller['other'], resolvedDefinition('claude', 'claude'));
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
  assert.ok(sameCallers(DEFAULT_CONSULT.stored, { ...DEFAULT_CONSULT.stored, unknownKind: legacy('x') }));
  assert.equal(sameCallers(DEFAULT_CONSULT.stored, { ...DEFAULT_CONSULT.stored, codex: legacy('codex') }), false);
});

test('sameCallers compares all five fields — a definition is not the legacy pair it resolves from', () => {
  const asDefinition = { ...DEFAULT_CONSULT.stored, claude: definition('codex', 'codex') };

  assert.equal(sameCallers(DEFAULT_CONSULT.stored, asDefinition), false);
  assert.equal(
    sameCallers(asDefinition, { ...asDefinition, claude: definition('codex', 'codex', '', 'https://x/v1') }),
    false,
    'an endpoint of its own is a different consultant',
  );
  assert.equal(
    sameCallers(asDefinition, { ...asDefinition, claude: definition('codex', 'codex', '', '', '/opt/codex') }),
    false,
    'so is a CLI path of its own',
  );
  assert.ok(sameCallers(asDefinition, { ...asDefinition }));
});

test('the same-vendor note is offered for thought rather than refused', () => {
  assert.match(sameVendorNote('claude', 'claude'), /stronger model/);
  assert.equal(sameVendorNote('claude', 'codex'), '');
});

test('a caller kind is not a runtime, and `gemini` is the case that proves it', () => {
  // The vendor row that runs Gemini models is on the `antigravity` runtime, so a name-to-name
  // comparison withheld the warning from the one caller most likely to be pointed at itself.
  assert.match(sameVendorNote('gemini', 'antigravity'), /stronger model/);
  assert.match(sameVendorNote('gemini', 'gemini'), /stronger model/, 'a row configured before that runtime retired still exists');
  assert.equal(sameVendorNote('gemini', 'claude'), '');
  // `other` is not any vendor, so nothing is "itself" — saying so of an arbitrary script would be
  // a guess dressed as advice.
  assert.equal(sameVendorNote('other', 'codex'), '');
});

test('a cap the server could not hold is not a cap', () => {
  // settings.json is a file a person edits by hand, and 2147483648 is a whole positive number that
  // C#'s int.TryParse refuses — the panel would show one number while the server enforced another.
  assert.equal(consultSettingsFrom(reader({ consultTurns: 2_147_483_648 })).turns, DEFAULT_CONSULT.turns);
  assert.equal(consultSettingsFrom(reader({ consultCallsPerSession: 2_147_483_647 })).callsPerSession, 2_147_483_647);
});

// ---------------------------------------------------------------------------------------------
// A legacy entry resolves into a definition when it is READ (PLAN_the_consultant_has_its_own_vendors, A1)

/** A `coai.vendors` row as `settings.json` holds it — `vendorsFrom` fills in the rest. */
const LUNA_ROW = { id: 'codex', runtime: 'codex', model: 'gpt-5.6-luna' };

test('a legacy entry reads as a definition — after the read its runtime is present', () => {
  // `codex → claude` is the shipped pair and a pristine install has no `claude` reviewer row, so on
  // 2026-09-14 a Codex caller's consultant was dead in a panel nobody had touched. The id names a
  // runtime this build can consult with; the read says so, and writes nothing.
  const settings = consultSettingsFrom(reader({ consultants: { codex: { vendor: 'claude' } }, vendors: [] }));

  assert.deepEqual(settings.byCaller['codex'], resolvedDefinition('claude', 'claude'));
});

test('a legacy codex entry with an empty model materialises its reviewer row’s model', () => {
  // An empty model on a legacy entry meant THE ROW'S model — `ConsultationService` falls back to
  // `row.Model`. The new shape reads an empty model as the runtime's default, so the row's model is
  // written into the definition: what a person was getting is what the entry now says.
  const settings = consultSettingsFrom(reader({ consultants: { claude: { vendor: 'codex' } }, vendors: [LUNA_ROW] }));

  assert.equal(settings.byCaller['claude']!.model, 'gpt-5.6-luna');
});

test('a pristine map is still the default after it resolves against customised reviewer rows', () => {
  // The trap: resolving a pristine map turns four legacy pairs into four definitions, and comparing
  // those against the shipped pairs field by field says "different" — so an install where nobody
  // configured a consultant would start writing `COAI_CONSULTANTS`. Nobody configured one, and the
  // server does exactly this with no key at all, so the comparison reads the STORED side.
  const settings = consultSettingsFrom(reader({ vendors: [LUNA_ROW] }));

  assert.equal(settings.byCaller['claude']!.model, 'gpt-5.6-luna', 'the premise: resolution changed what the caller gets');
  assert.ok(isDefaultConsult(settings), 'nobody configured a consultant, and the server would do exactly this with no key');
  assert.ok(sameCallers(settings.stored, DEFAULT_CONSULT.stored));
});

// ---------------------------------------------------------------------------------------------
// The one resolution rule, arm by arm

/** A codex row a person tuned: its own model and its own CLI path. */
const LUNA: Vendor = { ...vendor('codex'), model: 'gpt-5.6-luna', executablePath: '/opt/codex/bin/codex' };

function whyUnavailable(resolved: ResolvedConsultant): string {
  return resolved.kind === 'unavailable' ? resolved.why : '';
}

test('rule (a): a reviewer row with that id resolves — switched OFF as well as on', () => {
  // Off is a fact about REVIEWS. It took the consultant down with it, and a caller was refused a
  // consultation nobody had switched off.
  const off: Vendor = { ...vendor('deepseek', 'codex', false), model: 'deepseek-chat', baseUrl: 'https://api.deepseek.com/v1' };

  assert.deepEqual(resolveConsultant(legacy('deepseek'), [off]), {
    kind: 'definition', vendor: 'deepseek', runtime: 'codex', model: 'deepseek-chat', baseUrl: 'https://api.deepseek.com/v1', executablePath: '',
  });
});

test('rule (a): the entry’s own model wins over the row’s; the endpoint and CLI path come from the row', () => {
  assert.deepEqual(resolveConsultant(legacy('codex', 'gpt-5.5'), [LUNA]), {
    kind: 'definition', vendor: 'codex', runtime: 'codex', model: 'gpt-5.5', baseUrl: '', executablePath: '/opt/codex/bin/codex',
  });
});

test('rule (a) matches the id the way the server always has — case-insensitively — and never rewrites it', () => {
  // `ConsultationService` looks the row up with OrdinalIgnoreCase and `vendorsFrom` lower-cases ids,
  // so a hand-written `Codex` consulted fine while the section drew it as "not configured any more".
  const resolved = resolveConsultant(legacy('Codex'), [LUNA]);

  assert.equal(resolved.kind, 'definition');
  assert.equal(resolved.vendor, 'Codex', 'the id keys the vault entry and the ledger; a resolution that changed it would move a credential');
});

test('rule (b) canonicalises the id to the runtime it names; rule (a) never touches it — both halves of the asymmetry', () => {
  // In (a) the id names a ROW a person created: it keys their vault entry and their ledger, and a
  // resolution that rewrote it would move a credential. In (b) the id names a RUNTIME, and `codex`
  // and `Codex` are the same one — while `vendorsFrom` lower-cases every row id, so a `Claude` kept
  // in its stored casing would key a vault entry and a ledger line that no reviewer row can ever
  // share, under a runtime whose own name is `claude`.
  assert.deepEqual(resolveConsultant(legacy('Claude', 'opus'), []), {
    kind: 'definition', vendor: 'claude', runtime: 'claude', model: 'opus', baseUrl: '', executablePath: '',
  });
  assert.equal(
    resolveConsultant(legacy('Codex'), [LUNA]).vendor,
    'Codex',
    'rule (a): the id is a row somebody named, and it stays exactly as they wrote it',
  );
});

test('rule (a) is asked before rule (b): a row named after a runtime is the row, not the bare runtime', () => {
  // The shipped `codex` row tuned to gpt-5.6-luna: the consultant gets that model and that CLI path,
  // which is what the entry always meant — rule (b) would have thrown both away.
  assert.deepEqual(resolveConsultant(legacy('codex'), [LUNA]), {
    kind: 'definition', vendor: 'codex', runtime: 'codex', model: 'gpt-5.6-luna', baseUrl: '', executablePath: '/opt/codex/bin/codex',
  });
});

test('rule (b): an id that names a consulting runtime is that runtime, with nothing borrowed', () => {
  assert.deepEqual(resolveConsultant(legacy('claude', 'opus'), []), {
    kind: 'definition', vendor: 'claude', runtime: 'claude', model: 'opus', baseUrl: '', executablePath: '',
  });
  // A runtime that cannot consult is not an id to resolve by: `gemini` is a runtime, and refused.
  assert.equal(resolveConsultant(legacy('gemini'), []).kind, 'unavailable');
});

test('rule (c): an id matching nothing is UNAVAILABLE — raw, and with a reason a person can act on', () => {
  const resolved = resolveConsultant(legacy('mistral', 'large'), [LUNA]);

  assert.equal(resolved.kind, 'unavailable');
  assert.deepEqual([resolved.vendor, resolved.model], ['mistral', 'large'], 'never rewritten, never defaulted');
  assert.match(whyUnavailable(resolved), /'mistral'/, 'the reason must name the id, or it explains nothing');
  // Derived from the list the code holds, never repeated here: a test that spelt the four out would
  // stay green when a fifth consulting runtime arrived and the sentence forgot to name it.
  for (const runtime of CONSULTING_RUNTIMES) {
    assert.match(whyUnavailable(resolved), new RegExp(`\\b${runtime}\\b`), `and say what CAN consult — '${runtime}' is not named`);
  }
});

test('a definition resolves to itself, whatever the rows say', () => {
  const own = definition('codex', 'codex', 'gpt-5.5', '', '/usr/local/bin/codex');

  assert.deepEqual(resolveConsultant(own, [LUNA]), { kind: 'definition', ...own });
});

test('materialising is not permitting: a row on a runtime that cannot consult still resolves to THAT runtime', () => {
  // Whether `remote` may hold a consultation is CONSULTING_RUNTIMES' question, asked where one is
  // offered or run. A definition that names its runtime is what lets that refusal name it too.
  const team: Vendor = { ...vendor('remsoftdev-claude', 'remote'), baseUrl: 'https://coai.remsoft.dev' };

  assert.deepEqual(resolveConsultant(legacy('remsoftdev-claude'), [team]), {
    kind: 'definition', vendor: 'remsoftdev-claude', runtime: 'remote', model: '', baseUrl: 'https://coai.remsoft.dev', executablePath: '',
  });
});

test('what the reader produced resolves to itself again — with the same rows, or none', () => {
  // The section (story C5) will hold no rows, and B4 emits the resolved map: both lean on the
  // reader's output being a fixed point. A definition is itself; an unavailable entry matched nothing
  // with rows in hand, so it matches nothing without them either — and keeps its reason.
  const rows = vendorsFrom([LUNA_ROW]);
  const settings = consultSettingsFrom(reader({
    consultants: { claude: { vendor: 'codex' }, other: { vendor: 'mistral', model: 'large' } },
    vendors: [LUNA_ROW],
  }));

  for (const { id } of CALLER_KINDS) {
    const once = settings.byCaller[id]!;
    if (once.kind === 'definition') {
      assert.deepEqual(resolveConsultant(once, rows), once, `${id}'s definition is not a fixed point with rows in hand`);
      assert.deepEqual(resolveConsultant(once, []), once, `${id}'s definition is not a fixed point without them`);
    } else {
      // An unavailable entry IS the rule's answer already, reason included; resolving what it was
      // read from again — with rows or without — is the same answer with the same reason.
      assert.deepEqual(once, resolveConsultant(settings.stored[id]!, rows), `${id}'s unavailable answer differs from the rule's`);
      assert.deepEqual(once, resolveConsultant(settings.stored[id]!, []), `${id}'s unavailable answer depends on rows it matched none of`);
    }
  }
  assert.equal(settings.byCaller['other']!.kind, 'unavailable', 'the premise: one of the four matched nothing');
  assert.deepEqual([settings.byCaller['other']!.vendor, settings.byCaller['other']!.model], ['mistral', 'large'], 'raw, untouched');
});

test('an unavailable entry keeps its REASON through the read — the resolved map carries the result, not a look-alike choice', () => {
  // The resolved map used to hand back the STORED choice when the rule answered `unavailable`, so
  // `byCaller` typed a resolved definition and an unresolved legacy entry identically and the one
  // sentence a person can act on was thrown away. Story C5 renders that sentence; recovering it
  // would have meant re-running the rule against `stored` plus the rows — a second road to one
  // decision. (codex and gemini, independently, on A1's code round.)
  const settings = consultSettingsFrom(reader({
    consultants: { other: { vendor: 'mistral', model: 'large' } },
    vendors: [LUNA_ROW],
  }));
  const resolved = settings.byCaller['other']!;

  assert.equal(resolved.kind, 'unavailable');
  assert.deepEqual([resolved.vendor, resolved.model], ['mistral', 'large'], 'raw, never rewritten, never defaulted');
  assert.match(whyUnavailable(resolved), /'mistral'/, 'the reason survives the read');
});

test('sameCallers notices a change in EVERY field a choice has — the field list is derived from the type, not repeated beside it', () => {
  // A hand-written `(keyof ConsultantChoice)[]` catches a field REMOVED from the type and never one
  // ADDED: a sixth field, parsed and written, would be missing from the list, two choices differing
  // only in it would compare equal, and `envBlock` would keep a changed setting off the wire. The
  // list is the keys of a `Record<keyof ConsultantChoice, true>`, which the compiler holds level with
  // the type in both directions; this pins the derivation at run time against a real choice.
  const base = definition('codex', 'codex', 'gpt-5.5', 'https://x/v1', '/opt/codex');

  assert.deepEqual([...CHOICE_FIELDS].sort(), Object.keys(base).sort(), 'the list is not the fields a choice actually has');
  // Over the fields the CHOICE has, not over the list — so a field the list forgot is still one this
  // loop changes, and the comparison has to notice it on its own.
  for (const field of Object.keys(base)) {
    const changed: ConsultantChoice = { ...base, [field]: field === 'runtime' ? 'claude' : `${field}-changed` };
    assert.equal(sameCallers({ claude: base }, { claude: changed }), false, `a change in '${field}' went unnoticed`);
  }
});

// ---------------------------------------------------------------------------------------------
// What crosses the seam — unchanged by this story, and measured before B4 changes it

test('COAI_CONSULTANTS carries the bytes it carried before resolution existed: the stored pair, never what it resolved to', () => {
  const stored: Record<string, unknown> = {
    consultants: { claude: { vendor: 'codex', model: '' }, codex: { vendor: 'claude', model: 'opus' } },
    vendors: [LUNA_ROW],
  };
  const settings = settingsFrom(reader(stored));

  // Today's bytes, verified against the code that produced them before this story touched it. A
  // resolved entry would carry `gpt-5.6-luna` for the claude caller — a wire change B4 measures
  // against an OLD server half first, and this story makes none.
  assert.equal(
    envBlock(settings, vendorsFrom(stored['vendors']))['COAI_CONSULTANTS'],
    '{"claude":{"vendor":"codex","model":""},"codex":{"vendor":"claude","model":"opus"},"gemini":{"vendor":"codex","model":""},"other":{"vendor":"codex","model":""}}',
  );
  assert.equal(settings.consult.byCaller['claude']!.model, 'gpt-5.6-luna', 'the premise: the read resolved it');
});

// ---------------------------------------------------------------------------------------------
// Which rows may be consulted

test('a vendor that cannot hold a conversation is NAMED with its reason, never filtered away', () => {
  const { offered, refused } = consultableVendors([
    vendor('codex'),
    vendor('remsoftdev-claude', 'remote'),
    vendor('gem', 'gemini'),
    vendor('off', 'claude', false),
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

const rows = [vendor('codex'), vendor('claude', 'claude'), vendor('antigravity', 'antigravity')];

// A `byCaller` fixture goes through `resolveConsultant` with the SAME rows the body is handed, which
// is what the reader does: the map holds the rule's answers, and a hand-built one would either be a
// definition the rows contradict or a fixture typed by an `as`.

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
    { ...DEFAULT_CONSULT, byCaller: { ...DEFAULT_CONSULT.byCaller, claude: resolveConsultant(legacy('deepseek'), rows) } },
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
    { ...DEFAULT_CONSULT, byCaller: { ...DEFAULT_CONSULT.byCaller, claude: resolveConsultant(legacy('codex', 'gpt-4'), rows) } },
    { vendors: rows, codexModels: [{ id: 'gpt-5.6-luna', label: 'gpt-5.6-luna' }] },
  );
  assert.match(configured, /<option value="gpt-4" selected>gpt-4 \(yours\)/);

  // A vendor that is NOT configured any more has no model list at all, so the saved model would
  // vanish from the row that is asking about it. It is kept, and named for what it is.
  const stranded = consultantBody(
    { ...DEFAULT_CONSULT, byCaller: { ...DEFAULT_CONSULT.byCaller, claude: resolveConsultant(legacy('deepseek', 'r1'), rows) } },
    { vendors: rows },
  );
  assert.match(stranded, /r1 — not offered by this vendor/);
});

test('with nothing consultable the sentence is said BESIDE the rows, never instead of them', () => {
  const onlyGemini = [vendor('gem', 'gemini')];
  const html = consultantBody(
    { ...DEFAULT_CONSULT, byCaller: { ...DEFAULT_CONSULT.byCaller, claude: resolveConsultant(legacy('codex'), onlyGemini) } },
    { vendors: onlyGemini },
  );

  assert.match(html, /No configured vendor can hold a consultation yet/);
  // The rows stay: this is the one moment a person is deciding what to configure, and what each
  // caller is already set to is what they need to see. (codex, the code round.)
  for (const { id } of CALLER_KINDS) {
    assert.ok(html.includes(`data-caller="${id}"`), `${id} lost its row when nothing could answer`);
  }
  assert.match(html, /codex — not configured any more/);
  // The vendor is still named with its reason — it is configured, and a person can see it is.
  assert.match(html, /gem cannot consult/);
});

test('a reviewer switched off says SO, rather than reading as one that was deleted', () => {
  const withOff = [...rows, vendor('off', 'claude', false)];
  const html = consultantBody(
    { ...DEFAULT_CONSULT, byCaller: { ...DEFAULT_CONSULT.byCaller, claude: resolveConsultant(legacy('off'), withOff) } },
    { vendors: withOff },
  );

  assert.match(html, /off — switched off in Reviewers/);
  assert.ok(!html.includes('off — not configured any more'), 'the row contradicted the sentence underneath it');
});

test('a vendor id is escaped, because it is a name a person typed', () => {
  const html = consultantBody(
    { ...DEFAULT_CONSULT, byCaller: { ...DEFAULT_CONSULT.byCaller, other: resolveConsultant(legacy('<script>x</script>'), rows) } },
    { vendors: rows },
  );

  assert.ok(!html.includes('<script>x</script>'));
});

test('the default model option keeps its noun when the row has no model of its own', () => {
  const html = consultantBody(DEFAULT_CONSULT, { vendors: rows });

  assert.match(html, /<option value="" selected>the row’s own model<\/option>/);
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

  const changed = consultantRecordUpdate(stored, 'claude', 'consultVendor', 'antigravity', []);

  assert.deepEqual(changed['claude'], { vendor: 'antigravity', runtime: 'antigravity', model: '' },
    'a model named for one vendor is not a model the next one offers');
  assert.deepEqual(changed['codex'], stored['codex'], 'the callers nobody touched must survive the write');

  const model = consultantRecordUpdate(changed, 'claude', 'consultModel', 'gemini-3.7-flash-high', []);
  assert.deepEqual(model['claude'], { vendor: 'antigravity', runtime: 'antigravity', model: 'gemini-3.7-flash-high' });
});

/**
 * The write half of story A2: what is STORED stops being a reference to somebody's reviewer.
 *
 * <p>Story A1 made a legacy entry resolve on READ, which fixed the symptom with no write at all. It
 * left the file itself still holding `{vendor, model}` — so the consultant went on following a
 * reviewer row until somebody touched the section. These tests pin the other half: the first edit
 * writes what will actually run.</p>
 */
test('changing a model writes the caller\u0027s whole definition, not a bare reference', () => {
  const stored = { claude: { vendor: 'codex', model: '' } };
  const rows = [{ ...vendor('codex'), model: 'gpt-5.6-luna', executablePath: 'C:/codex.cmd' }];

  const changed = consultantRecordUpdate(stored, 'claude', 'consultModel', 'gpt-6-astra', rows);

  assert.deepEqual(
    changed['claude'],
    { vendor: 'codex', runtime: 'codex', model: 'gpt-6-astra', executablePath: 'C:/codex.cmd' },
    'the edit must materialise the runtime and the CLI path the entry was borrowing, not write a reference again');
});

test('choosing a vendor writes the model that vendor will actually use', () => {
  const stored = { claude: { vendor: 'antigravity', model: 'gemini-3.7' } };
  const rows = [{ ...vendor('codex'), model: 'gpt-5.6-luna' }];

  const changed = consultantRecordUpdate(stored, 'claude', 'consultVendor', 'codex', rows);

  assert.deepEqual(changed['claude'], { vendor: 'codex', runtime: 'codex', model: 'gpt-5.6-luna' },
    'the row lends its model to a consultant that names no model of its own, so that is what is stored');
});

test('an empty vendor id is never written', () => {
  const stored = { claude: { vendor: 'codex', model: 'gpt-5.6-luna' } };

  assert.deepEqual(consultantRecordUpdate(stored, 'claude', 'consultVendor', '   ', []), stored,
    'a blank id keys no vault entry and names no runtime; the map must come back untouched');
});

test('a base URL the person typed replaces the endpoint, never the model', () => {
  const stored = { claude: { vendor: 'deepseek', runtime: 'codex', model: 'deepseek-chat' } };

  const changed = consultantRecordUpdate(stored, 'claude', 'consultBaseUrl', 'https://api.deepseek.com/v1', []);

  assert.deepEqual(
    changed['claude'],
    { vendor: 'deepseek', runtime: 'codex', model: 'deepseek-chat', baseUrl: 'https://api.deepseek.com/v1' },
    'the model must survive an endpoint edit');
});

/**
 * A guard rather than a reproduction: both hold at HEAD, and they are here because the change above
 * is exactly the kind that would break them silently — rebuilding the row from `CALLER_KINDS`, or
 * inventing a runtime for an entry the rule could not place.
 */
test('a caller kind this build does not know survives a write', () => {
  const stored = { claude: { vendor: 'codex', model: '' }, futureKind: { vendor: 'x', model: 'y' } };

  const changed = consultantRecordUpdate(stored, 'claude', 'consultModel', 'opus', []);

  assert.deepEqual(changed['futureKind'], { vendor: 'x', model: 'y' },
    'a newer panel may know more caller kinds than this one, and the server keeps them for that reason');
});

test('editing the model of an entry this build cannot place invents no runtime', () => {
  const stored = { claude: { vendor: 'retired-vendor', model: '' } };

  const changed = consultantRecordUpdate(stored, 'claude', 'consultModel', 'something', []);

  assert.deepEqual(changed['claude'], { vendor: 'retired-vendor', model: 'something' },
    'an unplaceable entry is shown back exactly as stored; guessing a runtime would send the tree somewhere nobody chose');
});

test('an emptied prompt box removes the override rather than writing a prompt that says nothing', () => {
  assert.deepEqual(consultPromptWrite(''), { kind: 'remove' });
  assert.deepEqual(consultPromptWrite('  \n  '), { kind: 'remove' });
  assert.deepEqual(consultPromptWrite(undefined), { kind: 'remove' });
  // Kept verbatim: a prompt's trailing blank line is the author's, and the server composes its own
  // sections after it.
  assert.deepEqual(consultPromptWrite('Answer briefly.\n\n'), { kind: 'write', text: 'Answer briefly.\n\n' });
});
