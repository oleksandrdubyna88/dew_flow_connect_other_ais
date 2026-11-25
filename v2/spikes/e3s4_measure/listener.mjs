import http from 'node:http'; import fs from 'node:fs';
const log=process.argv[3]; http.createServer((req,res)=>{ let b=''; req.on('data',d=>b+=d); req.on('end',()=>{
 let keys=''; try{ keys=Object.keys(JSON.parse(b)).join(','); }catch{ keys='(form) '+[...new URLSearchParams(b).keys()].join(','); }
 fs.appendFileSync(log, `${new Date().toISOString()} ${req.method} ${req.url} ct=${req.headers['content-type']} bodykeys=${keys}\n`);
 res.writeHead(400,{'content-type':'application/json'}); res.end('{"error":"invalid_grant"}'); }); }).listen(Number(process.argv[2]),'127.0.0.1');
