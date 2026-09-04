/**
 * Drives a real `coai-mcp` over stdio: one server process per run, exactly as a VS Code window has.
 *
 * <p>Five of these at once IS the five-window case — not a simulation of it. The protocol is the
 * product's own (`initialize` → `open` → `review_plan` → `resolve` → `review_code` → `resolve`), so
 * what this measures is the gate, not a harness's idea of it.</p>
 *
 * usage: node gate-run.mjs <exe> <dataDir> <repo> <branch> <baseRef> <planFile> <label> <outFile>
 */
import { spawn } from 'node:child_process';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname } from 'node:path';

const [exe, dataDir, repo, branch, baseRef, planFile, label, outFile] = process.argv.slice(2);

class Server {
  constructor(exePath, dir) {
    this.next = 1;
    this.pending = new Map();
    this.buffer = '';
    this.stderr = '';
    this.child = spawn(exePath, [], {
      stdio: ['pipe', 'pipe', 'pipe'],
      env: { ...process.env, COAI_DATA_DIR: dir },
    });
    this.child.stdout.setEncoding('utf8');
    this.child.stdout.on('data', (chunk) => this.onData(chunk));
    this.child.stderr.on('data', (chunk) => (this.stderr += chunk.toString()));
  }

  onData(chunk) {
    this.buffer += chunk;
    // Newline-delimited JSON: the server writes one object per line on this transport.
    for (let at = this.buffer.indexOf('\n'); at >= 0; at = this.buffer.indexOf('\n')) {
      const line = this.buffer.slice(0, at).trim();
      this.buffer = this.buffer.slice(at + 1);
      if (line.length === 0) continue;
      let message;
      try {
        message = JSON.parse(line);
      } catch {
        continue;
      }
      const waiting = this.pending.get(message.id);
      if (waiting) {
        this.pending.delete(message.id);
        waiting(message);
      }
    }
  }

  send(method, params) {
    const id = this.next++;
    return new Promise((resolve) => {
      this.pending.set(id, resolve);
      this.child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id, method, params })}\n`);
    });
  }

  notify(method, params) {
    this.child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', method, params })}\n`);
  }

  async call(name, args) {
    const started = Date.now();
    const reply = await this.send('tools/call', { name, arguments: args });
    const text = reply?.result?.content?.[0]?.text ?? '';
    let answer = null;
    try {
      answer = JSON.parse(text);
    } catch {
      answer = { error: text.slice(0, 400) || JSON.stringify(reply).slice(0, 400) };
    }

    return { seconds: Math.round((Date.now() - started) / 100) / 10, answer };
  }

  stop() {
    this.child.kill();
  }
}

/** Accept everything: this measures the gate's output, not a decision-making policy. */
const acceptAll = (answer) =>
  JSON.stringify((answer.findings ?? []).map((_, finding) => ({ finding, action: 'accept' })));

const summarise = (stage, run) => ({
  stage,
  seconds: run.seconds,
  verdict: run.answer?.verdict ?? '',
  error: run.answer?.error ?? '',
  gating: run.answer?.gatingCount ?? 0,
  findings: (run.answer?.findings ?? []).length,
  reviewers: run.answer?.reviewers ?? '',
  tokensIn: run.answer?.cost?.tokensIn ?? 0,
  tokensOut: run.answer?.cost?.tokensOut ?? 0,
  // Kept whole so usefulness can be judged by reading rather than by counting.
  detail: (run.answer?.findings ?? []).map((f) => ({
    severity: f.severity, category: f.category, file: f.file, line: f.line,
    title: f.title, why: f.why, providers: f.providers, role: f.role, gating: f.isGating,
  })),
});

const server = new Server(exe, dataDir);
const record = { label, branch, startedUtc: new Date().toISOString(), stages: [] };
try {
  await server.send('initialize', {
    protocolVersion: '2024-11-05',
    capabilities: {},
    clientInfo: { name: 'gate-run', version: '1' },
  });
  server.notify('notifications/initialized', {});

  await server.call('open', { repoPath: repo, branch });

  const planText = readFileSync(planFile, 'utf8');
  const plan = await server.call('review_plan', { repoPath: repo, branch, planText });
  record.stages.push(summarise('plan', plan));
  if (plan.answer?.verdict) {
    await server.call('resolve', { repoPath: repo, branch, decisions: acceptAll(plan.answer) });
  }

  const code = await server.call('review_code', { repoPath: repo, branch, baseRef, planText });
  record.stages.push(summarise('code', code));
  if (code.answer?.verdict) {
    await server.call('resolve', { repoPath: repo, branch, decisions: acceptAll(code.answer) });
  }
} catch (error) {
  record.harnessError = String(error).slice(0, 300);
} finally {
  record.stderrTail = server.stderr.trim().split('\n').slice(-3).join(' | ').slice(0, 300);
  record.finishedUtc = new Date().toISOString();
  server.stop();
}

mkdirSync(dirname(outFile), { recursive: true });
writeFileSync(outFile, JSON.stringify(record, null, 1));
const line = record.stages
  .map((s) => `${s.stage}: ${s.verdict || 'ERROR'} ${s.findings}f ${s.seconds}s ${s.tokensIn}/${s.tokensOut}`)
  .join('  |  ');
console.log(`${label.padEnd(28)} ${line}${record.stages.length === 0 ? ` NOTHING RAN ${record.stderrTail}` : ''}`);
