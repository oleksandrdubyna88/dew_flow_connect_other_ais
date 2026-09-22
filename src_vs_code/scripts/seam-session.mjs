/**
 * The live MCP session the seam's legs drive the REAL `coai-mcp` through, over stdio.
 *
 * <p>Its own module for two reasons. `run-seam.mjs` had passed the repository's 800-line ceiling
 * (codex, on #465), and a session inside a script that starts servers at import time is a session no
 * test can load: fixing its stdin handling needed a red test on the FUNCTION, not only on a copy of
 * its mechanism. The factory takes the command rather than assuming `dotnet`, which is what lets a
 * test spawn a stand-in that dies mid-write.</p>
 */
import { spawn } from 'node:child_process';

/**
 * ONE live server, driven over the transport a real client uses.
 *
 * <p><b>One process for the whole leg, deliberately.</b> A fresh server per call would read the file
 * at startup and prove nothing about the case that actually happens: the panel writes while an MCP
 * client is already holding a server, and the NEXT call has to see it. `PanelServiceHost` re-stamps
 * the settings file on every tool call for exactly that reason, and this is what holds it — raised on
 * this story's plan round, where the first version of this leg spawned twice and could not tell.</p>
 *
 * <p>The three session variables are CLEARED rather than inherited: this script is itself running
 * under an assistant, so the caller kind would otherwise be whatever happens to be driving it, and
 * the leg would assert about a different row of the map on somebody else's machine. Cleared, the kind
 * is `other` — which is also the row a plain MCP client gets.</p>
 */
export function sessionsFor({ command, args, dataDir, timeoutMs }) {
  return function serverSession(extraEnv = {}) {
    const child = spawn(command, args, {
      env: {
        ...process.env,
        COAI_DATA_DIR: dataDir,
        CLAUDE_CODE_SESSION_ID: '',
        CODEX_SESSION_ID: '',
        GEMINI_CLI_SESSION_ID: '',
        ...extraEnv,
      },
      stdio: ['pipe', 'pipe', 'pipe'],
    });
    const waiting = new Map();
    // Every pending request's REJECTION, beside its resolution in `waiting`: a stdin write error has no
    // request of its own, so it fails all of them.
    const failing = new Map();
    // A write to a server that is dying emits an error on STDIN — which `child.on('error')` does not
    // see, and which, unhandled, is thrown as an uncaught 'error' event that ends this runner before
    // any leg can clean up. Reproduced before fixing: a write in flight to a child that exits throws
    // `Error: write EOF` (the Windows code; EPIPE elsewhere) 3 of 3 times without this listener, and is
    // handled 3 of 3 with it. (CodeRabbit, on #465.)
    child.stdin.on('error', (e) => {
      for (const reject of failing.values()) {
        reject(new Error(`the server's stdin failed (${e.code ?? e.message}) — it had gone away\n${err}`));
      }
      failing.clear();
      waiting.clear();
    });
    // When the process's stdio has ENDED, remembered from the start: a `close` listener added after
    // the event has fired never runs, so a caller arriving late would otherwise wait for good.
    const ended = new Promise((settle) => {
      child.on('close', (code) => settle(code));
    });
    let next = 1;
    let buffered = '';
    let err = '';

    child.stderr.on('data', (b) => {
      err += String(b);
    });
    child.stdout.on('data', (b) => {
      buffered += String(b);
      for (const line of buffered.split('\n').slice(0, -1)) {
        // A throw HERE runs inside a 'data' listener, outside every surrounding try — so it escapes
        // as an uncaught exception: the dotnet child is never killed, the temporary directories stay,
        // and the run prints a stack where its own `seam:` line belongs. A line that is not a frame is
        // not this reader's business. (CodeRabbit, on the pull request.)
        let message;
        try {
          message = JSON.parse(line);
        } catch {
          continue;
        }
        const settle = waiting.get(message.id);
        if (settle !== undefined) {
          waiting.delete(message.id);
          failing.delete(message.id);
          settle(message);
        }
      }
      buffered = buffered.slice(buffered.lastIndexOf('\n') + 1);
    });

    const say = (message) => child.stdin.write(`${JSON.stringify(message)}\n`);
    const ask = async (method, params) => {
      const id = (next += 1);

      return await new Promise((done, broke) => {
        const deadline = setTimeout(() => {
          waiting.delete(id);
          failing.delete(id);
          broke(new Error(`${method} did not answer within ${timeoutMs} ms\n${err}`));
        }, timeoutMs);
        waiting.set(id, (message) => {
          clearTimeout(deadline);
          done(message);
        });
        failing.set(id, (reason) => {
          clearTimeout(deadline);
          broke(reason);
        });
        child.on('error', (e) => {
          clearTimeout(deadline);
          broke(e);
        });
        say({ jsonrpc: '2.0', id, method, params });
      });
    };

    const call = async (name, args) => await ask('tools/call', { name, arguments: args });

    /**
     * End the session the way a client does — by closing stdin — and wait for the process to leave.
     *
     * <p>NOT `kill()`, which skips the `finally` in `ServeAsync` that drains the notice writer (story
     * 2.3.1); a clean end of stdin is the only road on which the file is complete. Bounded, and it
     * kills on the way out: a server that ignores EOF must not hang CI holding the data directory.</p>
     */
    const close = async () => {
      let killed = false;
      const deadline = setTimeout(() => {
        killed = true;
        child.kill('SIGKILL');
      }, timeoutMs);
      // `close`, not `exit`: Node documents that stdio "might still be open" at `exit`, and the caller
      // reads `stderr` to prove the secret never reached it. Not observed here (0 of 25 runs of a 4 MB
      // burst) — fixed on the documented contract. And a TIMEOUT kill waits for it too, so cleanup never
      // runs while the process still holds the directory. (codex, the code round.)
      child.stdin.end();
      const code = await ended;
      clearTimeout(deadline);

      return { exited: !killed, code, stderr: err };
    };

    return {
      ready: ask('initialize', {
        protocolVersion: '2024-11-05',
        capabilities: {},
        clientInfo: { name: 'seam', version: '1' },
      }).then(() => say({ jsonrpc: '2.0', method: 'notifications/initialized' })),
      tools: async () => await ask('tools/list', {}),
      consult: async (repoPath) => await call('consult', {
        repoPath,
        problem: 'The parser returns 3 where 4 is expected, after two fix attempts.',
      }),
      call,
      close,
      /**
       * Kill, and WAIT for the process to be gone — for a leg that is failing. (Once `stop()`: a name
       * that sounded graceful, beside `close()`, which is the graceful one.)
       *
       * <p>`end()` returns the instant the signal is sent, and on Windows the child still holds its
       * files for a moment after that: a leg that removed its directory straight after got `EPERM`, and
       * a stack was printed where its own `seam:` sentence belonged. Found by one of its own plants.</p>
       */
      killAndWait: async () => {
        child.kill('SIGKILL');
        // Bounded, because a process that will not report its own end must not hang the run that is
        // already trying to fail with a sentence.
        await Promise.race([ended, new Promise((done) => { setTimeout(done, timeoutMs); })]);
      },
      end: () => child.kill(),
    };
  }
}

/** The tool's own answer, which is a JSON object in the text content of the result. */
export function answerOf(reply) {
  const text = reply?.result?.content?.[0]?.text ?? '';
  try {
    return JSON.parse(text);
  } catch {
    return { error: text };
  }
}
