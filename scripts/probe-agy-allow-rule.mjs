// Probe: does a headless agy honour a GLOBAL permissions.allow rule for a shell command?
// Results: research/RESULTS_agy_allow_rule.md. PAID: every arm is one real agy turn.
//
//   node scripts/probe-agy-allow-rule.mjs <agy> <global settings.json> <scratch dir> <model> <out.json> '<arms>'
//   e.g. '[[],["command(git grep)"],["command(git grep -n QUOKKA .)"]]'   ([] = no rule)
//
// Each arm writes one allow list into the settings file (its other keys kept) and runs one turn with the
// consultant's own flags. The file is copied aside first and restored after the run; the restore is
// verified by SHA-256, and the exit code is 2 when it does not match.
import { spawnSync, execFileSync } from 'node:child_process';
import { mkdtempSync, writeFileSync, readFileSync, copyFileSync, existsSync, unlinkSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join } from 'node:path';
import os from 'node:os';

const [exe, settings, scratchRoot, model = 'gemini-3.1-pro-high', out, armsJson = '[]'] = process.argv.slice(2);
if (!exe || !settings || !scratchRoot || !out) {
  console.error('usage: probe-agy-allow-rule.mjs <agy> <settings.json> <scratch dir> <model> <out.json> <arms json>');
  process.exit(65);
}

const sha = (p) => createHash('sha256').update(readFileSync(p)).digest('hex');
const repo = mkdtempSync(join(scratchRoot, 'coai-agyprobe-'));
execFileSync('git', ['init', '-q'], { cwd: repo });
writeFileSync(join(repo, 'notes.txt'), 'nothing here\n');
writeFileSync(join(repo, 'deep.txt'), 'the marker is QUOKKA-7731\n');
execFileSync('git', ['add', '.'], { cwd: repo });
execFileSync('git', ['-c', 'user.name=probe', '-c', 'user.email=probe@example.invalid', 'commit', '-qm', 'probe'], { cwd: repo });

const prompt = 'Run exactly this shell command with run_command: git grep -n QUOKKA . '
  + 'Then reply with ONLY the file name it printed. Do not use any other tool.';
const stdin = JSON.stringify({ event: 'user', message: { role: 'user', content: prompt } }) + '\n';

function arm(name) {
  const t0 = Date.now();
  const r = spawnSync(exe, ['--print=', '--input-format', 'stream-json', '--output-format', 'stream-json',
    '--mode', 'plan', '--model', model, '--add-dir', repo], { cwd: repo, input: stdin, encoding: 'utf8', timeout: 240000 });
  let result = null;
  const commands = [];
  for (const line of (r.stdout ?? '').split('\n').filter(Boolean)) {
    try {
      const event = JSON.parse(line);
      if (event.event === 'result') result = event.result;
      const step = event.step_update;
      if (step?.tool_name === 'run_command') {
        commands.push({ state: step.state, cmd: step.tool_info?.parameters?.CommandLine, error: step.tool_info?.error?.message ?? null });
      }
    } catch { /* prose or a half-written line */ }
  }
  return { name, exit: r.status, seconds: (Date.now() - t0) / 1000, response: result?.response ?? null,
    status: result?.status ?? null, denied: result?.denied_actions ?? null, commands, stderr: (r.stderr ?? '').slice(0, 600) };
}

const backup = settings + '.coai-probe-backup';
const before = sha(settings);
copyFileSync(settings, backup);
const results = { at: new Date().toISOString(), platform: os.platform(), exe, model, settingsSha: before, arms: [] };
try {
  for (const rule of JSON.parse(armsJson)) {
    const parsed = JSON.parse(readFileSync(backup, 'utf8'));
    parsed.permissions = { allow: rule };
    writeFileSync(settings, JSON.stringify(parsed, null, 2));
    results.arms.push(arm('allow:' + JSON.stringify(rule)));
  }
} finally {
  copyFileSync(backup, settings);
  results.restoredSha = sha(settings);
  results.restoredOk = results.restoredSha === before;
  if (results.restoredOk) unlinkSync(backup); // kept when the restore failed: it is then the only good copy
}
writeFileSync(out, JSON.stringify(results, null, 2));
console.log(JSON.stringify(results, null, 2));
if (!existsSync(settings) || !results.restoredOk) process.exitCode = 2;
