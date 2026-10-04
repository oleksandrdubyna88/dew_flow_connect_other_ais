import * as vscode from 'vscode';
import { adminKey, adoptLegacyKeys, discardLegacyKeys, hasServer, legacyKeysHeld, Secrets, setContributorKey } from './bugsAdminKey';
import { ADOPT_PREFIX, DISCARD, NO_SERVER, OLD_KEYS_WAITING, SETTLE, settleChoice, Settled, settledSentence } from './bugzKeyChoices';
import { notify, notifyAndAsk, notifyThen } from './notify';
import { askPerson } from './personWait';

/**
 * The Bugz key flows that are not the keys page itself — the contributor key, and the keys saved before they were
 * filed per server (research/PLAN_bugz_keys_per_server.md). Only VS Code here; what is said and offered is decided in
 * `bugzKeyChoices.ts`, as values.
 */

/** Said wherever a key would be typed while this side names no Bugz server — before the box opens, never after. */
export async function refuseNoServer(): Promise<void> {
  await notify({ as: 'warning', class: 'refusal', source: 'bugsKeys', code: 'bugz-key-needs-a-server', title: NO_SERVER });
}

/**
 * The contributor key asked for and filed under this side's Bugz server — ONE flow for the sidebar button and the
 * command, which were two copies of it. With no server the box never opens.
 */
export async function askForContributorKey(secrets: Secrets, server: string): Promise<void> {
  if (!hasServer(server)) {
    await refuseNoServer();

    return;
  }
  const typed = await askPerson(() => vscode.window.showInputBox({
    title: 'The contributor key for the ingest server',
    prompt: 'Kept in the editor\'s secret storage on this machine only — never in settings, which sync.',
    password: true,
    ignoreFocusOut: true,
  }));
  if (typed !== undefined) {
    await setContributorKey(secrets, server, typed);
  }
}

/** The keys page's line about old keys: said when this server has no key yet and old ones are waiting. */
export async function oldKeysNote(secrets: Secrets, server: string): Promise<string> {
  const none = (await adminKey(secrets, server)).length === 0;

  return none && (await legacyKeysHeld(secrets)) ? OLD_KEYS_WAITING : '';
}

/**
 * Once per window while old keys are held: a toast whose one button OPENS the choice — nothing destructive is a click
 * away from a notification. Every detached edge is caught and logged.
 */
export async function offerOldBugzKeys(secrets: Secrets, serverOf: () => string): Promise<void> {
  if (!(await legacyKeysHeld(secrets))) {
    return;
  }
  void notifyThen(
    { as: 'warning', class: 'offer', source: 'bugsKeys', code: 'bugz-legacy-keys', title: OLD_KEYS_WAITING, action: SETTLE },
    (choice) => {
      if (choice === SETTLE) {
        settleOldBugzKeys(secrets, serverOf).catch(logged);
      }
    },
  ).catch(logged);
}

/**
 * *ConnectOtherAIs: Settle old Bugz keys* — a modal naming the server the keys would be filed under and what each
 * answer costs; the server is read when the choice is BUILT, and that same value is what an Adopt files them under,
 * because it is the one the person vouched for. Every outcome is said.
 */
export async function settleOldBugzKeys(secrets: Secrets, serverOf: () => string): Promise<void> {
  const server = serverOf();
  if (!(await legacyKeysHeld(secrets))) {
    await told('none', server);

    return;
  }
  const choice = settleChoice(server, hasServer(server));
  const answer = await notifyAndAsk({
    as: 'warning', class: 'confirmation', source: 'bugsKeys', code: 'bugz-settle-old-keys', modal: true,
    title: choice.title, detail: choice.detail, actions: choice.actions,
  });
  const outcome = await settled(secrets, server, answer);
  if (outcome !== undefined) {
    await told(outcome, server);
  }
}

async function settled(secrets: Secrets, server: string, answer: string | undefined): Promise<Settled | undefined> {
  if (answer === DISCARD) {
    await discardLegacyKeys(secrets);

    return 'discarded';
  }

  return answer?.startsWith(ADOPT_PREFIX) === true ? adoptLegacyKeys(secrets, server) : undefined;
}

async function told(outcome: Settled, server: string): Promise<void> {
  const sentence = settledSentence(outcome, server);
  await notify({ as: sentence.as, class: 'outcome', source: 'bugsKeys', code: 'bugz-old-keys-settled', title: sentence.title });
}

function logged(error: unknown): void {
  console.error('ConnectOtherAIs: the old Bugz keys could not be settled', error);
}
