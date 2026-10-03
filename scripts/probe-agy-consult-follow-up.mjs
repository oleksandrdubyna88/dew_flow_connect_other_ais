// Probe: (a) which declared read tools a headless agy runs without a prompt, and (b) whether a denied
// consultation, continued ONCE in prose on the same conversation, answers. PAID: every turn is a real agy turn.
// Results: research/RESULTS_agy_consult_follow_up.md. The person's agy settings are never touched.
//
//   node scripts/probe-agy-consult-follow-up.mjs <agy> <scratch dir> <model> <out.json> <repeats> '<follow-up text>'
//
// Arm "tools": one turn asking for grep_search, find_by_name and list_dir — which ran, which were denied.
// Arm "follow-up" (x repeats): turn 1 forces run_command (denied headless); turn 2 resumes the conversation
// with the follow-up text. Raw streams are kept beside the JSON so a fixture can be cut from them.
import { spawnSync, execFileSync } from 'node:child_process';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import os from 'node:os';
import { resolved } from '../.github/scripts/lib/resolved.mjs';
// git is resolved once rather than named to the spawner (S4036): the probe uses the operator's own git, visibly.
const GIT = resolved('git');

const [exe, scratchRoot, model = 'gemini-3.1-pro-high', out, repeatsArg = '3', followUp] = process.argv.slice(2);
if (!exe || !scratchRoot || !out || !followUp) {
  console.error('usage: probe-agy-consult-follow-up.mjs <agy> <scratch dir> <model> <out.json> <repeats> <follow-up text>');
  process.exit(65);
}
const repeats = Number.parseInt(repeatsArg, 10);

const repo = mkdtempSync(join(scratchRoot, 'coai-agyfollow-'));
const git = (...args) => execFileSync(GIT, ['-c', 'user.name=probe', '-c', 'user.email=probe@example.invalid',
  '-c', 'commit.gpgsign=false', ...args], { cwd: repo });
git('init', '-q');
writeFileSync(join(repo, 'notes.txt'), 'nothing here\n');
writeFileSync(join(repo, 'deep.txt'), 'the marker is QUOKKA-7731\n');
git('add', '.');
git('commit', '-qm', 'probe');

const message = (text) => JSON.stringify({ event: 'user', message: { role: 'user', content: text } }) + '\n';

function turn(text, conversation) {
  const t0 = Date.now();
  const args = ['--print=', '--input-format', 'stream-json', '--output-format', 'stream-json', '--mode', 'plan',
    ...(conversation ? ['--conversation', conversation] : []), '--model', model, '--add-dir', repo];
  const r = spawnSync(exe, args, { cwd: repo, input: message(text), encoding: 'utf8', timeout: 240000 });
  let result = null;
  let id = '';
  const tools = [];
  for (const line of (r.stdout ?? '').split('\n').filter(Boolean)) {
    try {
      const event = JSON.parse(line);
      const found = JSON.stringify(event).match(/"conversation_id":"([A-Za-z0-9_-]+)"/);
      if (found) id = found[1];
      if (event.event === 'result') result = event.result;
      const step = event.step_update;
      if (step?.step_type === 'tool' && step.tool_name && step.state !== 'ACTIVE') {
        tools.push({ tool: step.tool_name, state: step.state, error: step.tool_info?.error?.message?.slice(0, 160) ?? null });
      }
    } catch { /* prose or a half-written line */ }
  }
  return { exit: r.status, seconds: (Date.now() - t0) / 1000, conversation: id, response: result?.response ?? null,
    denied: result?.denied_actions ?? null, usage: result?.usage ?? null, tools,
    stdout: r.stdout ?? '', stderr: (r.stderr ?? '').slice(0, 800) };
}

const results = { at: new Date().toISOString(), platform: os.platform(), exe, model, followUp, tools: null, followUps: [] };

results.tools = turn('Use ONLY these tools, in this order: grep_search for the text QUOKKA in this directory, '
  + 'find_by_name for deep.txt, list_dir on this directory. Then reply with the name of the file that contains '
  + 'QUOKKA. Do not use run_command or view_file.');

for (let i = 0; i < repeats; i++) {
  const first = turn('Run exactly this shell command with run_command: git grep -n QUOKKA . '
    + 'Then tell me which file contains the marker and what the marker is.');
  const second = first.conversation ? turn(followUp, first.conversation) : null;
  results.followUps.push({ first, second });
}

writeFileSync(out, JSON.stringify(results, null, 2));
const brief = (t) => t && { exit: t.exit, seconds: t.seconds, response: t.response, denied: t.denied,
  tools: t.tools.map((x) => `${x.tool}:${x.state}`).join(','), usage: t.usage };
console.log(JSON.stringify({ platform: results.platform, tools: brief(results.tools),
  followUps: results.followUps.map((f) => ({ first: brief(f.first), second: brief(f.second) })) }, null, 2));
