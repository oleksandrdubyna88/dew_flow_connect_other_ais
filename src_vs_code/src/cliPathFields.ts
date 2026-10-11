import { droppedNoticeText, type DroppedValue } from './cliPathKeep';
import { otherSideCliKey } from './cliPathSave';
import { escapeHtml } from './escapeHtml';
import { HELP } from './help';
import { help } from './panelControls';
import { familyLabel, otherSideNote, sharedValueNote, sharedValueOfTheOtherOs, storedForThisSide, type PathFamily } from './pathFamily';
import type { PathFinding } from './pathFindings';
import { markAttributes, markClass, markNote, stableId } from './pathMark';
import { VENDOR_EXECUTABLE } from './pathSettings';
import { keepsItsOwnLine, otherSideFieldset, thisSideCaption, type PathSides } from './pathSides';
import { cliBoxKey, refusalAttributes, refusalNote, type TypedRefusal, type TypedRefusals } from './typedRefusals';
import type { Vendor } from './vendors';

/**
 * A Models card's CLI path (todo/PLAN_paths_per_side.md E4.1): ONE box — THIS side's field, exactly as it was drawn
 * before E4 — or, once both sides are known (`pathSides.ts`), two: this side's first, labelled *This side — Windows*,
 * then the OTHER side's in a `fieldset` with its legend and caption, editable as text only. No Browse on the other side's
 * box: this host cannot browse that disk, and its value is never `stat`ed from here.
 *
 * <p>The other side's box posts `executablePath.<family>` through the panel's one vendor write (`vendorFieldWrite`), which
 * saves only that side's member (`savedCliPath`). A path of this side's OS typed into it is refused by the host
 * (`cliPathRefusal`) and drawn BESIDE the box with what was typed (`typedRefusals.ts`). While a previously saved per-side
 * value is missing (`CliPathKeeper.dropped`), **Restore** sits under the field — the panel button E3's notice duplicates.</p>
 *
 * <p>While this side keeps its own settings (`coai.perSideSettings`, so `vendors` is saved into this side's own copy) the
 * other side's box is NOT drawn: what it saved would be read by no one on that side. A read-only sentence stands in its
 * place, and the host refuses a write a page drawn before the switch moved still posts (E4's own review, item 1).</p>
 *
 * <p>A row whose other side has no value of its own and whose SHARED legacy value is spelled for this window's OS draws
 * the other side's box EMPTY, with a quiet hint naming the shared value as this side's: that side skips it and looks the
 * CLI up on PATH, and the box's own rule would refuse that text (E4's own review, item 2). Decided by spelling only.</p>
 */

/** What the CLI-path field(s) are drawn from, beside the row. */
export interface CliPathFacts {
  /** The family of THIS side when the row's stored value is the other side's (the host decided it) — else undefined, no note. */
  readonly otherSideCli: PathFamily | undefined;
  /** The finding about this side's value — missing, or unanswerable — when the watch has one; else undefined, no mark. */
  readonly missingCli: PathFinding | undefined;
  /** The family the page is drawn for: this side's box shows the row's value for it (`storedForThisSide`). */
  readonly family: PathFamily;
  /** Both sides, when the path setting shows two fields; undefined draws one. */
  readonly sides?: PathSides | undefined;
  /** The row's previously saved value that is missing, offered back by **Restore**. */
  readonly dropped?: DroppedValue | undefined;
  /** What was typed into an other side's box and refused, by box. */
  readonly refusals?: TypedRefusals | undefined;
  /**
   * Whether `vendors` is saved into THIS side's own settings (`coai.perSideSettings`, `settingsShape.keptPerSide`): the
   * other side's box is then not drawn — a sentence stands in its place (E4's own review, item 1).
   */
  readonly ownSettings?: boolean | undefined;
}

/** The CLI-path field(s) of a row whose runtime runs a CLI here. */
export function cliPathFieldsHtml(vendor: Vendor, cli: CliPathFacts): string {
  return thisSideField(vendor, cli) + otherSidesNote(cli.otherSideCli) + otherSideField(vendor, cli) + restoreLine(cli.dropped);
}

/** A box's element id, unescaped — `cli-<row>-<family>`. */
function boxId(vendor: string, family: PathFamily): string {
  return `cli-${vendor}-${family}`;
}

/** This side's box — labelled only when there are two, so one field is drawn byte for byte as before E4. */
function thisSideField(vendor: Vendor, cli: CliPathFacts): string {
  const id = escapeHtml(boxId(vendor.id, cli.family));
  const label = cli.sides === undefined ? '' : `\n    <label for="${id}">${help('vendorExecutablePath')}${escapeHtml(thisSideCaption(cli.sides))}</label>`;
  const named = cli.sides === undefined ? '' : ` id="${id}"`;

  return `
  <div class="field${markClass(cli.missingCli)}">${label}
    <input type="text"${named} data-setting="executablePath" data-vendor="${escapeHtml(vendor.id)}" title="${escapeHtml(HELP.vendorExecutablePath)}"
           placeholder="CLI path — empty means look it up on PATH" value="${escapeHtml(storedForThisSide(vendor, cli.family))}"${markAttributes(cli.missingCli)}>${markNote(cli.missingCli)}
  </div>`;
}

/**
 * The quiet line under a CLI path spelled for the OTHER operating system (todo/PLAN_paths_per_side.md E1.5): VS Code
 * shares the row with the other side's window, where the path is right; here it is skipped and the CLI looked up on
 * PATH — the same decision the settings file this window writes (`pathForThisSide`) and coai-mcp's own read make.
 */
function otherSidesNote(family: PathFamily | undefined): string {
  return family === undefined
    ? ''
    : `<div class="hint path-other-side">${escapeHtml(otherSideNote(family === 'windows', VENDOR_EXECUTABLE))}</div>`;
}

/**
 * The other side's group: its box — or, while this side keeps its own settings, the sentence that says why there is none,
 * with a refusal of a write a page drawn before the switch moved still posted (E4's own review, item 1).
 */
function otherSideField(vendor: Vendor, cli: CliPathFacts): string {
  if (cli.sides === undefined) {
    return '';
  }
  const other = cli.sides.other;
  const box = cliBoxKey(vendor.id, other);
  const refusal = cli.refusals?.[box];
  const id = boxId(vendor.id, other);
  const body = cli.ownSettings === true ? keepsItsOwnLine() + refusalNote(refusal, box) : otherSideBox(vendor, other, refusal, id);

  return `\n  ${otherSideFieldset(cli.sides, captionOf(id), body)}`;
}

/** The id of the other side's caption, unescaped — the box points at it. */
function captionOf(id: string): string {
  return `${id}-caption`;
}

/** The other side's box: the value that side's field holds, or the text a refusal kept. */
function otherSideBox(vendor: Vendor, other: PathFamily, refusal: TypedRefusal | undefined, id: string): string {
  const box = cliBoxKey(vendor.id, other);
  const attributes = refusalAttributes(refusal, box);
  const shared = sharedHint(vendor, other, id);

  return `<div class="field"><label for="${escapeHtml(id)}">CLI path on ${familyLabel(other)}</label>`
    + `<input type="text" id="${escapeHtml(id)}" data-setting="${otherSideCliKey(other)}" data-vendor="${escapeHtml(vendor.id)}"`
    + ` placeholder="CLI path on that side — empty means that side looks it up on PATH" value="${escapeHtml(refusal?.typed ?? otherSideValue(vendor, other))}"`
    + ` aria-describedby="${escapeHtml(captionOf(id))}${shared.describedBy}${attributes.describedBy}"${attributes.invalid}>${shared.html}${refusalNote(refusal, box)}</div>`;
}

/**
 * What the other side's box shows: the value that side's field holds — or nothing, when that is only the SHARED legacy
 * value and it is spelled for THIS window's OS, which that side does not run (E4's own review, item 2).
 */
function otherSideValue(vendor: Vendor, other: PathFamily): string {
  return sharedValueOfTheOtherOs(vendor, other) ? '' : storedForThisSide(vendor, other);
}

/** The quiet hint beside a box drawn empty by {@link otherSideValue}, and the id the box points at — nothing otherwise. */
function sharedHint(vendor: Vendor, other: PathFamily, id: string): { readonly html: string; readonly describedBy: string } {
  if (!sharedValueOfTheOtherOs(vendor, other)) {
    return { html: '', describedBy: '' };
  }
  const note = `${id}-shared`;

  return {
    html: `<div class="hint path-other-side" id="${escapeHtml(note)}">${escapeHtml(sharedValueNote(vendor.executablePath, VENDOR_EXECUTABLE))}</div>`,
    describedBy: ` ${escapeHtml(note)}`,
  };
}

/** **Restore** and what it puts back — while a previously saved value of the row is missing. */
function restoreLine(dropped: DroppedValue | undefined): string {
  if (dropped === undefined) {
    return '';
  }
  const note = stableId('cli-kept', dropped.key);

  return `\n  <div class="hint path-kept" id="${note}">${escapeHtml(droppedNoticeText(dropped).title)}</div>`
    + `<button type="button" class="link" data-command="restoreCliPath" data-id="${escapeHtml(dropped.key)}" aria-describedby="${note}">Restore</button>`;
}
