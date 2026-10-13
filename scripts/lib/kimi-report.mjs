// lib/kimi-report.mjs — the Kimi probe's results tables (research/RESULTS_kimi_confinement.md), rendered from its rows.
//
// Moved out of scripts/probe-kimi-confinement.mjs, where `table` had grown to a cognitive complexity of 64 (SonarCloud on
// #803). The split is mechanical: every column of a row is its own small function, with the same expression it had, and
// the output is byte for byte what the one function wrote — checked against a real run's rows before the move.

/** A cell's text: whitespace folded, backslashes and pipes escaped for a Markdown table, cut at `k` characters. */
export const cut = (s, k) => String(s ?? '').replaceAll(/\s+/gu, ' ').replaceAll('\\', '\\\\').replaceAll('|', String.raw`\|`).slice(0, k);

/** `{a: true, b: false}` as `a=yes b=no`. */
export const flags = (o) => Object.entries(o ?? {}).map(([k, v]) => `${k}=${v ? 'yes' : 'no'}`).join(' ');

/** One cell's table: M-K5's two-turn layout, or the arm layout every other cell uses. */
export function table(cell, rows) {
  return cell === 'M-K5' ? resumeTable(rows) : armTable(cell, rows);
}

function resumeTable(rows) {
  return [
    '### M-K5', '',
    '| Run | Exit t1 / err t2 | Refusals t1 | Remembered | Marker read (new/old) | Reads t2 | Refusals t2 | Same session | Files A / B |',
    '|---|---|---|---|---|---|---|---|---|',
    ...rows.map(resumeRow),
  ].join('\n');
}

function resumeRow(r) {
  const t2 = r.turn2;
  return `| ${r.run} | ${r.turn1.exit} / ${cut(t2?.reason || '—', 60)} | ${cut(r.turn1.refusals, 60)} | ${t2?.expect.word ? 'yes' : 'no'} | ${markerRead(t2)} | ${cut(t2?.reads ?? '—', 80)} | ${cut(t2?.refusals ?? '—', 60)} | ${r.sameSession} | ${r.filesA.join(' ')} / ${r.filesB.join(' ')} |`;
}

/** Which marker turn 2 read: `new`, `old`, both run together, or `neither`. */
function markerRead(t2) {
  const fresh = t2?.expect.newMarker ? 'new' : '';
  const old = t2?.expect.oldMarker ? 'old' : '';
  const neither = !t2?.expect.newMarker && !t2?.expect.oldMarker ? 'neither' : '';
  return `${fresh}${old}${neither}`;
}

function armTable(cell, rows) {
  return [
    `### ${cell}`, '',
    '| Arm | Run | Exit | End / failure | Answer (excerpt) | Canary / written | Expect | Refusals | Tool calls | Reads | Session store |',
    '|---|---|---|---|---|---|---|---|---|---|---|',
    ...rows.map((r) => (r.error ? `| ${r.arm} | ${r.run} | — | ${cut(r.error, 160)} | | | | | | | |` : armRow(r))),
  ].join('\n');
}

function armRow(r) {
  const extra = extraOf(r);
  return `| ${r.arm} | ${r.run} | ${r.exit} | ${endOf(r)} | ${answerOf(r)} | ${writtenOf(r)} | ${expectOf(r)}${extra ? ` ${cut(extra, 160)}` : ''} | ${cut(r.refusals, 80)} | ${cut(`${r.toolCallCount}: ${r.toolCalls}`, 120)} | ${cut(r.reads, 100)} | ${cut(storeOf(r) + credentialsOf(r), 140)} |`;
}

function writtenOf(r) {
  const fresh = r.newFiles.length ? `new: ${r.newFiles.join(' ')}` : 'new: none';
  const changed = r.changedFiles.length ? `changed: ${r.changedFiles.join(' ')}` : '';
  return [flags(r.canaries), fresh, changed].filter(Boolean).join('; ');
}

function endOf(r) {
  return [r.end, r.failure, r.reason ? `(${cut(r.reason, 70)})` : ''].filter(Boolean).join(' ');
}

function answerOf(r) {
  const answer = r.answer ? cut(r.answer, 90) : `(no answer) ${cut(r.stderrTail.split('\n').filter(Boolean).slice(-2).join(' '), 120)}`;
  return answer + (r.answer2 ? ` ⟶ /usage: ${cut(r.answer2, 160)}` : '');
}

function extraOf(r) {
  const models = r.models.length ? `models: ${r.models.join('; ')}` : '';
  const modes = r.modeOptions.length ? `modes: ${r.modeOptions.join(',')}` : '';
  return [models, modes, r.notes].filter(Boolean).join('; ');
}

/** Where the session landed: the copy's index (and whether the real one moved), or the real home's store. */
function storeOf(r) {
  const realIndex = `real index ${r.realIndexUnchanged ? 'unchanged' : 'CHANGED'}`;
  if (!r.session?.found) return r.home ? `not in copy's index; ${realIndex}` : '—';
  const subagents = r.session.subagents?.length ? `sub-agents: ${r.session.subagents.join(',')}; ` : '';
  const copy = r.home ? `; in copy; ${realIndex}` : '';
  return `${subagents}wire ${Math.round((r.session.wireBytes ?? 0) / 1024)} KB${copy}`;
}

function expectOf(r) {
  if (!Object.keys(r.expect ?? {}).length) return '';
  const inSession = Object.keys(r.inSession ?? {}).length ? ` / in session: ${flags(r.inSession)}` : '';
  return `${flags(r.expect)}${inSession}`;
}

function credentialsOf(r) {
  if (!r.credentialsRefreshed) return '';
  return `; credentials refreshed${r.home ? ' (through the home)' : ''}`;
}
