import { pollWhileOpen, REAL_TICKER, Ticker } from './teamUsagePoll';
import { NO_SELECTION, opened, TeamSelection, usageWants, withPage, withServer, withShown, withWindow } from './teamTabSelection';
import { USAGE_FRESH_MS, UsageWant } from './teamUsageCache';

/**
 * The Review rounds page as the Team-server cache sees it — open or closed, in front or behind, showing the Team
 * server tab or not — and the clock it runs while open (todo/PLAN_team_usage_by_person.md, story 1.1).
 *
 * <p>Its own unit, free of `vscode`, because the defect it fixes lived in the wiring: only the page's own
 * `teamTab` message ever changed what the host asked for, a closing page sends no message, and a fresh page starts on
 * Rounds without saying so — so with the page closed the host went on asking every server for its company figures,
 * through every sidebar refresh. Opening, closing and coming to the front or going behind are now facts this model is
 * told by the panel (`RoundsLogPanel.whileOpen` and its view-state listener), and `teamTabHost.test.ts` drives them.</p>
 */
export interface TeamTabHostDeps {
  /** Ask whatever is due — the panel's `refreshTeamUsage`. */
  readonly refresh: () => Promise<void>;
  readonly ticker?: Ticker;
  readonly everyMs?: number;
}

export class TeamTabHost {
  private state: TeamSelection = NO_SELECTION;

  private stopClock: (() => void) | undefined;

  constructor(private readonly deps: TeamTabHostDeps) {}

  get selection(): TeamSelection {
    return this.state;
  }

  /** The page opened: its clock starts. Returns what closes it. */
  opened(): () => void {
    if (this.stopClock === undefined) {
      this.state = opened(this.state);
      this.stopClock = pollWhileOpen(() => this.deps.refresh(), this.deps.everyMs ?? USAGE_FRESH_MS, this.deps.ticker ?? REAL_TICKER);
    }

    return () => {
      this.state = withPage(this.state, 'closed');
      this.stopClock?.();
      this.stopClock = undefined;
    };
  }

  /** The page came to the front, or went behind another editor tab. A closed page stays closed. */
  visible(on: boolean): void {
    if (this.state.page !== 'closed') {
      this.state = withPage(this.state, on ? 'front' : 'behind');
    }
  }

  tabShown(shown: boolean): void {
    this.state = withShown(this.state, shown);
  }

  chooseWindow(id: string): void {
    this.state = withWindow(this.state, id);
  }

  chooseServer(id: string): void {
    this.state = withServer(this.state, id);
  }

  /** What one server's figures are wanted for, now. */
  wants(serverId: string, meWindow: string, adminIds: readonly string[]): readonly UsageWant[] {
    return usageWants(this.state, serverId, meWindow, adminIds);
  }
}
