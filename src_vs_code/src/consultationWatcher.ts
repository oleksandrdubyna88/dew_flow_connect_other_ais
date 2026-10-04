import * as vscode from 'vscode';
import { signatureOf } from './codeUnitOrder';
import { Consultation, isLive, parseConsultation } from './consultations';
import { type JsonDirectoryShape, jsonRecordFiles } from './jsonDirectory';
import { JsonDirectoryWatcher } from './jsonDirectoryWatcher';

/**
 * `<dataDir>/consultations/*.json`, as the sidebar's live region reads it: every consultation still going.
 *
 * <p><b>What a person can see, as one string.</b> The turn count and the status are in it because those are what the
 * card SHOWS; the timestamps are not, because the card's "3 min ago" is computed at paint time and would make every
 * read a change. Ordered by `signatureOf`, never `localeCompare`: any TOTAL order would do here, and collation is not
 * one — it calls two distinct ids equal (`é` as one code point and `e` plus an accent), leaves them in the order the
 * directory listed them, and an unchanged snapshot then reads as a change.</p>
 */
export const CONSULTATIONS: JsonDirectoryShape<vscode.Uri, Consultation> = {
  subdir: 'consultations',
  // `.tmp` is a write in flight, and the `answers` subdirectory holds a vendor's own output files.
  records: jsonRecordFiles,
  parse: (text) => parseConsultation(text),
  keep: (one) => isLive(one),
  // A consultation file is named by its id, which is how a failed read still knows what it lost.
  fileOf: (one) => `${one.id}.json`,
  signature: (consultations) => signatureOf(consultations.map((one) => `${one.id}:${one.status}:${one.turns.length}:${one.alert}`), '|'),
};

/**
 * Watches the server's consultation directory so the sidebar can say what is being asked right now — a configuration
 * of `JsonDirectoryWatcher` (S4b item 12), which carries the glob, the debounce, the UNC poll, the last good snapshot
 * and the generation guard for all three record watchers.
 *
 * <p><b>One surface, not three.</b> The escalation watcher raises a modal and holds a status-bar item because a round
 * is BLOCKED behind its question. Nothing is blocked here: an AI asked another vendor and is waiting on it, which is
 * information rather than a demand, so it appears where a person is already looking and nowhere else.</p>
 */
export class ConsultationWatcher extends JsonDirectoryWatcher<Consultation> {
  constructor(dataDir: vscode.Uri) {
    super([dataDir], { shape: CONSULTATIONS });
  }

  /** Everything still going — the sidebar renders these and nothing else. */
  get running(): readonly Consultation[] {
    return this.items;
  }
}
