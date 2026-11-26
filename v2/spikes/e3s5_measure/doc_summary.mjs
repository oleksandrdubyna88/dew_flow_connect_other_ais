// prints the sandbox/auth/config facts of a `codex doctor --json` report; no secret value is in the report (it is redacted); paths only
import fs from 'node:fs';
const j=JSON.parse(fs.readFileSync(process.argv[2],'utf8'));
for(const id of ['sandbox.helpers','sandbox.filesystem_paths','auth.credentials']){ const c=j.checks[id]; if(!c) continue; console.log('#',id,c.status,'-',c.summary);
  for(const [k,v] of Object.entries(c.details||{})){ if(/^(approval policy|filesystem sandbox|network sandbox|permission profile|denied-read|managed filesystem source|path \d+$|stored|auth storage)/.test(k)) console.log('  ',k,'=',String(v).replace(process.env.HOME,'~').slice(0,200)); } }
