// The sanitiser behind the caller-transcript fixtures (research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E1.md, E1.S3).
// Claude Code's transcript is full of the operator's prompts, tool inputs, file contents and paths; a fixture cut from it is
// published in a PUBLIC repository. So the rule is not "remove the secrets" — it is "keep only what is named":
//
//   - a STRING leaf survives only if its dotted path is in allowlist.json AND its value has that field's shape;
//     every other string is the sentinel. Booleans and nulls are kept; a number is kept only at an `int` path, every other
//     number is 0 (a size, a duration or an epoch outside the named fields is not evidence of anything E3 reads).
//   - an object KEY survives only if it is a segment of some allowlisted path (the closed key set, derived, never listed
//     twice); every other key becomes k<n>, numbered per object in input order, and nothing below a renamed key is
//     allowlisted any more: its strings are the sentinel and ALL its keys are renamed in turn. (`snapshot.trackedFileBackups`
//     has real file paths as keys; `secret.txt` has no separator and is a leak all the same.)
//   - ids (uuid / hexid / msgid / toolid) go through ONE map per fixture, sha256(salt + original) formatted back into the same
//     shape, and the same map renames the files, so joins (toolUseId <-> tool_use.id, parentAgentId <-> agentId) survive.
//   - paths are rewritten under a fixture root named by --repo <real>=<fixture>, and EVERY segment below the root becomes
//     p<n> (+ at most a short extension) through the fixture's one segment map, so "the same file" survives and no file or
//     folder name does. An unmapped path lands under the "other" root as p<n>, never as the original. The fixture root takes
//     the separator style of the path it replaces; the remainder keeps the separators as written.
//   - tool names: only the closed builtin list in allowlist.json survives; mcp__<server>__<tool> becomes mcp__s<n>__t<n>; any
//     other name is the sentinel. Agent types: a builtin survives, any other becomes custom-<n>.
//
//   - TIME and MAGNITUDE are re-drawn, never kept: every instant becomes a position on a synthetic clock (2000-01-01T00:00:00Z plus
//     one second per RANK of the distinct real instants of the fixture), and every usage count becomes the rank of its value among
//     the fixture's values at that path (0 stays 0). Order, ties, containment of a round in a window, equality of a message's
//     usage across its lines and the last/max relation survive; the operator's activity times, the gaps between them and the real
//     magnitudes do not (one constant offset would have kept the whole fingerprint). Call prepare() before sanitiseValue().
//
// Deterministic by construction: no clock, no randomness, no directory-order dependence (agent files are sorted).
// This file reads only the files cutSession is handed and the agent-<hex>.jsonl|.meta.json files beside that session; it
// never lists ~/.claude/sessions and never opens tool-results/.
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readdirSync, readFileSync, realpathSync, statSync, writeFileSync } from 'node:fs';
import { basename, dirname, isAbsolute, join, relative, resolve } from 'node:path';
import { compareOrdinal } from './ordinal.mjs';

/**
 * Expected failures are VALUES (the family's rule): a function that can be told "no" by its inputs returns `{ ok: true, value }` or
 * `{ ok: false, refusal }`, and only the unexpected (a disk, a bug) throws. A refusal sentence never carries what was typed.
 */
const accept = (value) => ({ ok: true, value });
const refuse = (refusal) => ({ ok: false, refusal });

/** The documented ceiling on the SUM of a session's files (main + subagent jsonl/meta), checked before anything is read. */
export const DEFAULT_MAX_INPUT_BYTES = 256 * 1024 * 1024;

const RENAMED = /^k\d+$/;
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const ID_SHAPES = new Set(['uuid', 'hexid', 'msgid', 'toolid']);
const IDENTIFIER = /^[A-Za-z_][A-Za-z0-9_]{0,40}$/;
const MCP_TOOL = /^mcp__(.+?)__(.+)$/;
const SHORT_EXTENSION = /^\.[a-z0-9]{1,5}$/i;
const MAX_REPORTED_KEYS = 200;
export const SYNTHETIC_EPOCH_MS = Date.UTC(2000, 0, 1);
const SYNTHETIC_STEP_MS = 1000;
/** An int path whose value is a structural depth, not a measured size: kept as it is (1, 2, 3). */
const KEPT_INTS = new Set(['spawnDepth']);
const ROUND_STATUSES = new Set(['done', 'running', 'failed', 'cancelled']);
const isCount = (value) => Number.isInteger(value) && value >= 0;

// ---- the allowlist ---------------------------------------------------------------------------------------------------
/**
 * Parses allowlist.json into { sentinel, keptKeys, vocabularies, fields: Map(path -> { shape, vocabulary?, accepts(string) }) }.
 * Throws on a malformed list.
 */
export function loadAllowlist(json) {
  if (typeof json?.sentinel !== 'string' || json.sentinel === '') throw new Error('allowlist: sentinel missing');
  const shapes = new Map(Object.entries(json.shapes ?? {}).map(([name, source]) => [name, new RegExp(source)]));
  const vocabularies = new Map(Object.entries(json.vocabularies ?? {}).map(([name, v]) => [name, loadVocabulary(name, v)]));
  const keptKeys = new Set();
  const fields = new Map();
  for (const { path, shape } of json.fields ?? []) {
    const segments = String(path).split('.').map((s) => s.replace(/(\[\])+$/, ''));
    for (const s of segments) {
      if (s === '') throw new Error(`allowlist: empty segment in ${path}`);
      if (RENAMED.test(s)) throw new Error(`allowlist: segment ${s} of ${path} matches ^k\\d+$, so a renamed key could collide with it`);
      keptKeys.add(s);
    }
    if (fields.has(path)) throw new Error(`allowlist: ${path} listed twice`);
    fields.set(path, { shape, vocabulary: vocabularies.get(shape), accepts: shapeTest(shape, shapes, vocabularies, path) });
  }
  return { sentinel: json.sentinel, keptKeys, vocabularies, fields };
}

function loadVocabulary(name, { builtin, rewritten }) {
  if (!Array.isArray(builtin) || builtin.length === 0) throw new Error(`allowlist: vocabulary ${name} has no builtin list`);
  return { builtin: new Set(builtin), rewritten: typeof rewritten === 'string' ? new RegExp(rewritten) : undefined };
}

function shapeTest(shape, shapes, vocabularies, path) {
  if (shape === 'int' || shape === 'bool') return () => false; // a STRING is never an int or a bool
  if (shape === 'fixture-path') return () => true; // checked by the rewrite, which cannot produce anything else
  if (vocabularies.has(shape)) return (s) => vocabularies.get(shape).builtin.has(s) || (vocabularies.get(shape).rewritten?.test(s) ?? false);
  if (shape.startsWith('enum:')) {
    const options = new Set(shape.slice(5).split('|').filter(Boolean));
    if (options.size === 0) throw new Error(`allowlist: empty enum for ${path}`);
    return (s) => options.has(s);
  }
  const re = shapes.get(shape);
  if (!re) throw new Error(`allowlist: unknown shape "${shape}" for ${path}`);
  return (s) => re.test(s);
}

// ---- the sanitiser ---------------------------------------------------------------------------------------------------
const fold = (path) => path.replaceAll('\\', '/');

/** The only destination roots a fixture may carry (the C# guard pins the same shape): D:/fixture/repo-<a-z>, .../other, .../scratch. */
const FIXTURE_ROOT_POLICY = /^(?:[A-Za-z]:)?\/fixture\/(?:repo-[a-z]|other|scratch)$/;

/** A destination root, folded and trimmed, when it is on the synthetic policy; otherwise a refusal that does not echo the input. */
export function checkFixtureRoot(text) {
  const folded = typeof text === 'string' ? fold(text).replace(/\/+$/, '') : '';
  return FIXTURE_ROOT_POLICY.test(folded)
    ? accept(folded)
    : refuse('a destination root is not a fixture root: it must be D:/fixture/repo-<a-z>, D:/fixture/other or D:/fixture/scratch (drive letter optional)');
}

/** Every --repo destination and the --other-root, checked BEFORE any input is read. */
function checkRoots(repos, otherRoot) {
  const roots = [...repos.map((r) => r.to), ...(otherRoot === undefined ? [] : [otherRoot])];
  return roots.map((root) => checkFixtureRoot(root)).find((r) => !r.ok) ?? accept(roots.length);
}

/** One sanitiser per fixture: its id map, path map and counters are the fixture's. */
export function createSanitiser({ allowlist, salt, repos = [], otherRoot = '/fixture/other' }) {
  const idMap = new Map();
  const idsIssued = new Set();
  const absMap = new Map();
  const segMap = new Map();
  const mcpServers = new Map();
  const mcpTools = new Map();
  const customAgents = new Map();
  let pathCounter = 0;
  const instantsSeen = new Set();
  const intsSeen = new Map();
  const instantRank = new Map();
  const intRank = new Map();
  const unknownKeys = new Map();
  const shapeFailures = new Map();
  let lockedKeys = 0;
  let zeroedNumbers = 0;
  const mappings = repos.map(({ from, to }) => ({
    from: fold(from).replace(/\/+$/, ''),
    insensitive: /^[A-Za-z]:/.test(from) || from.includes('\\'),
    to: fold(to).replace(/\/+$/, '')
  })).sort((a, b) => b.from.length - a.from.length);
  const bump = (map, key) => map.set(key, (map.get(key) ?? 0) + 1);
  const numbered = (map, key, make) => { if (!map.has(key)) map.set(key, make(map.size + 1)); return map.get(key); };

  function hexStream(seed, length) {
    let out = '';
    for (let i = 0; out.length < length; i++) out += createHash('sha256').update(`${salt}\0${seed}\0${i}`).digest('hex');
    return out.slice(0, length);
  }

  function format(original, attempt) {
    const seed = attempt === 0 ? original : `${original}\0${attempt}`;
    if (UUID.test(original)) {
      const h = hexStream(seed, 32);
      return `${h.slice(0, 8)}-${h.slice(8, 12)}-4${h.slice(13, 16)}-${'89ab'[parseInt(h[16], 16) % 4]}${h.slice(17, 20)}-${h.slice(20, 32)}`;
    }
    const prefix = /^(msg_|toolu_)/.exec(original)?.[1] ?? '';
    return prefix + hexStream(seed, original.length - prefix.length);
  }

  /** The ONE id map: the same original is the same replacement wherever it appears, in a value or in a file name. */
  function mapId(original) {
    const known = idMap.get(original);
    if (known !== undefined) return known;
    let replacement = format(original, 0);
    for (let attempt = 1; idsIssued.has(replacement); attempt++) replacement = format(original, attempt);
    idsIssued.add(replacement);
    idMap.set(original, replacement);
    return replacement;
  }

  function mappedPrefix(path) {
    const folded = fold(path);
    return mappings.find((m) => {
      const [a, b] = m.insensitive ? [folded.toLowerCase(), m.from.toLowerCase()] : [folded, m.from];
      return a.startsWith(b) && (a.length === b.length || a[b.length] === '/');
    });
  }

  /** One path segment -> p<n> (one number per distinct name, shared fixture-wide) + at most a short extension. */
  function mapSegment(segment, insensitive) {
    if (segment === '.' || segment === '..') return segment;
    const dot = segment.lastIndexOf('.');
    const extension = dot > 0 && SHORT_EXTENSION.test(segment.slice(dot)) ? segment.slice(dot).toLowerCase() : '';
    const stem = extension === '' ? segment : segment.slice(0, dot);
    return numbered(segMap, insensitive ? stem.toLowerCase() : stem, () => `p${++pathCounter}`) + extension;
  }

  function underRoot(path, mapping) {
    const remainder = path.slice(mapping.from.length);
    if (remainder.split(/[\\/]/).includes('..')) return undefined;
    const sep = /^[\\/]/.test(remainder) ? remainder[0] : (path.includes('\\') ? '\\' : '/');
    const root = sep === '\\' ? mapping.to.replaceAll('/', '\\') : mapping.to;
    return root + remainder.replace(/[^\\/]+/g, (segment) => mapSegment(segment, mapping.insensitive));
  }

  function otherPath(path) {
    if (/^([A-Za-z]:([\\/]|$)|[\\/])/.test(path)) return numbered(absMap, path, () => `${fold(otherRoot).replace(/\/+$/, '')}/p${++pathCounter}`);
    return path.replace(/[^\\/]+/g, (segment) => mapSegment(segment, false));
  }

  /** A real path in, a fixture path out — never the original. */
  function rewritePath(path) {
    if (path === '') return allowlist.sentinel;
    const mapping = mappedPrefix(path);
    return (mapping && underRoot(path, mapping)) ?? otherPath(path);
  }

  /** The vocabulary rewrite: a closed builtin list survives, a private name never does. undefined = not representable. */
  function rewriteVocabulary(name, text) {
    const vocabulary = allowlist.vocabularies.get(name);
    if (vocabulary.builtin.has(text)) return text;
    if (vocabulary.rewritten === undefined) return undefined; // an exact list: anything else is the sentinel
    if (name === 'tool-name') return rewriteMcpTool(text);
    return text === '' ? undefined : numbered(customAgents, text, (n) => `custom-${n}`);
  }

  function rewriteMcpTool(text) {
    const m = MCP_TOOL.exec(text);
    if (!m) return undefined;
    const server = numbered(mcpServers, m[1], (n) => n);
    const tools = numbered(mcpTools, m[1], () => new Map());
    return `mcp__s${server}__t${numbered(tools, m[2], (n) => n)}`;
  }

  function leaf(path, locked, text) {
    const field = locked ? undefined : allowlist.fields.get(path);
    if (!field) return allowlist.sentinel;
    if (field.shape === 'fixture-path') return rewritePath(text);
    if (field.shape === 'iso-utc') return instant(path, text);
    const value = field.vocabulary ? rewriteVocabulary(field.shape, text) : (field.accepts(text) ? text : undefined);
    if (value === undefined) { bump(shapeFailures, path); return allowlist.sentinel; }
    return ID_SHAPES.has(field.shape) ? mapId(value) : value;
  }

  /** A real instant -> its place on the synthetic clock; the sentinel when it is not an instant at all. */
  function instant(path, text) {
    const ms = Date.parse(text);
    if (!allowlist.fields.get(path).accepts(text) || Number.isNaN(ms)) { bump(shapeFailures, path); return allowlist.sentinel; }
    const synthetic = instantRank.get(ms);
    if (synthetic === undefined) throw new Error(`an instant at ${path} was not given to prepare()`);
    return new Date(synthetic).toISOString();
  }

  function number(path, locked, value) {
    const isInt = !locked && allowlist.fields.get(path)?.shape === 'int';
    if (isInt && KEPT_INTS.has(path) && isCount(value)) return value;
    if (isInt && isCount(value)) {
      if (value === 0) return 0;
      const synthetic = intRank.get(path)?.get(value);
      if (synthetic === undefined) throw new Error(`a count at ${path} was not given to prepare()`);
      return synthetic;
    }
    zeroedNumbers += value === 0 ? 0 : 1;
    return 0;
  }

  /** Pass one of two: the instants and counts a value will carry, in the same walk the sanitiser takes. */
  function collect(node, path, locked) {
    if (typeof node === 'string') {
      const field = locked ? undefined : allowlist.fields.get(path);
      const ms = Date.parse(node);
      if (field?.shape === 'iso-utc' && field.accepts(node) && !Number.isNaN(ms)) instantsSeen.add(ms);
    } else if (typeof node === 'number') {
      if (!locked && allowlist.fields.get(path)?.shape === 'int' && isCount(node) && node > 0 && !KEPT_INTS.has(path)) {
        if (!intsSeen.has(path)) intsSeen.set(path, new Set());
        intsSeen.get(path).add(node);
      }
    } else if (Array.isArray(node)) node.forEach((e) => collect(e, `${path}[]`, locked));
    else if (node !== null && typeof node === 'object') {
      for (const [key, value] of Object.entries(node)) {
        if (!locked && allowlist.keptKeys.has(key)) collect(value, path ? `${path}.${key}` : key, false);
        else collect(value, '', true);
      }
    }
  }

  /**
   * Fixes the synthetic clock and the count ranks for ONE fixture from everything it will contain: `values` are the lines and
   * metas to be sanitised, `extraInstants` the ISO instants of its rounds file. Rank, not offset: ties stay ties, order stays order.
   */
  function prepare(values, extraInstants = []) {
    values.forEach((v) => collect(v, '', false));
    extraInstants.forEach((text) => { const ms = Date.parse(text); if (!Number.isNaN(ms)) instantsSeen.add(ms); });
    [...instantsSeen].sort((a, b) => a - b).forEach((ms, rank) => instantRank.set(ms, SYNTHETIC_EPOCH_MS + rank * SYNTHETIC_STEP_MS));
    for (const [path, set] of intsSeen) intRank.set(path, new Map([...set].sort((a, b) => a - b).map((v, i) => [v, i + 1])));
  }

  function renameKey(parentPath, key, locked) {
    if (locked) lockedKeys++;
    else bump(unknownKeys, IDENTIFIER.test(key) ? (parentPath ? `${parentPath}.${key}` : key) : '<non-identifier key>');
  }

  function walk(node, path, locked) {
    if (typeof node === 'string') return leaf(path, locked, node);
    if (typeof node === 'number') return number(path, locked, node);
    if (Array.isArray(node)) return node.map((e) => walk(e, `${path}[]`, locked));
    if (node === null || typeof node !== 'object') return node;
    const out = {};
    let renamed = 0;
    for (const [key, value] of Object.entries(node)) {
      if (!locked && allowlist.keptKeys.has(key)) { out[key] = walk(value, path ? `${path}.${key}` : key, false); continue; }
      renameKey(path, key, locked);
      out[`k${++renamed}`] = walk(value, '', true);
    }
    return out;
  }

  const instantOrSentinel = (text) => (typeof text === 'string' && instantRank.has(Date.parse(text))
    ? new Date(instantRank.get(Date.parse(text))).toISOString() : allowlist.sentinel);

  /** The rounds file of a case: ids through the one map, the path under a fixture root, instants on the synthetic clock, a hashed sha. */
  function sanitiseRounds(rounds) {
    return {
      callerSessionId: mapId(rounds.callerSessionId),
      gateSessions: rounds.gateSessions.map((g) => ({
        id: mapId(g.id), repoPath: rewritePath(g.repoPath), openedUtc: instantOrSentinel(g.openedUtc),
        rounds: g.rounds.map((r) => ({
          number: isCount(r.number) ? r.number : 0, startedUtc: instantOrSentinel(r.startedUtc), completedUtc: instantOrSentinel(r.completedUtc),
          status: ROUND_STATUSES.has(r.status) ? r.status : allowlist.sentinel,
          headSha: createHash('sha256').update(`${salt}\0sha\0${r.headSha}`).digest('hex').slice(0, 40) }))
      }))
    };
  }

  /** The hand-built registry entry of a case, from real inputs: an id, a path, a version. */
  function sanitiseRegistry(entry) {
    return { sessionId: mapId(entry.sessionId), cwd: rewritePath(entry.cwd),
      version: allowlist.fields.get('version').accepts(entry.version) ? entry.version : allowlist.sentinel };
  }

  return {
    sanitiseValue: (value) => walk(value, '', false),
    prepare,
    sanitiseRounds,
    sanitiseRegistry,
    mapId,
    rewritePath,
    stats: () => ({
      unknownKeys: Object.fromEntries([...unknownKeys].slice(0, MAX_REPORTED_KEYS)),
      shapeFailures: Object.fromEntries(shapeFailures),
      lockedKeys,
      zeroedNumbers
    })
  };
}

// ---- the cutter ------------------------------------------------------------------------------------------------------
const AGENT_FILE = /^agent-([0-9a-f]{8,64})\.(jsonl|meta\.json)$/;
const slugOf = (cwd) => cwd.replace(/[^A-Za-z0-9]/g, '-');

function readLines(file) {
  const parsed = [];
  let torn = 0;
  for (const raw of readFileSync(file, 'utf8').split('\n')) {
    const line = raw.endsWith('\r') ? raw.slice(0, -1) : raw;
    if (line.trim() === '') continue;
    try { parsed.push(JSON.parse(line)); } catch { torn++; }
  }
  return { parsed, torn };
}

const timestampOf = (line) => {
  const t = Date.parse(line?.timestamp ?? line?.snapshot?.timestamp ?? '');
  return Number.isNaN(t) ? undefined : t;
};

const toolUseIdsOf = (line) => (Array.isArray(line?.message?.content) ? line.message.content : [])
  .filter((c) => c?.type === 'tool_use' && typeof c.id === 'string').map((c) => c.id);

function windowFilter(from, to) {
  const lo = from === undefined ? -Infinity : Date.parse(from);
  const hi = to === undefined ? Infinity : Date.parse(to);
  return { unbounded: from === undefined && to === undefined, has: (t) => t !== undefined && t >= lo && t < hi };
}

/** The main file's lines inside the window (or pinned); a line with no timestamp at all follows the line before it. */
function keepMainLines(lines, window, pinned = new Set()) {
  if (window.unbounded) return lines;
  let previous = false;
  return lines.filter((line) => { const t = timestampOf(line); previous = t === undefined ? previous : window.has(t); return previous || pinned.has(line); });
}

/** An agent file clipped to the window: only the lines inside it (or pinned). Without --clip-agents the file is kept whole. */
function keepAgentLines(lines, window, clip, pinned) {
  return clip && !window.unbounded ? lines.filter((l) => window.has(timestampOf(l)) || pinned.has(l)) : lines;
}

/** With ids in `include`: the wanted agents plus those agents, each with its whole chain of parents (a nested agent is no use without its parent's file). */
function withIncluded(wanted, loaded, include) {
  if (include.length === 0) return wanted;
  const byId = new Map(loaded.map((a) => [a.id, a]));
  const chain = new Set();
  for (let a of [...wanted, ...include.map((id) => byId.get(id))]) {
    for (; a !== undefined && !chain.has(a); a = byId.get(a.meta?.parentAgentId)) chain.add(a);
  }
  return loaded.filter((a) => chain.has(a));
}

/** The tools whose call is a write (their path argument decides where the work happened): a kept call keeps its answer. */
const WRITE_TOOLS = new Set(['Write', 'Edit', 'MultiEdit', 'NotebookEdit']);

const writeCallIdsOf = (line) => (Array.isArray(line?.message?.content) ? line.message.content : [])
  .filter((c) => c?.type === 'tool_use' && WRITE_TOOLS.has(c.name) && typeof c.id === 'string').map((c) => c.id);

const answeredIdsOf = (line) => (Array.isArray(line?.message?.content) ? line.message.content : [])
  .filter((c) => c?.type === 'tool_result' && typeof c.tool_use_id === 'string').map((c) => c.tool_use_id);

/**
 * The lines of ONE file that answer a write call kept in it: pinned into the cut like a launching line, so the answered shape
 * (a call and its result) survives a window that ends between them. A reader that attributes by an answered write needs both halves.
 */
function answerLines(keptLines, fileLines) {
  const wanted = new Set(keptLines.flatMap(writeCallIdsOf));
  return wanted.size === 0 ? new Set() : new Set(fileLines.filter((line) => answeredIdsOf(line).some((id) => wanted.has(id))));
}

/**
 * Cuts ONE file, and cuts it AGAIN when it must: `cut(extra)` keeps the window's lines plus the pinned ones and `extra`. When the
 * first cut keeps a write call whose answering line it dropped, the file is re-cut with those lines pinned too. One hop is the
 * fixpoint: an answering line is a `user` line of `tool_result` blocks, so pinning it adds no new write call to answer.
 */
function cutWithAnswers(fileLines, cut) {
  const kept = cut(new Set());
  const answers = answerLines(kept, fileLines);
  return answers.size === 0 ? kept : cut(answers);
}

/** The one line (anywhere) that launched each wanted agent: kept even outside the window so the meta's toolUseId still joins. */
function launchLines(wanted, allLines) {
  const wantedIds = new Set(wanted.map((a) => a.meta?.toolUseId).filter(Boolean));
  return new Set(allLines.filter((line) => toolUseIdsOf(line).some((id) => wantedIds.has(id))));
}

function discoverAgents(subagentsDir) {
  const found = new Map();
  let skipped = 0;
  const names = existsSync(subagentsDir) ? readdirSync(subagentsDir).sort(compareOrdinal) : [];
  for (const name of names) {
    const m = AGENT_FILE.exec(name);
    if (!m) { skipped++; continue; }
    const entry = found.get(m[1]) ?? { id: m[1] };
    entry[m[2] === 'jsonl' ? 'file' : 'meta'] = join(subagentsDir, name);
    found.set(m[1], entry);
  }
  return { agents: [...found.values()], skipped };
}

/** The deepest existing ancestor's real path plus the rest — so a not-yet-created folder and a symlink both resolve. */
function realResolve(path) {
  let head = resolve(path);
  const rest = [];
  while (!existsSync(head) && dirname(head) !== head) { rest.unshift(basename(head)); head = dirname(head); }
  return join(realpathSync.native(head), ...rest);
}

/** Exit-65 refusal: the cutter writes only inside the fixture folder unless the caller says otherwise. */
export function checkOutputAllowed(outDir, allowedRoot) {
  const rel = relative(realResolve(allowedRoot), realResolve(outDir));
  return rel.startsWith('..') || isAbsolute(rel) ? refuse(`the output directory is outside ${allowedRoot} (use --out-anywhere for a scratch cut)`) : accept(outDir);
}

/** "real=fixture" -> { from, to }; the last "=" splits, since a real path may contain one. A malformed pair is a refusal, not a throw. */
export function parseRepoMapping(text) {
  const at = text.lastIndexOf('=');
  if (at <= 0 || at === text.length - 1) return refuse('--repo wants <real-prefix>=<fixture-root>');
  return accept({ from: text.slice(0, at), to: text.slice(at + 1) });
}

/** What a failure that nobody planned for means to an operator: an exit code and a sentence that names no path and no message. */
export function explainFailure(error) {
  const known = {
    ENOENT: [74, 'an input file or folder does not exist'],
    EACCES: [74, 'permission denied reading an input or writing the output'],
    EPERM: [74, 'permission denied reading an input or writing the output'],
    EISDIR: [74, 'an input that should be a file is a directory'],
    ENOTDIR: [74, 'a path component that should be a folder is a file'],
    ENOSPC: [74, 'the disk is full'],
    EMFILE: [74, 'too many files are open'],
  }[error?.code];
  if (known) return { exit: known[0], sentence: known[1] };
  if (error instanceof SyntaxError) return { exit: 65, sentence: 'an input is not valid JSON' };
  return { exit: 74, sentence: `unexpected ${error?.name ?? 'failure'} (its message is withheld: it could name a path)` };
}

/** Bytes of a session's main file plus every agent-<hex>.jsonl|.meta.json beside it: what the cutter would load. */
function sessionInputBytes(sessionFile, sessionName) {
  const dir = join(dirname(sessionFile), sessionName, 'subagents');
  const agents = existsSync(dir) ? readdirSync(dir).filter((n) => AGENT_FILE.test(n)).map((n) => statSync(join(dir, n)).size) : [];
  return agents.reduce((sum, size) => sum + size, statSync(sessionFile).size);
}

const mib = (bytes) => Number((bytes / 1048576).toFixed(1));

const jsonl = (objects) => objects.map((o) => `${JSON.stringify(o)}\n`).join('');

function safeJson(text) {
  try { return JSON.parse(text); } catch { return undefined; }
}

const roundInstants = (rounds) => rounds.gateSessions.flatMap((g) => [g.openedUtc, ...g.rounds.flatMap((r) => [r.startedUtc, r.completedUtc])]);

/**
 * Writes a case's side files from a REAL case spec ({ rounds: {callerSessionId, gateSessions}, registry?: {pid, sessionId, cwd, version} }):
 * rounds.json, and sessions/<pid>.json when the spec has a registry entry. Everything goes through the sanitiser: ids, path, clock.
 */
function writeCaseFiles(sanitiser, outDir, caseSpec) {
  writeFileSync(join(outDir, 'rounds.json'), `${JSON.stringify(sanitiser.sanitiseRounds(caseSpec.rounds), null, 2)}\n`);
  if (caseSpec.registry === undefined) return;
  mkdirSync(join(outDir, 'sessions'), { recursive: true });
  writeFileSync(join(outDir, 'sessions', `${caseSpec.registry.pid}.json`), `${JSON.stringify(sanitiser.sanitiseRegistry(caseSpec.registry))}\n`);
}

/** What every cut checks before it reads or writes anything: the roots, the registry pid, an empty output folder. */
function preflight({ outDir, repos, otherRoot, caseSpec }) {
  const roots = checkRoots(repos, otherRoot);
  if (!roots.ok) return roots;
  if (caseSpec?.registry !== undefined && !/^[0-9]{1,9}$/.test(String(caseSpec.registry.pid))) return refuse('a registry pid is digits only');
  return existsSync(outDir) && readdirSync(outDir).length > 0 ? refuse('the output directory already holds files') : accept(true);
}

/** A case with no transcript at all (a caller that ran without persistence): just its rounds file and registry entry. Returns a tagged result. */
export function cutCaseWithoutTranscript({ outDir, allowlist, salt, repos = [], otherRoot, caseSpec }) {
  const checked = preflight({ outDir, repos, otherRoot, caseSpec });
  if (!checked.ok) return checked;
  const sanitiser = createSanitiser({ allowlist, salt, repos, otherRoot });
  sanitiser.prepare([], roundInstants(caseSpec.rounds));
  mkdirSync(outDir, { recursive: true });
  writeCaseFiles(sanitiser, outDir, caseSpec);
  return accept({ mainLines: 0, agentFiles: 0, metas: 0, joinBreaks: 0, tornLines: 0, skippedFiles: 0, ...sanitiser.stats() });
}

/**
 * Cuts one caller session (main file + its subagent files and metas) into outDir/projects/<slug>/…, sanitised.
 * Returns `{ ok: true, value: counts }` — counts only, never a value from the input — or `{ ok: false, refusal }` BEFORE anything is
 * read: a destination root off the synthetic policy, a file that is not <sessionId>.jsonl, a non-empty output folder, or session files
 * totalling more than `maxInputBytes` (default DEFAULT_MAX_INPUT_BYTES: every file is parsed whole before the window applies).
 */
export function cutSession({ sessionFile, outDir, allowlist, salt, repos = [], otherRoot, from, to, dropAgentFiles = [], dropMetas = [],
  clipAgents = false, includeAgents = [], caseSpec, maxInputBytes = DEFAULT_MAX_INPUT_BYTES }) {
  const checked = preflight({ outDir, repos, otherRoot, caseSpec });
  if (!checked.ok) return checked;
  const sessionName = /^(.+)\.jsonl$/.exec(basename(sessionFile))?.[1] ?? '';
  if (!UUID.test(sessionName)) return refuse('the session file must be named <sessionId>.jsonl');
  const bytes = sessionInputBytes(sessionFile, sessionName);
  if (bytes > maxInputBytes) return refuse(`the session's files total ${mib(bytes)} MiB which is over the limit of ${mib(maxInputBytes)} MiB (the cutter parses them whole before the window applies; raise the limit deliberately with --max-input-mb)`);

  const sanitiser = createSanitiser({ allowlist, salt, repos, otherRoot });
  const window = windowFilter(from, to);
  const main = readLines(sessionFile);
  const { agents, skipped } = discoverAgents(join(dirname(sessionFile), sessionName, 'subagents'));
  let torn = main.torn;

  const loaded = agents.map((a) => {
    const lines = a.file ? readLines(a.file) : { parsed: [], torn: 0 };
    torn += lines.torn;
    return { ...a, lines: lines.parsed, meta: a.meta ? safeJson(readFileSync(a.meta, 'utf8')) : undefined };
  });

  const launchedAt = new Map();
  for (const line of [...main.parsed, ...loaded.flatMap((a) => a.lines)])
    for (const id of toolUseIdsOf(line)) if (!launchedAt.has(id)) launchedAt.set(id, timestampOf(line));
  const inWindow = loaded.filter((a) => window.unbounded || window.has(launchedAt.get(a.meta?.toolUseId)) || window.has(timestampOf(a.lines[0])));
  const wanted = withIncluded(inWindow, loaded, includeAgents);
  const pinned = clipAgents || includeAgents.length > 0 ? launchLines(wanted, [...main.parsed, ...loaded.flatMap((a) => a.lines)]) : new Set();
  const pinnedWith = (extra) => (extra.size === 0 ? pinned : new Set([...pinned, ...extra]));

  const keptMain = cutWithAnswers(main.parsed, (extra) => keepMainLines(main.parsed, window, pinnedWith(extra)));
  const cutAgents = wanted.map((a) => ({ ...a, lines: cutWithAnswers(a.lines, (extra) => keepAgentLines(a.lines, window, clipAgents, pinnedWith(extra))) }));
  const writtenAgents = cutAgents.filter((w) => w.file && !dropAgentFiles.includes(w.id));
  const writtenMetas = cutAgents.filter((w) => w.meta && !dropMetas.includes(w.id));
  sanitiser.prepare([...keptMain, ...writtenAgents.flatMap((a) => a.lines), ...writtenMetas.map((a) => a.meta)], caseSpec ? roundInstants(caseSpec.rounds) : []);
  const sanitisedMain = keptMain.map((l) => sanitiser.sanitiseValue(l));
  const cwd = sanitisedMain.find((l) => typeof l.cwd === 'string')?.cwd;
  const sessionDir = join(outDir, 'projects', cwd === undefined ? 'fixture' : slugOf(cwd));
  const agentDir = join(sessionDir, sanitiser.mapId(sessionName), 'subagents');
  mkdirSync(agentDir, { recursive: true });
  writeFileSync(join(sessionDir, `${sanitiser.mapId(sessionName)}.jsonl`), jsonl(sanitisedMain));

  const keptToolUses = new Set(keptMain.flatMap(toolUseIdsOf));
  let agentFiles = 0;
  let metas = 0;
  let joinBreaks = 0;
  for (const a of writtenAgents) {
    writeFileSync(join(agentDir, `agent-${sanitiser.mapId(a.id)}.jsonl`), jsonl(a.lines.map((l) => sanitiser.sanitiseValue(l))));
    a.lines.flatMap(toolUseIdsOf).forEach((id) => keptToolUses.add(id));
    agentFiles++;
  }
  for (const a of writtenMetas) {
    writeFileSync(join(agentDir, `agent-${sanitiser.mapId(a.id)}.meta.json`), `${JSON.stringify(sanitiser.sanitiseValue(a.meta))}\n`);
    metas++;
    if (!keptToolUses.has(a.meta.toolUseId)) joinBreaks++;
  }
  if (caseSpec) writeCaseFiles(sanitiser, outDir, caseSpec);
  return accept({ mainLines: keptMain.length, agentFiles, metas, joinBreaks, tornLines: torn, skippedFiles: skipped, ...sanitiser.stats() });
}
