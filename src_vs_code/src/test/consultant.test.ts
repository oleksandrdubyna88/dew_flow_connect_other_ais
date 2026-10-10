import assert from 'node:assert/strict';
import { test } from 'node:test';
import { statSync as statSyncForProbe } from 'node:fs';
import { fileAt, useFileProbe } from '../pathFamily';
import {
  CALLER_KINDS,
  CHOICE_FIELDS,
  CONSULTANT_DEFINITION_SINCE,
  CONSULTING_RUNTIMES,
  ConsultantChoice,
  DEFAULT_CONSULT,
  ResolvedConsultant,
  consultSettingsFrom,
  consultantSkewNote,
  isDefaultConsult,
  resolveConsultant,
  isCallersOwnRuntime,
  sameCallers,
  vaultKeyNote,
} from '../consultSettings';
import { consultPromptWrite } from '../consultPrompt';
import { consultantBody } from '../consultantView';
import { badEndpoint, endpointAnswer, endpointConflict } from '../consultantWrite';
import { envBlock, settingWrite, settingsFrom } from '../settingsShape';
import { Runtime } from '../models';
import { hostFamily } from '../hostSide';
import { Vendor, vendorsFrom } from '../vendors';

// The host's disk, as activation installs it (paths per side, E1.2) — the rule asks it about other-OS spellings.
useFileProbe(fileAt(statSyncForProbe));

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

test('a caller kind is not a runtime, and `gemini` is the case that proves it', () => {
  // The vendor row that runs Gemini models is on the `antigravity` runtime, so a name-to-name
  // comparison withheld the warning from the one caller most likely to be pointed at itself.
  // What the Consultant tab says it with is run in consultantPicks.test.ts; this is the rule it asks.
  assert.equal(isCallersOwnRuntime('gemini', 'antigravity'), true);
  assert.equal(isCallersOwnRuntime('gemini', 'gemini'), true, 'a row configured before that runtime retired still exists');
  assert.equal(isCallersOwnRuntime('gemini', 'claude'), false);
  assert.equal(isCallersOwnRuntime('claude', 'claude'), true);
  // `other` is not any vendor, so nothing is "itself" — saying so of an arbitrary script would be
  // a guess dressed as advice.
  assert.equal(isCallersOwnRuntime('other', 'codex'), false);
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
// What crosses the seam — the DEFINITION, since story B4 measured one against the released server
//
// Measured 2026-09-15 against mcp-v0.22.0 (4fe3cb02), the last released server, whose
// `ConsultantDto` is `(Vendor, Model)`: handed a definition it consulted through the REVIEWER ROW
// with the same id — that row's runtime, endpoint and CLI path — with the entry's model, exactly as
// it did for the legacy pair; a definition whose id had no row was refused "not configured". So the
// wire now carries what the panel resolved, and `consultantSkewNote` says what an older server does
// with it. The replies are recorded verbatim in `research/module_server.md` under B4.

/** What `COAI_CONSULTANTS` carries, parsed — one map, as the server reads it. */
function wire(stored: Record<string, unknown>): Record<string, unknown> {
  const raw = envBlock(settingsFrom(reader(stored)), vendorsFrom(stored['vendors']))['COAI_CONSULTANTS'];

  assert.ok(raw !== undefined, 'the premise: a map that differs from the shipped pairs is on the wire');

  return JSON.parse(raw) as Record<string, unknown>;
}

test('COAI_CONSULTANTS carries the RESOLVED definition — all five fields — never the stored pair', () => {
  // A Codex caller pointed at `codex` — the same vendor, an explicit choice the shipped pair is not —
  // as a legacy entry whose row is tuned. Before B4 this sent `{vendor:"codex", model:""}` and the
  // server re-derived the row's model itself; now the panel's resolution IS what crosses, and an
  // older server drops the three fields it does not know (measured — the block comment above). The
  // KEY ORDER is not asserted here: `panelServerDefaultsAgreement` reads it off the C# DTO, and a
  // second list of the same names beside this one would be the copy that rule forbids.
  const map = wire({ consultants: { codex: { vendor: 'codex', model: '' } }, vendors: [LUNA_ROW] });

  assert.deepEqual(map['codex'], { vendor: 'codex', runtime: 'codex', model: 'gpt-5.6-luna', baseUrl: '', executablePath: '' });
});

test('a stored definition crosses as itself — its own endpoint and CLI path, nothing borrowed from a row', () => {
  // A path spelled for THIS host's OS: one of the other OS is the other side's and is skipped (todo/PLAN_paths_per_side.md E1.2).
  const own = hostFamily() === 'windows' ? 'C:\\opt\\claude\\claude.exe' : '/opt/claude/bin/claude';
  const map = wire({
    consultants: { codex: { vendor: 'claude', runtime: 'claude', model: 'opus', baseUrl: '', executablePath: own } },
    vendors: [LUNA_ROW],
  });

  assert.deepEqual(map['codex'], { vendor: 'claude', runtime: 'claude', model: 'opus', baseUrl: '', executablePath: own });
});

test('a stored definition\'s CLI path of the other OS crosses empty — the server there runs it, this one looks it up on PATH', () => {
  const other = hostFamily() === 'windows' ? '/opt/claude/bin/claude' : 'C:\\opt\\claude\\claude.exe';
  const map = wire({
    consultants: { codex: { vendor: 'claude', runtime: 'claude', model: 'opus', baseUrl: '', executablePath: other } },
    vendors: [LUNA_ROW],
  });

  assert.equal((map['codex'] as Record<string, unknown>)['executablePath'], '');
});

test('only the callers that differ from the shipped pair are on the wire — the file carries what differs, per caller', () => {
  // Every caller used to travel whenever one differed, as a legacy pair the server resolved to the
  // same thing it would have chosen with no key. A RESOLVED definition is not that: it freezes the
  // panel's reading of a caller nobody configured, so an untouched caller now stays off the wire and
  // the server resolves its own absence — which is what `ConsultantRouting.For` does for a kind the
  // map lacks.
  const map = wire({ consultants: { codex: { vendor: 'codex', model: '' } }, vendors: [LUNA_ROW] });

  assert.deepEqual(Object.keys(map), ['codex']);
});

test('an UNAVAILABLE entry travels RAW — vendor and model, no runtime — so the server refuses it by name', () => {
  // Rule (c) on the panel is rule (c) on the server: an entry that matched no row and names no
  // runtime must reach the server exactly as stored, so `NothingNamed` can say which id, and must
  // carry NO runtime — a runtime invented here would be a definition the server builds a provider
  // for, sending a working tree to a vendor nobody chose.
  const map = wire({ consultants: { other: { vendor: 'mistral', model: 'large' } }, vendors: [LUNA_ROW] });

  assert.deepEqual(map, { other: { vendor: 'mistral', model: 'large' } });
});

// ---------------------------------------------------------------------------------------------
// The skew note — what an older server does with a definition, said while one is installed

/** A version one step below the marker, derived from it so the test cannot drift when the marker moves. */
function justBelow(marker: string): string {
  const parts = marker.split('.').map(Number);
  const last = parts.length - 1;

  return [...parts.slice(0, last), parts[last]! - 1].join('.');
}

const OLDER = justBelow(CONSULTANT_DEFINITION_SINCE);

test("an older server is called out while some caller's STORED entry is a definition", () => {
  // Measured: mcp-v0.22.0 consulted through the reviewer row with the same id and dropped the
  // definition's runtime, endpoint and CLI path — the section shows one thing, the row runs. The note
  // names the installed version, the version that reads the definition, and WHO is affected.
  const settings = consultSettingsFrom(reader({
    consultants: { claude: { vendor: 'claude', runtime: 'claude', model: 'opus', executablePath: '/opt/claude' } },
  }));
  const note = consultantSkewNote(OLDER, settings, []);

  assert.ok(note.includes(OLDER), `the installed version is not named: ${note}`);
  assert.ok(note.includes(CONSULTANT_DEFINITION_SINCE), `the version to update to is not named: ${note}`);
  assert.ok(note.includes('Claude Code'), `the affected caller is not named: ${note}`);
  assert.ok(note.includes('reviewer row'), `what will actually run is not said: ${note}`);
});

test('a legacy entry that crosses as a definition is called out too — the gate is the WIRE, not the file', () => {
  // The hole B4's plan round found. `envBlock` emits the RESOLVED entry, so a legacy reference the
  // reader turned into a definition crosses AS one — and an upgraded install whose settings.json has
  // not been edited since is exactly that case. Gating the note on the STORED shape left the person
  // who never touched the section, which is most of them, warned about nothing.
  // `claude` names no reviewer row here, so the reader resolves it by rule (b) — a definition on the
  // claude runtime backed by nothing. An older server has no rule (b): it looks the id up in the
  // reviewer rows and refuses. A real difference, and the file still holds the legacy shape.
  const settings = consultSettingsFrom(reader({
    consultants: { claude: { vendor: 'claude', model: 'opus' } },
    vendors: [],
  }));

  assert.equal(settings.stored['claude']!.runtime, '', 'the premise: the file still holds a legacy reference');
  assert.equal(settings.byCaller['claude']!.kind, 'definition', 'the premise: it resolves into one');
  assert.match(consultantSkewNote(OLDER, settings, []), /Claude Code/,
    'what an older server mishandles is what CROSSES, and a definition backed by no row crossed');
});

test('the note names the callers an older server would answer differently, and no other', () => {
  // One reviewer row, `codex`, plain. Against it:
  //   claude  — a definition on an id NO row backs: an older server refuses it. NAMED.
  //   gemini  — a definition the codex row reproduces exactly: the same place on both halves. Silent.
  //   codex   — a customised LEGACY entry resolved FROM that row: the same place again. Silent.
  const rows = [{ id: 'codex', runtime: 'codex', model: 'gpt-5.6-luna' }];
  const settings = consultSettingsFrom(reader({
    consultants: {
      claude: { vendor: 'anthropic-direct', runtime: 'claude', model: 'opus' },
      gemini: { vendor: 'codex', runtime: 'codex', model: 'gpt-6-astra' },
      codex: { vendor: 'codex', model: '' },
    },
    vendors: rows,
  }));
  const note = consultantSkewNote(OLDER, settings, vendorsFrom(rows));

  assert.ok(note.includes('Claude Code'), `the caller no row backs is not named: ${note}`);
  assert.ok(!note.includes('Gemini'), `a definition the row reproduces was named: ${note}`);
  assert.ok(!note.includes('Codex'), `a legacy entry resolved from that row was named: ${note}`);
});

test('a definition backed by a SWITCHED-OFF row is called out — an older server refuses that row', () => {
  // The refusal for a disabled reviewer row was only removed in story B3. An older server still has
  // it, so a definition whose fields a disabled row reproduces exactly does NOT reach the same place
  // there: it is refused as switched off. Matching the fields is not enough; the row has to be one
  // that older server would actually use. (gemini, B4's code round.)
  const rows = [{ id: 'codex', runtime: 'codex', model: 'gpt-5.6-luna', enabled: false }];
  const settings = consultSettingsFrom(reader({
    consultants: { claude: { vendor: 'codex', runtime: 'codex', model: 'gpt-5.6-luna' } },
    vendors: rows,
  }));

  assert.match(consultantSkewNote(OLDER, settings, vendorsFrom(rows)), /Claude Code/,
    'an older server refuses a switched-off row, so it does not reproduce the definition');
});

test('the note is silent for a server that reads the definition, a later one, and one nobody has installed', () => {
  const settings = consultSettingsFrom(reader({
    consultants: { claude: { vendor: 'claude', runtime: 'claude', model: 'opus' } },
  }));

  assert.equal(consultantSkewNote(CONSULTANT_DEFINITION_SINCE, settings, []), '', 'the one that reads it says nothing');
  assert.equal(consultantSkewNote('9.0.0', settings, []), '', 'nor does a later one');
  assert.equal(consultantSkewNote('', settings, []), '', 'a server nobody has installed is not behind');
});

test('the note is silent when nothing on the wire is a definition — a legacy entry means the row on both halves', () => {
  // A customised LEGACY entry crosses as the resolved row, and an older server runs that same row:
  // nothing it gets wrong. A pristine map crosses as nothing at all.
  const legacyOnly = consultSettingsFrom(reader({ consultants: { codex: { vendor: 'codex', model: 'gpt-5.5' } }, vendors: [LUNA_ROW] }));

  assert.equal(consultantSkewNote(OLDER, legacyOnly, vendorsFrom([LUNA_ROW])), '');
  assert.equal(consultantSkewNote(OLDER, DEFAULT_CONSULT, []), '');
});

// ---------------------------------------------------------------------------------------------
// Which rows may be consulted

test('the offered runtimes are the ones the server can actually resolve', () => {
  // The extension's copy of `ConsultantResolution.Consulting`. It decides what a picker OFFERS,
  // which has to be drawable before the server is installed — so it is a mirror, and this is the
  // list the agreement test holds against the C#.
  // `api` since PLAN_one_model_catalog.md D9 (E2.3) — the server reads the same shared file.
  assert.deepEqual([...CONSULTING_RUNTIMES], ['codex', 'claude', 'antigravity', 'local', 'api']);
});

// ---------------------------------------------------------------------------------------------
// The section around the callers' picks: the caps and the prompt box

test('the three caps are on screen with the numbers in force', () => {
  const html = consultantBody({ ...DEFAULT_CONSULT, turns: 3, callsPerSession: 4, idleMinutes: 30 }, {}, '');

  assert.match(html, /data-setting="consultTurns" value="3"/);
  assert.match(html, /data-setting="consultCallsPerSession" value="4"/);
  assert.match(html, /data-setting="consultIdleMinutes" value="30"/);
  assert.match(html, /data-setting="consultEnabled" checked/);
});

test('the prompt box shows the override on disk, and says what empty means', () => {
  const shipped = consultantBody(DEFAULT_CONSULT, {}, '');
  assert.match(shipped, /data-setting="consultPrompt" data-file="consult.md"/);
  assert.match(shipped, /Empty is the prompt this build ships with/);
  assert.match(shipped, /data-command="restoreConsultPrompt"/);

  const edited = consultantBody(DEFAULT_CONSULT, { consultPrompt: 'Answer <b>briefly</b>.' }, '');
  assert.match(edited, /Answer &lt;b&gt;briefly&lt;\/b&gt;\./, 'a prompt is text, and it is escaped like every other value');
});

// ---------------------------------------------------------------------------------------------
// What a changed control writes

test('a caller-keyed control is routed by its caller, not as a vendor called `claude`', () => {
  assert.deepEqual(
    settingWrite({ key: 'consultantRow', value: 'claude', caller: 'codex' }),
    { kind: 'caller', key: 'consultantRow', value: 'claude', caller: 'codex' },
  );
  // And the caller wins over a vendor that rode along: the consultant rows are the only controls
  // that carry one, and a row for the `claude` caller must never be looked up as the `claude` row.
  assert.equal(settingWrite({ key: 'consultantRow', value: 'x', vendor: 'claude', caller: 'claude' })?.kind, 'caller');
});

test('a stored runtime is recognised without case, so both halves read one file the same way', () => {
  // The server treats any non-empty runtime as a definition and refuses one outside its allowlist.
  // This side read the list exactly, so `Codex` was no runtime at all: the entry fell back to a
  // legacy reference, resolved by its id, and the panel drew a consultant the server would refuse.
  const read = consultSettingsFrom(reader({ consultants: { claude: { vendor: 'mine', runtime: 'Codex' } }, vendors: [] }));

  assert.deepEqual(read.byCaller['claude'], resolvedDefinition('mine', 'codex'),
    'recognised without case and answered in the list own spelling, so one name reaches the wire');
});

// ---------------------------------------------------------------------------------------------
// A model of your own: what the name and the URL boxes of *Add a model* refuse (story C6's checks)

test('an id already held at a DIFFERENT endpoint is refused — one name is one credential', () => {
  // Two endpoints under one name would send one key to whichever answered second. The check spans
  // the reviewer rows AND the callers' consultants, because both key the vault by id.
  const rows = [{ ...vendor('mistral'), baseUrl: 'https://api.mistral.ai/v1' }];
  const consultants = { gemini: { vendor: 'acme', runtime: 'codex', model: '', baseUrl: 'https://acme.example/v1' } };

  assert.match(endpointConflict('mistral', 'https://other.example/v1', rows, consultants), /mistral/);
  assert.match(endpointConflict('acme', 'https://other.example/v1', rows, consultants), /acme/);
  assert.equal(endpointConflict('mistral', 'https://api.mistral.ai/v1', rows, consultants), '',
    'the same endpoint under the same name is one vault key used twice, which is the point of a name');
  assert.equal(endpointConflict('brand-new', 'https://new.example/v1', rows, consultants), '');
});

test('a caller\'s PICK of a catalog row holds no endpoint of its own — its row does', () => {
  // A pick is stored as a bare reference (`{ vendor: '<row id>' }`, E4.2); read as a holder it had an empty endpoint, so
  // configuring that very row at its own URL in Add a model was refused as a clash with "another caller's consultant"
  // (E5.3's code round, four findings). The row itself is the holder, at its real URL.
  const rows = [{ ...vendor('my-model'), baseUrl: 'http://localhost:11434/v1' }];
  const picked = { claude: { vendor: 'my-model' }, codex: { vendor: 'my-model' } };

  assert.equal(endpointConflict('my-model', 'http://localhost:11434/v1', rows, picked), '',
    'the row\'s own endpoint was refused because a caller picked the row');
  assert.match(endpointConflict('my-model', 'https://elsewhere.example/v1', rows, picked), /my-model/,
    'another endpoint under the row\'s name is still one name at two endpoints');
});

test('a name the CATALOGUE already means something by is refused at a different endpoint', () => {
  // With no `deepseek` reviewer row a person could name their own endpoint `deepseek`, and the next
  // caller to pick DeepSeek from the catalogue would share its vault key with a different service.
  // The catalogue is a holder of names exactly as the rows are. (codex, C6's code round.)
  assert.match(endpointConflict('deepseek', 'https://mine.example/v1', [], {}), /deepseek/);
  assert.equal(endpointConflict('deepseek', 'https://api.deepseek.com/v1', [], {}), '',
    'the catalogue entry at its own URL is the same service named once');
});

test('the cure never names an endpoint that does not exist', () => {
  // `claude` is a catalogue preset with no base URL of its own. Telling a person to "use that
  // endpoint" when there is none to use is advice they cannot follow. (gemini, C6's code round.)
  const why = endpointConflict('claude', 'https://mine.example/v1', [], {});

  assert.match(why, /claude/);
  assert.ok(!/use that endpoint/.test(why), 'there is no endpoint to point at, so the sentence must not point at one');
});

test('one name at two endpoints is SAID, however the second one got there', () => {
  // `endpointConflict` guards the flow that MINTS a name; a stored consultant definition — written before
  // the picks, or by hand — goes nowhere near it, and can hold any URL under a name a reviewer row already
  // holds, so the vault entry that name keys is pointed at two services. The panel does not rewrite what
  // somebody stored, so it SAYS it — beside the section, the way the server-skew note does, because the
  // sentence needs the reviewer rows and the section has none. (gemini, C6's code round.)
  const consult = {
    ...DEFAULT_CONSULT,
    byCaller: {
      ...DEFAULT_CONSULT.byCaller,
      claude: resolvedDefinition('mistral', 'codex', '', 'https://mine.example/v1'),
    },
  };

  const said = vaultKeyNote(consult, [{ ...vendor('mistral'), baseUrl: 'https://api.mistral.ai/v1' }]);
  assert.match(said, /mistral/);
  assert.match(said, /https:\/\/api\.mistral\.ai\/v1/);
  assert.equal(vaultKeyNote(consult, [{ ...vendor('mistral'), baseUrl: 'https://mine.example/v1' }]), '',
    'the same endpoint under one name is one service named once, which is what a shared key is FOR');
  assert.equal(vaultKeyNote(consult, []), '', 'and with no row of that name there is nothing to disagree with');
});

test('an endpoint has to parse as one, and carry no key', () => {
  // `startsWith('http')` accepted `http-not-a-url`: the box closed, the setting was stored, and the
  // person found out the next time they were stuck. (codex, C6's code round.)
  assert.equal(badEndpoint('https://api.example.com/v1'), undefined);
  assert.equal(badEndpoint('  http://localhost:11434/v1  '), undefined);
  assert.match(badEndpoint('http-not-a-url') ?? '', /base URL/);
  assert.match(badEndpoint('') ?? '', /base URL/);
  assert.match(badEndpoint('ftp://example.com/v1') ?? '', /http or https/);
  // A key in the URL lands in settings.json, and a WORKSPACE settings.json is a file people commit.
  assert.match(badEndpoint('https://me:sk-secret@api.example.com/v1') ?? '', /vault entry/);
  assert.match(badEndpoint('https://api.example.com/v1?api_key=sk-secret') ?? '', /vault entry/);
});

test('a key in the URL is caught however the gateway spells the parameter', () => {
  // The first list was the half-dozen names I happened to think of, matched exactly. A gateway names
  // that parameter what it likes, and three of these are what real ones use — so the match is
  // case-blind and a parameter whose name CONTAINS a known word counts, because `x-api-key` and
  // `apiKey` are the same mistake as `api_key`. The fragment is read too: it never reaches the
  // server, but it does reach settings.json, which is the file this is about.
  // (codex, the code round on this split.)
  for (const url of [
    'https://gateway.example/v1?x-api-key=sk-live-123',
    'https://gateway.example/v1?apiKey=sk-live-123',
    'https://gateway.example/v1?authorization=Bearer%20abc',
    'https://gateway.example/v1?X-Api-Key=sk-live-123',
    'https://gateway.example/v1?subscription-key=abc',
    'https://gateway.example/v1#access_token=abc',
  ]) {
    assert.match(badEndpoint(url) ?? '', /vault entry/, `${url} was accepted`);
  }

  // An ordinary query is still ordinary. Refusing every URL carrying a `?` would refuse the
  // endpoints that legitimately name a version or a deployment, which is how Azure spells one.
  assert.equal(badEndpoint('https://gateway.example/v1?api-version=2024-02-01'), undefined);
  assert.equal(badEndpoint('https://gateway.example/openai/deployments/gpt4?model=gpt-4'), undefined);
});

test('a URL box left empty is no endpoint — a custom endpoint with no URL is the plain runtime', () => {
  // Storing `baseUrl: ''` would turn what a person asked for as their own endpoint into the CLI's
  // default routing, under a name they chose for something else. (gemini, C6's code round.)
  assert.equal(endpointAnswer('mistral', ''), undefined);
  assert.equal(endpointAnswer('mistral', '   '), undefined);
});

test('a dismissed box writes nothing — the SECOND one as much as the first', () => {
  // A person who typed a name and then closed the URL box has consented to no more than one who
  // closed the first, and an endpoint minted from a name alone would have nowhere to reach.
  assert.equal(endpointAnswer(undefined, undefined), undefined);
  assert.equal(endpointAnswer('mistral', undefined), undefined);
  assert.equal(endpointAnswer(undefined, 'https://api.mistral.ai/v1'), undefined);
  assert.equal(endpointAnswer('  ', 'https://api.mistral.ai/v1'), undefined, 'a name that keys no vault entry is no name');
  assert.deepEqual(endpointAnswer('My Mistral!', '  https://api.mistral.ai/v1  '),
    { id: 'my-mistral', baseUrl: 'https://api.mistral.ai/v1' });
});

test('an emptied prompt box removes the override rather than writing a prompt that says nothing', () => {
  assert.deepEqual(consultPromptWrite(''), { kind: 'remove' });
  assert.deepEqual(consultPromptWrite('  \n  '), { kind: 'remove' });
  assert.deepEqual(consultPromptWrite(undefined), { kind: 'remove' });
  // Kept verbatim: a prompt's trailing blank line is the author's, and the server composes its own
  // sections after it.
  assert.deepEqual(consultPromptWrite('Answer briefly.\n\n'), { kind: 'write', text: 'Answer briefly.\n\n' });
});
