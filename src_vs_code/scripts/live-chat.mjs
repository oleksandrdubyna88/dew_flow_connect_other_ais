/**
 * The live check: one real two-turn conversation per vendor, driven through THIS build's own
 * adapters and session rather than through a hand-rolled imitation of them.
 *
 * <p>`npm test` cannot do this and should not try: no vendor is real in CI, every CLI here needs a
 * signed-in account, and every run of this script spends real turns on three of them. It is a
 * script rather than a test for exactly that reason — and it is IN the repository rather than in
 * somebody's scratch directory because it found three defects no unit test could, and the next
 * change to an adapter deserves the same check.</p>
 *
 * <p><b>What it proves:</b> the second turn asks for a number planted in the first, so a session
 * that lost the conversation answers with a shrug and the script says so. What it does NOT prove is
 * anything about the quality of an answer — only that the wire, the shapes and the context survive.</p>
 *
 * <p>Run it as `node scripts/live-chat.mjs` after `npm run compile`, or with one vendor's name to
 * spend turns on only that one. Exit code 0 means every vendor asked kept its context.</p>
 */
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { pathToFileURL } from 'node:url';
const OUT = pathToFileURL(join(process.cwd(), 'out/')).href;
const { launch } = await import(`${OUT}processLauncher.js`);
const { CliChatSession } = await import(`${OUT}cliChatSession.js`);
const { agyAdapter } = await import(`${OUT}agyAdapter.js`);
const { claudeAdapter } = await import(`${OUT}claudeAdapter.js`);
const { codexAdapter } = await import(`${OUT}codexAdapter.js`);
const { launchSpecFor } = await import(`${OUT}cliChatLaunch.js`);
const { resolvedExecutable } = await import(`${OUT}versionProbe.js`);

const BUDGETS = { startupMs: 60_000, turnMs: 240_000 };

const row = (id, runtime) => ({
  id, runtime, model: '', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
});

const NUMBER = String(Math.floor(Math.random() * 9000) + 1000);

async function conversation(name, vendor, adapter) {
  const home = mkdtempSync(join(tmpdir(), 'coai-live-'));
  const resolved = await resolvedExecutable(vendor.executablePath || { antigravity: 'agy', claude: 'claude', codex: 'codex' }[vendor.runtime]);
  if (!resolved) { console.log(name.padEnd(12) + 'NOT INSTALLED'); return false; }
  const session = new CliChatSession(
    (resume) => {
      const spec = launchSpecFor(vendor, home, resume, resolved);

      return launch(spec.executable, spec.args, { cwd: spec.cwd, shell: spec.shell });
    },
    BUDGETS,
    undefined,
    adapter,
  );

  const first = Date.now();
  const one = await session.send(
    `Remember the number ${NUMBER}. Reply with the single word OK and nothing else.`,
  );
  const firstMs = Date.now() - first;

  const second = Date.now();
  const two = await session.send('What number did I ask you to remember? Reply with the number alone.');
  const secondMs = Date.now() - second;
  session.dispose();

  const answer = two.ok ? two.answer : `FAILED: ${two.failure}`;
  const kept = two.ok && two.answer.includes(NUMBER);
  console.log(
    `${name.padEnd(12)} shape=${adapter.shape.padEnd(10)} turn1=${(firstMs / 1000).toFixed(1)}s `
    + `turn2=${(secondMs / 1000).toFixed(1)}s context=${kept ? 'KEPT' : 'LOST'} `
    + `contextLostFlag=${two.ok ? String(two.contextLost ?? false) : 'n/a'}`,
  );
  console.log(`  turn 1: ${one.ok ? one.answer.slice(0, 60) : `FAILED: ${one.failure}`}`);
  console.log(`  turn 2: ${answer.slice(0, 60)}   (planted ${NUMBER})`);

  return kept;
}

const which = process.argv[2] ?? 'all';
const all = [
  ['agy', row('antigravity', 'antigravity'), agyAdapter],
  ['claude', row('claude', 'claude'), claudeAdapter],
  ['codex', row('codex', 'codex'), codexAdapter],
].filter(([name]) => which === 'all' || which === name);

let ok = true;
for (const [name, vendor, adapter] of all) {
  try {
    ok = (await conversation(name, vendor, adapter)) && ok;
  } catch (reason) {
    ok = false;
    console.log(`${name.padEnd(12)} THREW: ${reason?.message ?? reason}`);
  }
}
console.log(ok ? 'ALL KEPT THEIR CONTEXT' : 'SOMETHING LOST IT');
process.exit(ok ? 0 : 1);
