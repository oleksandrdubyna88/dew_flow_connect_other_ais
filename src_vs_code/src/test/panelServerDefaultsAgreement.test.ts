import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import path from 'node:path';
import { DEFAULTS, envBlock } from '../settingsShape';
import { CALLER_KINDS, CONSULTING_RUNTIMES, DEFAULT_CONSULT } from '../consultSettings';
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

test('every role default in the panel is the number the server falls back to', () => {
  for (const role of ROLES) {
    const server = serverDefault(role.stage === 'plan' ? 'PlanDefault' : 'CodeDefault');

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
  for (const { id } of CALLER_KINDS) {
    // `[CallerIdentity.Claude] = new("codex"),` — the kind is the C# constant's name, capitalised.
    const kind = id.charAt(0).toUpperCase() + id.slice(1);
    const m = new RegExp(`CallerIdentity[.]${kind}\\][^"]*"([a-z-]+)"`).exec(routing);

    assert.ok(m, id + ' is not in ConsultantRouting.Shipped — a caller the panel offers and the server does not route');
    assert.strictEqual(
      DEFAULT_CONSULT.byCaller[id]!.vendor,
      m[1],
      id + ': the panel shows ' + DEFAULT_CONSULT.byCaller[id]!.vendor + ', the server would ask ' + m[1],
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

test('a pristine panel writes no consult key either', () => {
  assert.deepStrictEqual(
    Object.keys(envBlock(DEFAULTS)).filter((k) => k.startsWith('COAI_CONSULT')),
    [],
    'a default panel wrote a consult key, so the two halves disagree about what the default is');
});
