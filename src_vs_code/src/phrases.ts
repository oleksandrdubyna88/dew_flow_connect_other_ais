import { NAME_LIMIT, freshId, nameFor, record, rowById, text, withId } from './savedRows';

/**
 * The phrases a person keeps, so they stop retyping the same sentence into the Claude Code box.
 *
 * <p>Pure, over whatever `settings.json` actually holds — no `vscode` handle, so every rule below is
 * a unit test rather than a claim. The rules themselves are `savedRows.ts`, shared with the prompt
 * presets, because a phrase list and a prompt list are the same BOUNDARY seen twice: an array a
 * person edits by hand, where one mistyped row must never take the rest with it.</p>
 *
 * <p><b>What is different here is what a row MEANS.</b> A prompt preset is a name and an
 * instruction, and both matter. A phrase is a piece of writing the person wants back verbatim; the
 * name is only what fits on a button. That asymmetry decides the one rule this module does not share
 * with its sibling, and it is in `phrasesFrom` below.</p>
 *
 * <p><b>Person-level, and it does not cross to the server</b> — absent from `envBlock`, absent from
 * the settings mirrored into `coai-mcp`, and not in `OVERLAID_SETTINGS`. The same two decisions
 * `chatPresets.ts` records, for the same reason: a setting that travels where it is not read is a
 * setting that will one day be read by accident.</p>
 */

export interface Phrase {
  readonly id: string;
  /** What the button says. Capped, because a button in a column has a width. */
  readonly name: string;
  /** What lands on the clipboard, verbatim and never truncated. */
  readonly text: string;
}

/** A row as it sits in `settings.json`, which is the shape the CRUD tab writes back. */
export interface PhraseRow {
  id: string;
  name: string;
  text: string;
}

/**
 * The phrases, out of whatever the setting holds.
 *
 * <p>Nothing throws: `settings.json` is a file people edit with an editor, so a string where a
 * record should be, a null in the middle of the array, or the whole value being a number are all
 * ordinary. Each bad row is dropped and the rest survive.</p>
 */
/**
 * One row, or nothing.
 *
 * <p><b>The TEXT is what makes a phrase exist, and the name is not.</b> This is where this list
 * parts company with the prompt presets, which require both: there, a preset with no name is a
 * button nobody can label and the instruction is one a person can write again. Here the text IS the
 * thing they were trying not to retype, and a row with no name is what a hand-edited
 * `settings.json` looks like — `{ "text": "deploy it" }` is the obvious thing to type. Dropping it
 * would delete their writing to punish a missing label, so the label is derived from its own first
 * line instead. (Gate finding 1, accepted 2026-09-14.)</p>
 *
 * <p>The TEXT is never truncated: a name has a width to respect and a phrase has meaning to keep.</p>
 */
function phraseFrom(one: Record<string, unknown>, index: number, taken: Set<string>): Phrase[] {
  const body = typeof one['text'] === 'string' ? one['text'].trim() : '';
  if (body.length === 0) {
    return [];
  }
  const written = text(one['name']).slice(0, NAME_LIMIT);

  return [{
    id: withId(text(one['id']), index, taken, 'phrase'),
    name: written.length === 0 ? nameFor(body) : written,
    text: body,
  }];
}

export function phrasesFrom(saved: unknown): readonly Phrase[] {
  const rows = Array.isArray(saved) ? saved : [];
  const taken = new Set<string>();

  return rows.flatMap((row, index): Phrase[] => {
    const one = record(row);

    return one === undefined ? [] : phraseFrom(one, index, taken);
  });
}

/** The phrase a button named, or nothing — a click naming an id that is gone chooses nothing. */
export const phraseById = rowById;

/** A new row, in the shape `phrasesFrom` will KEEP — the `freshPromptRow` rule, for the same reason. */
export function freshPhraseRow(taken: readonly { readonly id: string }[]): PhraseRow {
  return { id: freshId(taken, 'phrase'), name: 'New phrase', text: 'Say something' };
}
