// usage: node forwarder.mjs <unix-socket> <hosts-log>   OUTSIDE the unit. Accepts HTTP proxy requests (CONNECT host:port, or an absolute-URI request) on a unix socket, opens the TCP connection, pipes bytes.
// Logs ONLY the target "host:port" and the kind (CONNECT/HTTP); never a header, a path or a byte of content.
import net from 'node:net'; import fs from 'node:fs';
const [sock, log] = process.argv.slice(2); try { fs.unlinkSync(sock); } catch {}
const note = (k, hp, extra='') => fs.appendFileSync(log, `${new Date().toISOString()} ${k} ${hp} ${extra}\n`);
net.createServer(c => {
  let buf = Buffer.alloc(0); c.on('error', () => {});
  const onData = d => { buf = Buffer.concat([buf, d]); const i = buf.indexOf('\r\n\r\n'); if (i < 0) { if (buf.length > 65536) c.destroy(); return; }
    c.removeListener('data', onData); const head = buf.slice(0, i).toString('latin1'); const rest = buf.slice(i + 4); const first = head.split('\r\n')[0].split(' ');
    let host, port, connect = first[0] === 'CONNECT';
    if (connect) { [host, port] = first[1].split(':'); } else { try { const u = new URL(first[1]); host = u.hostname; port = u.port || '80'; } catch { c.destroy(); return; } }
    const up = net.connect(Number(port), host); up.on('error', e => { note(connect ? 'CONNECT' : 'HTTP', `${host}:${port}`, 'FAILED ' + e.code); c.destroy(); });
    up.on('connect', () => { note(connect ? 'CONNECT' : 'HTTP', `${host}:${port}`, 'OK'); if (connect) { c.write('HTTP/1.1 200 Connection established\r\n\r\n'); if (rest.length) up.write(rest); } else { up.write(buf); } c.pipe(up); up.pipe(c); });
    c.on('close', () => up.destroy()); up.on('close', () => c.destroy()); };
  c.on('data', onData);
}).listen(sock, () => fs.chmodSync(sock, 0o600));
