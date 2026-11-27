import { framed } from './remoteFraming';
import { USER_CLASS, uidFits } from './remotePlaces';

/**
 * The users script of a WSL distro and the reader of its reply (E3.S1, spec § W6). Pure — no process, no file, no `vscode`.
 *
 * <p>The script runs as `root`, by name, in a RUNNING distro and prints which Linux users may be read there: the entries of `/etc/passwd`
 * that pass every rule of the spec. It opens `/etc/passwd` and no other file and starts no program: every word of it is a shell builtin,
 * so the UID-0 check of the accounts script does not apply to it. The reply is believed whole or not at all (J5): one line outside the
 * rules makes the whole reply `unreadable`, and then that distro has no place in the round.</p>
 */

/**
 * The text that is sent to a distro, byte for byte the first `sh` block after the heading `## W6` of the S1 spec: 39 lines, 1106 bytes, ASCII only,
 * each line ends with LF. It holds `${` twice and six backslashes, so each is written escaped here. `remoteUsersScript.test.ts` holds the hash, the spec's
 * pin line and the block to one another (E3S1-T10); a change of the text is a change of the spec.
 */
export const USERS_SCRIPT = `coai_users() {
  unset IFS
  set -f
  LC_ALL=C
  printf 'COAI-USERS 1\\n' || return 0
  [ "$#" -eq 0 ] || { printf 'COAI-BAD operands\\n'; return 0; }
  [ -r /etc/passwd ] || { printf 'COAI-BAD passwd\\n'; return 0; }
  n=0
  while IFS=: read -r name pw uid gid gecos home shell || [ -n "$name" ]; do
    case $name in
      ''|*[!a-z0-9_-]*|[!a-z_]*) continue ;;
    esac
    [ "\${#name}" -le 32 ] || continue
    case $uid in
      ''|*[!0-9]*|0?*) continue ;;
    esac
    [ "\${#uid}" -le 5 ] || continue
    if [ "$name" = root ]; then
      [ "$uid" -eq 0 ] || continue
    else
      [ "$uid" -ge 1000 ] || continue
      [ "$uid" -le 59999 ] || continue
    fi
    case $home in
      /*) ;;
      *) continue ;;
    esac
    [ -d "$home" ] || continue
    case $shell in
      nologin|false|*/nologin|*/false) continue ;;
    esac
    n=$((n + 1))
    [ "$n" -le 16 ] || continue
    printf 'COAI-USER %s %s\\n' "$name" "$uid" || return 0
  done </etc/passwd
  [ "$n" -le 16 ] || printf 'COAI-MORE %s\\n' "$((n - 16))"
  return 0
}
{ </dev/null 2>/dev/null coai_users "$@"; printf 'COAI-USERS-END\\n'; }
`;

/** A Linux user the reply names: the name by its class and the UID by its range. */
export interface WslUser {
  readonly name: string;
  readonly uid: number;
}

/** The words of a reply that is not believed. A closed list: no value of the reply is in any of them. */
export type UsersUnreadable = 'exit' | 'no-sentinel' | 'version' | 'cut' | 'shape' | 'operands' | 'passwd' | 'rule' | 'cap' | 'duplicate';

export type UsersRead =
  | { readonly kind: 'users'; readonly users: readonly WslUser[]; readonly more: number }
  | { readonly kind: 'unreadable'; readonly why: UsersUnreadable };

export const USERS_UNREADABLE_SAID: Readonly<Record<UsersUnreadable, string>> = {
  exit: 'the call ended with an error',
  'no-sentinel': 'no answer of the users script was found',
  version: 'the answer is of another version',
  cut: 'the answer was cut',
  shape: 'the answer has a line this build does not understand',
  operands: 'the script refused its operands',
  passwd: 'the script could not read the list of users',
  rule: 'a user in the answer broke a rule',
  cap: 'the answer names more users than the limit',
  duplicate: 'a user is named twice',
};

/** The caps of the reply: users, lines and bytes of a line. */
export const USERS_MAX = 16;
const LINES_MAX = 18;
const LINE_BYTES_MAX = 64;
const SENTINELS = { begin: 'COAI-USERS 1', family: 'COAI-USERS ', end: 'COAI-USERS-END' } as const;
const USER_LINE = /^COAI-USER ([^ ]+) (0|[1-9][0-9]{0,4})$/u;
const MORE_LINE = /^COAI-MORE ([1-9][0-9]{0,4})$/u;
/** `COAI-BAD operands` or `COAI-BAD passwd`, alone in the frame. A map, so no word of a reply is a key of an object's prototype. */
const BAD: ReadonlyMap<string, UsersUnreadable> = new Map([['COAI-BAD operands', 'operands'], ['COAI-BAD passwd', 'passwd']]);

/** The rule of the script, checked again here: the name's class, and `root` only with UID 0 and UID 0 only for `root`, any other from 1000 to 59 999. */
export const userRuleOk = (name: string, uid: number): boolean => USER_CLASS.test(name) && Number.isInteger(uid) && uidFits(name, uid);

/** One line of the frame, by its form. `why` says the line is outside the rules. */
type Line = { readonly user: WslUser } | { readonly more: number } | { readonly why: UsersUnreadable };

const userLine = (name: string, uid: string): Line => (userRuleOk(name, Number(uid)) ? { user: { name, uid: Number(uid) } } : { why: 'rule' });

function lineOf(line: string): Line {
  const user = USER_LINE.exec(line);
  const more = MORE_LINE.exec(line);
  if (user !== null) {
    return userLine(user[1] as string, user[2] as string);
  }

  return more === null ? { why: 'shape' } : { more: Number(more[1]) };
}

const usersIn = (lines: readonly Line[]): readonly WslUser[] => lines.flatMap((one) => ('user' in one ? [one.user] : []));
const moreIn = (lines: readonly Line[]): number => lines.reduce((sum, one) => sum + ('more' in one ? one.more : 0), 0);
const isMore = (one: Line): boolean => 'more' in one;

type Rule = (lines: readonly Line[], raw: readonly string[]) => UsersUnreadable | undefined;

/** The rules of the whole reply, in order: the first that fails names the word. */
const RULES: readonly Rule[] = [
  (_lines, raw) => (raw.length > LINES_MAX || raw.some((one) => Buffer.byteLength(one) > LINE_BYTES_MAX) ? 'shape' : undefined),
  (lines) => (lines.map((one) => ('why' in one ? one.why : undefined)).find((why) => why !== undefined)),
  (lines) => (lines.slice(0, -1).some(isMore) ? 'shape' : undefined),
  (lines) => (new Set(usersIn(lines).map((one) => one.name)).size < usersIn(lines).length ? 'duplicate' : undefined),
  (lines) => (usersIn(lines).length > USERS_MAX || (moreIn(lines) > 0 && usersIn(lines).length !== USERS_MAX) ? 'cap' : undefined),
];

function usersFrom(raw: readonly string[]): UsersRead {
  const lines = raw.map(lineOf);
  const why = RULES.map((rule) => rule(lines, raw)).find((word) => word !== undefined);

  return why === undefined ? { kind: 'users', users: usersIn(lines), more: moreIn(lines) } : { kind: 'unreadable', why };
}

const unreadable = (why: UsersUnreadable): UsersRead => ({ kind: 'unreadable', why });

const missOf = (found: { readonly kind: 'no-begin'; readonly otherVersion: boolean } | { readonly kind: 'no-end' }): UsersUnreadable =>
  (found.kind === 'no-end' ? 'cut' : found.otherVersion ? 'version' : 'no-sentinel');

function fromFrame(lines: readonly string[]): UsersRead {
  const bad = lines.length === 1 ? BAD.get(lines[0] as string) : undefined;

  return bad === undefined ? usersFrom(lines) : unreadable(bad);
}

/**
 * What a distro's users script answered, as one value: the frame `COAI-USERS 1` … `COAI-USERS-END` (through `framed`), at most 18 lines of 64 bytes, each
 * a user, or — only as the last — a `COAI-MORE <n>`, or alone a `COAI-BAD` word. One line outside the rules makes the whole reply unreadable. Never throws.
 * The home and the shell are the distro's to test.
 */
export function usersOf(run: { readonly code: number; readonly stdout: string }): UsersRead {
  if (run.code !== 0) {
    return unreadable('exit');
  }
  const found = framed(run.stdout, SENTINELS);

  return found.kind === 'framed' ? fromFrame(found.lines) : unreadable(missOf(found));
}
