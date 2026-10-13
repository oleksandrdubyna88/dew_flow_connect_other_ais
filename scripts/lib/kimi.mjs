// lib/kimi.mjs — what the Kimi measurement script launches with, and how it treats the person's Kimi home.
//
// Shared by scripts/probe-kimi-confinement.mjs (the confinement cells, research/RESULTS_kimi_confinement.md §4–§13). Kimi-only:
// the generic pieces — the tree kill by pid, the file walk, the minimal environment — are lib/vibe.mjs's and are imported,
// not copied. Everything here that is a FACT about Kimi names the measurement it came from.
//
// The person's ~/.kimi-code is never edited by anything here: an arm that needs a user-level change (a hook, an MCP server)
// runs on a COPY of the home under KIMI_CODE_HOME, which Kimi documents as relocating its whole data root. The one thing a
// copy carries that matters is the OAuth credential pair; `CredentialGuard` watches whether a refresh ROTATES the refresh
// token and, if it does, puts the live pair back where the person's Kimi expects it (RESULTS §12).
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { copyFileSync, existsSync, lstatSync, mkdirSync, readFileSync, readdirSync, rmSync, rmdirSync, statSync, writeFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { basename, dirname, join, resolve, sep } from 'node:path';
import { SYSTEM32, files, fwd, run } from './vibe.mjs';

export const REAL_HOME = join(homedir(), '.kimi-code');
/** Where Moonshot's installer puts it on Windows — KimiAcpProfile.InstalledExecutableFor, the shim's own default. */
export const KIMI_EXE = join(REAL_HOME, 'bin', 'kimi.exe');
export const DEFAULT_MODEL = 'kimi-code/k3-256k';
/** The shim's exit codes (AskAcpMode): the contract the probe reads, never the stdout text. */
export const EXIT = { ok: 0, badRequest: 65, unavailable: 69, failed: 70, spent: 75, protocol: 76, refused: 77 };
/** KimiAcpProfile.Exclusions — the names the shipped boundary refuses at every depth; the M-K5 client refuses the same. */
export const EXCLUSIONS = ['AGENTS.md', 'agents.md', '.kimi-code', '.vibe', '.agents/skills', '.git'];

/** `coai-mcp --ask-acp …` as KimiRuntime will build it (plan §4 S4): the prompt rides on stdin, never here. */
export function shimArgs({ cwd, model, deadlineSeconds, executable = '' }) {
  return ['--ask-acp', '--profile', 'kimi', '--cwd', cwd, '--model', model, '--deadline-seconds', String(deadlineSeconds),
    ...(executable ? ['--executable', executable] : [])];
}

export const sha256 = (data) => createHash('sha256').update(data).digest('hex');
export const fileHash = (path) => sha256(readFileSync(path));

/** Every file under `dir` with its SHA-256 — what a before/after comparison of a home is made of. */
export function manifest(dir) {
  return Object.fromEntries(files(dir).map((f) => [f, fileHash(join(dir, f))]));
}

/** Whether each marker occurs in the session's main wire — the system prompt and tool results — whatever the answer did with it. */
export function inWire(sessionDir, markers) {
  const wire = join(sessionDir ?? '', 'agents', 'main', 'wire.jsonl');
  if (!sessionDir || !existsSync(wire)) return {};
  const text = readFileSync(wire, 'utf8');
  return Object.fromEntries(Object.entries(markers).map(([k, t]) => [k, text.includes(t)]));
}

/** The paths whose hash differs, or that exist on one side only. */
export function manifestDiff(before, after) {
  const names = new Set([...Object.keys(before), ...Object.keys(after)]);
  return [...names].filter((n) => before[n] !== after[n]).sort((a, b) => a.localeCompare(b))
    .map((n) => ({ path: n, change: !(n in before) ? 'added' : !(n in after) ? 'removed' : 'changed' }));
}

/**
 * Kimi's key for a working folder (measured 2026-10-13 against the person's own store): the session folder is
 * `wd_<basename>_<sha256(forward-slash path)[:12]>`, the workspace-trust entry the same over the LOWER-CASED path.
 */
export const wdKey = (workdir) => `wd_${basename(workdir)}_${sha256(fwd(workdir)).slice(0, 12)}`;
export const trustKey = (workdir) => `wd_${basename(workdir).toLowerCase()}_${sha256(fwd(workdir).toLowerCase()).slice(0, 12)}`;

/** Writes the trust entry Kimi itself writes (`workspace-trust/<key>`: `{root, trustedAt}`) into `home` — a positive control's lever. */
export function trustInHome(home, workdir) {
  const path = join(home, 'workspace-trust', trustKey(workdir));
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, JSON.stringify({ root: workdir, trustedAt: Date.now() }));
  return path;
}

// ---------------------------------------------------------------------------------------------------- the copied home

/** What a copied home takes from the real one: the sign-in and the config, the device identity, and the bundled rg/fd (never kimi.exe). */
const HOME_FILES = ['config.toml', 'tui.toml', 'device_id', 'region', 'migrations-effort.json'];
const HOME_BIN = ['rg.exe', 'fd.exe'];

/**
 * A fresh home at `home` copied from `real`. Every copy is verified by hash; the list of what was copied is returned.
 * `credentials: false` leaves the sign-in out (the not-signed-in arm); `config: false` leaves config.toml out too.
 */
export function copyHome(real, home, { credentials = true, config = true } = {}) {
  mkdirSync(join(home, 'bin'), { recursive: true });
  const copied = [];
  const take = (rel) => {
    const from = join(real, rel);
    if (!existsSync(from)) return;
    const to = join(home, rel);
    mkdirSync(dirname(to), { recursive: true });
    copyFileSync(from, to);
    if (fileHash(from) !== fileHash(to)) throw new Error(`copy of ${rel} into ${home} does not hash like its source`);
    copied.push(rel);
  };
  for (const f of HOME_FILES) if (config || f !== 'config.toml') take(f);
  for (const f of HOME_BIN) take(join('bin', f));
  if (credentials && existsSync(join(real, 'credentials'))) {
    for (const f of readdirSync(join(real, 'credentials'))) if (statSync(join(real, 'credentials', f)).isFile()) take(join('credentials', f));
  }
  return copied;
}

/**
 * The sensitive files a copied home must not keep once the run is over: the sign-in and the config (it carries an API key).
 * A `credentials` that is a LINK (the shared-sign-in arm, M-K8b) is unlinked with `rmdirSync`, which removes the junction and
 * never what it points at — checked on a throwaway junction before the arm existed; a recursive remove would have followed it.
 */
export function scrubHome(home) {
  const gone = [];
  for (const rel of ['credentials', 'config.toml']) {
    const p = join(home, rel);
    const link = isLink(p);
    if (!existsSync(p) && !link) continue;
    if (link) rmdirSync(p); else rmSync(p, { recursive: true, force: true });
    if (existsSync(p) || isLink(p)) throw new Error(`${p} still exists after the scrub`);
    gone.push(link ? `${rel} (link)` : rel);
  }
  return gone;
}

const isLink = (p) => { try { return lstatSync(p).isSymbolicLink(); } catch { return false; } };

/** A directory junction `link` → `target` (a cmd builtin, so cmd.exe by its absolute path); the error text when it could not be made. */
export function junction(link, target) {
  const r = run(SYSTEM32('cmd.exe'), ['/d', '/c', 'mklink', '/J', link, target], { encoding: 'utf8', timeout: 30_000 });
  return r.status === 0 ? '' : `mklink /J failed: ${(r.stdout + r.stderr).trim()}`;
}

// ------------------------------------------------------------------------------------------------- the credential guard

/** Fingerprints of a home's OAuth files — the file's hash and the refresh token's hash; the values themselves are never held. */
export function credentialFingerprint(home) {
  const dir = join(home, 'credentials');
  if (!existsSync(dir)) return {};
  const out = {};
  for (const f of readdirSync(dir).filter((n) => n.endsWith('.json'))) {
    const text = readFileSync(join(dir, f), 'utf8');
    let refresh = '';
    let expiresAt = 0;
    try {
      const j = JSON.parse(text);
      refresh = typeof j.refresh_token === 'string' ? sha256(j.refresh_token).slice(0, 12) : '';
      expiresAt = Number(j.expires_at ?? 0);
    } catch { /* not JSON: the file hash still says whether it moved */ }
    out[f] = { file: sha256(text).slice(0, 12), refresh, expiresAt };
  }
  return out;
}

const refreshOf = (fp) => Object.fromEntries(Object.entries(fp).map(([f, v]) => [f, v.refresh]));
const sameRefresh = (a, b) => JSON.stringify(refreshOf(a)) === JSON.stringify(refreshOf(b));

/** Copies every credential file from one home to another, verified by hash; returns the names. */
export function syncCredentials(from, to) {
  const src = join(from, 'credentials');
  if (!existsSync(src)) return [];
  mkdirSync(join(to, 'credentials'), { recursive: true });
  const names = [];
  for (const f of readdirSync(src).filter((n) => statSync(join(src, n)).isFile())) {
    copyFileSync(join(src, f), join(to, 'credentials', f));
    if (fileHash(join(src, f)) !== fileHash(join(to, 'credentials', f))) throw new Error(`credential ${f} did not copy byte for byte`);
    names.push(f);
  }
  return names;
}

/**
 * Watches the sign-in across homes. Kimi's access token lives 900 s (`expires_in`, read off the file), so nearly every
 * launch refreshes it; whether the REFRESH token rotates on refresh is measured on the real home's first refresh. A copied
 * home that refreshed under rotation holds the only live pair — it is written back to the real home, verified, and the
 * write-back is recorded. Under no rotation nothing is ever written to the real home.
 */
export class CredentialGuard {
  #real;
  #last;
  rotates = null; // unknown until a refresh of the real home has been seen
  events = [];

  constructor(real) {
    this.#real = real;
    this.#last = credentialFingerprint(real);
  }

  /** After a launch on the real home: learn whether the refresh token rotated. */
  afterRealRun(tag) {
    const now = credentialFingerprint(this.#real);
    const fileMoved = JSON.stringify(now) !== JSON.stringify(this.#last);
    if (fileMoved) {
      this.rotates = !sameRefresh(now, this.#last);
      this.events.push({ tag, where: 'real', refreshed: true, rotated: this.rotates });
    }
    this.#last = now;
    return fileMoved;
  }

  /** Before a launch on a copied home that carries credentials: give it the freshest pair. */
  beforeCopiedRun(home, tag) {
    if (!existsSync(join(home, 'credentials'))) return;
    this.events.push({ tag, where: 'copy', synced: syncCredentials(this.#real, home) });
  }

  /** After a launch on a copied home: if it refreshed and rotation is possible, the live pair goes back to the real home. */
  afterCopiedRun(home, tag) {
    if (!existsSync(join(home, 'credentials'))) return false;
    const copy = credentialFingerprint(home);
    if (sameRefresh(copy, this.#last)) return false;
    const refreshedInCopy = Object.keys(copy).length > 0;
    if (refreshedInCopy && this.rotates !== false) {
      this.events.push({ tag, where: 'copy', rotatedInCopy: true, writtenBack: syncCredentials(home, this.#real) });
      this.#last = credentialFingerprint(this.#real);
      return true;
    }
    this.events.push({ tag, where: 'copy', rotatedInCopy: refreshedInCopy, writtenBack: [] });
    return false;
  }
}

// ----------------------------------------------------------------------------------------------- configs and canaries

/** A fixed Python snippet: the path arrives as argv[1], so nothing about the path is ever Python source (the Vibe probe's rule). */
export const CANARY_SNIPPET = "import sys; open(sys.argv[1], 'w').write('x')";
const toml = (s) => JSON.stringify(s); // a JSON string is a valid TOML basic string
export const canaryCommand = (path) => `python -c "${CANARY_SNIPPET}" "${fwd(path)}"`;

/** `[[hooks]]` as Kimi's own docs spell it (customization/hooks): event, command, timeout; nothing else, or the file fails to load. */
export function hooksToml(canary, event = 'SessionStart') {
  return `\n[[hooks]]\nevent = ${toml(event)}\ncommand = ${toml(canaryCommand(canary))}\ntimeout = 30\n`;
}

/** `mcp.json` as Kimi's own docs spell it (customization/mcp): a stdio server whose command writes the canary and exits. */
export function mcpJson(canary) {
  return `${JSON.stringify({ mcpServers: { 'probe-canary': { command: 'python', args: ['-c', CANARY_SNIPPET, fwd(canary)] } } }, null, 2)}\n`;
}

// --------------------------------------------------------------------------------------------------------- prompt mode

/** `kimi -p` — the positive-control launch: no client, Kimi's own `auto` permission (RESULTS §1), stream-json on stdout. */
export function promptMode(exe, { cwd, env, prompt, timeoutMs, viaStdin = false }) {
  const args = ['-p', viaStdin ? '-' : prompt, '--output-format', 'stream-json'];
  return run(exe, args, { cwd, env, input: viaStdin ? prompt : '', encoding: 'utf8', timeout: timeoutMs, maxBuffer: 64 * 1024 * 1024 });
}

/** The assistant text and the session id out of stream-json lines. */
export function parseStreamJson(text) {
  let answer = '';
  let sessionId = '';
  let lines = 0;
  const roles = {};
  for (const raw of String(text ?? '').split('\n')) {
    if (!raw.trim()) continue;
    let el;
    try { el = JSON.parse(raw); } catch { continue; }
    lines++;
    roles[el.role ?? '?'] = (roles[el.role ?? '?'] ?? 0) + 1;
    if (el.role === 'assistant') {
      const c = el.content;
      const t = typeof c === 'string' ? c : Array.isArray(c) ? c.map((x) => (typeof x === 'string' ? x : x?.text ?? '')).join('') : '';
      if (t.trim()) answer = t;
    }
    if (el.role === 'meta' && el.type === 'session.resume_hint') sessionId = el.session_id ?? '';
  }
  return { answer, sessionId, lines, roles };
}

// ------------------------------------------------------------------------------------------------- the session store

/** The session folder Kimi recorded for `sessionId` in `home`, and what it holds — the facts the shim's report cannot carry. */
export function sessionFacts(home, sessionId) {
  const index = join(home, 'session_index.jsonl');
  if (!sessionId || !existsSync(index)) return { found: false };
  const line = readFileSync(index, 'utf8').split('\n').filter(Boolean).map((l) => { try { return JSON.parse(l); } catch { return null; } })
    .find((l) => l?.sessionId === sessionId);
  if (!line) return { found: false };
  const dir = line.sessionDir;
  try {
    const agents = existsSync(join(dir, 'agents')) ? readdirSync(join(dir, 'agents')) : [];
    const wire = join(dir, 'agents', 'main', 'wire.jsonl');
    const wireText = existsSync(wire) ? readFileSync(wire, 'utf8') : '';
    const count = (re) => (wireText.match(re) ?? []).length;
    const log = join(dir, 'logs', 'kimi-code.log');
    return {
      found: true, sessionDir: dir, workDir: line.workDir, subagents: agents.filter((a) => a !== 'main'), wireBytes: wireText.length,
      wireMentions: { hook: count(/hook/giu), mcp: count(/mcp/giu), trust: count(/trust/giu), skill: count(/skill/giu), agentsMd: count(/AGENTS\.md/gu) },
      log: existsSync(log) ? readFileSync(log, 'utf8') : '',
    };
  } catch (e) {
    // A deep home's session path can be one this process cannot open with long paths off (M-K8): the index line is the fact.
    return { found: true, sessionDir: dir, workDir: line.workDir, subagents: [], wireBytes: 0, error: String(e.code ?? e.message), log: '' };
  }
}

// ------------------------------------------------------------------------------------- a small ACP client (M-K5, M-K7)

/** The option that says no, as AcpSession.Choose does: reject_once, else the first reject*, else cancelled. */
function choose(options) {
  const offered = Array.isArray(options) ? options.filter((o) => o && typeof o === 'object') : [];
  const hit = offered.find((o) => o.kind === 'reject_once') ?? offered.find((o) => String(o.kind ?? '').startsWith('reject'));
  return hit && offered.filter((o) => o.optionId === hit.optionId).length === 1 ? { optionId: hit.optionId, kind: hit.kind } : null;
}

/** FolderReadBoundary's rule, lexically: an absolute path inside `root` whose components hold no excluded name. */
function boundaryAnswer(root, requested) {
  const rootFull = resolve(root).toLowerCase() + sep;
  const full = resolve(String(requested ?? ''));
  if (!full.toLowerCase().startsWith(rootFull)) return { served: false, reason: 'outside the working folder' };
  const parts = full.slice(rootFull.length).split(/[\\/]/u).filter(Boolean).map((p) => p.toLowerCase());
  for (const ex of EXCLUSIONS) {
    const exParts = ex.toLowerCase().split('/');
    for (let at = 0; at + exParts.length <= parts.length; at++) {
      if (exParts.every((p, i) => parts[at + i] === p)) return { served: false, reason: `'${ex}' is not served — a vendor configuration or instruction path` };
    }
  }
  if (!existsSync(full) || statSync(full).isDirectory()) return { served: false, reason: 'no such file in the working folder' };
  return { served: true, text: readFileSync(full, 'utf8') };
}

function killTree(pid) {
  if (pid) run(SYSTEM32('taskkill.exe'), ['/PID', String(pid), '/T', '/F'], { encoding: 'utf8', timeout: 15_000 });
}

/**
 * One ACP conversation against `kimi acp`, NOT through the shim — used only where the shim has no path yet: `session/load`
 * (M-K5; S5 adds it to the shim) and a `session/new` whose `configOptions` name the models (M-K7, no prompt, no spend).
 * It mirrors the shim's client decisions — the same `initialize` offer, `mcpServers: []`, every permission refused by kind,
 * reads served by the boundary rule, every other request an error — and records every line both ways.
 */
export function acpTurn({ exe, cwd, env, deadlineMs, model = '', prompt = '', load = '', followUp = '' }) {
  return new Promise((done) => {
    const child = spawn(exe, ['acp'], { cwd, env, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
    const state = { pid: child.pid, lines: [], answer: '', answer2: '', sessionId: load, refusals: [], reads: [], toolCalls: {}, usage: null,
      sessionResult: null, stopReason: '', error: '', exit: null, stderrTail: '', prompting: false, second: false };
    const pending = new Map();
    let nextId = 1;
    let finished = false;
    let buffered = '';
    let stderr = '';
    const finish = (why) => {
      if (finished) return;
      finished = true;
      state.error ||= why;
      clearTimeout(timer);
      try { child.stdin.end(); } catch { /* already closed */ }
      setTimeout(() => { killTree(child.pid); state.stderrTail = stderr.slice(-1500); setTimeout(() => done(state), 300); }, 1500);
    };
    const timer = setTimeout(() => finish(`deadline ${deadlineMs} ms`), deadlineMs);
    const send = (obj) => { const line = JSON.stringify(obj); state.lines.push(`> ${line}`); child.stdin.write(`${line}\n`); };
    const call = (method, params) => new Promise((res, rej) => { const id = nextId++; pending.set(id, { res, rej }); send({ jsonrpc: '2.0', id, method, params }); });
    const reply = (id, result) => send({ jsonrpc: '2.0', id, result });
    const fail = (id, code, message) => send({ jsonrpc: '2.0', id, error: { code, message } });
    const onRequest = (m) => {
      if (m.method === 'session/request_permission') {
        const c = choose(m.params?.options);
        state.refusals.push({ tool: m.params?.toolCall?.title ?? '', kind: m.params?.toolCall?.kind ?? '', option: c ? c.kind : 'cancelled' });
        return reply(m.id, { outcome: c ? { outcome: 'selected', optionId: c.optionId } : { outcome: 'cancelled' } });
      }
      if (m.method === 'fs/read_text_file') {
        const a = boundaryAnswer(cwd, m.params?.path);
        state.reads.push({ path: m.params?.path ?? '', served: a.served, reason: a.reason ?? '' });
        return a.served ? reply(m.id, { content: a.text }) : fail(m.id, -32602, a.reason);
      }
      return fail(m.id, -32601, `\`${m.method}\` is not offered by this client`);
    };
    const onNotification = (m) => {
      const u = m.params?.update ?? {};
      if (u.sessionUpdate === 'agent_message_chunk' && state.prompting && u.content?.type === 'text') state[state.second ? 'answer2' : 'answer'] += u.content.text ?? '';
      if (u.sessionUpdate === 'usage_update') state.usage = { used: u.used, size: u.size };
      if ((u.sessionUpdate === 'tool_call' || u.sessionUpdate === 'tool_call_update') && u.toolCallId) {
        const was = state.toolCalls[u.toolCallId] ?? {};
        state.toolCalls[u.toolCallId] = { title: u.title ?? was.title ?? '', kind: u.kind ?? was.kind ?? '', status: u.status ?? was.status ?? '' };
      }
    };
    child.stdout.on('data', (chunk) => {
      buffered += chunk.toString('utf8');
      let at;
      while ((at = buffered.indexOf('\n')) >= 0) {
        const line = buffered.slice(0, at).replace(/\r$/u, '');
        buffered = buffered.slice(at + 1);
        if (!line.trim()) continue;
        state.lines.push(`< ${line}`);
        let m;
        try { m = JSON.parse(line); } catch { continue; }
        if (m.method && m.id !== undefined && m.id !== null) onRequest(m);
        else if (m.method) onNotification(m);
        else if (pending.has(m.id)) {
          const p = pending.get(m.id);
          pending.delete(m.id);
          if (m.error) p.rej(new Error(`${m.error.code}: ${m.error.message}`)); else p.res(m.result ?? {});
        }
      }
    });
    child.stderr.on('data', (c) => { stderr += c.toString('utf8'); });
    child.on('exit', (code) => { state.exit = code; for (const p of pending.values()) p.rej(new Error(`the agent exited with code ${code}`)); pending.clear(); });
    child.on('error', (e) => finish(`spawn: ${e.message}`));
    (async () => {
      try {
        await call('initialize', { protocolVersion: 1, clientCapabilities: { fs: { readTextFile: true, writeTextFile: false }, terminal: false } });
        state.sessionResult = load
          ? await call('session/load', { sessionId: load, cwd, mcpServers: [] })
          : await call('session/new', { cwd, mcpServers: [] });
        state.sessionId = state.sessionResult?.sessionId || load;
        if (model) await call('session/set_config_option', { sessionId: state.sessionId, configId: 'model', value: model });
        if (prompt) {
          state.prompting = true;
          const r = await call('session/prompt', { sessionId: state.sessionId, prompt: [{ type: 'text', text: prompt }] });
          state.stopReason = r?.stopReason ?? '';
        }
        if (followUp) {
          // A second prompt on the SAME session — what the one-turn shim cannot do: `/usage` after a paid turn (M-K2).
          state.second = true;
          await call('session/prompt', { sessionId: state.sessionId, prompt: [{ type: 'text', text: followUp }] });
        }
        finish('');
      } catch (e) {
        finish(String(e.message ?? e));
      }
    })();
  });
}
