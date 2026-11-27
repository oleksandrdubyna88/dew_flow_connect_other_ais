/**
 * The accounts script and everything that is fixed about it (E1.S2, spec § A2.2 and § A2.3; revision 2 is E3.S2, spec § R2.2 to § R2.5; revision 3 is the
 * owner's of 2026-11-27, spec § R2.3, revision 3): the text that is sent to a VM, its operands (the usage mode takes the account's digest; the first mode may take the word
 * that says agy's quota command is not run), the limits that are written into it, and the caps of the reply it prints. Pure — no process, no file, no `vscode`.
 *
 * <p><b>What the text does.</b> It reads which AI command-line programs a VM has (Claude Code, Codex, agy), who is signed in to
 * them, and — only when asked — what Claude Code's own usage command prints. It writes nothing and starts no package manager. It starts
 * `codex app-server` (account and rate limits, no model call) and `agy -p /quota` (the quota, no model call): each calls its own vendor's server
 * with its own sign-in. It opens one file of its own reading, the last megabyte of agy's `cli.log`, for the one line that names agy's account.
 * `~/.codex/auth.json` and agy's sign-in file are only tested for existence; no token file is ever opened. Everything it prints leaves
 * through a `jq` program that names its fields or a `grep` pattern that anchors its lines; the parser (`remoteAccountsReply.ts`) then decides
 * what each value means.</p>
 *
 * <p><b>What we do about Claude Code's own programs, and what we do not claim.</b> We PASS `--no-session-persistence`,
 * `--strict-mcp-config` and `--setting-sources ""` to the usage command. A flag is a request until its effect has been seen, so
 * nothing here says that Claude Code starts nothing or that no hook runs: that is measured on a real host (parent § 5, W7).
 * The usage command runs only for the operand `claude-usage` with the digest of the account the first reply named (§ R2.2).</p>
 *
 * <p><b>The text is one function read to its end and then called.</b> The last line is ONE brace group — the call with its
 * redirections first, then the end line — so a script that is cut on its way starts no program, opens no path for writing and
 * prints no line (spec § A2.1, decision 4). The text is pinned: `remoteAccountsScript.test.ts` holds its sha256, the spec's
 * block and the spec's pin line to one another.</p>
 */

import { createHash } from 'node:crypto';
import { framed } from './remoteFraming';

/** The protocol number of the begin line `COAI-ACCOUNTS <n>`. Not the session script's: one script's answer is never read by the other parser. */
export const ACCOUNTS_PROTOCOL = 1;

/** The two modes. `claude-usage` is only ever a place's SECOND call in a round and takes a digest (§ R2.2); every place's first call is `no-claude-usage`. */
export type AccountsMode = 'claude-usage' | 'no-claude-usage';
export const ACCOUNTS_MODES: readonly AccountsMode[] = ['claude-usage', 'no-claude-usage'];

/** The optional second operand of the first mode: the script does not run agy's quota command, because an earlier reply showed that it asks the model (revision 3). */
export const ACCOUNTS_SKIP_AGY = 'no-agy-quota';

/**
 * Whether `operand` is the one operand the mode takes: the usage mode a digest of its class, the first mode nothing or the word that skips agy's quota.
 * The ONE test of it; `remoteSsh.ts` and `remoteWsl.ts` both ask it.
 */
export const operandFits = (mode: AccountsMode, operand: string | undefined): boolean =>
  (mode === 'claude-usage' ? operand !== undefined && isAccountsDigest(operand) : operand === undefined || operand === ACCOUNTS_SKIP_AGY);

/** Membership in `ACCOUNTS_MODES` and nothing else: no value from a terminal's name or from a VM is ever an operand. */
export function isAccountsMode(word: string): word is AccountsMode {
  return ACCOUNTS_MODES.some((mode) => mode === word);
}

/** The digest operand of `claude-usage`: 64 characters of `0-9a-f` (§ R2.2), the sha256 of the account lines as the first reply printed them. */
export const ACCOUNTS_DIGEST = /^[0-9a-f]{64}$/u;

/** Membership in `ACCOUNTS_DIGEST` and nothing else: no email, no host and no name is ever an operand. */
export const isAccountsDigest = (word: string): boolean => ACCOUNTS_DIGEST.test(word);

/**
 * The digest of a FIRST reply (§ R2.2, D5): over the framed lines as received (before `plainValue`), the line that is exactly
 * `COAI-VENDOR claude found` and the lines directly after it that start with `:email `, `:org ` or `:plan `, each with one LF, as UTF-8; the
 * sha256 in lower-case hex. `undefined` when the frame or that marker is not there. It is what the script holds in one variable before it prints,
 * so both sides hash the same bytes. Never throws.
 */
export function accountsDigest(stdout: string): string | undefined {
  const found = framed(stdout, { begin: `COAI-ACCOUNTS ${ACCOUNTS_PROTOCOL}`, family: 'COAI-ACCOUNTS ', end: 'COAI-ACCOUNTS-END' });
  if (found.kind !== 'framed') {
    return undefined;
  }
  const at = found.lines.indexOf('COAI-VENDOR claude found');
  if (at < 0) {
    return undefined;
  }
  const rest = found.lines.slice(at + 1);
  const keys = rest.findIndex((line) => !/^:(?:email|org|plan) /u.test(line));
  const kept = ['COAI-VENDOR claude found', ...(keys < 0 ? rest : rest.slice(0, keys))];

  return createHash('sha256').update(`${kept.join('\n')}\n`, 'utf8').digest('hex');
}

/**
 * The vendor commands' limits in seconds: a TERM after this long (`timeout -k 1 <n>`; a KILL one second later). `agy` is its version command, `agyQuota` its quota
 * command; `codex` is `codex app-server`, which is given three seconds of input first (the `sleep 3` inside its own limit). `shebang` is the read of the first line of a vendor program, as UID 0 only.
 */
export const ACCOUNTS_SECONDS = { account: 3, version: 2, usage: 8, agy: 2, codex: 4, agyQuota: 4, shebang: 1 } as const;

/** The limits of the readers at the end of each pipe; the sum of them and ssh's connect stays under the ceiling. `agyLog` is each stage of the one-line read of agy's log. */
export const ACCOUNTS_READ_SECONDS = { account: 4, version: 2, usage: 9, agy: 2, digest: 1, codex: 5, agyQuota: 5, agyLog: 1, shebang: 1 } as const;

/** The KILL second after a TERM in a step that is NOT inside its reader's limit: the two version commands (§ R2.5). */
export const ACCOUNTS_KILL_SECONDS = { version: 1, agy: 1 } as const;

/** ssh's `ConnectTimeout`: a call to a host spends up to this before the script starts. A WSL call has none. */
export const ACCOUNTS_CONNECT_SECONDS = 5;

/** The runner kills the whole call here, and then every vendor of that host is lost. */
export const ACCOUNTS_CEILING_SECONDS = 30;

/** The account JSON given to `jq`: the last 64 KiB. */
export const ACCOUNT_JSON_MAX = 65536;
/** The usage JSON given to `jq`: the last 1 MiB; a longer output is cut at its start, does not parse, and is `failed error`. */
export const USAGE_JSON_MAX = 1048576;
/** The usage lines sent. */
export const USAGE_LINES_MAX = 8;
/** The Codex JSON given to `jq`: the last 1 MiB of what `codex app-server` printed. */
export const CODEX_JSON_MAX = 1048576;
/** agy's quota JSON given to `jq`: the last 1 MiB. */
export const AGY_QUOTA_JSON_MAX = 1048576;
/** The last bytes of agy's `cli.log` searched for the line that names its account. */
export const AGY_LOG_TAIL_MAX = 1048576;
/** The `:quota` lines of agy's quota that are sent. */
export const AGY_WINDOWS_MAX = 8;

/** The tools every Linux has and the script checks first; a host without one says `COAI-BAD tool` and nothing is read. `sha256sum` is checked in the usage mode only. */
export const ACCOUNTS_TOOLS: readonly string[] = ['id', 'timeout', 'tail', 'sleep', 'sed', 'grep'];

/** The `PATH` the script sets for itself (§ R2.3, D2): fixed text, never the caller's. */
export const ACCOUNTS_PATH = '/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin';

/** The package managers the script can NAME for a host without `jq` (it runs none), in the order it tests them, and `none`. */
export type ManagerWord = 'apt-get' | 'dnf' | 'yum' | 'apk' | 'none';
export const MANAGER_WORDS: readonly ManagerWord[] = ['apt-get', 'dnf', 'yum', 'apk', 'none'];

/** The reply's own caps: 64 lines of 512 bytes between the sentinels. */
export const ACCOUNTS_LINES_MAX = 64;
export const ACCOUNTS_LINE_BYTES_MAX = 512;
/** The largest stdout a legal reply can be: its two sentinel lines and every line end counted. The runner's own cap (S3's `ACCOUNTS_LIMITS`) must not be under it. */
export const ACCOUNTS_REPLY_BYTES_MAX = (ACCOUNTS_LINES_MAX + 2) * (ACCOUNTS_LINE_BYTES_MAX + 1);

/**
 * The worst time of one mode, in seconds (T46, § R2.5): the reader limits of the steps that mode runs, the KILL seconds that are not inside a
 * reader's limit, and ssh's connect time. The KILL seconds of the account, the usage, Codex and agy's quota commands are inside their readers' limits.
 */
export function worstSeconds(mode: AccountsMode, root = false): number {
  const r = ACCOUNTS_READ_SECONDS;
  const claude = r.account + r.version + ACCOUNTS_KILL_SECONDS.version;
  const rest = mode === 'claude-usage'
    ? r.digest + r.usage
    : r.codex + r.agy + ACCOUNTS_KILL_SECONDS.agy + r.agyQuota + r.agyLog;

  return claude + rest + (root ? shebangSeconds(mode) : 0) + ACCOUNTS_CONNECT_SECONDS;
}

/** As UID 0 the first line of each vendor program the mode starts is read once more: three programs in the first mode, Claude Code's alone in the usage mode. */
const shebangSeconds = (mode: AccountsMode): number => ACCOUNTS_READ_SECONDS.shebang * (mode === 'claude-usage' ? 1 : 3);

/**
 * The text that is sent to a VM, byte for byte the first `sh` block after the heading `## R2.3` of the E3.S2 spec (revision 3): 413 lines, ASCII only,
 * each line ends with LF. It holds `${` 15 times and 82 backslashes, so every backslash is written twice here.
 * Hash and spec are held to this constant by `remoteAccountsScript.test.ts` (E3S2-T1); a change of the text is a change of the spec.
 */
export const ACCOUNTS_SCRIPT = `coai_accounts() {
  unset IFS
  set -f
  nl='
'
  mail_re='[A-Za-z0-9._%+-]{1,64}@[A-Za-z0-9.-]{3,190}$'
  dot=$(printf '\\302\\267')
  window='[0-9]{1,3}(\\.[0-9]{1,2})?% used '"$dot"' resets [A-Za-z0-9,: ]{1,40} \\([A-Za-z0-9_/+-]{1,40}\\)$'
  version='1s/^[^0-9]*\\([0-9][0-9.]\\{0,31\\}\\).*$/\\1/p'
  one='def one: tostring | split("\\n") | join(" ") | split("\\r") | join(" ") | .[0:120];'
  jq_auth="$one"'
    def key(k): if type == "string" and . != "" then ":" + k + " " + one else empty end;
    [inputs] as $all
    | ([$all[] | select(type == "object") | select(has("coai_exit")) | .coai_exit] | .[-1]) as $rc
    | ([$all[] | select(type == "object") | select(has("loggedIn"))] | .[0]) as $a
    | if $a == null then
        (if $rc == 124 or $rc == 137 then "COAI-VENDOR claude unreadable timeout" else "COAI-VENDOR claude unreadable auth" end)
      elif $a.loggedIn != true then "COAI-VENDOR claude signed-out"
      else "COAI-VENDOR claude found",
        ($a.email | key("email")), ($a.orgName | key("org")), ($a.subscriptionType | key("plan"))
      end'
  jq_usage='[inputs] as $all
    | ([$all[] | select(type == "object") | select(has("coai_exit")) | .coai_exit] | .[-1]) as $rc
    | ([$all[] | select(type == "object") | select(.type == "result")] | .[0]) as $r
    | if $rc == 124 or $rc == 137 then "=timeout"
      elif $rc != 0 or $r == null then "=error"
      elif $r.is_error != false or ($r.result | type) != "string" or ($r.num_turns | type) != "number" then "=error"
      else ($r.result | split("\\n")) as $l
        | "=read " + ($r.num_turns | floor | tostring) + " " + ([$l[] | select(startswith("Current "))] | length | tostring),
          (if $r.num_turns == 0 then $l[] | "-" + . else empty end)
      end'
  jq_codex="$one"'
    def num: if type == "number" then (((. * 100 + 0.5) | floor) / 100 | tostring) else "-" end;
    def int: if type == "number" then (floor | tostring) else "-" end;
    def win: if type == "object" then ":window " + (.usedPercent | num) + " " + (.windowDurationMins | int) + " " + (.resetsAt | int) else empty end;
    [inputs | fromjson? | select(type == "object")] as $all
    | ([$all[] | select(has("coai_exit")) | .coai_exit] | .[-1]) as $rc
    | ([$all[] | select(.id == 2) | .result.account | select(type == "object")] | .[0]) as $a
    | ([$all[] | select(.id == 3) | .result | select(type == "object")] | .[0]) as $r3
    | ([$r3.rateLimits | select(type == "object")] | .[0]) as $r
    | (if ($a.email | type) == "string" then ":account " + ($a.email | one) else empty end),
      (($r.planType // $a.planType) | if type == "string" and . != "" then ":plan " + one else empty end),
      (if $r3 == null then (if $rc == 124 or $rc == 137 then "COAI-USAGE codex failed timeout" else "COAI-USAGE codex failed error" end)
        elif $r == null or ([$r.primary, $r.secondary] | map(select(type == "object")) | length) == 0 then "COAI-USAGE codex failed none"
        else "COAI-USAGE codex read", ($r.primary | win), ($r.secondary | win) end)'
  jq_agy='def kind: if type == "string" and startswith("gemini-") then "gemini" elif type == "string" and startswith("3p-") then "third-party" else empty end;
    def span: if . == "5h" or . == "weekly" then . else empty end;
    def left: if type == "number" and 0 <= . and . <= 1 then ((. * 100 + 0.5) | floor | tostring) else empty end;
    def at: (if type == "string" and .[10:11] == "T" and (.[19:20] == "Z" or .[19:20] == ".") then (try (.[0:19] + "Z" | fromdateiso8601) catch 0) else 0 end) | if 0 < . then tostring else "-" end;
    [inputs] as $all
    | ([$all[] | select(type == "object") | select(has("coai_exit")) | .coai_exit] | .[-1]) as $rc
    | ([$all[] | select(type == "object") | select(has("num_turns"))] | .[0]) as $r
    | if $rc == 124 or $rc == 137 then "=timeout"
      elif $rc != 0 or $r == null then "=error"
      elif $r.num_turns != 0 or (try ($r.usage.total_tokens // 0) catch 1) != 0 or $r.conversation_id != "" or ($r.command | type) != "object" or $r.command.name != "usage" then "=model-call"
      else "=read",
        ($r.command.data.groups[]? | select(type == "object") | .buckets[]? | select(type == "object")
          | (.id | kind) as $g | (.window | span) as $w | (.remaining_fraction | left) as $l
          | ":quota " + $g + " " + $w + " " + $l + " " + (.reset_time | at))
      end'

  coai_manager() {
    if [ -x /usr/bin/apt-get ]; then
      printf 'apt-get'
    elif [ -x /usr/bin/dnf ]; then
      printf 'dnf'
    elif [ -x /usr/bin/yum ]; then
      printf 'yum'
    elif [ -x /sbin/apk ]; then
      printf 'apk'
    else
      printf 'none'
    fi
  }

  coai_version() {
    case $1 in
      ''|*[!0-9.]*) ;;
      *) printf ':version %s\\n' "$1" ;;
    esac
  }

  coai_sign_in() {
    if [ -e "$1" ]; then
      printf ':sign-in-file present\\n'
    else
      printf ':sign-in-file absent\\n'
    fi
  }

  coai_closed() {
    left=$1
    hops=0
    own=$("$st" -c '%u %a' -- /) || { unsafe=/; return 1; }
    case $own in
      '0 '*[2367]|'0 '*[2367]?) unsafe=/; return 1 ;;
      '0 '*) ;;
      *) unsafe=/; return 1 ;;
    esac
    while :; do
      q=
      rest=$left
      again=no
      while [ -n "$rest" ]; do
        rest=\${rest#/}
        part=\${rest%%/*}
        case $rest in
          */*) rest=/\${rest#*/} ;;
          *) rest= ;;
        esac
        [ -n "$part" ] || continue
        q=$q/$part
        own=$("$st" -c '%u %a' -- "$q") || { unsafe=$q; return 1; }
        case $own in
          '0 '*) ;;
          *) unsafe=$q; return 1 ;;
        esac
        if [ -L "$q" ]; then
          hops=$((hops + 1))
          [ "$hops" -le 16 ] || { unsafe=$q; return 1; }
          to=$("$rl" -- "$q") || { unsafe=$q; return 1; }
          case $to in
            /*) left=$to$rest ;;
            *) left=\${q%/*}/$to$rest ;;
          esac
          again=yes
          break
        fi
        case $own in
          *[2367]|*[2367]?) unsafe=$q; return 1 ;;
        esac
      done
      [ "$again" = yes ] || return 0
    done
  }

  coai_may() {
    found=$(command -v "$1") || return 1
    [ "$uid" = 0 ] || return 0
    case $found in
      /*) coai_closed "$found" || return 2 ;;
      *) unsafe=$found; return 2 ;;
    esac
    return 0
  }

  coai_clean() {
    case $unsafe in
      ''|*[!A-Za-z0-9._/@+-]*) unsafe=- ;;
    esac
    [ "\${#unsafe}" -le 200 ] || unsafe=-
  }

  coai_path() {
    coai_clean
    printf ':path %s\\n' "$unsafe"
  }

  coai_warn() {
    if [ -n "$vwarn" ]; then
      unsafe=$vwarn
      coai_clean
      case $unsafe in
        /*|-) ;;
        *) unsafe=- ;;
      esac
      printf ':unsafe-path %s\\n' "$unsafe"
    fi
  }

  coai_script() {
    [ "$uid" = 0 ] || return 0
    found=$(command -v "$1") || return 0
    [ -f "$found" ] || return 0
    shebang=$(LC_ALL=C timeout -k 1 1 sed -n -e '1{/^#!/p;q;}' "$found")
    case $shebang in
      '#!'*) ;;
      *) return 0 ;;
    esac
    set -- \${shebang#??}
    interp=\${1-}
    case $interp in
      */env)
        shift
        while [ "$#" -gt 0 ]; do
          case $1 in
            -*|*=*) shift ;;
            *) break ;;
          esac
        done
        interp=\${1-}
        ;;
    esac
    case $interp in
      '') return 0 ;;
      /*) ipath=$interp ;;
      *) ipath=$(command -v "$interp") || return 0 ;;
    esac
    if ! coai_closed "$ipath"; then
      [ -n "$vwarn" ] || vwarn=$unsafe
    fi
    return 0
  }

  coai_vendor() {
    vwarn=
    coai_may "$1"
    case $? in
      0) ;;
      1) [ "$#" -gt 1 ] || printf 'COAI-VENDOR %s absent\\n' "$1"; return 1 ;;
      *) vwarn=$unsafe ;;
    esac
    coai_script "$1"
    return 0
  }

  coai_claude() {
    coai_vendor claude || return 0
    [ "$jq" = found ] || { printf 'COAI-VENDOR claude unreadable no-jq\\n'; return 0; }
    auth=$(
      {
        timeout -k 1 3 claude auth status --json
        printf '\\n{"coai_exit":%s}\\n' "$?"
      } | timeout -k 1 4 tail -c 65536 | timeout -k 1 4 jq -n -r "$jq_auth"
    )
    case $auth in
      'COAI-VENDOR claude found'|'COAI-VENDOR claude found'"$nl":*) ;;
      'COAI-VENDOR claude signed-out'|'COAI-VENDOR claude unreadable timeout') printf '%s\\n' "$auth"; return 0 ;;
      *) printf 'COAI-VENDOR claude unreadable auth\\n'; return 0 ;;
    esac
    printf '%s\\n' "$auth" || return 0
    coai_version "$(timeout -k 1 2 claude --version | LC_ALL=C timeout -k 1 2 sed -n -e "$version" -e 1q)"
    coai_warn
    if [ "$mode" != claude-usage ]; then
      printf 'COAI-USAGE claude skipped\\n'
      return 0
    fi
    sum=$(printf '%s\\n' "$auth" | timeout -k 1 1 sha256sum)
    if [ "\${sum%% *}" != "$want" ]; then
      printf 'COAI-USAGE claude skipped account-changed\\n'
      return 0
    fi
    kept=$(
      {
        TZ=UTC timeout -k 1 8 claude -p "/usage" --output-format json --no-session-persistence --strict-mcp-config --setting-sources ""
        printf '\\n{"coai_exit":%s}\\n' "$?"
      } | timeout -k 1 9 tail -c 1048576 | timeout -k 1 9 jq -n -r "$jq_usage" |
        LC_ALL=C grep -E -e '^=timeout$' -e '^=read [0-9]{1,6} [0-9]{1,6}$' -e "^-Current session: $window" -e "^-Current week \\\\([A-Za-z0-9][A-Za-z0-9 ._-]{0,39}\\\\): $window"
    )
    head=\${kept%%"$nl"*}
    case $head in
      '=timeout') printf 'COAI-USAGE claude failed timeout\\n'; return 0 ;;
      '=read '*) ;;
      *) printf 'COAI-USAGE claude failed error\\n'; return 0 ;;
    esac
    set -- $head
    sent=$(printf '%s\\n' "$kept" | LC_ALL=C grep -c '^-')
    [ "$sent" -le 8 ] || sent=8
    printf 'COAI-USAGE claude read\\n' || return 0
    printf ':turns %s\\n' "$2"
    printf '%s\\n' "$kept" | LC_ALL=C sed -n -e 's/^-/:line /p' | LC_ALL=C sed -n -e '1,8p'
    printf ':unmatched %s\\n' "$(($3 - sent))"
    return 0
  }

  coai_codex() {
    coai_vendor codex quiet || {
      if [ -d "$HOME/.codex" ]; then
        [ "$jq" = found ] || { printf 'COAI-VENDOR codex unreadable no-jq\\n'; return 0; }
        printf 'COAI-VENDOR codex found\\n' || return 0
        coai_sign_in "$HOME/.codex/auth.json"
        printf 'COAI-USAGE codex failed not-started\\n'
      else
        printf 'COAI-VENDOR codex absent\\n'
      fi
      return 0
    }
    [ "$jq" = found ] || { printf 'COAI-VENDOR codex unreadable no-jq\\n'; return 0; }
    printf 'COAI-VENDOR codex found\\n' || return 0
    coai_sign_in "$HOME/.codex/auth.json"
    coai_warn
    record=$(
      {
        printf '%s\\n' '{"method":"initialize","id":1,"params":{"clientInfo":{"name":"coai","title":"coai","version":"1"}}}' '{"method":"initialized"}' '{"method":"account/read","id":2,"params":{}}' '{"method":"account/rateLimits/read","id":3}'
        sleep 3
      } | {
        timeout -k 1 4 codex app-server
        printf '\\n{"coai_exit":%s}\\n' "$?"
      } | timeout -k 1 5 tail -c 1048576 | timeout -k 1 5 jq -n -R -r "$jq_codex" |
        LC_ALL=C grep -E -e "^:account $mail_re" -e '^:plan ' -e '^COAI-USAGE codex (read|failed none|failed error|failed timeout)$' -e '^:window [-0-9.]{1,8} [-0-9]{1,6} [-0-9]{1,11}$'
    )
    case $record in
      *'COAI-USAGE codex '*) printf '%s\\n' "$record" ;;
      *) printf 'COAI-USAGE codex failed error\\n' ;;
    esac
    return 0
  }

  coai_quota() {
    kept=$(
      {
        timeout -k 1 4 agy -p /quota --output-format json
        printf '\\n{"coai_exit":%s}\\n' "$?"
      } | timeout -k 1 5 tail -c 1048576 | timeout -k 1 5 jq -n -r "$jq_agy" |
        LC_ALL=C grep -E -e '^=(timeout|error|model-call|read)$' -e '^:quota (gemini|third-party) (5h|weekly) [0-9]{1,3} (-|[0-9]{1,11})$'
    )
    case \${kept%%"$nl"*} in
      '=timeout') printf 'COAI-USAGE agy failed timeout\\n' ;;
      '=model-call') printf 'COAI-USAGE agy failed model-call\\n' ;;
      '=read')
        printf 'COAI-USAGE agy read\\n'
        printf '%s\\n' "$kept" | LC_ALL=C sed -n -e '/^:quota /p' | LC_ALL=C sed -n -e '1,8p'
        ;;
      *) printf 'COAI-USAGE agy failed error\\n' ;;
    esac
  }

  coai_agy() {
    coai_vendor agy || return 0
    printf 'COAI-VENDOR agy found\\n' || return 0
    coai_version "$(timeout -k 1 2 agy --version | LC_ALL=C timeout -k 1 2 sed -n -e "$version" -e 1q)"
    coai_sign_in "$HOME/.gemini/antigravity-cli/antigravity-oauth-token"
    coai_warn
    if [ "$jq" != found ]; then
      printf 'COAI-USAGE agy failed no-jq\\n'
      return 0
    fi
    if [ "$askq" = yes ]; then
      quota=$(coai_quota)
    else
      quota='COAI-USAGE agy skipped'
    fi
    logf=$HOME/.gemini/antigravity-cli/cli.log
    mail=
    if [ "$uid" != 0 ] || coai_closed "$logf"; then
      mail=$(
        timeout -k 1 1 tail -c 1048576 "$logf" | LC_ALL=C timeout -k 1 1 sed -n -e 's/^.*applyAuthResult: email=\\([^,]*\\),.*$/\\1/p' |
          timeout -k 1 1 tail -n 1 | LC_ALL=C timeout -k 1 1 grep -E -e "^$mail_re"
      )
    fi
    [ -z "$mail" ] || printf ':account %s\\n' "$mail"
    printf '%s\\n' "$quota"
    return 0
  }

  printf 'COAI-ACCOUNTS 1\\n' || return 0
  askq=yes
  case $#:\${1-}:\${2-} in
    1:no-claude-usage:) want= ;;
    2:no-claude-usage:no-agy-quota) want=; askq=no ;;
    2:claude-usage:*)
      want=$2
      case $want in
        *[!0-9a-f]*) want= ;;
      esac
      [ "\${#want}" -eq 64 ] || { printf 'COAI-BAD operands\\n'; return 0; }
      ;;
    *) printf 'COAI-BAD operands\\n'; return 0 ;;
  esac
  mode=$1
  case \${HOME-} in
    *:*) printf 'COAI-BAD home\\n'; return 0 ;;
    /*) ;;
    *) printf 'COAI-BAD home\\n'; return 0 ;;
  esac
  cd / || { printf 'COAI-BAD folder\\n'; return 0; }
  PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
  export PATH
  command -v id >/dev/null || { printf 'COAI-BAD tool\\n'; return 0; }
  uid=$(id -u)
  case $uid in
    ''|*[!0-9]*) printf 'COAI-BAD tool\\n'; return 0 ;;
  esac
  unsafe=
  home_unsafe=
  if [ "$uid" = 0 ]; then
    st=$(command -v stat) || { printf 'COAI-BAD tool\\n'; return 0; }
    rl=$(command -v readlink) || { printf 'COAI-BAD tool\\n'; return 0; }
    if coai_closed "$HOME/.local/bin"; then
      PATH=$HOME/.local/bin:$PATH
    else
      home_unsafe=$unsafe
    fi
  else
    PATH=$HOME/.local/bin:$PATH
  fi
  tools='timeout tail sleep sed grep'
  [ "$mode" != claude-usage ] || tools="$tools sha256sum"
  for tool in $tools; do
    coai_may "$tool"
    case $? in
      0) ;;
      1) printf 'COAI-BAD tool\\n'; return 0 ;;
      *) printf 'COAI-BAD unsafe\\n'; coai_path; return 0 ;;
    esac
  done
  coai_may jq
  case $? in
    0) jq=found; printf 'COAI-JQ found\\n' || return 0 ;;
    1) jq=missing; printf 'COAI-JQ missing %s\\n' "$(coai_manager)" || return 0 ;;
    *) printf 'COAI-BAD unsafe\\n'; coai_path; return 0 ;;
  esac
  if [ -n "$home_unsafe" ]; then
    PATH=$PATH:$HOME/.local/bin
  fi
  vwarn=
  coai_claude
  [ "$mode" != claude-usage ] || return 0
  coai_codex
  coai_agy
  return 0
}
{ </dev/null 2>/dev/null coai_accounts "$@"; printf 'COAI-ACCOUNTS-END\\n'; }
`;
