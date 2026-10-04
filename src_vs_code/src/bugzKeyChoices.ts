import { canonicalTeamServerUrl } from './teamServers';

/**
 * What the Bugz key flows say and offer — decided here, as values, so every branch is a unit test; `bugzKeyFlows.ts`
 * only shows them (research/PLAN_bugz_keys_per_server.md, its code round, coai session d5cdb1b2).
 */

/** The command that settles keys saved before they were filed per server — the way back to them, not only a toast. */
export const SETTLE_COMMAND = 'coai.settleOldBugzKeys';

/** Said wherever a key is typed while this side names no Bugz server. Asked BEFORE the box opens, never after a paste. */
export const NO_SERVER = 'Set the Bugz server for this side first: a Bugz key is filed under the server that issued it, so '
  + 'there is nowhere to keep one yet.';

/** The toast's one button. Non-destructive: it opens the choice, it does not make it. */
export const SETTLE = 'Settle them…';
export const ADOPT_PREFIX = 'Adopt them for ';
export const DISCARD = 'Discard them';

/** The toast that says old keys are waiting. Shown once per window while they are held. */
export const OLD_KEYS_WAITING = 'Bugz keys saved before keys were filed per server are kept aside and sent nowhere. '
  + 'Run "ConnectOtherAIs: Settle old Bugz keys" to adopt them for the server that issued them, or to discard them.';

/** The choice itself: a modal naming the server and what each answer does. */
export interface SettleChoice {
  readonly title: string;
  readonly detail: string;
  readonly actions: readonly string[];
}

/**
 * The settle choice for this side's server. Adopting is offered only when the side names a server the keys can be
 * filed under, and the server is spelt as it will be filed; discarding is always possible, and says what it costs.
 */
export function settleChoice(server: string, filable: boolean): SettleChoice {
  const where = canonicalTeamServerUrl(server);
  const adopt = filable ? [`${ADOPT_PREFIX}${where}`] : [];

  return {
    title: 'Bugz keys saved before keys were filed per server',
    detail: [
      filable
        ? `Adopt them only if ${where} issued them: they will be sent to it from now on.`
        : 'This side names no Bugz server, so they cannot be adopted here; set the server first to adopt them.',
      'If your sides ever named different Bugz servers, discard them instead, then revoke and re-issue the admin key '
        + 'and ask for a new contributor key: a key that reached another server may have been kept by it.',
      'Discarding deletes them from this machine and does NOT revoke them: a contributor key stays live on its server '
        + 'until it is revoked there.',
    ].join('\n\n'),
    actions: [...adopt, DISCARD],
  };
}

/** What a settle did, as the person is told it. */
export type Settled = 'adopted' | 'none' | 'no-server' | 'not-kept' | 'discarded';

export function settledSentence(outcome: Settled, server: string): { readonly as: 'information' | 'warning'; readonly title: string } {
  const where = canonicalTeamServerUrl(server);
  const sentences: Readonly<Record<Settled, { readonly as: 'information' | 'warning'; readonly title: string }>> = {
    adopted: { as: 'information', title: `The old Bugz keys are filed under ${where}; a key already filed there was kept.` },
    discarded: { as: 'information', title: 'The old Bugz keys were deleted from this machine. Revoke them on their server if they may still be live.' },
    none: { as: 'information', title: 'There are no old Bugz keys left to settle — another window may have settled them.' },
    'no-server': { as: 'warning', title: NO_SERVER },
    'not-kept': { as: 'warning', title: `The old Bugz keys could not be filed under ${where} (the secret store did not hold them), so they were kept where they were. Try again from the command.` },
  };

  return sentences[outcome];
}

/**
 * Why a held key cannot be revoked from here, or `''` when it can. A record that does not say which server issued the
 * key is refused and kept: revoking against this side's server instead would read its 404 as proof the key was gone.
 */
export function revokeRefusal(issuer: string): string {
  return issuer.trim().length > 0
    ? ''
    : 'This key\'s record does not say which server issued it, so it cannot be revoked from here and is kept. '
      + 'Revoke it on the server that issued it, then copy it to forget it.';
}
