import { escapeHtml } from './escapeHtml';
import type { CheckLine, FailureLine, LimitationLine, RowHealth, SideBlock, SnippetBlock } from './consultantHealthState';

/**
 * Each Consultant row's health block, as markup — E5.2 of `research/PLAN_the_consultant_works_on_every_vendor.md`.
 *
 * <p>Renders a decided value (`consultantHealthState.ts`) and decides nothing: whether a failure is shown, whether a
 * Check exists and whether it is disabled were answered before this was called, where a test could see them without a
 * tag. Every value is escaped; the structure is a `section` per side opening with a real heading (`h4`, below the
 * tab's own headings) and a description list, so a screen reader can jump to "On Windows" and then hears each fact by
 * its name (the whole-branch review, P).</p>
 *
 * <p><b>No live region.</b> The check lines used to sit in a `role="status" aria-live` region, which a full repaint
 * replaces with the whole document — a region created fresh on every repaint announces nothing, so it promised what it
 * never did. A paid Check that lands is announced through the notification funnel instead
 * (`consultantHealthHost.ts`, `landedSentence`).</p>
 *
 * <p><b>This side</b> gets the Check button (`data-command="checkConsultant"`, `data-id` the caller kind) and, on
 * Linux/WSL, agy's allow rule with its warning and a Copy button. <b>Another side</b> is read-only: no button at all,
 * the time its facts were taken, and where a window that can check it would be.</p>
 */

/** The copy commands the page's `copied` acknowledgement may relabel — and no other command's buttons. */
export const COPY_COMMANDS: readonly string[] = ['copyPhrase', 'copyConsultantSnippet'];

/** The row's whole block: this side, then every other side named. Empty when the row was handed no health. */
export function healthBlock(health: RowHealth | undefined): string {
  if (health === undefined) {
    return '';
  }

  return [health.thisSide, ...health.otherSides].map(sideSection).join('\n');
}

function sideSection(block: SideBlock): string {
  return `  <section class="consultant-health${block.readOnly ? ' read-only' : ''}" aria-label="${escapeHtml(headingOf(block))}">
    <h4 class="health-side">${escapeHtml(headingOf(block))}</h4>
${block.notices.map((notice) => `    <p class="hint stale">${escapeHtml(notice)}</p>\n`).join('')}    <dl class="health-facts">
${facts(block)}
    </dl>
${snippet(block.snippet)}${agyFact(block.agyFact)}${pointer(block.pointer)}  </section>`;
}

function headingOf(block: SideBlock): string {
  const asOf = block.asOf.length > 0 ? `, as of ${block.asOf}` : '';

  return block.readOnly ? `On ${block.side} — read-only${asOf}` : `On ${block.side}${asOf}`;
}

function facts(block: SideBlock): string {
  return [
    fact('CLI', block.cli.length > 0 ? escapeHtml(block.cli) : ''),
    fact('Confinement', limitation(block.limitation)),
    fact('Last failure', failure(block.failure)),
    fact('Check', check(block)),
  ].filter((one) => one.length > 0).join('\n');
}

function fact(term: string, description: string): string {
  return description.length === 0 ? '' : `      <dt>${escapeHtml(term)}</dt><dd>${description}</dd>`;
}

function limitation(line: LimitationLine | undefined): string {
  if (line === undefined) {
    return '';
  }

  return `<span class="standing standing-${escapeHtml(line.standing)}">${escapeHtml(line.standingLabel)}</span> ${escapeHtml(line.text)}`
    + ` <span class="hint">${escapeHtml(line.backing)}</span>`;
}

function failure(line: FailureLine | undefined): string {
  if (line === undefined) {
    return '';
  }

  return `<b>${escapeHtml(line.label)}</b>, <time datetime="${escapeHtml(line.utc)}">${escapeHtml(line.when)}</time> — ${escapeHtml(line.what)}`
    + `${line.cure.length > 0 ? `<div>${escapeHtml(line.cure)}</div>` : ''}`
    + `${line.evidence.length > 0 ? `<div class="hint">Its transcript: <code>${escapeHtml(line.evidence)}</code></div>` : ''}`;
}

/** The check's lines and this side's button beside them — no live region: a landed Check is announced by the host. */
function check(block: SideBlock): string {
  if (block.check.lines.length === 0 && block.checkButton === undefined) {
    return '';
  }

  return `<div class="health-check">${lines(block.check)}</div>${button(block)}`;
}

function lines(line: CheckLine): string {
  return line.lines.map((one) => `<div>${escapeHtml(one)}</div>`).join('');
}

function button(block: SideBlock): string {
  const one = block.checkButton;
  if (one === undefined) {
    return '';
  }
  const disabled = one.disabled ? ' disabled aria-disabled="true"' : '';

  return `<button type="button" class="link" data-command="checkConsultant" data-id="${escapeHtml(one.id)}"${disabled}`
    + ` title="One real, paid turn of this consultant in a scratch folder — never your code">${escapeHtml(one.label)}</button>`;
}

function snippet(one: SnippetBlock | undefined): string {
  if (one === undefined) {
    return '';
  }

  return `    <div class="health-snippet">
      <p class="hint">An allow rule for agy's settings file, <code>${escapeHtml(one.settingsPath)}</code> — coai never writes it:</p>
      <pre>${escapeHtml(one.text)}</pre>
      <p class="hint stale">${escapeHtml(one.warning)}</p>
      <button type="button" class="link" data-command="copyConsultantSnippet" data-id="${escapeHtml(one.id)}">Copy</button>
    </div>
`;
}

function agyFact(text: string): string {
  return text.length === 0 ? '' : `    <p class="hint">${escapeHtml(text)}</p>\n`;
}

function pointer(text: string): string {
  return text.length === 0 ? '' : `    <p class="hint">${escapeHtml(text)}</p>\n`;
}

/**
 * The block's own rules, beside the consultant row's in the panel stylesheet. A thin top rule separates it from the
 * row's controls; another side's block is indented and dimmed a little, so read-only reads as read-only before a word
 * of it is read. No colour of ours: theme tokens only.
 */
export const CONSULTANT_HEALTH_CSS = `
  .consultant-health { border-top: 1px dashed var(--vscode-widget-border); margin: 6px 0 4px; padding: 4px 0 0; }
  .consultant-health.read-only { padding-left: 8px; opacity: .85; }
  .consultant-health .health-side { font-size: calc(11rem / 13); font-weight: 600; opacity: .8; margin: 0 0 2px; }
  .consultant-health .health-facts { display: grid; grid-template-columns: max-content 1fr; gap: 2px 8px; margin: 2px 0; }
  .consultant-health .health-facts dt { font-size: calc(11rem / 13); opacity: .7; }
  .consultant-health .health-facts dd { margin: 0; min-width: 0; overflow-wrap: anywhere; }
  .consultant-health .standing { font-weight: 600; }
  .consultant-health .standing-unconfined { color: var(--vscode-errorForeground); }
  .consultant-health .standing-confined { color: var(--vscode-charts-green); }
  .consultant-health pre { white-space: pre-wrap; overflow-wrap: anywhere; margin: 2px 0; padding: 4px 6px; background: var(--vscode-textCodeBlock-background); }
  .consultant-health p { margin: 2px 0; }
`;
