// usage: idcmp.mjs <stream.jsonl> <hooklog.jsonl>  : compares the tool events of the stream with the hook payloads (ids, indexes, names, decisions); prints no file content
import fs from 'node:fs';
const S=fs.readFileSync(process.argv[2],'utf8').split('\n').filter(Boolean).map(l=>JSON.parse(l));
const H=fs.readFileSync(process.argv[3],'utf8').split('\n').filter(Boolean).map(l=>{try{return JSON.parse(l)}catch{return null}}).filter(Boolean);
const tools=new Map(); for(const e of S){ if(e.event!=='step_update')continue; const s=e.step_update; if(s.step_type!=='tool')continue; const k=s.step_index; const o=tools.get(k)||{states:[],name:s.tool_name}; o.states.push(s.state); if(s.tool_info&&s.tool_info.error)o.err=String(s.tool_info.error.message||'').slice(0,60); tools.set(k,o); }
console.log('stream tool events (distinct step_index):',tools.size,' hook payloads:',H.length);
console.log('stream tool_info keys seen:',[...new Set(S.filter(e=>e.event==='step_update'&&e.step_update.tool_info).flatMap(e=>Object.keys(e.step_update.tool_info)))].join(','));
let match=0,miss=0;
for(const h of H){ const idx=h.stepIdx, t=tools.get(idx); const line=`hook stepIdx=${idx} toolCall.id=${String(h.toolCall&&h.toolCall.id).slice(0,12)} name=${h.toolCall&&h.toolCall.name} | stream step_index ${t?'found':'MISSING'} name=${t&&t.name} states=${t&&t.states.join('>')}`; if(t&&t.name&&true)match++; else miss++; console.log(line); }
console.log('hook payload keys:',[...new Set(H.flatMap(h=>Object.keys(h)))].join(',')); console.log('hook toolCall keys:',[...new Set(H.flatMap(h=>Object.keys(h.toolCall||{})))].join(','));
const ids=H.map(h=>h.toolCall&&h.toolCall.id); console.log('toolCall.id appears in the stream text:', ids.filter(i=>i&&JSON.stringify(S).includes(i)).length+' of '+ids.length);
console.log('step_index matches stepIdx for',match,'of',H.length,'(by index; names compared by eye above)');
