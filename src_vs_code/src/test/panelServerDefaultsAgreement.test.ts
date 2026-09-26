import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import path from 'node:path';
import { DEFAULTS, envBlock, settingsFrom } from '../settingsShape';
import { CALLER_KINDS, CONSULTING_RUNTIMES, DEFAULT_CONSULT } from '../consultSettings';
import { vendorsFrom } from '../vendors';
import { RUNTIMES } from '../models';
import { CONSULT_PROMPT_PATH } from '../consultPrompt';
import { RUNNING_STATUSES } from '../consultations';
import { ROLES } from '../prompts';

/**
 * The gate a person is shown in a pristine panel is the gate the server actually runs.
 *
 * <p><b>Why this is a test and not a comment.</b> `envBlock` writes a round or threshold key ONLY
 * when it differs from {@link DEFAULTS} — so that returning a control to its default removes the
 * key rather than pinning a stale value. That design is correct, and it has one precondition
 * nothing was checking: the panel's default and the server's fallback must be the SAME NUMBER.
 * The moment they diverge, a pristine configuration writes no key, the server falls back to its
 * own number, and the panel displays a budget nobody is running.</p>
 *
 * <p>It diverged. `feat(gate): the shipped defaults are the budget this project actually runs on`
 * moved every round and threshold in the panel and in the VS Code manifest — and left
 * `PanelConfig.PlanDefault` / `CodeDefault` in C# untouched. A new install read `1 round,
 * threshold 6` off the panel and ran three rounds at threshold 2. The commit's own subject was the
 * thing that stopped being true.</p>
 *
 * <p>So this reads the C# rather than transcribing it. A hand-copied constant is a third place to
 * forget; the file is the source, and the match fails loudly if its shape changes.</p>
 */

const sessionState = path.join(__dirname, '..', '..', '..', 'src_mcp', 'core', 'Rounds', 'SessionState.cs');

/** `public static readonly RoleGate PlanDefault = new(3, 2);` -> `{ rounds: 3, threshold: 2 }`. */
/** Read once. The loop below asks for a default per role and they come from one file. */
const sessionStateSource = fs.readFileSync(sessionState, 'utf8');

function serverDefault(name: string): { readonly rounds: number; readonly threshold: number } {
  const pattern = new RegExp('RoleGate\\s+' + name + '\\s*=\\s*new\\((\\d+),\\s*(\\d+)\\)');
  const m = pattern.exec(sessionStateSource);

  assert.ok(m, name + ' not found in SessionState.cs — the shape this test reads has changed');
  return { rounds: Number(m[1]), threshold: Number(m[2]) };
}

/** Which C# constant a panel bucket falls back to — the third arm since the feature stage (S2.1). */
function defaultNameFor(stage: string): string {
  switch (stage) {
    case 'plan':
      return 'PlanDefault';
    case 'feature':
      return 'FeatureDefault';
    default:
      return 'CodeDefault';
  }
}

test('every role default in the panel is the number the server falls back to', () => {
  for (const role of ROLES) {
    const server = serverDefault(defaultNameFor(role.stage));

    assert.strictEqual(DEFAULTS.rounds[role.id], server.rounds,
      role.id + ': the panel shows ' + DEFAULTS.rounds[role.id] + ' rounds, the server would run ' + server.rounds);
    assert.strictEqual(DEFAULTS.thresholds[role.id], server.threshold,
      role.id + ': the panel shows threshold ' + DEFAULTS.thresholds[role.id]
      + ', the server would use ' + server.threshold);
  }
});

test('a pristine panel writes no round or threshold key at all', () => {
  // The other half of the same rule, from the direction a user meets it: touching nothing produces
  // an env block with nothing in it, which is only safe while the test above holds.
  const env = envBlock(DEFAULTS);

  assert.deepStrictEqual(
    Object.keys(env).filter((k) => k.startsWith('COAI_ROUNDS_') || k.startsWith('COAI_THRESHOLD_')),
    [],
    'a default panel wrote a gate key, so the two halves disagree about what the default is');
});

/**
 * The consultant, held to the same rule by the same reasoning.
 *
 * <p>Five settings cross this seam and the panel writes each one only when it DIFFERS, so a
 * pristine install sends nothing and the server's own fallback is what runs. Every number below is
 * read out of the C# rather than transcribed: a hand-copied constant is a third place to forget,
 * and the failure it produces is silent — the panel says one thing, the server does another, and a
 * consultation goes to a vendor nobody picked.</p>
 */
const mcp = (...parts: string[]): string => path.join(__dirname, '..', '..', '..', 'src_mcp', ...parts);

const panelSettings = fs.readFileSync(mcp('src', 'Server', 'PanelSettings.cs'), 'utf8');
const routing = fs.readFileSync(mcp('src', 'Server', 'Consultation', 'ConsultantRouting.cs'), 'utf8');
const resolution = fs.readFileSync(mcp('runners', 'Consultation', 'ConsultantResolution.cs'), 'utf8');

/** `IntVar(env, "COAI_CONSULT_TURNS", 5)` -> 5. The fallback the server uses with nothing set. */
function serverFallback(key: string): number {
  const m = new RegExp(`"${key}",\\s*(\\d+)`).exec(panelSettings);

  assert.ok(m, key + ' is not read in PanelSettings.cs — the shape this test reads has changed');

  return Number(m[1]);
}

test("every consult default in the panel is the server's own fallback", () => {
  assert.strictEqual(DEFAULT_CONSULT.turns, serverFallback('COAI_CONSULT_TURNS'));
  assert.strictEqual(DEFAULT_CONSULT.callsPerSession, serverFallback('COAI_CONSULT_CALLS_PER_SESSION'));
  assert.strictEqual(DEFAULT_CONSULT.idleMinutes, serverFallback('COAI_CONSULT_IDLE_MINUTES'));
  // On unless somebody switched it off, on BOTH sides: the server reads this key through the
  // not-switched-off parser, which is the shape of "absent means on".
  assert.strictEqual(DEFAULT_CONSULT.enabled, true);
  assert.match(panelSettings, /NotSwitchedOff\(env, "COAI_CONSULT_ENABLED"\)/);
});

test('the shipped consultant for every caller kind is the one the server would choose', () => {
  // `[CallerIdentity.Claude] = new("codex"),` — the kind is the C# constant's name, capitalised.
  const shipped = new Map(
    [...routing.matchAll(/CallerIdentity\.([A-Za-z]+)\][^"]*"([a-z-]+)"/g)]
      .map((one) => [one[1]!.toLowerCase(), one[2]!]),
  );

  // BOTH directions. The one-way walk was green for a kind the server routes and the panel has no
  // row for — which is the worse half: the server would consult on that caller's behalf and nothing
  // in the panel could say who, or change it. (codex, this story's code round.)
  assert.deepStrictEqual(
    [...shipped.keys()].sort(),
    CALLER_KINDS.map(({ id }) => id).sort(),
    'the panel and the server disagree about which callers EXIST',
  );

  // The STORED pair is the contract: `Shipped` is what the server holds before it resolves anything,
  // and `byCaller` is that pair already resolved against no rows.
  for (const { id } of CALLER_KINDS) {
    assert.strictEqual(
      DEFAULT_CONSULT.stored[id]!.vendor,
      shipped.get(id),
      id + ': the panel shows ' + DEFAULT_CONSULT.stored[id]!.vendor + ', the server would ask ' + shipped.get(id),
    );
  }
});

test('the runtimes the picker offers are the runtimes the server can resolve', () => {
  // The extension cannot ask the server which they are — the picker has to be drawable before the
  // server is installed — so it holds a copy, and a copy needs this.
  const m = /Consulting[^=]*=[^"]*((?:"[a-z]+",?[ ]*)+)/.exec(resolution);

  assert.ok(m, 'ConsultantResolution.Consulting is not in the shape this test reads');
  assert.deepStrictEqual(
    [...(m[1] ?? '').matchAll(/"([a-z]+)"/g)].map((one) => one[1]),
    [...CONSULTING_RUNTIMES],
    'the panel would offer a vendor the server cannot consult with, or hide one it can',
  );
});

/**
 * The prompt override is a PATH two halves derive independently, which is the other way this seam
 * breaks silently: the panel reports a saved prompt, the server reads somewhere else, and every
 * consultation runs on the shipped words with nothing anywhere saying so. Raised on the plan round.
 * The live half of it cannot be observed without spending a vendor turn — the prompt reaches the
 * consultant and not the reply — so what is checked is the agreement itself.
 */
test('the panel writes the prompt override where the server looks for it', () => {
  const prompts = fs.readFileSync(mcp('src', 'Server', 'RolePrompts.cs'), 'utf8');

  const directory = /OverrideDir\s*=>\s*Path\.Combine\(dataDir,\s*"([a-z]+)"\)/.exec(prompts);
  const file = /FileOf\(string promptId\)\s*=>\s*\$"\{FileName\.Safe\([^)]*\)\}\.md"/.exec(prompts);

  assert.ok(directory, 'RolePrompts.OverrideDir is not in the shape this test reads');
  assert.ok(file, 'RolePrompts.FileOf is not in the shape this test reads — the extension assumes <id>.md');
  assert.deepStrictEqual(
    [...CONSULT_PROMPT_PATH],
    [directory[1], 'consult.md'],
    'the panel would write the consultant a prompt the server never reads',
  );
});

/**
 * The states the sidebar draws are the states the server writes.
 *
 * <p>`RUNNING_STATUSES` is the extension's copy of `ConsultationStatuses`, and without this the panel
 * would quietly stop drawing a state the server had started writing — a consultation running with no
 * card, which looks exactly like a consultation that is not running. Raised on the code round as an
 * unversioned duplicated protocol; taken the way the runtimes and the caller kinds were taken, by
 * reading the C#, because there is one writer and a version number would be ceremony.</p>
 */
test('the states the sidebar shows are the states the server can write', () => {
  const statuses = fs.readFileSync(mcp('src', 'Server', 'Consultation', 'ConsultationRecord.cs'), 'utf8');
  const declared = [...statuses.matchAll(/public const string [A-Za-z]+ = "([a-z]+)";/g)].map((one) => one[1]!);

  // Every state, so a new one added in C# fails here rather than going undrawn.
  assert.deepStrictEqual(
    declared.sort(),
    ['asking', 'closed', 'failed', 'interrupted', 'open'].sort(),
    'the server writes a consultation state this panel has never heard of',
  );
  // And the ones the sidebar shows are exactly the ones that are not over.
  assert.deepStrictEqual(
    [...RUNNING_STATUSES].sort(),
    ['asking', 'interrupted', 'open'].sort(),
  );
});

test('a pristine panel writes no consult key either', () => {
  assert.deepStrictEqual(
    Object.keys(envBlock(DEFAULTS)).filter((k) => k.startsWith('COAI_CONSULT')),
    [],
    'a default panel wrote a consult key, so the two halves disagree about what the default is');
});

/**
 * The shipped consultant map has a THIRD copy, and it is the one no test was reading.
 *
 * <p>`ConsultantRouting.Shipped` in C# and `DEFAULT_CONSULT` in TypeScript are held level by the test
 * above. The manifest's `coai.consultants.default` is those four pairs again — what VS Code shows in
 * the settings editor, and what it restores when somebody presses the revert arrow. A schema edit
 * changing one pair there would leave both other copies green while a reset chose a different
 * consultant. This family has already paid for a decision living in three places and being updated in
 * two. (codex, A2's plan round.)</p>
 */
/**
 * The manifest's declared default for one setting, read without telling the compiler to stop looking.
 *
 * <p>An `as` on `JSON.parse` would compile for ever against a manifest that had moved underneath it —
 * the shape would be asserted, never checked. This walks the three steps and FAILS NAMING the one
 * that is missing, so a renamed `contributes` section is a red test that says which section rather
 * than an undefined that reads as "the default changed". (codex, A2's code round.)</p>
 */
function declaredProperty(setting: string, ...rest: readonly string[]): unknown {
  const manifest: unknown = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
  const step = (value: unknown, key: string): unknown => {
    assert.ok(typeof value === 'object' && value !== null && key in value, `the manifest has no ${key}`);

    return (value as Record<string, unknown>)[key];
  };

  return ['contributes', 'configuration', 'properties', setting, ...rest].reduce(step, manifest);
}

function declaredDefault(setting: string): unknown {
  return declaredProperty(setting, 'default');
}

/**
 * Every runtime a consultant row can HOLD is a runtime the settings editor accepts.
 *
 * <p>The enum was written from the four runtimes that may CONSULT, which is a different question from
 * what may be stored. Rule (a) of `resolveConsultant` lends a reviewer row's runtime whatever it is —
 * materialising is not permitting — so a legacy entry naming a retired `gemini` row, or a Team-server
 * `remote` one, materialises into a stored `runtime` this build then refuses BY NAME when a
 * consultation is asked for. Refused at the consultation is right; marked invalid in a person's own
 * settings file is not. Derived from `RUNTIMES` so a seventh runtime cannot be added to the product
 * and forgotten here. (CodeRabbit, on PR #262.)</p>
 */
test('the settings editor accepts every runtime a consultant row can hold', () => {
  const declared = declaredProperty('coai.consultants', 'additionalProperties', 'properties', 'runtime', 'enum');

  assert.deepStrictEqual(
    [...(declared as string[])].sort(),
    [...RUNTIMES, ''].sort(),
    'a runtime the reader accepts is flagged as invalid by the manifest, or one it cannot hold is offered',
  );
});

test('the manifest ships the same four consultant pairs as the code', () => {
  assert.deepStrictEqual(
    declaredDefault('coai.consultants'),
    Object.fromEntries(CALLER_KINDS.map(({ id }) => [id, {
      vendor: DEFAULT_CONSULT.stored[id]!.vendor,
      model: DEFAULT_CONSULT.stored[id]!.model,
    }])),
    'the settings editor would restore a consultant map the code does not ship',
  );
});

/**
 * The same contract from the direction the reader meets it since a legacy entry resolves on read.
 *
 * <p>`DEFAULTS` is a value that never went through `consultSettingsFrom`; a real panel's settings did,
 * and its four shipped pairs came back as four DEFINITIONS carrying whatever the reviewer rows hold.
 * Compared field by field against the shipped pairs those are "different", and an install where
 * nobody configured a consultant would start writing `COAI_CONSULTANTS` — while the server, with no
 * key, resolves the same absence to the same consultant. The decision reads what is STORED.</p>
 */
test('a pristine consultant map writes no key after it resolves against customised reviewer rows', () => {
  const stored: Record<string, unknown> = { vendors: [{ id: 'codex', runtime: 'codex', model: 'gpt-5.6-luna' }] };
  const settings = settingsFrom((section) => stored[section]);

  assert.strictEqual(settings.consult.byCaller['claude']!.model, 'gpt-5.6-luna', 'the premise: the read resolved the pair');
  assert.deepStrictEqual(
    Object.keys(envBlock(settings, vendorsFrom(stored['vendors']))).filter((k) => k.startsWith('COAI_CONSULT')),
    [],
    'a panel nobody configured wrote a consult key, so the two halves disagree about what the default is');
});

/**
 * The wire entry's keys are the DTO's parameters — read from the C#, since story B4 put the
 * definition on the wire.
 *
 * <p>Two implementations of one wire shape, held level the way the rest of this file holds them: by
 * reading the other half's source rather than retyping its names. The failure this guards is the one
 * B4 measured against the released server — `System.Text.Json` SKIPS a member the DTO does not
 * declare, silently, so a key spelled `baseURL` here would be a definition whose endpoint never
 * arrives, and the consultation would run on the CLI's own endpoint with nothing anywhere saying so.
 * The comparison is on the LIST, order included: `envBlock` writes the keys in the DTO's declared
 * order, and a list is what makes a swapped or missing name a red test that says which.</p>
 */
const consultantDto = /record ConsultantDto\(([\s\S]*?)\);/.exec(
  fs.readFileSync(mcp('src', 'Server', 'SettingsJsonContext.cs'), 'utf8'),
);

/** `string? Vendor,` / `string? Model = null` -> `vendor`, `model`: the wire spelling of each parameter. */
function dtoWireNames(): readonly string[] {
  assert.ok(consultantDto, 'ConsultantDto is not in the shape this test reads');

  return [...consultantDto[1]!.matchAll(/string\?\s+([A-Za-z]+)/g)]
    .map((one) => one[1]!)
    .map((name) => name.charAt(0).toLowerCase() + name.slice(1));
}

test("a definition on the wire carries exactly the DTO's properties, in the DTO's order", () => {
  // A stored DEFINITION, so nothing is borrowed from a row and every one of the five fields is the
  // entry's own — the shape a person's first edit in the section writes since story A2.
  const stored: Record<string, unknown> = {
    consultants: { claude: { vendor: 'claude', runtime: 'claude', model: 'opus', baseUrl: '', executablePath: '/opt/claude' } },
  };
  const wire = envBlock(settingsFrom((section) => stored[section]))['COAI_CONSULTANTS'];

  assert.ok(wire !== undefined, 'the premise: a definition is on the wire');
  assert.deepStrictEqual(
    Object.keys((JSON.parse(wire) as Record<string, object>)['claude']!),
    dtoWireNames(),
    "the panel writes a key the server's DTO does not declare — System.Text.Json would drop it silently",
  );
});

test('an unavailable entry on the wire names only DTO properties too, and never the runtime', () => {
  // The raw shape is a SUBSET, and `runtime` must not be in it: rule (c) is the server refusing by
  // name, and a runtime the panel invented would turn the refusal into a launch.
  const stored: Record<string, unknown> = { consultants: { other: { vendor: 'mistral', model: 'large' } } };
  const wire = envBlock(settingsFrom((section) => stored[section]))['COAI_CONSULTANTS'];

  assert.ok(wire !== undefined, 'the premise: the entry is on the wire');
  const keys = Object.keys((JSON.parse(wire) as Record<string, object>)['other']!);
  assert.ok(keys.every((key) => dtoWireNames().includes(key)), `a key the DTO does not declare: ${keys.join(', ')}`);
  assert.ok(!keys.includes('runtime'), 'an unavailable entry travelled with a runtime');
  assert.deepStrictEqual(keys.sort(), ['model', 'vendor']);
});
