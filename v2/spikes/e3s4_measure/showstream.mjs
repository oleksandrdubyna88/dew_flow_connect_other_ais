import fs from 'node:fs';
const L=fs.readFileSync(process.argv[2],'utf8').split('\n').filter(Boolean).map(l=>{try{return JSON.parse(l)}catch{return {type:'?',raw:l.slice(0,100)}}});
for(const e of L){ const i=e.item;
 if(e.type==='item.completed'||e.type==='item.started'&&0){ if(i.type==='command_execution') console.log('CMD exit='+i.exit_code+' status='+i.status+' :: '+(i.command||'').replace(/\s+/g,' ').slice(0,150)+' => '+(i.aggregated_output||'').replace(/\s+/g,' ').slice(0,160));
  else if(i.type==='file_change') console.log('FILECHANGE status='+i.status+' '+JSON.stringify(i.changes||'').slice(0,160));
  else if(i.type==='agent_message') console.log('MSG: '+(i.text||'').replace(/\n/g,' | ').slice(0,900));
  else console.log('ITEM '+i.type+' '+JSON.stringify(i).slice(0,200)); }
 else if(e.type==='turn.completed') console.log('USAGE '+JSON.stringify(e.usage)); else if(e.type==='error'||e.type==='turn.failed') console.log('ERR '+JSON.stringify(e).slice(0,300)); }
