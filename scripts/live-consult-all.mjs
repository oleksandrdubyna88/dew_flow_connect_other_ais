// The consultant's live check: every consultant, two turns each, the second depending on the first,
// with a number planted in turn 1 — the harness shape this family already uses for vendor
// conversations (src_vs_code/scripts/live-chat.mjs is its sibling).
//
// It spends REAL vendor turns on REAL accounts, so it is run by hand and never in CI. Its results
// belong in research/module_server.md with the subject sha beside them.
//
//   node scripts/live-consult-all.mjs <repoPath> <coai-mcp executable> '<vendor rows as JSON>'
//
// e.g.
//   node scripts/live-consult-all.mjs D:/rsd/checkout \
//     src_mcp/src/bin/Debug/net10.0/coai-mcp.exe \
//     '[{"id":"claude","runtime":"claude","model":""}]'
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync, readFileSync, existsSync, readdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const repo = process.argv[2];
const exe = process.argv[3];
const vendors = JSON.parse(process.argv[4]);
const planted = 5417;

const run = async (vendor) => {
  const data = mkdtempSync(join(tmpdir(), `coai-live-${vendor.id}-`));
  const server = spawn(exe, [], {
    stdio: ['pipe', 'pipe', 'pipe'],
    env: {
      ...process.env,
      COAI_DATA_DIR: data,
      COAI_VENDORS: JSON.stringify([vendor]),
      COAI_CONSULTANTS: JSON.stringify({ claude: { vendor: vendor.id }, other: { vendor: vendor.id } }),
      COAI_CONSULT_TURNS: '3',
      COAI_REVIEWER_TIMEOUT_MINUTES: '5',
      COAI_LOG_LEVEL: 'warning',
    },
  });
  let buffer = '';
  const waiting = new Map();
  server.stdout.on('data', (chunk) => {
    buffer += chunk.toString('utf8');
    let at;
    while ((at = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, at).trim();
      buffer = buffer.slice(at + 1);
      if (!line) continue;
      const m = JSON.parse(line);
      if (waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
    }
  });
  const stderr = [];
  server.stderr.on('data', (c) => stderr.push(c.toString('utf8')));
  let nextId = 1;
  const call = (method, params) => new Promise((resolve) => {
    const id = nextId++;
    waiting.set(id, resolve);
    server.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
  });
  const consult = async (args) => {
    const started = Date.now();
    const reply = await call('tools/call', { name: 'consult', arguments: args });
    const text = reply.result?.content?.[0]?.text ?? JSON.stringify(reply);
    return { seconds: ((Date.now() - started) / 1000).toFixed(1), body: JSON.parse(text) };
  };

  try {
    await call('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'live', version: '0' } });
    server.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');

    const one = await consult({
      repoPath: repo,
      problem: `Before anything else, remember this number: ${planted}. Then, briefly: this repository has an MCP tool that consults another vendor's model in the live working tree. Name one risk in doing that, in one sentence.`,
      suspectedFiles: '[]',
    });
    const id = one.body.consultationId;
    const first = { seconds: one.seconds, ok: !one.body.error, error: one.body.error, id };
    if (!id) return { vendor: vendor.id, first, second: null, data, stderr };

    const two = await consult({
      repoPath: repo,
      consultationId: id,
      problem: 'I checked your point. Now: what number did I ask you to remember at the start? Reply with the number and nothing else.',
      suspectedFiles: '[]',
    });
    const advice = two.body.advice ?? '';
    return {
      vendor: vendor.id,
      first,
      second: {
        seconds: two.seconds,
        ok: !two.body.error,
        error: two.body.error,
        keptContext: advice.includes(String(planted)),
        turnIndex: two.body.turnIndex,
      },
      data,
      stderr,
    };
  } finally {
    server.kill();
  }
};

for (const vendor of vendors) {
  console.log(`\n===== ${vendor.id} (${vendor.runtime}) =====`);
  try {
    const r = await run(vendor);
    console.log(`turn1: ${r.first.ok ? 'ok' : 'REFUSED'} ${r.first.seconds}s ${r.first.error ? '\n  ' + r.first.error.slice(0, 300) : ''}`);
    if (r.second) {
      console.log(`turn2: ${r.second.ok ? 'ok' : 'REFUSED'} ${r.second.seconds}s turn=${r.second.turnIndex} contextKept=${r.second.keptContext}${r.second.error ? '\n  ' + r.second.error.slice(0, 300) : ''}`);
    }
    const ledger = join(r.data, 'usage.jsonl');
    if (existsSync(ledger)) {
      for (const line of readFileSync(ledger, 'utf8').trim().split('\n')) {
        const x = JSON.parse(line);
        console.log(`  ledger kind=${x.kind} outcome=${x.outcome} ${x.seconds}s in=${x.tokensIn} out=${x.tokensOut}`);
      }
    }
    const dir = join(r.data, 'consultations');
    if (existsSync(dir)) {
      for (const f of readdirSync(dir).filter((f) => f.endsWith('.json'))) {
        const rec = JSON.parse(readFileSync(join(dir, f), 'utf8'));
        console.log(`  record status=${rec.status} turns=${rec.turns?.length ?? 0}/${rec.maxTurns} memory=${rec.memory} handle=${rec.handle || '(none)'} alert=${rec.alert || '(none)'}`);
      }
    }
    if (!r.first.ok) console.log('  stderr:', r.stderr.join('').split('\n').slice(-3).join(' | '));
    try { rmSync(r.data, { recursive: true, force: true }); } catch { /* a held handle */ }
  } catch (e) {
    console.log('harness error:', e.message);
  }
}
