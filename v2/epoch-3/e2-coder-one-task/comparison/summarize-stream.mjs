// usage: node summarize-stream.mjs codex|agy <stream-file> [full]   Prints events' kinds, tool uses, usage and the final message. Never prints an environment.
import fs from 'node:fs';
const [vendor, file, full] = process.argv.slice(2);
const L = fs.readFileSync(file, 'utf8').split('\n').filter(Boolean).map(l => { try { return JSON.parse(l); } catch { return { raw: l.slice(0, 200) }; } });
const home = process.env.HOME || '';
const clip = s => String(s ?? '').replace(home, '~').replace(/\s+/g, ' ').slice(0, 220);
if (vendor === 'codex') {
  for (const e of L) {
    const i = e.item;
    if (e.type === 'thread.started') console.log('THREAD', e.thread_id);
    else if (e.type === 'item.completed') {
      if (i.type === 'command_execution') console.log('CMD exit=' + i.exit_code + ' :: ' + clip(i.command) + ' => ' + clip(i.aggregated_output).slice(0, 100));
      else if (i.type === 'file_change') console.log('FILECHANGE ' + i.status + ' ' + clip(JSON.stringify(i.changes)));
      else if (i.type === 'agent_message') console.log('MSG ' + (full ? i.text : clip(i.text)));
      else if (i.type === 'reasoning') console.log('REASONING');
      else console.log('ITEM ' + i.type + ' ' + clip(JSON.stringify(i)).slice(0, 160));
    } else if (e.type === 'turn.completed') console.log('USAGE ' + JSON.stringify(e.usage));
    else if (e.type === 'error' || e.type === 'turn.failed') console.log('ERR ' + clip(JSON.stringify(e)));
    else if (e.raw) console.log('RAW ' + e.raw);
  }
} else {
  for (const e of L) {
    if (e.event === 'init') console.log('INIT model=' + e.init?.model + ' mode=' + e.init?.permission_mode + ' tools=' + (e.init?.tools || []).length);
    else if (e.event === 'step_update') { const s = e.step_update; if (s.step_type === 'tool' && s.state !== 'ACTIVE') console.log('TOOL ' + s.state + ' ' + s.tool_name + ' ' + clip(JSON.stringify(s.tool_info?.parameters || {})) + (s.tool_info?.error ? ' ERR=' + clip(s.tool_info.error.message) : '')); }
    else if (e.event === 'result') console.log('RESULT ' + (full ? JSON.stringify(e.result) : clip(JSON.stringify(e.result))));
    else if (e.raw) console.log('RAW ' + e.raw);
  }
}
