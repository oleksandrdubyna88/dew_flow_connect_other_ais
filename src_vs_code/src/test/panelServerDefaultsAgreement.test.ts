import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import path from 'node:path';
import { DEFAULTS, envBlock } from '../settingsShape';
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
function serverDefault(name: string): { readonly rounds: number; readonly threshold: number } {
  const cs = fs.readFileSync(sessionState, 'utf8');
  const pattern = new RegExp('RoleGate\\s+' + name + '\\s*=\\s*new\\((\\d+),\\s*(\\d+)\\)');
  const m = pattern.exec(cs);

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
