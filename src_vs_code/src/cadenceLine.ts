import { escapeHtml } from './escapeHtml';
import { repoNameOf } from './pathTail';
import type { SessionFile } from './rounds';

/**
 * A plan's consultation cadence as the sidebar draws it (todo/PLAN_consult_on_a_cadence.md, epic 4 story 4.2).
 *
 * <p>The answer is the server's — `coai-mcp --cadence` prints exactly what `status` carries as `cadence` —
 * and this module only reads and words it. The extension cannot work the cadence out from the session
 * files: the record holds neither the plan's epics nor the grouping (the risk consultation for story 4.2,
 * point 4). Types and pure functions only, so nothing here drags a spawn into whatever imports it; the
 * probe is `cadenceProbe.ts`.</p>
 */

/** One group of epics and whether its consultation is on record. */
export interface CadenceGroup {
  readonly range: string;
  readonly consulted: boolean;
}

/** One named risky epic or story (`7`, `7/7.2`) and whether its consultation is on record. */
export interface CadenceRisk {
  readonly key: string;
  readonly reason: string;
  readonly consulted: boolean;
}

/** The C# `CadenceAnswer`, field for field. */
export interface CadenceAnswer {
  readonly plan: string;
  readonly mode: string;
  readonly epics: number;
  readonly epicsClosed: readonly number[];
  readonly groups: readonly CadenceGroup[];
  readonly risk: readonly CadenceRisk[];
  readonly riskAnswered: boolean;
  /** Why the plan's record could not be read, or empty — never read as "nothing done". */
  readonly unreadable: string;
}

/** One session's answer, with where it is from. */
export interface CadenceLine {
  readonly repoPath: string;
  readonly branch: string;
  readonly answer: CadenceAnswer;
}

/** How long ago a session may last have moved and still be probed. */
const A_DAY_MS = 24 * 60 * 60 * 1000;

/**
 * Whether a session is worth a probe: touched in the last day, holding a plan, on a named repo and branch.
 *
 * <p>The bound on how many spawns a sidebar makes (the risk consultation for story 4.2, point 3). A session
 * file lingers for weeks after its work merged, and asking about every one on every half minute would be
 * a process per stale branch for nothing a person is looking at.</p>
 */
export function cadenceWanted(session: SessionFile, nowMs: number): boolean {
  return planOf(session).length > 0 && isNamed(session) && isRecent(nowMs - lastMoved(session));
}

/**
 * How far AHEAD a round may be dated and still count — two sides of one machine (Windows and WSL) keep
 * their own clocks. Past it the stamp is broken, and a broken stamp must not make a session recent for
 * ever (the code round, codex).
 */
const SKEW_MS = 5 * 60 * 1000;

function isRecent(ageMs: number): boolean {
  return ageMs >= -SKEW_MS && ageMs <= A_DAY_MS;
}

/** The plan a session holds, trimmed — empty for one that holds none or predates the field. */
export function planOf(session: SessionFile): string {
  return typeof session.plan === 'string' ? session.plan.trim() : '';
}

function isNamed(session: SessionFile): boolean {
  return session.state.repoPath.length > 0 && session.state.branch.length > 0;
}

/**
 * When a session last moved, by its rounds' own instants; never, for a session with none.
 *
 * <p>One pass, no spread: `Math.max(...all)` throws past the engine's argument limit on a long enough
 * history (the code round, codex).</p>
 */
function lastMoved(session: SessionFile): number {
  return session.rounds.reduce((latest, round) => Math.max(latest, instantOf(round.completedUtc), instantOf(round.startedUtc ?? '')),
    Number.NEGATIVE_INFINITY);
}

/** An ISO instant in ms, or never for anything that is not one. */
function instantOf(instant: string): number {
  const ms = Date.parse(instant);

  return Number.isFinite(ms) ? ms : Number.NEGATIVE_INFINITY;
}

/**
 * What the server printed: an answer, `null` for nothing to draw (the cadence is off, or no plan), or
 * `undefined` for a body that is not an answer at all — which the probe treats as a failure.
 */
export function parseCadence(text: string): CadenceAnswer | null | undefined {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    return undefined;
  }
  if (parsed === null) {
    return null;
  }

  return isAnswer(parsed) ? answerFrom(parsed) : undefined;
}

type Raw = Record<string, unknown>;

function isAnswer(value: unknown): value is Raw {
  return !Array.isArray(value) && shaped(value, { plan: 'string', epics: 'number' });
}

/** Believed only as far as its shape: a group or an item that is not one is dropped, not drawn. */
function answerFrom(raw: Raw): CadenceAnswer {
  return {
    plan: String(raw['plan']),
    mode: typeof raw['mode'] === 'string' ? raw['mode'] : '',
    epics: Number(raw['epics']),
    epicsClosed: listOf(raw['epicsClosed']).filter((n): n is number => typeof n === 'number'),
    groups: listOf(raw['groups']).flatMap((g) => (isGroup(g) ? [{ range: g.range, consulted: g.consulted }] : [])),
    risk: listOf(raw['risk']).flatMap((r) => (isRisk(r) ? [{ key: r.key, reason: r.reason, consulted: r.consulted }] : [])),
    riskAnswered: raw['riskAnswered'] === true,
    // Absent from a server of epic 3, which is older than the field — and its absence says nothing failed.
    unreadable: typeof raw['unreadable'] === 'string' ? raw['unreadable'] : '',
  };
}

function listOf(value: unknown): readonly unknown[] {
  return Array.isArray(value) ? value : [];
}

/** An object whose named fields have the named `typeof`s — the one shape check both kinds of row use. */
function shaped(value: unknown, fields: Readonly<Record<string, string>>): value is Raw {
  return typeof value === 'object' && value !== null
    && Object.entries(fields).every(([field, type]) => typeof (value as Raw)[field] === type);
}

function isGroup(value: unknown): value is CadenceGroup {
  return shaped(value, { range: 'string', consulted: 'boolean' });
}

function isRisk(value: unknown): value is CadenceRisk {
  return shaped(value, { key: 'string', reason: 'string', consulted: 'boolean' });
}

/** `4-6` → [4, 6]; `7` → [7, 7]; anything else → nothing. */
function bounds(range: string): readonly [number, number] | undefined {
  const m = /^([0-9]+)(?:-([0-9]+))?$/.exec(range.trim());

  return m === null ? undefined : [Number(m[1]), Number(m[2] ?? m[1])];
}

/** The group holding the first epic, in the plan's own order, that is not yet closed. */
function currentGroup(answer: CadenceAnswer): CadenceGroup | undefined {
  const closed = new Set(answer.epicsClosed);

  return answer.groups.find((group) => {
    const range = bounds(group.range);
    if (range === undefined) {
      return false;
    }
    for (let epic = range[0]; epic <= range[1]; epic += 1) {
      if (!closed.has(epic)) {
        return true;
      }
    }
    return false;
  });
}

/**
 * The sentence: `PLAN_x.md · epics closed 4/14 · consultation for epics 4-6: due`.
 *
 * <p>An unreadable record is said as that and nothing else — the counts beside it would be an empty
 * record's, and "0 closed" is believable and false (the risk consultation for story 4.2, point 2).</p>
 */
export function cadenceSaid(answer: CadenceAnswer): string {
  // By its file name — `repoNameOf` is the last segment of any path — because that is what a person recognises.
  const name = repoNameOf(answer.plan);
  if (answer.unreadable.length > 0) {
    return `${name} · the cadence record could not be read: ${answer.unreadable}`;
  }
  if (answer.epics <= 0) {
    return `${name} · no epics counted yet`;
  }

  const closed = `${answer.epicsClosed.length}/${answer.epics}`;
  const group = currentGroup(answer);
  const where = group === undefined
    ? `${name} · every epic closed (${closed})`
    : `${name} · epics closed ${closed} · consultation for ${epicsWord(group.range)}: ${groupState(group, answer.mode)}`;

  return `${where}${riskSaid(answer.risk)}`;
}

function epicsWord(range: string): string {
  return range.includes('-') ? `epics ${range}` : `epic ${range}`;
}

function groupState(group: CadenceGroup, mode: string): string {
  if (group.consulted) {
    return 'taken';
  }

  return mode === 'require' ? 'due — the code round waits for it' : 'due';
}

function riskSaid(risk: readonly CadenceRisk[]): string {
  return risk.length === 0 ? '' : ` · risky pieces consulted ${risk.filter((r) => r.consulted).length}/${risk.length}`;
}

/** Whether the line asks something of the person reading it: an unreadable record, or a consultation due. */
function wantsAttention(answer: CadenceAnswer): boolean {
  const group = currentGroup(answer);

  return answer.unreadable.length > 0 || (group !== undefined && !group.consulted) || answer.risk.some((r) => !r.consulted);
}

/** The lines, above the running rounds — nothing at all when there are none. */
export function cadenceLinesHtml(lines: readonly CadenceLine[]): string {
  if (lines.length === 0) {
    return '';
  }

  return `<div class="cadence-lines">${lines.map((line) => `
  <div class="${wantsAttention(line.answer) ? 'stale' : 'hint'}" title="${escapeHtml(line.repoPath)} · ${escapeHtml(line.branch)}">${escapeHtml(cadenceSaid(line.answer))} <span class="decided">${escapeHtml(line.branch)}</span></div>`).join('')}
</div>`;
}
