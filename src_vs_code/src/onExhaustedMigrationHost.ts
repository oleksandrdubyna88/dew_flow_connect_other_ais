import { randomBytes } from 'node:crypto';
import * as vscode from 'vscode';
import { inCatalogTurn } from './catalogMigrationHost';
import { thisSide } from './installer';
import { notify } from './notify';
import { migrateOnExhausted, MigrationPorts, userLayerOf } from './onExhaustedMigration';
import { inForceOf, NOT_RESET } from './onExhaustedTold';
import { readOverlay } from './sideSettings';

/**
 * The host half of the one-time reset of *When the rounds run out* to Auto (E2.S2,
 * research/PLAN_auto_never_asks_a_person_E2_S2_spec.md § S2.5). It holds no decision: it builds the ports of
 * `migrateOnExhausted` — this profile's `globalState`, the person's own settings layer, the catalog's queue, the two
 * notifications — and awaits it. The run starts from `activate`, never from the panel.
 */

/** What the tell reads of the window's ONE `FeaturesCache` (extension.ts): its last settled list, and its question. */
export interface FeaturesPort {
  settled(): readonly string[] | undefined;
  ask(): Promise<unknown>;
}

/** Resolves when `answer` does — or rejects, which is the same here — or after `capMs`; the timer is cleared either way. */
function settledWithin(answer: Promise<unknown>, capMs: number): Promise<void> {
  return new Promise((done) => {
    const timer = setTimeout(done, capMs);
    const finish = (): void => {
      clearTimeout(timer);
      done();
    };
    answer.then(finish, finish);
  });
}

/** The ports over the real editor. `inForce` makes the three reads `readerFor` makes, asked at the moment it tells. */
function portsOf(context: vscode.ExtensionContext, version: string, after: () => void, features: FeaturesPort): MigrationPorts {
  return {
    store: context.globalState,
    user: userLayerOf(() => vscode.workspace.getConfiguration('coai'), vscode.ConfigurationTarget.Global),
    version,
    window: randomBytes(6).toString('hex'),
    now: Date.now,
    pause: (ms) => new Promise((done) => {
      setTimeout(done, ms);
    }),
    turn: inCatalogTurn,
    after,
    inForce: () => {
      const config = vscode.workspace.getConfiguration('coai');

      return inForceOf(
        config.inspect('onExhausted'),
        config.get('perSideSettings') === true,
        readOverlay(context.globalState, thisSide(context.globalStorageUri)),
      );
    },
    features: () => features.settled(),
    settleFeatures: (capMs) => settledWithin(features.ask(), capMs),
    tell: (sentence) => notify({ as: 'information', class: 'outcome', source: 'onExhaustedMigrationHost', code: 'on-exhausted-migrated', title: sentence }),
    refused: (why) => notify({ as: 'warning', class: 'failure', source: 'onExhaustedMigrationHost', code: 'on-exhausted-not-reset', title: NOT_RESET, detail: why }),
    log: (what, error) => {
      console.error(`ConnectOtherAIs: ${what}`, ...(error === undefined ? [] : [error]));
    },
  };
}

/**
 * Called once from `activate`. Never rejects: the run itself never does, and an outcome other than the ones a person is
 * told about goes to the extension host's console, as activation logs a refused side stamp.
 */
export async function startOnExhaustedMigration(
  context: vscode.ExtensionContext,
  version: string,
  after: () => void,
  features: FeaturesPort,
): Promise<void> {
  const outcome = await migrateOnExhausted(portsOf(context, version, after, features));
  if (outcome === 'incomplete' || outcome === 'not-ready') {
    console.error(`ConnectOtherAIs: the reset of "When the rounds run out" to Auto ended ${outcome}; it is tried again at the next start`);
  }
}
