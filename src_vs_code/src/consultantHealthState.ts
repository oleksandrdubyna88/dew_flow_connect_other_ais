import {
  type CheckRecord,
  type CheckRunResult,
  type ConsultantAgy,
  type ConsultantRow,
  type ConsultantsReport,
  type HealthFailure,
  type HealthFile,
  type JudgedCheck,
  LIVENESS_UNKNOWN,
  canaryWording,
  checkStateOf,
  checkStateOnThisDisk,
  failureLabel,
  utcShort,
} from './consultantHealth';
import { type SideFiles, seenOf } from './consultantHealthRead';
import type { ConsultSettings, ResolvedConsultant } from './consultSettings';

/**
 * What each Consultant row's health block SHOWS, decided — the pure half of E5.2 of
 * `research/PLAN_the_consultant_works_on_every_vendor.md`.
 *
 * <p>The split `consultantView.ts` lives by: every question the markup would otherwise ask — which notice a row carries,
 * whether its last failure is still about the consultant the row now names, which check record is the newest truth,
 * whether the Check button exists and whether it is disabled — is answered here, as a value a test asserts without
 * parsing HTML. `consultantHealthView.ts` only renders one.</p>
 *
 * <p><b>Durable status.</b> The button's state is derived from what the SERVER persisted — the check state it settled by
 * its lock, and the state file a check writes before it launches anything — and this window's own flag only adds to
 * it. A reload loses the flag and keeps the truth (`durable-status.md`).</p>
 */

/** What `--consultants` gave this side, as `consultantsProbe.ts` holds it. */
export type ProbeShown =
  | { readonly kind: 'not-installed' }
  | { readonly kind: 'asking' }
  | { readonly kind: 'answered'; readonly report: ConsultantsReport }
  /** Exit 64, or a body from before the field: the cure is a version. */
  | { readonly kind: 'too-old' }
  /** It did not answer this time; `last` is the answer it gave before, which stays on screen. */
  | { readonly kind: 'unanswered'; readonly why: string; readonly last: ConsultantsReport | undefined };

/** One Check press's outcome, and when it landed — on this side's clock, which is the server's clock too. */
export interface LandedRun {
  readonly result: CheckRunResult;
  readonly landedUtc: string;
}

/** This window's side: the server's answer, the state files, and what this window itself has in flight. */
export interface ThisSideHealth {
  readonly label: string;
  readonly probe: ProbeShown;
  readonly files: SideFiles | undefined;
  /** Caller kinds this window is checking right now — optimistic, never the only source. */
  readonly checking: readonly string[];
  readonly runs: Readonly<Record<string, LandedRun>>;
}

/** Another installation's side, read-only, from the files its own server wrote. */
export interface OtherSideHealth {
  readonly label: string;
  readonly dir: string;
  /** `undefined` while that side has never been read successfully. */
  readonly files: SideFiles | undefined;
}

export interface ConsultantHealthState {
  readonly thisSide: ThisSideHealth;
  readonly otherSides: readonly OtherSideHealth[];
  /** The instant the other sides' heartbeats are judged at — taken once per render. */
  readonly nowMs: number;
}

/** The consultant a row NAMES now — what a failure of another vendor or model is hidden against. */
export interface RowIdentity {
  readonly vendor: string;
  readonly model: string;
}

export interface LimitationLine {
  readonly standing: string;
  readonly standingLabel: string;
  readonly text: string;
  /** Measured where and when — or sourced, and said not to be measured here. */
  readonly backing: string;
}

export interface FailureLine {
  readonly label: string;
  readonly what: string;
  readonly cure: string;
  readonly utc: string;
  readonly when: string;
  readonly evidence: string;
}

export interface CheckLine {
  readonly state: string;
  /** A check is running, by the persisted state or this window's own press. Never true for "cannot tell". */
  readonly busy: boolean;
  readonly lines: readonly string[];
}

export interface CheckButton {
  readonly id: string;
  readonly disabled: boolean;
  readonly label: string;
}

export interface SnippetBlock {
  readonly id: string;
  readonly text: string;
  readonly warning: string;
  readonly settingsPath: string;
}

/** One side's block under one caller's row. */
export interface SideBlock {
  readonly kind: string;
  readonly side: string;
  readonly readOnly: boolean;
  /** When the facts were taken, or empty when nothing was. */
  readonly asOf: string;
  readonly notices: readonly string[];
  readonly cli: string;
  readonly limitation: LimitationLine | undefined;
  readonly failure: FailureLine | undefined;
  readonly check: CheckLine;
  /** This side only, and only where a check can be had. */
  readonly checkButton: CheckButton | undefined;
  /** This side only: agy's allow rule on Linux/WSL, with its warning. */
  readonly snippet: SnippetBlock | undefined;
  /** A Windows agy row: agy allows only exact commands there. */
  readonly agyFact: string;
  /** Another side: why there is no button, and where one is. */
  readonly pointer: string;
}

export interface RowHealth {
  readonly thisSide: SideBlock;
  readonly otherSides: readonly SideBlock[];
}

/** One caller's health, on this side and every other side named. */
export function rowHealth(kind: string, identity: RowIdentity, state: ConsultantHealthState): RowHealth {
  return {
    thisSide: thisSideBlock(kind, identity, state.thisSide, state.nowMs),
    otherSides: state.otherSides.map((side) => otherSideBlock(kind, side, state.nowMs)),
  };
}

/**
 * One CALLER's health block, decided against the consultant its entry RESOLVES to (todo/PLAN_one_model_catalog.md
 * E5.1b) — drawn under each caller's pick (`consultantPicks.pickHtml`). Both Consultant tabs drew by it until E5.3
 * removed the old page's caller definitions.
 *
 * <p><b>Why the resolved pair, and why one function.</b> The server reports the consultant it RESOLVED — through
 * `--consultants`, the vendor and the model it would run — and a failure is hidden when it is about another pair than
 * the row names (`aboutAnother`, `currentFailure`). A pick is stored as a bare reference to a catalog
 * row (`{ vendor: 'deep-high' }`, no model); matched by what is STORED, the row's own failure — reported with the row's
 * model — would be filtered away as "about another consultant". `consult.byCaller` holds what the entry means, model
 * materialised, which is what the server compared.</p>
 *
 * <p>A caller the map does not hold is matched as no consultant at all (an empty pair), since an absent entry draws an
 * unplaceable row named ''.</p>
 */
export function callerHealth(kind: string, consult: ConsultSettings, state: ConsultantHealthState): RowHealth {
  return rowHealth(kind, resolvedIdentity(consult.byCaller[kind]), state);
}

/** The pair a resolved entry names — a definition's and an unplaceable entry's alike, since both carry them. */
function resolvedIdentity(resolved: ResolvedConsultant | undefined): RowIdentity {
  return resolved === undefined ? NO_CONSULTANT : { vendor: resolved.vendor, model: resolved.model };
}

/** The pair of a caller the map does not hold. */
const NO_CONSULTANT: RowIdentity = { vendor: '', model: '' };

/** The states during which a Check must not be pressed again. */
const RUNNING = ['checking', 'already-checking'];

function thisSideBlock(kind: string, identity: RowIdentity, side: ThisSideHealth, nowMs: number): SideBlock {
  const report = reportOf(side.probe);
  const row = rowFor(report, kind);
  const check = thisSideCheck({ kind, row, report, side, identity, nowMs });

  return {
    ...facts(kind, side.label, report, row, identity),
    readOnly: false,
    notices: [...probeNotices(side.probe), ...rowNotices(report, row, identity)],
    check,
    checkButton: row?.available === true ? checkButton(kind, check.busy) : undefined,
    snippet: snippetOf(kind, row),
    pointer: '',
  };
}

function otherSideBlock(kind: string, side: OtherSideHealth, nowMs: number): SideBlock {
  const report = foundReport(side.files);
  const row = rowFor(report, kind);
  // On that side its OWN consultant is the identity: its settings are its own, and this window's row says nothing about them.
  const base = facts(kind, side.label, report, row, identityOf(row));

  return {
    ...base,
    readOnly: true,
    notices: otherNotices(side, row),
    failure: relabelled(base.failure, side.label),
    check: otherSideCheck(side, kind, row, nowMs),
    checkButton: undefined,
    snippet: undefined,
    pointer: pointerTo(side.label),
  };
}

function foundReport(files: SideFiles | undefined): ConsultantsReport | undefined {
  const report = files?.report;

  return report?.kind === 'found' ? report.value : undefined;
}

function rowFor(report: ConsultantsReport | undefined, kind: string): ConsultantRow | undefined {
  return report?.consultants.find((one) => one.callerKind === kind);
}

const NOBODY: RowIdentity = { vendor: '', model: '' };

function identityOf(row: ConsultantRow | undefined): RowIdentity {
  return row === undefined ? NOBODY : { vendor: row.vendor, model: row.model };
}

function relabelled(failure: FailureLine | undefined, label: string): FailureLine | undefined {
  return failure === undefined ? undefined : { ...failure, evidence: onThatSide(failure.evidence, label) };
}

/** What both sides show the same way: the CLI, the limitation, the current failure, the time, the agy fact. */
function facts(
  kind: string, label: string, report: ConsultantsReport | undefined, row: ConsultantRow | undefined, identity: RowIdentity,
): Pick<SideBlock, 'kind' | 'side' | 'asOf' | 'cli' | 'limitation' | 'failure' | 'agyFact'> {
  return {
    kind,
    side: label,
    asOf: asOfOf(report),
    cli: cliOf(row),
    limitation: limitationOf(row),
    failure: currentFailure(row, identity),
    agyFact: agyFactOf(report, row),
  };
}

function asOfOf(report: ConsultantsReport | undefined): string {
  return report === undefined ? '' : utcShort(report.utc);
}

function cliOf(row: ConsultantRow | undefined): string {
  return row === undefined ? '' : cliLine(row);
}

function limitationOf(row: ConsultantRow | undefined): LimitationLine | undefined {
  return row?.limitation === undefined ? undefined : limitationLine(row.limitation);
}

function reportOf(probe: ProbeShown): ConsultantsReport | undefined {
  return probe.kind === 'answered' ? probe.report : probe.kind === 'unanswered' ? probe.last : undefined;
}

/** What to say about the server's answer itself — never silence, because silence reads as "nothing configured". */
function probeNotices(probe: ProbeShown): readonly string[] {
  return probe.kind === 'unanswered' ? [unansweredNotice(probe.why, probe.last)] : FIXED_NOTICES[probe.kind];
}

const FIXED_NOTICES: Readonly<Record<Exclude<ProbeShown['kind'], 'unanswered'>, readonly string[]>> = {
  'not-installed': ['The MCP server is not installed on this side, so nothing can say whether this consultant works.'],
  'asking': ['Asking the MCP server about its consultants…'],
  'too-old': ['This MCP server is too old to report on its consultants — update the MCP server from the ConnectOtherAIs panel.'],
  'answered': [],
};

function unansweredNotice(why: string, last: ConsultantsReport | undefined): string {
  return `The MCP server could not report on its consultants: ${why}.${last === undefined ? '' : ' What is shown is its last answer.'}`;
}

/** What to say about this caller's row in an answer — absent, refused, or about another consultant than the row names. */
function rowNotices(report: ConsultantsReport | undefined, row: ConsultantRow | undefined, identity: RowIdentity): readonly string[] {
  if (report === undefined) {
    return [];
  }
  if (row === undefined) {
    return ['The MCP server did not report this caller kind.'];
  }

  return [...refusal(row), ...unreadableHealth(row), ...aboutAnother(row, identity)];
}

/**
 * A health file the server could not read — said, never left to read as a consultant that is fine (the whole-branch
 * review, A4): an unreadable failure file may hide a current failure, and an unreadable answer may hide a recovery.
 */
function unreadableHealth(row: ConsultantRow): readonly string[] {
  return row.healthUnreadable.length > 0
    ? [`This consultant's last outcome cannot be read, so whether it works is not known: ${row.healthUnreadable}`]
    : [];
}

function refusal(row: ConsultantRow): readonly string[] {
  return row.available ? [] : [`The MCP server would refuse a consultation here: ${row.reason}`];
}

function aboutAnother(row: ConsultantRow, identity: RowIdentity): readonly string[] {
  return sameConsultant(row, identity)
    ? []
    : [`These facts are about ${named(row.vendor, row.model)}, which is not what this row names now — they are refreshed when the server is asked again.`];
}

function otherNotices(side: OtherSideHealth, row: ConsultantRow | undefined): readonly string[] {
  return side.files === undefined ? [`${side.dir} cannot be read right now — is that side running?`] : reportNotices(side.files.report, row);
}

function reportNotices(report: HealthFile<ConsultantsReport> | undefined, row: ConsultantRow | undefined): readonly string[] {
  if (report === undefined) {
    return ['That side has not written its consultants yet — its MCP server writes them when it starts.'];
  }

  return report.kind === 'unreadable' ? [`Its consultants.json cannot be read: it ${report.why}.`] : refusalOf(row);
}

function refusalOf(row: ConsultantRow | undefined): readonly string[] {
  return row === undefined ? ['That side did not report this caller kind.'] : [...refusal(row), ...unreadableHealth(row)];
}

function sameConsultant(one: { readonly vendor: string; readonly model: string }, identity: RowIdentity): boolean {
  return one.vendor === identity.vendor && one.model === identity.model;
}

function named(vendor: string, model: string): string {
  return model.length > 0 ? `${vendor} · ${model}` : vendor;
}

/** The CLI as it described itself — its auth SOURCE, which is never a sign-in. */
function cliLine(row: ConsultantRow): string {
  if (!row.cli.probed) {
    return '';
  }

  return row.cli.found ? foundCli(row) : `CLI not found${row.cli.note.length > 0 ? `: ${row.cli.note}` : ''}`;
}

function foundCli(row: ConsultantRow): string {
  return `${row.runtime} CLI ${orElse(row.cli.version, 'of an unstated version')} · auth source: ${orElse(row.cli.authSource, 'not said')}`
    + ` — where its credential would come from, not a sign-in check${capabilityOf(row)}`;
}

function orElse(value: string, fallback: string): string {
  return value.length > 0 ? value : fallback;
}

function capabilityOf(row: ConsultantRow): string {
  return row.capability.length > 0 ? ` · --restricted: ${row.capability}` : '';
}

const STANDING_LABELS: Readonly<Record<string, string>> = {
  'confined': 'Confined',
  'unconfined': 'Not confined',
  'default-deny': 'Denies by default',
  'unmeasured': 'Not measured',
};

function limitationLine(limitation: NonNullable<ConsultantRow['limitation']>): LimitationLine {
  return {
    standing: limitation.standing,
    standingLabel: STANDING_LABELS[limitation.standing] ?? limitation.standing,
    text: limitation.text,
    backing: limitation.evidence === 'measured'
      ? `measured ${limitation.measuredDate} on CLI ${limitation.measuredCliVersion}, ${limitation.measuredCells} (${limitation.measuredDocument})`
      : `not measured here — ${limitation.source}`,
  };
}

/**
 * The last failure — while the server calls it current, which it decides per consultant (`ConsultHealth.Current`, the
 * whole-branch review, A3); the panel no longer compares the failure's own vendor a second time. What it still hides is
 * a ROW whose facts are about another consultant than the one this window's row names now — the server was asked
 * before the person chose another, and a notice says so (`aboutAnother`). On another side `identity` is that side's own
 * row, so nothing is hidden there.
 */
function currentFailure(row: ConsultantRow | undefined, identity: RowIdentity): FailureLine | undefined {
  const failure = currentOf(row);

  return failure !== undefined && row !== undefined && sameConsultant(row, identity) ? failureLine(failure) : undefined;
}

function currentOf(row: ConsultantRow | undefined): HealthFailure | undefined {
  return row?.failureCurrent === true ? row.lastFailure : undefined;
}

function failureLine(failure: HealthFailure): FailureLine {
  return {
    label: failureLabel(failure.kind),
    what: failure.what,
    cure: failure.cure,
    utc: failure.utc,
    when: utcShort(failure.utc),
    evidence: failure.evidence,
  };
}

function onThatSide(evidence: string, label: string): string {
  return evidence.length > 0 ? `${evidence} — a path on ${label}` : '';
}

/** On Windows agy runs only an EXACT allowed command line (P0, RESULTS_agy_allow_rule.md) — said where the snippet would be. */
function agyFactOf(report: ConsultantsReport | undefined, row: ConsultantRow | undefined): string {
  return onWindows(report) && agyWithoutSnippet(row) ? windowsAgyFact(row?.agy) : '';
}

function onWindows(report: ConsultantsReport | undefined): boolean {
  return report?.side === 'windows';
}

function agyWithoutSnippet(row: ConsultantRow | undefined): boolean {
  return row?.runtime === 'antigravity' && !hasSnippet(row.agy);
}

function hasSnippet(agy: ConsultantAgy | undefined): boolean {
  return (agy?.snippet ?? '').length > 0;
}

function windowsAgyFact(agy: ConsultantAgy | undefined): string {
  return 'On Windows agy runs only an exact allowed command line — a prefix rule such as command(git grep) was measured denied there (2026-10-02, agy 1.2.15) — so there is no rule to paste on this side. '
    + `Its settings file is ${orElse(agy?.settingsPath ?? '', 'not reported')}; coai never writes it.`;
}

function snippetOf(kind: string, row: ConsultantRow | undefined): SnippetBlock | undefined {
  const agy = row?.agy;

  return agy === undefined || agy.snippet.length === 0
    ? undefined
    : { id: kind, text: agy.snippet, warning: agy.snippetWarning, settingsPath: agy.settingsPath };
}

function checkButton(kind: string, busy: boolean): CheckButton {
  return { id: kind, disabled: busy, label: busy ? 'Checking…' : 'Check' };
}

function pointerTo(label: string): string {
  const where = label.startsWith('WSL') ? `a Remote-WSL window on ${label}` : label === 'Windows' ? 'a plain Windows window' : 'a window on that installation';

  return `Read-only: this is the ${label} side's own store, as its MCP server last wrote it. To run a Check there, open ${where}.`;
}

/** A record, and the instant it last said anything — its end, else its heartbeat, else its start, else when it landed. */
interface Candidate {
  readonly judged: JudgedCheck;
  readonly at: number;
}

function stampOf(record: CheckRecord, landedUtc = ''): number {
  const stamps = [record.finishedUtc, record.heartbeatUtc, record.startedUtc, landedUtc].map((one) => Date.parse(one));

  return stamps.find((one) => Number.isFinite(one)) ?? Number.NEGATIVE_INFINITY;
}

/** The newest candidate; on a tie the EARLIER in the list — the order says whose word counts more. */
function newest(candidates: readonly (Candidate | undefined)[]): Candidate | undefined {
  return candidates.reduce<Candidate | undefined>((best, one) => (one !== undefined && (best === undefined || one.at > best.at) ? one : best), undefined);
}

/** A record as the SERVER that wrote it on this side settled it, or as this window's own press reported it. */
function candidate(record: CheckRecord | undefined, landedUtc = ''): Candidate | undefined {
  return record === undefined ? undefined : { judged: checkStateOf(record, 'this', 0), at: stampOf(record, landedUtc) };
}

/** A record read from ANOTHER side, judged by how long this window has seen its heartbeat stand still (the review, O). */
function acrossCandidate(record: CheckRecord | undefined, kind: string, files: SideFiles | undefined, nowMs: number): Candidate | undefined {
  return record === undefined ? undefined : { judged: checkStateOf(record, 'other', nowMs, seenOf(files, kind, record, nowMs)), at: stampOf(record) };
}

function found(file: HealthFile<CheckRecord> | undefined): CheckRecord | undefined {
  return file?.kind === 'found' ? file.value : undefined;
}

/**
 * This side's check: the server's settled record, the state file, and this window's own run — the NEWEST wins, and
 * on a tie the server's word (it asked the lock). A final state on disk newer than the server's last answer is a check
 * that finished after it was asked; a `checking` whose heartbeat moved past the server's `abandoned` is alive.
 */
/** Everything this side's check line is decided from. */
interface ThisSideCheckInputs {
  readonly kind: string;
  readonly row: ConsultantRow | undefined;
  /** The server's answer the row came from — WHEN it was taken decides whether it settled a check on disk. */
  readonly report: ConsultantsReport | undefined;
  readonly side: ThisSideHealth;
  readonly identity: RowIdentity;
  readonly nowMs: number;
}

function thisSideCheck(inputs: ThisSideCheckInputs): CheckLine {
  const { kind, row, report, side, identity, nowMs } = inputs;
  const run = side.runs[kind];
  const file = fileOf(side.files, kind);
  const best = newest([candidate(row?.check), runCandidate(run), onDiskCandidate(found(file), report, run, nowMs)]);
  const line = side.checking.includes(kind) ? startingLine(best) : checkLine(best, 'this side', unreadableOf(file), identity);

  return { ...line, lines: [...line.lines, ...runNote(run, best)] };
}

/**
 * This window's own press, when it reported a record. NOT an `already-checking`: that record names another check's
 * times, often none at all, and it would outrank that check's own result for ever by the time it landed (epic 5's
 * review) — the press is said as a note instead, and the other check's state decides.
 */
function runCandidate(run: LandedRun | undefined): Candidate | undefined {
  return run === undefined ? undefined : candidate(rankedRecord(run.result), run.landedUtc);
}

function rankedRecord(result: CheckRunResult): CheckRecord | undefined {
  return result.kind === 'reported' && result.record.state !== 'already-checking' ? result.record : undefined;
}

/**
 * The state file on this side. A final state is what it says. A `checking` there is what the holder wrote BEFORE it was
 * settled, so it counts only while nothing has settled it since: a server answer taken after the check started has
 * asked the lock (the server's word wins), and this window's own run of it ending says the process is gone. Otherwise
 * it is judged by its own published heartbeat against this window's clock — the same clock as the server that wrote it,
 * on this side — so a check that died while nobody asked the server reads abandoned once its heartbeat is stale, never
 * Checking… for ever (epic 5's review).
 */
function onDiskCandidate(record: CheckRecord | undefined, report: ConsultantsReport | undefined, run: LandedRun | undefined, nowMs: number): Candidate | undefined {
  if (record === undefined || record.state !== 'checking') {
    return candidate(record);
  }

  return settledSince(record, report, run) ? undefined : { judged: checkStateOnThisDisk(record, nowMs), at: stampOf(record) };
}

function settledSince(record: CheckRecord, report: ConsultantsReport | undefined, run: LandedRun | undefined): boolean {
  return atOrAfter(stampOrNothing(report?.utc), record.startedUtc) || atOrAfter(stampOrNothing(run?.landedUtc), lastWord(record));
}

function stampOrNothing(stamp: string | undefined): string {
  return stamp ?? '';
}

/** The last instant a record said anything while running — its heartbeat, else its start. */
function lastWord(record: CheckRecord): string {
  return record.heartbeatUtc.length > 0 ? record.heartbeatUtc : record.startedUtc;
}

function atOrAfter(laterUtc: string, sinceUtc: string): boolean {
  const later = Date.parse(laterUtc);

  return Number.isFinite(later) && later >= Date.parse(sinceUtc);
}

function fileOf(files: SideFiles | undefined, kind: string): HealthFile<CheckRecord> | undefined {
  return files?.checks[kind];
}

/** Nothing at all for a side never read: the notice says it cannot be read, and "never checked" would be a claim. */
const NOT_READ: CheckLine = { state: 'not-read', busy: false, lines: [] };

function otherSideCheck(side: OtherSideHealth, kind: string, row: ConsultantRow | undefined, nowMs: number): CheckLine {
  return side.files === undefined
    ? NOT_READ
    : checkLine(newest([acrossCandidate(found(fileOf(side.files, kind)), kind, side.files, nowMs), acrossCandidate(row?.check, kind, side.files, nowMs)]), 'that side', '', identityOf(row));
}

function unreadableOf(file: HealthFile<CheckRecord> | undefined): string {
  return file?.kind === 'unreadable' ? file.why : '';
}

/** This window just pressed Check: running, with the state file's own times once the check has written them. */
function startingLine(best: Candidate | undefined): CheckLine {
  const running = best?.judged.state === 'checking' ? best.judged.record : undefined;

  return {
    state: 'checking',
    busy: true,
    lines: [running === undefined ? 'Checking… the MCP server is starting the check.' : runningLine(running)],
  };
}

function runningLine(record: CheckRecord): string {
  return `Checking… started ${utcShort(record.startedUtc)}; it ends by ${utcShort(record.deadlineUtc)} whatever happens.`;
}

function checkLine(best: Candidate | undefined, where: string, unreadable: string, identity: RowIdentity): CheckLine {
  if (best === undefined) {
    return { state: unreadable.length > 0 ? 'unreadable' : 'none', busy: false, lines: [unreadable.length > 0 ? `The check's state file ${unreadable}.` : `Never checked on ${where}.`] };
  }

  return { state: best.judged.state, busy: RUNNING.includes(best.judged.state), lines: [...forAnother(best.judged.record, identity), ...linesOf(best.judged)] };
}

/**
 * A check of the consultant a row USED to name — a person who switched vendor sees it labelled, never read as a verdict
 * on the one the row names now (epic 5's review). A record naming no vendor (an unavailable row) is nobody's to label.
 */
function forAnother(record: CheckRecord, identity: RowIdentity): readonly string[] {
  return record.vendor.length === 0 || sameConsultant(record, identity)
    ? []
    : [`For ${named(record.vendor, record.model)}, which this row no longer names:`];
}

/** What each state says, in words a person acts on — one entry per word of `shared/consult-check-words.json`, and the panel's own. */
const STATE_LINES: Readonly<Record<string, (judged: JudgedCheck) => readonly string[]>> = {
  'checking': (judged) => [runningLine(judged.record)],
  [LIVENESS_UNKNOWN]: (judged) => [`checking, as of ${utcShort(judged.record.heartbeatUtc || judged.record.startedUtc)} — whether it is still running cannot be told from here`],
  'answered': (judged) => answeredLines(judged.record),
  'failed': (judged) => failedLines(judged.record),
  'unavailable': (judged) => [`The check could not run: ${judged.record.reason}`],
  'already-checking': (judged) => [`Another check of this consultant is running — started ${utcShort(judged.record.startedUtc)}.`],
  'abandoned': (judged) => [`The last check ended without finishing: ${judged.reason}`],
  'unreadable': (judged) => [`The check's state cannot be read: ${judged.record.reason}`],
};

/**
 * Every check state this panel puts into words — the server's seven and the panel's own {@link LIVENESS_UNKNOWN}. The
 * extension's test holds it against `shared/consult-check-words.json` (the whole-branch review, J).
 */
export const CHECK_STATES_WORDED: readonly string[] = Object.keys(STATE_LINES);

function linesOf(judged: JudgedCheck): readonly string[] {
  return (STATE_LINES[judged.state] ?? (() => [`The check is in a state this panel does not know (“${judged.state}”).`]))(judged);
}

function answeredLines(record: CheckRecord): readonly string[] {
  return [
    `Checked ${utcShort(record.finishedUtc)}: ${named(record.vendor, record.model)} answered in ${Math.round(record.seconds)} s${record.confinement.length > 0 ? `, sent ${record.confinement}` : ''}.`,
    record.markerRead ? 'It read the marker in CHECK.md — the file was read, not guessed.' : 'It did NOT read the marker in CHECK.md.',
    ...(record.canary.length > 0 ? [`Canary: ${canaryWording(record.canary)}.`] : []),
  ];
}

function failedLines(record: CheckRecord): readonly string[] {
  return [
    `Checked ${utcShort(record.finishedUtc)}: ${record.vendor.length > 0 ? `${named(record.vendor, record.model)} — ` : ''}${failureLabel(record.failureKind)} — ${record.failureWhat}`,
    ...(record.failureCure.length > 0 ? [record.failureCure] : []),
    ...(record.evidence.length > 0 ? [`Its transcript: ${record.evidence}`] : []),
  ];
}

/** A Check that could not even report — said under the result, while nothing newer has happened since. */
function runNote(run: LandedRun | undefined, best: Candidate | undefined): readonly string[] {
  return run === undefined || superseded(run, best) ? [] : noteOf(run.result);
}

function superseded(run: LandedRun, best: Candidate | undefined): boolean {
  return Date.parse(run.landedUtc) < (best?.at ?? Number.NEGATIVE_INFINITY);
}

function noteOf(result: CheckRunResult): readonly string[] {
  return result.kind === 'reported' ? reportedNote(result.record) : [RUN_NOTES[result.kind](result)];
}

/** A press that found another check running launched nothing — said, while that check's own state decides the row. */
function reportedNote(record: CheckRecord): readonly string[] {
  return record.state === 'already-checking' ? ['Your press found another check of this consultant already running — nothing was launched.'] : [];
}

const RUN_NOTES: Readonly<Record<'too-old' | 'refused' | 'crashed', (result: CheckRunResult) => string>> = {
  'too-old': () => 'This MCP server is too old to run a check — update the MCP server from the ConnectOtherAIs panel.',
  'refused': (result) => `The MCP server refused the check: ${result.kind === 'refused' ? result.said : ''}`,
  'crashed': (result) => `The check did not finish: ${result.kind === 'crashed' ? result.why : ''}`,
};

/** The consultant THIS side's server reported for a caller kind — what a Check's confirmation names. */
export function reportedAbout(probe: ProbeShown, kind: string): RowIdentity {
  return identityOf(rowFor(reportOf(probe), kind));
}

/** The allow rule THIS side's server sent for a caller kind — what a Copy press copies, and nothing it did not send. */
export function reportedSnippet(probe: ProbeShown, kind: string): { readonly text: string; readonly settingsPath: string } | undefined {
  const snippet = snippetOf(kind, rowFor(reportOf(probe), kind));

  return snippet === undefined ? undefined : { text: snippet.text, settingsPath: snippet.settingsPath };
}
