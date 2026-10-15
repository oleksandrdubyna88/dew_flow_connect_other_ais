// What the caller's Claude Code transcripts look like, as COUNTS (E1.S2 of PLAN_a_round_records_who_wrote_the_code_and_how_much_E1.md;
// predictions in research/RESULTS_caller_transcripts_on_both_sides.md). Pure functions over parsed lines and directory listings,
// plus the one read-only line reader. Node 20 APIs only: the same file runs inside WSL, where `node:sqlite` does not exist.
//
// THE RULE THIS FILE LIVES UNDER: nothing it returns can carry text. A value reaches the report only as a count, a boolean, a
// duration, or a NAME that passed a shape check below (a version, a model id, an effort level, a key or type name). A prompt, a
// path, a command, a branch or a description is read to be classified and dropped; a name that fails its shape becomes
// `(other)`. Nothing here writes — files are opened for reading only, and no path is ever composed from a transcript value
// (a caller id is used to look something UP only after `isSessionId` says it is a UUID).
import { closeSync, openSync, readSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { performance } from 'node:perf_hooks';
import { StringDecoder } from 'node:string_decoder';

export const USAGE_TERMS = ['input_tokens', 'output_tokens', 'cache_creation_input_tokens', 'cache_read_input_tokens'];
const WRITE_TOOLS = new Set(['Edit', 'Write', 'MultiEdit', 'NotebookEdit']);
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
const NAME_CAP = 200;
const OTHER = '(other)';
const NONE = '(none)';

// ---- shapes: the only ways a transcript value may become output text -------------------------------------------------------

export const isSessionId = (value) => typeof value === 'string' && UUID.test(value);

const shapeOf = (pattern, fallback = OTHER) => (value) => (typeof value === 'string' && pattern.test(value) ? value : fallback);
/** A key or a line/content `type`: identifier-shaped, short. */
export const safeName = shapeOf(/^[A-Za-z][A-Za-z0-9._-]{0,47}$/u);
/** `2.1.289`, optionally with a pre-release tail. */
export const safeVersion = shapeOf(/^\d{1,3}(?:\.\d{1,4}){1,3}(?:-[a-z0-9.]{1,16})?$/u);
/** An effort level: lower-case letters only, so a sentence cannot pass. */
export const safeEnum = shapeOf(/^[a-z]{2,12}$/u);
/** A model id or alias or vendor label: lower-case words joined by `-` or `.`; `<synthetic>` is Claude Code's own. */
export const safeModel = shapeOf(/^<?[a-z][a-z0-9]*(?:[-.][a-z0-9]+)*>?$/u);
const safeDepth = (value) => (Number.isInteger(value) && value >= 0 && value < 100 ? String(value) : OTHER);

// ---- tallies ---------------------------------------------------------------------------------------------------------------

export const newTable = () => Object.create(null);
const bump = (table, key, by = 1) => {
  table[key] = (table[key] ?? 0) + by;
};
export const sortedObject = (table) => Object.fromEntries(Object.entries(table).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)));
const addName = (set, key) => {
  if (set.size < NAME_CAP || set.has(key)) set.add(key);
};
const namesOf = (set) => [...set].sort();
const isObject = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);

/** Nearest-rank percentile of numbers; `null` (unknown, not zero) when there are none. */
export function percentile(values, fraction) {
  if (values.length === 0) return null;
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.min(sorted.length - 1, Math.max(0, Math.ceil(fraction * sorted.length) - 1))];
}

// ---- reading ---------------------------------------------------------------------------------------------------------------

/** Calls `onLine` with each line of a file, read in chunks and opened for reading only (libuv shares it with the writer). */
export function forEachLine(path, onLine) {
  const fd = openSync(path, 'r');
  try {
    const chunk = Buffer.allocUnsafe(1 << 20);
    const decoder = new StringDecoder('utf8');
    let carry = '';
    for (let read = readSync(fd, chunk, 0, chunk.length, null); read > 0; read = readSync(fd, chunk, 0, chunk.length, null)) {
      const parts = (carry + decoder.write(chunk.subarray(0, read))).split('\n');
      carry = parts.pop();
      for (const part of parts) onLine(part.endsWith('\r') ? part.slice(0, -1) : part);
    }
    carry += decoder.end();
    if (carry !== '') onLine(carry.endsWith('\r') ? carry.slice(0, -1) : carry);
  } finally {
    closeSync(fd);
  }
}

/** `{ ok, value }`; a torn or foreign line is `ok: false`, never an exception and never an echo of its text. */
export function parseJsonLine(text) {
  try {
    return { ok: true, value: JSON.parse(text) };
  } catch {
    return { ok: false, value: undefined };
  }
}

const listDir = (path) => {
  try {
    return readdirSync(path, { withFileTypes: true });
  } catch {
    return [];
  }
};
const isDirectory = (path) => {
  try {
    return statSync(path).isDirectory();
  } catch {
    return false;
  }
};
const sizeOf = (path) => {
  try {
    return statSync(path).size;
  } catch {
    return 0;
  }
};

// ---- the project tree ------------------------------------------------------------------------------------------------------

/** Every `<uuid>.jsonl` one level under the projects folder (one listing per project folder, no slug computed), keyed by lower-cased session id. */
export function indexProjects(projectsDir) {
  const sessions = new Map();
  let otherJsonl = 0;
  for (const project of listDir(projectsDir).filter((entry) => entry.isDirectory())) {
    const folder = join(projectsDir, project.name);
    for (const entry of listDir(folder).filter((candidate) => candidate.isFile() && candidate.name.endsWith('.jsonl'))) {
      const id = entry.name.slice(0, -'.jsonl'.length);
      if (!isSessionId(id)) {
        otherJsonl += 1;
        continue;
      }
      const found = sessions.get(id.toLowerCase()) ?? [];
      found.push({ main: join(folder, entry.name), dir: join(folder, id) });
      sessions.set(id.toLowerCase(), found);
    }
  }
  return { sessions, otherJsonl };
}

/** A session's files: the main transcript, and what sits under `<id>/` (names are compared, never printed). */
export function describeSession(entry) {
  const subagents = join(entry.dir, 'subagents');
  const listing = listDir(subagents);
  const agentFiles = new Map();
  const metaFiles = new Map();
  let otherEntries = 0;
  for (const item of listing) {
    const agent = /^agent-(.+)\.jsonl$/u.exec(item.name);
    const meta = /^agent-(.+)\.meta\.json$/u.exec(item.name);
    if (item.isFile() && meta) metaFiles.set(meta[1], join(subagents, item.name));
    else if (item.isFile() && agent) agentFiles.set(agent[1], join(subagents, item.name));
    else otherEntries += 1;
  }
  const hasToolResults = listDir(entry.dir).some((item) => item.isDirectory() && item.name === 'tool-results');
  const bytes = sizeOf(entry.main) + [...agentFiles.values()].reduce((sum, path) => sum + sizeOf(path), 0);
  return { main: entry.main, hasSubagents: isDirectory(subagents), hasToolResults, agentFiles, metaFiles, otherEntries, bytes };
}

// ---- Q5: coverage ----------------------------------------------------------------------------------------------------------

/**
 * Which of five verdicts a round gets. `endsMs` are the last timestamps (epoch ms, NaN when none parsed) of the caller's
 * transcript files found on disk; an empty list means no file. The round is covered only when a transcript REACHES its start.
 */
export function classifyRound(round, endsMs) {
  if (!isSessionId(round.caller)) return 'notASessionId';
  if (endsMs.length === 0) return 'notFound';
  const start = Date.parse(round.started_utc);
  if (Number.isNaN(start)) return 'startUnreadable';
  return endsMs.some((end) => end >= start) ? 'found' : 'foundButEndsBefore';
}

export function summariseCoverage(rounds, endsOf) {
  const verdicts = { found: 0, foundButEndsBefore: 0, notFound: 0, notASessionId: 0, startUnreadable: 0 };
  const byVendor = newTable();
  const callers = new Set();
  for (const round of rounds) {
    const caller = typeof round.caller === 'string' ? round.caller : '';
    const verdict = classifyRound({ ...round, caller }, isSessionId(caller) ? endsOf(caller.toLowerCase()) : []);
    verdicts[verdict] += 1;
    const vendor = round.caller_vendor === '' || round.caller_vendor == null ? '(blank)' : safeModel(round.caller_vendor);
    byVendor[vendor] ??= { rounds: 0, found: 0, foundButEndsBefore: 0, notFound: 0, notASessionId: 0, startUnreadable: 0 };
    byVendor[vendor].rounds += 1;
    byVendor[vendor][verdict] += 1;
    callers.add(caller);
  }
  const ids = [...callers.keys()];
  const real = ids.filter(isSessionId);
  return {
    rounds: { total: rounds.length, ...verdicts },
    callers: {
      distinct: ids.length,
      notASessionId: ids.length - real.length,
      withTranscript: real.filter((id) => endsOf(id.toLowerCase()).length > 0).length,
      withoutTranscript: real.filter((id) => endsOf(id.toLowerCase()).length === 0).length,
    },
    byVendor: sortedObject(byVendor),
  };
}

// ---- Q3: a message written as several lines ---------------------------------------------------------------------------------

const usageOf = (message) => USAGE_TERMS.map((term) => (isObject(message.usage) && Number.isFinite(message.usage[term]) ? message.usage[term] : null));

/**
 * `groups` holds, per message id, its lines as `{ t, order, usage }`. Reports whether the four usage terms are identical across
 * a message's lines and, where they are not, whether the LAST line (timestamp, then file order) holds the per-term maximum.
 * It answers; it does not choose the counting rule.
 */
export function summariseMessages(groups) {
  const out = { ids: 0, lines: 0, maxLinesPerId: 0, idsWithSeveralLines: 0, linesWithoutUsage: 0, differing: 0, lastHoldsMax: 0, lastLacksMax: 0, differingByTerm: newTable() };
  const spans = [];
  for (const lines of groups) {
    out.ids += 1;
    out.lines += lines.length;
    out.maxLinesPerId = Math.max(out.maxLinesPerId, lines.length);
    out.linesWithoutUsage += lines.filter((line) => line.usage.every((value) => value === null)).length;
    if (lines.length < 2) continue;
    out.idsWithSeveralLines += 1;
    spans.push(spanMs(lines));
    const differs = USAGE_TERMS.map((term, at) => new Set(lines.map((line) => line.usage[at])).size > 1);
    if (!differs.some(Boolean)) continue;
    out.differing += 1;
    USAGE_TERMS.forEach((term, at) => differs[at] && bump(out.differingByTerm, term));
    bump(out, lastLineHoldsMaximum(lines) ? 'lastHoldsMax' : 'lastLacksMax');
  }
  return { ...out, differingByTerm: sortedObject(out.differingByTerm), spanMs: summariseSpans(spans) };
}

/** Milliseconds between the first and the last line of one message id (lines without a readable timestamp are left out). */
function spanMs(lines) {
  const times = lines.map((line) => line.t).filter((t) => !Number.isNaN(t));
  return times.length < 2 ? 0 : Math.max(...times) - Math.min(...times);
}

/** Counts and quantiles of the per-id spans (Q4 follow-up: lines of ONE message are not written together). Numbers only. */
function summariseSpans(spans) {
  const sorted = [...spans].sort((a, b) => a - b);
  const at = (q) => (sorted.length === 0 ? 0 : sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * q))]);
  const over = (ms) => sorted.filter((span) => span > ms).length;
  return { ids: sorted.length, p50: at(0.5), p99: at(0.99), max: at(1), over1s: over(1000), over10s: over(10000), over60s: over(60000), over1h: over(3600000) };
}

function lastLineHoldsMaximum(lines) {
  const key = (line) => (Number.isNaN(line.t) ? Number.NEGATIVE_INFINITY : line.t);
  const ordered = [...lines].sort((a, b) => key(a) - key(b) || a.order - b.order);
  const last = ordered[ordered.length - 1];
  return USAGE_TERMS.every((term, at) => {
    const seen = ordered.map((line) => line.usage[at]).filter((value) => value !== null);
    return seen.length === 0 || last.usage[at] === Math.max(...seen);
  });
}

// ---- Q8: where a write lands -----------------------------------------------------------------------------------------------

const emptyWrites = () => ({ total: 0, missing: 0, absolute: 0, relative: 0, separators: newTable(), drive: newTable(), underCwd: newTable() });
const normalise = (path) => path.replaceAll('\\', '/').replace(/\/+$/u, '');

/** Booleans about a path against a working directory — never the path. */
export function classifyPath(path, cwd) {
  if (typeof path !== 'string' || path === '') return { missing: true };
  const absolute = /^(?:[A-Za-z]:[\\/]|[\\/]{2}|\/)/u.test(path);
  const slash = path.includes('/');
  const backslash = path.includes('\\');
  const drive = /^[A-Za-z]:/u.exec(path);
  return {
    missing: false,
    absolute,
    separators: slash && backslash ? 'mixed' : slash ? 'slash' : backslash ? 'backslash' : 'none',
    drive: drive ? (drive[0][0] === drive[0][0].toLowerCase() ? 'lower' : 'upper') : 'none',
    underCwd: absolute && typeof cwd === 'string' && cwd !== '' ? (isUnder(path, cwd) ? 'yes' : 'no') : 'unknown',
  };
}

function isUnder(path, cwd) {
  const folded = /^[A-Za-z]:/u.test(cwd);
  const [p, c] = [normalise(path), normalise(cwd)].map((text) => (folded ? text.toLowerCase() : text));
  return c !== '' && (p === c || p.startsWith(`${c}/`));
}

function countWrite(writes, input, cwd) {
  const shape = classifyPath(input?.file_path ?? input?.notebook_path, cwd);
  writes.total += 1;
  if (shape.missing) return void (writes.missing += 1);
  writes[shape.absolute ? 'absolute' : 'relative'] += 1;
  bump(writes.separators, shape.separators);
  bump(writes.drive, shape.drive);
  bump(writes.underCwd, shape.underCwd);
}

// ---- the line fold ---------------------------------------------------------------------------------------------------------

export function newAggregate() {
  return {
    files: { read: 0, unreadable: 0, lines: 0, unparseable: 0, unparseableAtEnd: 0 },
    lineTypes: newTable(),
    models: newTable(),
    requestedModels: newTable(),
    versions: new Map(),
    messages: new Map(),
    messageFirstFile: new Map(),
    idsInSeveralFiles: 0,
    writes: { main: emptyWrites(), agent: emptyWrites() },
    toolResults: { total: 0, isErrorTrue: 0, isErrorFalse: 0, isErrorAbsent: 0 },
    contentAsString: 0,
    taskNamedToolUses: 0,
    firstLine: { files: 0, withTimestamp: 0, withMessage: 0, types: newTable() },
    layout: { sessions: 0, withSubagentsDir: 0, withToolResultsDir: 0, withAgentFiles: 0, withMetas: 0, agentFiles: 0, metas: 0, otherEntries: 0, mainFiles: 0 },
    agents: { agentToolUses: 0, metas: 0, metasUnparseable: 0, metasWithoutFile: 0, filesWithoutMeta: 0, metasUnmatchedInMain: 0, metasUnmatchedAnywhere: 0, metasWithParent: 0, toolUsesWithoutMeta: 0, sessionsAgreeing: 0, sessionsDisagreeing: 0, spawnDepth: newTable(), requestShape: newTable(), modelAlias: newTable() },
  };
}

function versionBucket(aggregate, version) {
  if (!aggregate.versions.has(version)) {
    aggregate.versions.set(version, { lines: 0, assistantLines: 0, withEffort: 0, effort: newTable(), metas: 0, metasWithModel: 0, metasWithoutModel: 0, topKeys: new Set(), messageKeys: new Set(), contentTypes: new Set(), contentKeys: new Set() });
  }
  return aggregate.versions.get(version);
}

function foldContent(aggregate, bucket, line, ctx) {
  const content = line.message.content;
  if (typeof content === 'string') return void (aggregate.contentAsString += 1);
  if (!Array.isArray(content)) return;
  for (const block of content.filter(isObject)) {
    addName(bucket.contentTypes, safeName(block.type));
    Object.keys(block).forEach((key) => addName(bucket.contentKeys, safeName(key)));
    if (block.type === 'tool_use') foldToolUse(aggregate, block, line, ctx);
    else if (block.type === 'tool_result') foldToolResult(aggregate, block);
  }
}

function foldToolUse(aggregate, block, line, ctx) {
  if (block.name === 'Agent' && typeof block.id === 'string') {
    ctx.session.anyAgentUseIds.add(block.id);
    if (ctx.role === 'main') ctx.session.mainAgentUseIds.add(block.id);
  }
  if (block.name === 'Task') aggregate.taskNamedToolUses += 1;
  if (WRITE_TOOLS.has(block.name)) countWrite(aggregate.writes[ctx.role], block.input, line.cwd);
}

function foldToolResult(aggregate, block) {
  aggregate.toolResults.total += 1;
  if (block.is_error === true) aggregate.toolResults.isErrorTrue += 1;
  else if (block.is_error === false) aggregate.toolResults.isErrorFalse += 1;
  else aggregate.toolResults.isErrorAbsent += 1;
}

function foldAssistant(aggregate, bucket, line, ctx) {
  bucket.assistantLines += 1;
  if ('effort' in line) {
    bucket.withEffort += 1;
    bump(bucket.effort, safeEnum(line.effort));
  }
  Object.keys(line.message).forEach((key) => addName(bucket.messageKeys, safeName(key)));
  if (typeof line.message.model === 'string') bump(aggregate.models, safeModel(line.message.model));
  if (typeof line.requestedModel === 'string') bump(aggregate.requestedModels, safeModel(line.requestedModel));
  if (typeof line.message.id === 'string') recordMessageLine(aggregate, line, ctx);
}

function recordMessageLine(aggregate, line, ctx) {
  const key = `${ctx.fileIndex}\u0000${line.message.id}`;
  const lines = aggregate.messages.get(key) ?? [];
  lines.push({ t: Date.parse(line.timestamp), order: ctx.order, usage: usageOf(line.message) });
  aggregate.messages.set(key, lines);
  const first = aggregate.messageFirstFile.get(line.message.id);
  if (first === undefined) aggregate.messageFirstFile.set(line.message.id, ctx.fileIndex);
  else if (first !== ctx.fileIndex && lines.length === 1) aggregate.idsInSeveralFiles += 1;
}

/** Folds one PARSED line. `ctx`: `{ role: 'main'|'agent', fileIndex, order (within its file), session, file }`. */
export function foldLine(aggregate, line, ctx) {
  if (!isObject(line)) return;
  const version = typeof line.version === 'string' ? safeVersion(line.version) : NONE;
  const bucket = versionBucket(aggregate, version);
  bucket.lines += 1;
  ctx.file.version ??= typeof line.version === 'string' ? version : undefined;
  Object.keys(line).forEach((key) => addName(bucket.topKeys, safeName(key)));
  bump(aggregate.lineTypes, typeof line.type === 'string' ? safeName(line.type) : NONE);
  const stamp = Date.parse(line.timestamp);
  if (!Number.isNaN(stamp)) ctx.file.lastStamp = stamp;
  if (isObject(line.message)) {
    if (line.type === 'assistant') foldAssistant(aggregate, bucket, line, ctx);
    foldContent(aggregate, bucket, line, ctx);
  }
}

/** Reads one transcript file into the aggregate; a file that cannot be read is counted and skipped. */
export function foldFile(aggregate, path, ctx) {
  const file = { lastStamp: Number.NaN, version: undefined };
  let first = true;
  let lastBad = false;
  let order = 0;
  try {
    forEachLine(path, (text) => {
      if (text === '') return;
      aggregate.files.lines += 1;
      const parsed = parseJsonLine(text);
      lastBad = !parsed.ok;
      if (!parsed.ok) return void (aggregate.files.unparseable += 1);
      if (first && ctx.role === 'main') foldFirstLine(aggregate, parsed.value);
      first = false;
      order += 1;
      foldLine(aggregate, parsed.value, { ...ctx, order, file });
    });
    aggregate.files.read += 1;
    if (lastBad) aggregate.files.unparseableAtEnd += 1;
  } catch {
    aggregate.files.unreadable += 1;
  }
  return file;
}

function foldFirstLine(aggregate, line) {
  aggregate.firstLine.files += 1;
  if (!isObject(line)) return;
  bump(aggregate.firstLine.types, typeof line.type === 'string' ? safeName(line.type) : NONE);
  if (typeof line.timestamp === 'string') aggregate.firstLine.withTimestamp += 1;
  if ('message' in line) aggregate.firstLine.withMessage += 1;
}

// ---- Q1, Q7: a whole session ----------------------------------------------------------------------------------------------

function foldMeta(aggregate, path, agentVersion) {
  const bucket = versionBucket(aggregate, agentVersion);
  const parsed = readJson(path);
  aggregate.agents.metas += 1;
  bucket.metas += 1;
  if (!isObject(parsed)) {
    aggregate.agents.metasUnparseable += 1;
    return undefined;
  }
  if (typeof parsed.model === 'string' && parsed.model !== '') {
    bucket.metasWithModel += 1;
    bump(aggregate.agents.modelAlias, safeModel(parsed.model));
  } else bucket.metasWithoutModel += 1;
  if ('parentAgentId' in parsed) aggregate.agents.metasWithParent += 1;
  if ('spawnDepth' in parsed) bump(aggregate.agents.spawnDepth, safeDepth(parsed.spawnDepth));
  if ('requestShape' in parsed) bump(aggregate.agents.requestShape, safeModel(parsed.requestShape));
  return typeof parsed.toolUseId === 'string' ? parsed.toolUseId : undefined;
}

function readJson(path) {
  let text = '';
  try {
    forEachLine(path, (line) => void (text += `${line}\n`));
  } catch {
    return undefined;
  }
  const parsed = parseJsonLine(text);
  return parsed.ok ? parsed.value : undefined;
}

/** Folds a whole caller session: its main transcript, every agent file and every meta. Returns the main file's last stamp. */
export function foldSession(aggregate, description, fileIndexStart) {
  const session = { mainAgentUseIds: new Set(), anyAgentUseIds: new Set() };
  const main = foldFile(aggregate, description.main, { role: 'main', fileIndex: fileIndexStart, session });
  const agentVersions = new Map();
  let fileIndex = fileIndexStart;
  for (const [id, path] of description.agentFiles) {
    fileIndex += 1;
    agentVersions.set(id, foldFile(aggregate, path, { role: 'agent', fileIndex, session }).version ?? NONE);
  }
  foldLayout(aggregate, description);
  foldAgents(aggregate, description, session, agentVersions);
  return { mainEnd: main.lastStamp, files: fileIndex - fileIndexStart + 1 };
}

function foldLayout(aggregate, description) {
  const layout = aggregate.layout;
  layout.sessions += 1;
  layout.mainFiles += 1;
  layout.withSubagentsDir += description.hasSubagents ? 1 : 0;
  layout.withToolResultsDir += description.hasToolResults ? 1 : 0;
  layout.withAgentFiles += description.agentFiles.size > 0 ? 1 : 0;
  layout.withMetas += description.metaFiles.size > 0 ? 1 : 0;
  layout.agentFiles += description.agentFiles.size;
  layout.metas += description.metaFiles.size;
  layout.otherEntries += description.otherEntries;
}

function foldAgents(aggregate, description, session, agentVersions) {
  const agents = aggregate.agents;
  const toolUseIds = new Set();
  for (const [id, path] of description.metaFiles) {
    const toolUseId = foldMeta(aggregate, path, agentVersions.get(id) ?? NONE);
    toolUseIds.add(toolUseId);
    if (!description.agentFiles.has(id)) agents.metasWithoutFile += 1;
    if (!session.mainAgentUseIds.has(toolUseId)) agents.metasUnmatchedInMain += 1;
    if (!session.anyAgentUseIds.has(toolUseId)) agents.metasUnmatchedAnywhere += 1;
  }
  agents.filesWithoutMeta += [...description.agentFiles.keys()].filter((id) => !description.metaFiles.has(id)).length;
  agents.agentToolUses += session.mainAgentUseIds.size;
  agents.toolUsesWithoutMeta += [...session.mainAgentUseIds].filter((id) => !toolUseIds.has(id)).length;
  agents[session.mainAgentUseIds.size === description.metaFiles.size ? 'sessionsAgreeing' : 'sessionsDisagreeing'] += 1;
}

export function renderAggregate(aggregate) {
  const versions = {};
  for (const [name, bucket] of [...aggregate.versions].sort(([a], [b]) => (a < b ? -1 : 1))) {
    versions[name] = {
      lines: bucket.lines, assistantLines: bucket.assistantLines, withEffort: bucket.withEffort, effort: sortedObject(bucket.effort),
      metas: bucket.metas, metasWithModel: bucket.metasWithModel, metasWithoutModel: bucket.metasWithoutModel,
      topKeys: namesOf(bucket.topKeys), messageKeys: namesOf(bucket.messageKeys), contentTypes: namesOf(bucket.contentTypes), contentKeys: namesOf(bucket.contentKeys),
    };
  }
  const writes = (table) => ({ ...table, separators: sortedObject(table.separators), drive: sortedObject(table.drive), underCwd: sortedObject(table.underCwd) });
  return {
    files: aggregate.files,
    layout: aggregate.layout,
    firstLine: { ...aggregate.firstLine, types: sortedObject(aggregate.firstLine.types) },
    lineTypes: sortedObject(aggregate.lineTypes),
    models: sortedObject(aggregate.models),
    requestedModels: sortedObject(aggregate.requestedModels),
    versions,
    messages: { ...summariseMessages(aggregate.messages.values()), idsInSeveralFiles: aggregate.idsInSeveralFiles },
    agents: { ...aggregate.agents, spawnDepth: sortedObject(aggregate.agents.spawnDepth), requestShape: sortedObject(aggregate.agents.requestShape), modelAlias: sortedObject(aggregate.agents.modelAlias), taskNamedToolUses: aggregate.taskNamedToolUses },
    writes: { main: writes(aggregate.writes.main), agent: writes(aggregate.writes.agent) },
    toolResults: aggregate.toolResults,
    contentAsString: aggregate.contentAsString,
  };
}

// ---- Q6: sizes and read time -------------------------------------------------------------------------------------------------

const MEGABYTE = 1024 * 1024;

/** The distribution of whole-session bytes (main + subagents) over every session under `--projects`. Stat only. */
export function summariseSizes(sessions) {
  const bytes = sessions.map((session) => session.bytes);
  return {
    sessions: bytes.length,
    totalBytes: bytes.reduce((sum, value) => sum + value, 0),
    p50Bytes: percentile(bytes, 0.5),
    p90Bytes: percentile(bytes, 0.9),
    p99Bytes: percentile(bytes, 0.99),
    maxBytes: bytes.length === 0 ? null : Math.max(...bytes),
    atLeast: Object.fromEntries([1, 10, 50, 100].map((mb) => [`${mb}MB`, bytes.filter((value) => value >= mb * MEGABYTE).length])),
  };
}

const DAY = /^\d{4}-\d{2}-\d{2}$/u;
export const isDay = (value) => typeof value === 'string' && DAY.test(value);
const STAMP_DAY = /"timestamp":"(\d{4}-\d{2}-\d{2})T/u;

/** Lines, bytes and a per-UTC-day line histogram of a session, without parsing a line as JSON. */
export function profileSession(description) {
  const files = [description.main, ...description.agentFiles.values()];
  const days = newTable();
  let lines = 0;
  for (const path of files) {
    try {
      forEachLine(path, (text) => {
        if (text === '') return;
        lines += 1;
        const day = STAMP_DAY.exec(text);
        if (day) bump(days, day[1]);
      });
    } catch {
      // An unreadable file is simply not measured; the pass below skips it the same way.
    }
  }
  return { files, lines, bytes: description.bytes, days };
}

/** The day holding the most lines; ties go to the earliest; '' when no line carried a day. */
export function busiestDay(days) {
  const ranked = Object.entries(days).sort(([dayA, a], [dayB, b]) => b - a || (dayA < dayB ? -1 : 1));
  return ranked.length === 0 ? '' : ranked[0][0];
}

function parsePass(files, gate) {
  let lines = 0;
  let parsed = 0;
  let assistant = 0;
  for (const path of files) {
    try {
      forEachLine(path, (text) => {
        if (text === '') return;
        lines += 1;
        if (gate !== '' && !text.includes(gate)) return;
        const result = parseJsonLine(text);
        parsed += 1;
        if (result.ok && result.value?.type === 'assistant') assistant += 1;
      });
    } catch {
      // counted as zero lines for this file, in both passes alike
    }
  }
  return { lines, parsed, assistant };
}

function stopwatch(work) {
  const started = performance.now();
  const result = work();
  return { ms: performance.now() - started, result };
}

const round1 = (value) => (value === null ? null : Math.round(value * 10) / 10);
const summariseRuns = (durations) => ({ firstRunMs: round1(durations[0]), p50OfRestMs: round1(percentile(durations.slice(1), 0.5)), runs: durations.length });

/** Times a full parse and a day-gated parse over one profiled session; both reported as first run apart, then p50 of the rest. */
export function timeSession(profile, runs, day, progress = () => {}) {
  const gate = day !== '' ? day : busiestDay(profile.days);
  const kept = profile.days[gate] ?? 0;
  const full = [];
  const gated = [];
  let parsedFull;
  let parsedGated;
  for (let at = 0; at < runs; at += 1) {
    const a = stopwatch(() => parsePass(profile.files, ''));
    progress({ phase: 'time', pass: 'full parse', run: at + 1, runs });
    const b = stopwatch(() => parsePass(profile.files, gate));
    progress({ phase: 'time', pass: 'day-gated parse', run: at + 1, runs });
    full.push(a.ms);
    gated.push(b.ms);
    parsedFull = a.result;
    parsedGated = b.result;
  }
  return {
    bytes: profile.bytes,
    lines: profile.lines,
    files: profile.files.length,
    gateDay: gate,
    gateDayFromOption: day !== '',
    linesOnGateDay: kept,
    shareOfLinesKept: profile.lines === 0 ? null : Math.round((parsedGated.parsed / profile.lines) * 10000) / 10000,
    fullParse: { ...summariseRuns(full), linesParsed: parsedFull.parsed },
    dayGatedParse: { ...summariseRuns(gated), linesParsed: parsedGated.parsed },
  };
}

// ---- the whole measurement --------------------------------------------------------------------------------------------------

/**
 * `rounds`: rows from the exporter (`caller`, `caller_vendor`, `started_utc`). Reads under `projectsDir` only. Returns one
 * plain object of counts, booleans, durations and shape-checked names. `progress` is called with counts only
 * (`{ phase: 'read', done, of }`, `{ phase: 'time', session, of, pass, run, runs }`) so a caller can tell a long run is alive; it never
 * receives a name, a path or a value.
 */
export function measure({ rounds, projectsDir, largest = 1, runs = 7, day = '', progress = () => {} }) {
  const index = indexProjects(projectsDir);
  const rows = rounds.filter(isObject);
  const aggregate = newAggregate();
  const ends = new Map();
  let fileIndex = 0;
  const callers = new Set(rows.map((round) => round.caller).filter(isSessionId).map((id) => id.toLowerCase()));
  let done = 0;
  for (const id of callers) {
    for (const entry of index.sessions.get(id) ?? []) {
      const folded = foldSession(aggregate, describeSession(entry), fileIndex);
      fileIndex += folded.files;
      ends.set(id, [...(ends.get(id) ?? []), folded.mainEnd]);
    }
    done += 1;
    progress({ phase: 'read', done, of: callers.size });
  }
  const all = [...index.sessions.values()].flat().map(describeSession);
  const biggest = [...all].sort((a, b) => b.bytes - a.bytes).slice(0, largest);
  return {
    node: process.version,
    platform: process.platform,
    arch: process.arch,
    timedBy: 'node',
    rounds: { read: rounds.length, malformed: rounds.length - rows.length },
    coverage: summariseCoverage(rows, (id) => ends.get(id) ?? []),
    ...renderAggregate(aggregate),
    sizes: { ...summariseSizes(all), nonSessionJsonl: index.otherJsonl },
    largest: biggest.map((description, at) => timeSession(profileSession(description), runs, day, (step) => progress({ ...step, session: at + 1, of: biggest.length }))),
  };
}
