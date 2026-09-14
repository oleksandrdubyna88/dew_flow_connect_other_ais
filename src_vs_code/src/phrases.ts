import { NAME_LIMIT, freshId, hasWords, nameFor, rawText, record, text, withId } from './savedRows';

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
 * <p><b>The TEXT is taken VERBATIM — not trimmed, not truncated.</b> A name has a width to respect
 * and a phrase has meaning to keep, and the meaning includes its whitespace: an indented snippet
 * pastes as an indented snippet, and a trailing newline is a keystroke somebody deliberately saved.
 * `hasWords` asks whether there is anything but whitespace without copying the body to find out.
 * (Code round, 2026-09-14: the first version trimmed, which is right for a prompt and wrong for
 * something whose whole job is to be pasted.)</p>
 */
function phraseFrom(one: Record<string, unknown>, index: number, taken: Set<string>): Phrase[] {
  if (!hasWords(one['text'])) {
    return [];
  }
  const body = rawText(one['text']);
  const written = text(one['name']).slice(0, NAME_LIMIT);

  return [{
    id: withId(text(one['id']), index, taken, 'phrase'),
    name: written.length === 0 ? nameFor(body) : written,
    text: body,
  }];
}

/**
 * The phrases, out of whatever the setting holds.
 *
 * <p>Nothing throws: `settings.json` is a file people edit with an editor, so a string where a
 * record should be, a null in the middle of the array, or the whole value being a number are all
 * ordinary. Each bad row is dropped and the rest survive.</p>
 */
export function phrasesFrom(saved: unknown): readonly Phrase[] {
  const rows = Array.isArray(saved) ? saved : [];
  const taken = new Set<string>();

  return rows.flatMap((row, index): Phrase[] => {
    const one = record(row);

    return one === undefined ? [] : phraseFrom(one, index, taken);
  });
}

/** The phrase a button named, or nothing — a click naming an id that is gone chooses nothing. */
export { rowById as phraseById } from './savedRows';

/**
 * A new row, in the shape `phrasesFrom` will KEEP — the `freshPromptRow` rule, for the same reason.
 *
 * <p>A `Phrase` rather than a mutable twin of one: this list has a single entity shape, so a second
 * type for the same three fields would only make the tab that edits them convert between two names
 * for one thing. (Code round, 2026-09-14.)</p>
 */
export function freshPhraseRow(taken: readonly { readonly id: string }[]): Phrase {
  return { id: freshId(taken, 'phrase'), name: 'New phrase', text: 'Say something' };
}
