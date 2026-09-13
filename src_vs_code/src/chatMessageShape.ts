import { ChatMessage } from './chatPage';

/**
 * Whether a stored value is a message this build can RENDER.
 *
 * <p>Its own module, and the reason is a defect rather than tidiness. There were two copies of this
 * rule — one in `chatStore.ts` for a record on disk, one in `chatTabs.ts` for a record in the
 * memento — and when the first was tightened to check the optional fields the second was not. That
 * gap is not cosmetic: a memento entry carrying `model: { id: 'x', label: null }` passed the laxer
 * validator, was carried into the store by the migration, and came back from the store as
 * `incompatible` — so a conversation that should have been set aside in the quarantine, where a
 * person can go and get it, became one that reads as unreadable instead. One rule, one place.</p>
 *
 * <p>A third module rather than one importing the other: `chatStore.ts` already imports `SavedTab`
 * from `chatTabs.ts`, so either direction would be a runtime cycle between two modules that both
 * export functions.</p>
 *
 * <p><b>What is checked, and what deliberately is not.</b> `role` and `text` make a message; without
 * either there is nothing to draw. `model` and `marks` are OPTIONAL — records written before those
 * fields existed have neither, and dropping those would discard real conversations — but a value
 * that is PRESENT and malformed is rejected rather than repaired, like every other field here. The
 * page reads `message.model.label.length` directly, so a stored `label: null` is a render-time throw
 * on a file a hand edit or a torn write can leave. (CodeRabbit, pull request #223.)</p>
 */

const isText = (value: unknown): value is string => typeof value === 'string';

/** Which model answered, as the page renders it: both strings, or it is not one. */
const isAnsweredBy = (value: unknown): boolean => {
  const row = value as { id?: unknown; label?: unknown } | null;

  return row !== null && typeof row === 'object' && isText(row.id) && isText(row.label);
};

/** What a question was asked with: both halves strings, or it is not one. */
const isMarks = (value: unknown): boolean => {
  const row = value as { role?: unknown; task?: unknown } | null;

  return row !== null && typeof row === 'object' && isText(row.role) && isText(row.task);
};

/** One message, whole enough to draw. */
export const isChatMessage = (value: unknown): value is ChatMessage => {
  const row = value as { role?: unknown; text?: unknown; model?: unknown; marks?: unknown } | null;

  return row !== null && typeof row === 'object'
    && (row.role === 'you' || row.role === 'model') && isText(row.text)
    && (row.model === undefined || isAnsweredBy(row.model))
    && (row.marks === undefined || isMarks(row.marks));
};
