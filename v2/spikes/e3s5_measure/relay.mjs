// usage: node relay.mjs <port> <unix-socket>   INSIDE the unit (PrivateNetwork=yes): 127.0.0.1:<port> -> the unix socket of the forwarder. Logs nothing.
import net from 'node:net';
const [port, sock] = process.argv.slice(2);
net.createServer(c => { const u = net.connect(sock); c.on('error', () => u.destroy()); u.on('error', () => c.destroy()); c.pipe(u); u.pipe(c); }).listen(Number(port), '127.0.0.1');
