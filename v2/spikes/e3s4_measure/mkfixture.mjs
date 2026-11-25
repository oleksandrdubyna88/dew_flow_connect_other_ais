// usage: mkfixture.mjs <out> <cli> <cliVersion> <how> <argvJson> <envOverridesJson> <stdinFile|-> <exitCode> <seconds> <stdoutFile> <stderrFile>
import fs from 'node:fs';
const [out,cli,ver,how,argv,env,stdinF,exit,secs,so,se]=process.argv.slice(2);
const rd=f=>f==='-'?'':fs.readFileSync(f,'utf8');
// stderr: drop the CLI's WARNING line about helper binaries under /tmp (scratch-folder artefact) and any scratch path
let stderr=rd(se).split('\n').filter(l=>!l.startsWith('WARNING: proceeding')).join('\n').replace(/.*coai-e3s4-fx\/[^\s"]*/g,'<scratch folder>/...');
const o={cli,cliVersion:ver,recordedUtc:new Date().toISOString(),how,argv:JSON.parse(argv),envOverrides:JSON.parse(env),stdin:rd(stdinF),exitCode:Number(exit),signal:null,seconds:Number(secs),stdout:rd(so),stderr};
fs.writeFileSync(out,JSON.stringify(o,null,2)+'\n'); console.log('wrote',out);
