import { CATALOG_BADGES, CATALOG_CHIPS, CATALOG_TOKENS } from './catalogSheet';
import { escapeHtml } from './escapeHtml';
import { TEAM_SERVER_WITH_PEOPLE } from './teamServerTab';
import { DEFAULT_TEAM_WINDOW, TEAM_WINDOWS } from './teamTabSelection';

/**
 * The Team server tab's place on the Review rounds page: its button, its section, its sheet and its script
 * (todo/PLAN_team_usage_by_person.md, story 1.3). The figures inside it arrive by push (`teamServerTab.ts`).
 *
 * <p><b>Always drawn, hidden.</b> The page is built once, and whether this account is an admin anywhere is learned
 * after it — from a catalog that can take seconds. So the button and the section are in every page, hidden, and the
 * push's `admin` flag reveals or hides them; a push to a section that was never drawn would throw in the listener.</p>
 *
 * <p><b>The toolbar, the search and the sort are never replaced</b> — a push replaces the pieces under them, the cards
 * the way the consultations tabs keep their folds open — so the text being typed, the caret, the order and an opened
 * card all survive the figures moving underneath.</p>
 */

/** What the tab is called — on the page, and in every language of the help, which names it (helpCoverage.test.ts). */
export const TEAM_TAB_LABEL = 'Team server';

/** The tab button. The `admin` mark is a CHILD, because the tab handler rewrites the button's className. */
export const TEAM_TAB_BUTTON = '<button type="button" class="tab" data-tab="team" id="team-tab" hidden>'
  + `${TEAM_TAB_LABEL}<span class="tag-admin">admin</span></button>`;

function windowChips(): string {
  return TEAM_WINDOWS.map((one) => `<button type="button" class="chip" data-team-window="${one.id}" `
    + `aria-pressed="${one.id === DEFAULT_TEAM_WINDOW ? 'true' : 'false'}">${one.label}</button>`).join('');
}

const NEEDS = escapeHtml(`needs Team server ≥ ${TEAM_SERVER_WITH_PEOPLE}`);

function sortOptions(): string {
  return '<option value="launches">Launches</option><option value="tokens">Tokens</option>'
    + `<option value="cost" disabled>~$ — ${NEEDS}</option><option value="seen" disabled>Last seen — ${NEEDS}</option>`;
}

/** The section, with every piece a push fills in empty — and the toolbar it never touches. */
export function teamSectionHtml(waiting: string): string {
  return `<section id="tab-team" data-section="team" class="catalog team" hidden>
<div class="toolbar">
  <span id="team-server-pick" hidden><label class="hint" for="team-server">Server</label> <select id="team-server"></select></span>
  <span class="chip-row" role="group" aria-label="Window">${windowChips()}</span>
  <span class="spacer"></span>
  <span id="team-read" class="hint" aria-live="polite"></span>
  <button type="button" class="secondary" id="team-refresh">Refresh</button>
</div>
<div id="team-status">${waiting}</div>
<div id="team-badges" class="badges"></div>
<div id="team-summary"></div>
<div class="team-cols">
  <div>
    <div class="colhead"><h2>People</h2><span id="team-count" class="hint"></span><span class="spacer"></span>
      <input type="search" id="team-q" placeholder="Search name or email" aria-label="Search people" autocomplete="off">
      <label class="hint" for="team-sort">Sort</label> <select id="team-sort" aria-label="Sort people">${sortOptions()}</select>
    </div>
    <div id="team-people" class="team-list"></div>
    <div id="team-none" class="quiet" hidden>Nobody matches that search.</div>
    <div id="team-idle"></div>
  </div>
  <div>
    <div class="colhead"><h2>Vendors</h2><span class="spacer"></span><span class="hint">the server's accounts</span></div>
    <div id="team-vendors"></div>
    <div id="team-chart"></div>
  </div>
</div>
<div id="team-notes" class="notes"></div>
</section>`;
}

/**
 * The tab's sheet: the Settings design's tokens, chips and badges (the leaf `catalogSheet.ts`, not a copy), and this
 * tab's own layout, scoped to it — the Settings cards' four-row subgrid stays the Settings page's. No selector names a
 * button: the page's button rules are a fixed four (roundsLogPage.test.ts).
 */
export const TEAM_TAB_CSS = `${CATALOG_TOKENS}${CATALOG_CHIPS}${CATALOG_BADGES}
  .tabs .tag-admin { font-size: .72em; text-transform: uppercase; letter-spacing: .05em; border: 1px solid var(--vscode-textLink-foreground); color: var(--vscode-textLink-foreground); border-radius: 3px; padding: 0 4px; margin-left: 6px; vertical-align: 1px; }
  .team .toolbar { display: flex; flex-wrap: wrap; gap: 8px 14px; align-items: center; margin: 4px 0 10px; }
  .team .spacer { flex: 1; }
  .team .num { font-variant-numeric: tabular-nums; }
  .team .warn { color: var(--warn); }
  .team .team-status { margin: 0 0 8px; }
  .team .team-summary { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); border: 1px solid var(--border); border-radius: 4px; background: var(--card); margin: 8px 0 16px; }
  .team .team-summary > div { padding: 10px 14px; border-left: 1px solid var(--border); min-width: 0; }
  .team .team-summary > div:first-child { border-left: none; }
  .team .team-summary .k { font-size: .85em; text-transform: uppercase; letter-spacing: .06em; color: var(--muted); }
  .team .team-summary .v { font-size: 1.5em; font-weight: 600; margin-top: 2px; }
  .team .team-summary .s { color: var(--muted); font-size: .92em; }
  @media (max-width: 760px) { .team .team-summary { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
  .team .team-cols { display: grid; grid-template-columns: minmax(0, 1fr); gap: 18px; align-items: start; }
  @media (min-width: 1100px) { .team .team-cols { grid-template-columns: minmax(0, 1.35fr) minmax(0, 1fr); } }
  .team .colhead { display: flex; flex-wrap: wrap; gap: 8px 12px; align-items: center; margin: 0 0 8px; }
  .team .colhead h2 { font-size: 1em; margin: 0; }
  .team .team-list { display: grid; gap: 10px; }
  .team .person { background: var(--card); border: 1px solid var(--border); border-left: 3px solid var(--border-strong); border-radius: 4px; }
  .team .person[open] { border-left-color: var(--focus); }
  .team .person > summary { list-style: none; cursor: pointer; padding: 10px 12px; display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: 2px 10px; align-items: center; }
  .team .person > summary::-webkit-details-marker { display: none; }
  .team .avatar { width: 28px; height: 28px; border-radius: 50%; display: grid; place-items: center; font-size: .8em; font-weight: 600; background: var(--secondary); grid-row: span 2; }
  .team .who { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .team .cost { text-align: right; color: var(--muted); }
  .team .line2 { grid-column: 2 / 4; color: var(--muted); font-size: .92em; display: flex; flex-wrap: wrap; gap: 2px 12px; }
  .team .stack { grid-column: 2 / 4; height: 6px; border-radius: 3px; background: var(--border); display: flex; overflow: hidden; margin-top: 4px; }
  .team .stack i { display: block; height: 100%; }
  .team .detail { padding: 2px 12px 12px 50px; display: grid; gap: 8px; }
  .team table.t { width: 100%; border-collapse: collapse; font-size: .92em; }
  .team table.t th, .team table.t td { text-align: right; padding: 3px 0 3px 10px; border-bottom: 1px solid var(--border); white-space: nowrap; }
  .team table.t th { font-weight: 400; color: var(--muted); }
  .team table.t th:first-child, .team table.t td:first-child { text-align: left; padding-left: 0; }
  .team .tablewrap { overflow-x: auto; }
  .team .dot { display: inline-block; width: 8px; height: 8px; border-radius: 50%; margin-right: 6px; }
  .team .quiet { border: 1px dashed var(--border-strong); border-radius: 4px; padding: 8px 12px; color: var(--muted); margin-top: 10px; }
  .team .quiet > summary { cursor: pointer; }
  .team .vendor { background: var(--card); border: 1px solid var(--border); border-left: 3px solid var(--vc, var(--border-strong)); border-radius: 4px; padding: 10px 14px; }
  .team .vendor + .vendor { margin-top: 10px; }
  .team .vhead { display: flex; gap: 8px; align-items: baseline; }
  .team .vhead b { color: var(--vc); }
  .team .vhead .model { color: var(--muted); font-family: var(--vscode-editor-font-family); font-size: .92em; }
  .team .vline { color: var(--muted); font-size: .92em; margin-top: 4px; }
  .team .chart { background: var(--card); border: 1px solid var(--border); border-radius: 4px; padding: 10px 12px; margin-top: 10px; }
  .team .notes { margin-top: 16px; color: var(--muted); font-size: .92em; display: grid; gap: 3px; max-width: 110ch; }`;

/** Whether a card's search text holds what was typed — the server's emails, lower-cased on the way in. Embedded. */
export function teamMatches(search: string, query: string): boolean {
  return search.indexOf(query.trim().toLowerCase()) >= 0;
}

/** The order of two cards: the chosen figure, highest first, then by email so equal ones never swap. Embedded. */
export function teamOrder(
  a: { readonly launches: number; readonly tokens: number; readonly search: string },
  b: { readonly launches: number; readonly tokens: number; readonly search: string },
  key: string,
): number {
  const by = key === 'tokens' ? b.tokens - a.tokens : b.launches - a.launches;

  return by !== 0 ? by : (a.search < b.search ? -1 : Number(a.search > b.search));
}

/**
 * The tab's half of the page script. Plain page JavaScript, inserted into the page's own closure, so it shares its
 * \`send\`, \`told\`, \`showTab\` and \`replaceKeepingFolds\`; the two helpers above are embedded by their source, the
 * existing way, so the functions a test runs are the ones the page runs. No backticks in here.
 */
export const TEAM_TAB_SCRIPT = `
  var teamMatches = ${teamMatches.toString()};
  var teamOrder = ${teamOrder.toString()};
  var team = { server: '', query: '', sort: 'launches', servers: null };
  function teamFacts(card) {
    return { launches: Number(card.getAttribute('data-launches')) || 0, tokens: Number(card.getAttribute('data-tokens')) || 0,
      search: card.getAttribute('data-search') || '' };
  }
  // Search and sort are the page's own: no round trip, and they survive every push because they run after it.
  function teamArrange() {
    var list = document.getElementById('team-people');
    var cards = Array.prototype.slice.call(list.querySelectorAll('details[data-fold]'));
    cards.sort(function (a, b) { return teamOrder(teamFacts(a), teamFacts(b), team.sort); });
    var shown = 0;
    for (var i = 0; i < cards.length; i++) {
      list.appendChild(cards[i]);
      cards[i].hidden = !teamMatches(cards[i].getAttribute('data-search') || '', team.query);
      if (!cards[i].hidden) { shown++; }
    }
    document.getElementById('team-count').textContent = cards.length === 0 ? '' : shown + ' of ' + cards.length;
    document.getElementById('team-none').hidden = cards.length === 0 || shown > 0;
  }
  function teamMarkWindow(which) {
    var chips = document.querySelectorAll('[data-team-window]');
    for (var i = 0; i < chips.length; i++) {
      chips[i].setAttribute('aria-pressed', chips[i].getAttribute('data-team-window') === which ? 'true' : 'false');
    }
  }
  function teamClock(iso) {
    var d = new Date(iso);
    var p = function (n) { return (n < 10 ? '0' : '') + n; };
    return isNaN(d.getTime()) ? '' : 'Read ' + p(d.getHours()) + ':' + p(d.getMinutes());
  }
  // The toolbar is UPDATED, never replaced: the picker's options only when the list of servers changed.
  function teamToolbar(message) {
    var picker = document.getElementById('team-server');
    if (team.servers !== message.servers) {
      team.servers = message.servers;
      picker.innerHTML = message.servers;
    }
    picker.value = message.selected;
    document.getElementById('team-server-pick').hidden = picker.querySelectorAll('option').length < 2;
    team.server = message.selected;
    teamMarkWindow(message.window);
    document.getElementById('team-read').textContent = message.readUtc ? teamClock(message.readUtc) : '';
  }
  function teamFill(parts) {
    var plain = ['status', 'badges', 'summary', 'vendors', 'chart', 'notes'];
    for (var i = 0; i < plain.length; i++) {
      document.getElementById('team-' + plain[i]).innerHTML = parts[plain[i]] || '';
    }
    replaceKeepingFolds(document.getElementById('team-people'), parts.people || '');
    replaceKeepingFolds(document.getElementById('team-idle'), parts.idle || '');
    teamArrange();
  }
  // The admin flag reveals the tab or takes it away; a page that was SHOWING it when the flag went goes back to Rounds.
  function teamReveal(admin) {
    var button = document.getElementById('team-tab');
    button.hidden = !admin;
    if (admin) { return; }
    if (currentTab === 'team') { showTab('rounds'); }
    document.getElementById('tab-team').hidden = true;
  }
  function teamPushed(message) {
    told.team = true;
    teamReveal(message.admin === true);
    if (message.admin !== true) { return; }
    teamToolbar(message);
    teamFill(message.parts || {});
  }
  function teamWindowPressed(chip) {
    var which = chip.getAttribute('data-team-window');
    teamMarkWindow(which);
    send({ type: 'command', command: 'teamWindow', id: team.server + '|' + which }, chip);
  }
  document.getElementById('team-q').addEventListener('input', function (event) {
    team.query = event.target.value;
    teamArrange();
  });
  document.getElementById('team-sort').addEventListener('change', function (event) {
    team.sort = event.target.value;
    teamArrange();
  });
  document.getElementById('team-server').addEventListener('change', function (event) {
    team.server = event.target.value;
    send({ type: 'command', command: 'teamServer', id: team.server }, event.target);
  });
  document.getElementById('team-refresh').addEventListener('click', function (event) {
    send({ type: 'command', command: 'teamRefresh', id: team.server }, event.target);
  });
`;
