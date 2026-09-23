/**
 * The live check for AGENT mode (issue #289): one real conversation per vendor, launched through this
 * build's own adapters with `access: 'agent'`, in a scratch workspace, asked to do the four things the
 * box promises — and each one checked on the DISK or in the answer, never taken on the model's word.
 *
 * <p>Like `live-chat.mjs` this is a script rather than a test: every CLI needs a signed-in account and
 * each run spends real turns. It is in the repository because an argv test proves what we SEND, not
 * what a vendor DOES with it, and the next change to an agent flag deserves the same check.</p>
 *
 * <p><b>The four steps:</b> create a file in the workspace; translate it into another file; read a
 * file OUTSIDE the workspace (a token planted in a second temp folder); run `git --version`. The last
 * two are what "full access to this computer" means, and a check inside the folder would not prove
 * it (codex, the plan round).</p>
 *
 * <p>Run it as `node scripts/live-agent.mjs` after `npm run compile`, or with one vendor's name. Exit
 * code 0 means every vendor asked did all four.</p>
 */
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
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
const { NEW_CONVERSATION } = await import(`${OUT}chatAdapter.js`);
const { AGENT_BUDGETS } = await import(`${OUT}chatSession.js`);
const { resolvedExecutable } = await import(`${OUT}versionProbe.js`);

// The file is `note.txt`, not `token.txt`: codex refused to read a file by that name "since it may contain a secret" — a refusal
// about the NAME, measured on the first run, not about access.
const TOKEN = 'COAI-OUTSIDE-4417';

const row = (id, runtime) => ({
  id, runtime, model: '', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
});

function steps(workspace, outside) {
  return [
    {
      name: 'create',
      ask: 'Create a file named hello.txt in the current directory containing exactly the line: Hello, world. Reply DONE when it exists.',
      held: () => existsSync(join(workspace, 'hello.txt')),
    },
    {
      name: 'translate',
      ask: 'Translate the contents of hello.txt into Ukrainian and write the translation to hello.uk.txt in the same directory. Reply DONE.',
      held: () => existsSync(join(workspace, 'hello.uk.txt'))
        && readFileSync(join(workspace, 'hello.uk.txt'), 'utf8').trim().length > 0,
    },
    {
      name: 'outside',
      ask: `Read the file ${join(outside, 'note.txt')} and reply with its first line and nothing else.`,
      held: (answer) => answer.includes(TOKEN),
    },
    {
      name: 'command',
      ask: 'Run the shell command `git --version` and reply with its output and nothing else.',
      held: (answer) => /git version \d/.test(answer),
    },
  ];
}

async function conversation(name, vendor, adapter) {
  // Resolved FIRST, so a vendor that is not installed leaves no scratch folders behind. (codex, the
  // code round.)
  const resolved = await resolvedExecutable({ antigravity: 'agy', claude: 'claude', codex: 'codex' }[vendor.runtime]);
  if (!resolved) { console.log(`${name.padEnd(8)} NOT INSTALLED`); return false; }
  const home = mkdtempSync(join(tmpdir(), 'coai-agent-home-'));
  const workspace = mkdtempSync(join(tmpdir(), 'coai-agent-ws-'));
  const outside = mkdtempSync(join(tmpdir(), 'coai-agent-outside-'));
  writeFileSync(join(outside, 'note.txt'), `${TOKEN}\n`);
  const session = new CliChatSession(
    (resume) => {
      const spec = launchSpecFor(vendor, home, { ...NEW_CONVERSATION, resume, access: 'agent' }, resolved, undefined, workspace);
      if (spec.refusal) { throw new Error(spec.refusal); }

      return launch(spec.executable, spec.args, { cwd: spec.cwd, shell: spec.shell, env: spec.env });
    },
    AGENT_BUDGETS,
    undefined,
    adapter,
  );
  let all = true;
  for (const step of steps(workspace, outside)) {
    const started = Date.now();
    const turn = await session.send(step.ask);
    const answer = turn.ok ? turn.answer : `FAILED: ${turn.failure}`;
    const held = turn.ok && step.held(answer);
    all = all && held;
    console.log(`${name.padEnd(8)} ${step.name.padEnd(10)} ${held ? 'HELD  ' : 'FAILED'} ${((Date.now() - started) / 1000).toFixed(1)}s  ${answer.replace(/\s+/g, ' ').slice(0, 90)}`);
  }
  session.dispose();
  for (const dir of [home, workspace, outside]) {
    try { rmSync(dir, { recursive: true, force: true }); } catch (reason) { console.log(`  (${dir} stayed: ${reason?.code ?? reason})`); }
  }

  return all;
}

const VENDORS = [
  ['agy', row('antigravity', 'antigravity'), agyAdapter],
  ['claude', row('claude', 'claude'), claudeAdapter],
  ['codex', row('codex', 'codex'), codexAdapter],
];

const which = process.argv[2] ?? 'all';
const chosen = VENDORS.filter(([name]) => which === 'all' || which === name);
if (chosen.length === 0) {
  console.log(`no such vendor: ${which}. Try one of: all, ${VENDORS.map(([name]) => name).join(', ')}`);
  process.exit(2);
}
console.log(`conditions: vendors=${chosen.map(([name]) => name).join(',')} node=${process.version} platform=${process.platform}`);

let ok = true;
for (const [name, vendor, adapter] of chosen) {
  try {
    ok = (await conversation(name, vendor, adapter)) && ok;
  } catch (reason) {
    ok = false;
    console.log(`${name.padEnd(8)} THREW: ${reason?.message ?? reason}`);
  }
}
console.log(ok ? 'EVERY VENDOR DID ALL FOUR' : 'SOMETHING DID NOT');
process.exit(ok ? 0 : 1);
