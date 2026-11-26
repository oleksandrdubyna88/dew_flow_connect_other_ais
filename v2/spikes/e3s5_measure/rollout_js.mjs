// usage: rollout_js.mjs <CODEX_HOME> <threadId> : prints the JavaScript calls (first 200 chars) and the output text lines of the exec tool for a thread, from its rollout file
import fs from 'node:fs'; import path from 'node:path';
const [home, tid] = process.argv.slice(2);
const walk = d => fs.readdirSync(d, { withFileTypes: true }).flatMap(e => e.isDirectory() ? walk(path.join(d, e.name)) : [path.join(d, e.name)]);
const f = walk(home + '/sessions').find(p => p.endsWith(tid + '.jsonl'));
const L = fs.readFileSync(f, 'utf8').split('\n').filter(Boolean).map(l => JSON.parse(l));
for (const e of L) { const p = e.payload || {};
  if (p.type === 'custom_tool_call') console.log('=== JS CALL (' + String(p.input).length + ' chars)');
  if (p.type === 'custom_tool_call_output') { const o = typeof p.output === 'string' ? p.output : (p.output || []).map(x => x.text || x.type).join('\n'); console.log('=== OUTPUT\n' + o); } }
