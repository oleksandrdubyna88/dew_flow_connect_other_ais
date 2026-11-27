# PLAN — E3.S2 spec: revision 2 of the accounts script, its parser by mode and the round's join (CORNERSTONE)

> Status: **plan only, nothing implemented yet, 2026-11-26.** Written by Fable before any code of the story.
> Cornerstone spec of story E3.S2 of
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E3.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E3.md)
> (parent [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md):
> § 2, § 4, *Security*, the test plan's row *Revision 2 of the script*, and E3 and E4 of its `## Epics`; read on the
> plan's branch at `f5f3362b`, and held by gate E3-G1 against `50ac8f90`, epoch 5: entry 4.2 lets E4 be in E3's
> pull request and merges nothing before the one real-window check). It changes the spec of revision 1,
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_S2_spec.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1_S2_spec.md):
> what that spec says holds unless a line here changes it, and that file stays the record of revision 1. It keeps
> no change ledger: a change is an entry in the parent. **The text of § R2.3 was run with false programs
> (§ R2.0); it was not run as a real UID 0, with the real `jq` or with a real Claude Code.**
>
> Scope: `src_vs_code` only. `src/remoteAccountsScript.ts` (the text, the operands, the limits),
> `src/remoteAccountsReply.ts` (the parser by mode, four new words), `src/remoteSsh.ts` (the digest in the argv),
> `src/remoteAccountsRound.ts` (the usage call and the join), the table's model of E2.S2 (three sentences), the
> tests. Coder: sonnet. Review: opus. Paths are under `src_vs_code/` unless they start with `todo/` or `research/`.

## R2.0 — What was read, what was run, what was not

- **Read:** the parent as named above; its appendix, § K.4, § K.12, § K.29 and § K.31; W9
  (`research/RESULTS_wsl_on_this_pc.md` on the plan's branch) and W6, W7 and W8
  ([../research/RESULTS_accounts_and_usage_on_a_vm.md](../research/RESULTS_accounts_and_usage_on_a_vm.md), § 13 on
  the plan's branch); the code on `origin/main` at `401bbf0a`: `src/remoteAccountsScript.ts` (the text of revision
  1: 222 lines, sha256 `e42bae87…`), `src/remoteAccountsReply.ts:132-150` (`ACCOUNTS_GRAMMAR`), `:303`
  (`firstFault`), `:431` (`readAccounts`), `src/remoteAccountsRound.ts:39` (`USAGE_POLICY`), `:108`
  (`claudeAccountKey`), `:140` (`needsSecondCall`), `:281` (`publishedState`), `:327` (`settled`), `:380`
  (`hostRound`), `src/remoteSsh.ts:55` (`accountsArgv`), `:155` (`runAccounts`).
- **Run, 2026-11-26, on this PC:** the text of § R2.3 by `sh` (dash) and by `bash`, 14 runs each with the same
  output: `sh -n` clean; false `claude`, `agy` and `jq` in a temporary `HOME`, never the real one. Seen: the first
  mode prints the three parts; `claude-usage` with the fitting digest starts the usage command once and prints
  no Codex and no agy line, and the false `agy` records no start; another digest gives `skipped
  account-changed` and no usage start; a digest of 63 characters, one in capitals, a digest with the first mode
  and the usage mode with none give `COAI-BAD operands` and no start; a `claude` that is only on the inherited
  `PATH` is `absent` and is not started; the six runs that stand for UID 0 (the S1 spec, § W8).
- **Not run:** the real `jq` (it is not on that machine; the three `jq` programs are those of revision 1, byte
  for byte); a real UID 0; a real Claude Code; busybox `sh`; the timing run of § R2.5. The story's tests run
  them on CI's `jq` (E3S2-T1…T24); a real UID 0 is the owner's row R13.

## R2.1 — Decisions

| # | Decision | Why |
|---|---|---|
| D1 | The begin line stays `COAI-ACCOUNTS 1` | A script and its parser ship in one build and never meet another revision; a new number would only add a rule |
| D2 | **The `PATH` is set, never added to**: `$HOME/.local/bin`, then `/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin`. A `HOME` that holds `:` is `COAI-BAD home` | The parent's § 4, item 1. A `:` in `HOME` would add a folder to the list. `/snap/bin` and `/usr/games` are not in it: § R2.14, 1 |
| D3 | As UID 0 the home folder is added only after its check, and every program is checked before its first start | The S1 spec, § W8, owns the rule; this text holds its functions |
| D4 | `claude-usage` takes ONE more operand, 64 characters of `0-9a-f`; `no-claude-usage` takes none. Anything else is `COAI-BAD operands` before any start | § 4, item 3 |
| D5 | **The digest is the sha256 of the account lines as printed**: the line `COAI-VENDOR claude found` and the `:email`, `:org` and `:plan` lines after it, each with its LF. Not the `:version` line | It is what the script holds in one variable before it prints, so both sides hash the same bytes |
| D6 | `sha256sum` is checked with the other tools **in the usage mode only** | A place without it still shows its accounts; its usage call answers `COAI-BAD tool` |
| D7 | In `claude-usage` the script returns after the Claude part: no Codex file is opened and `agy` is not started | § 4, item 2 |
| D8 | The limits are those of revision 1; the digest gets 1 s. Each mode's worst path is 17 s, 22 s with ssh's connect (§ R2.5) | § 4, item 4, with the digest's second, which the parent's table does not count (§ R2.14, 7) |
| D9 | The parser answers a new arm, `claude-only`, for the usage mode; a reply whose parts do not fit its mode is `unreadable shape` | "The parser reads a reply against the mode that was sent" |
| D10 | **The round never replaces a place's reading by a usage reply: it joins** (§ R2.8). Usage counts for an account only when the second reply names the SAME account key as the first | § 2, *The round JOINS two replies* |
| D11 | One pull request holds the text, the parser and the join | The parent's E3: "never two" |
| D12 | `USAGE_POLICY` becomes `every-round` in its own commit, after this story's code, in the same pull request or a later one (§ R2.9) | W6 and W7; the parent's E4, as entry 4.2 left it |

## R2.2 — The operands and the digest (`src/remoteAccountsScript.ts`, `src/remoteSsh.ts`)

- `ACCOUNTS_DIGEST = /^[0-9a-f]{64}$/u`, and `isAccountsDigest(word)`.
- **`accountsDigest(stdout: string): string | undefined`** — pure (`node:crypto`). Over the framed lines of a
  FIRST reply, as received and before `plainValue`: the line that is exactly `COAI-VENDOR claude found` and the
  lines directly after it that start with `:email `, `:org ` or `:plan `; each line and one LF, as UTF-8; the
  sha256 in lower-case hex. `undefined` when the frame or that marker is not there.
- `accountsArgv(host, mode, digest?)` THROWS the fixed text unless: the mode is `no-claude-usage` and there is no
  digest, or it is `claude-usage` and the digest fits `ACCOUNTS_DIGEST`. The remote words are then `sh -s --
  <mode>` or `sh -s -- claude-usage <digest>`, each quoted as today. `runAccounts(host, mode, digest, …)` answers
  `refused` with no call on the same rule. The WSL argv takes the same two forms (the S1 spec, § W7).
- **No other text is an operand, ever**: no email, no host, no name of a distro or a user.
- A reply whose bytes were not UTF-8 is decoded with a replacement character, so its digest does not fit and
  the usage call answers `skipped account-changed`: that account's usage is not read, and the cell says so.

## R2.3 — `ACCOUNTS_SCRIPT`, revision 3, exact (`src/remoteAccountsScript.ts`)

`ACCOUNTS_SCRIPT` is the text below, byte for byte: **413 lines**, 14 798 bytes, each line ends with LF, ASCII
only. It holds `${` 15 times, 82 backslashes and no backtick.

Pinned sha256: `6d02ea1cb3bb1644fee0a2d65c948ff5b9ea7f0cb829496156a86569bd18bfa2`

*Revision 3 (the owner, 2026-11-27) changed this text a second time; the pin above is its sha256.* The accounts tab now says what
Codex and Antigravity say just now, instead of reading what they left behind. **Codex:** the three session files (`ls`, `stat`,
`tail` of each, and their `jq` program) are gone; the script starts `codex app-server` (`initialize`, `account/read`,
`account/rateLimits/read`, a `sleep 3` keeping its stdin open; no model call) and prints the account (`:account`), the plan and the
`:window` lines of its primary and secondary limits. Its exit code is kept (`coai_exit`): figures are accepted whenever the rate-limit reply came;
without it, 124 and 137 are `COAI-USAGE codex failed timeout` and anything else `failed error`; only a real reply with no limit is `failed none`. A
`~/.codex` with no `codex` on the script's `PATH` is `COAI-VENDOR codex found` with `COAI-USAGE codex failed not-started`. `:as-of` and `failed no-sessions`
are gone. **Antigravity:** `agy -p /quota --output-format json` is run when `jq` is found and the first call does not carry the operand
`no-agy-quota`; only a reply with `num_turns` 0, `command.name` `usage` and an empty `conversation_id` is read (any other is `COAI-USAGE
agy failed model-call`, because an unknown slash command is sent to the model), as `:quota <gemini|third-party> <5h|weekly> <percent left>
<reset epoch or ->` lines; agy's account is the last `applyAuthResult: email=` line of its own `cli.log`, read after that call, one line, a
shape-checked email, as `:account` (as UID 0 the log's path, and the target of its link, is checked first and the account is skipped when it fails). `jq_agy` uses no regular expression, and a reply with `usage.total_tokens` not 0 is `failed model-call` too. **Any** outcome of the quota command that is not a read blocks the place (the extension keeps the block apart from its memory, in `globalState`, by place, version and reason). **Operands:** the first mode takes the optional second operand `no-agy-quota`; no other. `sleep` replaces
`ls` in the tool list. **As UID 0 (the owner, 2026-11-27):** a vendor program (`claude`, `codex`, `agy`) whose path fails `coai_closed` is NOT refused: it runs and the
script sends one `:unsafe-path <path>` line for it (`vwarn`); the first line of its file is read, and when it is a `#!` script its interpreter (`env` resolved on the
`PATH`) is checked the same way; a `$HOME/.local/bin` that fails the check goes at the END of the `PATH`. The tools and `jq` keep `COAI-BAD unsafe`.
**Time (§ R2.5, redone):** `codex` TERM 4 s, its readers 5 s; agy's version TERM 2 s, its quota TERM 4 s, its readers 5 s; agy's log 1 s; as UID 0 one second for
the first line of each vendor program; the first mode's worst path is 21 s, 26 s with ssh's connect (29 s as UID 0); the usage mode is 17 s, 22 s (23 s as UID 0). The text was run by `sh` (dash) and `bash`
against false programs in a temporary `HOME` (`remoteAccountsRuns.test.ts`) and once against the real `codex` and `agy` of the author's machine.

*Gate E3-G1 changed this text (finding B1; the pin above is its new sha256, by `sha256sum` over the block):* as
UID 0, `stat` and `readlink` are found on the six system folders BEFORE root's `~/.local/bin` goes in front, and
are called by that path from then on (`$st`, `$rl`) — in every walk and in the Codex read's `stat`. Before, they
were called by name after the prepend, so a `stat` in root's own folder ran as root unchecked. The judge ran the
old and the new text by `sh` (dash) and `bash` with false `claude` (behind a relative link), `agy` and `jq` in a
temporary `HOME`, as a user and with the UID-0 prelude of the S1 spec, § W8: both modes, the digest fitting and
not, root's folder closed and group-writable — the same lines and the same starts.

The test A2-T2 of revision 1 holds three things to one another, and now reads THIS file: the sha256 of the
constant, the pin line above, and the first `sh` block after the heading `## R2.3`. A later change of the text
moves the block, the pin and the test's literal in one commit.

```sh
coai_accounts() {
  unset IFS
  set -f
  nl='
'
  mail_re='[A-Za-z0-9._%+-]{1,64}@[A-Za-z0-9.-]{3,190}$'
  dot=$(printf '\302\267')
  window='[0-9]{1,3}(\.[0-9]{1,2})?% used '"$dot"' resets [A-Za-z0-9,: ]{1,40} \([A-Za-z0-9_/+-]{1,40}\)$'
  version='1s/^[^0-9]*\([0-9][0-9.]\{0,31\}\).*$/\1/p'
  one='def one: tostring | split("\n") | join(" ") | split("\r") | join(" ") | .[0:120];'
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
      else ($r.result | split("\n")) as $l
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
      *) printf ':version %s\n' "$1" ;;
    esac
  }

  coai_sign_in() {
    if [ -e "$1" ]; then
      printf ':sign-in-file present\n'
    else
      printf ':sign-in-file absent\n'
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
        rest=${rest#/}
        part=${rest%%/*}
        case $rest in
          */*) rest=/${rest#*/} ;;
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
            *) left=${q%/*}/$to$rest ;;
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
    [ "${#unsafe}" -le 200 ] || unsafe=-
  }

  coai_path() {
    coai_clean
    printf ':path %s\n' "$unsafe"
  }

  coai_warn() {
    if [ -n "$vwarn" ]; then
      unsafe=$vwarn
      coai_clean
      case $unsafe in
        /*|-) ;;
        *) unsafe=- ;;
      esac
      printf ':unsafe-path %s\n' "$unsafe"
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
    set -- ${shebang#??}
    interp=${1-}
    case $interp in
      */env)
        shift
        while [ "$#" -gt 0 ]; do
          case $1 in
            -*|*=*) shift ;;
            *) break ;;
          esac
        done
        interp=${1-}
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
      1) [ "$#" -gt 1 ] || printf 'COAI-VENDOR %s absent\n' "$1"; return 1 ;;
      *) vwarn=$unsafe ;;
    esac
    coai_script "$1"
    return 0
  }

  coai_claude() {
    coai_vendor claude || return 0
    [ "$jq" = found ] || { printf 'COAI-VENDOR claude unreadable no-jq\n'; return 0; }
    auth=$(
      {
        timeout -k 1 3 claude auth status --json
        printf '\n{"coai_exit":%s}\n' "$?"
      } | timeout -k 1 4 tail -c 65536 | timeout -k 1 4 jq -n -r "$jq_auth"
    )
    case $auth in
      'COAI-VENDOR claude found'|'COAI-VENDOR claude found'"$nl":*) ;;
      'COAI-VENDOR claude signed-out'|'COAI-VENDOR claude unreadable timeout') printf '%s\n' "$auth"; return 0 ;;
      *) printf 'COAI-VENDOR claude unreadable auth\n'; return 0 ;;
    esac
    printf '%s\n' "$auth" || return 0
    coai_version "$(timeout -k 1 2 claude --version | LC_ALL=C timeout -k 1 2 sed -n -e "$version" -e 1q)"
    coai_warn
    if [ "$mode" != claude-usage ]; then
      printf 'COAI-USAGE claude skipped\n'
      return 0
    fi
    sum=$(printf '%s\n' "$auth" | timeout -k 1 1 sha256sum)
    if [ "${sum%% *}" != "$want" ]; then
      printf 'COAI-USAGE claude skipped account-changed\n'
      return 0
    fi
    kept=$(
      {
        TZ=UTC timeout -k 1 8 claude -p "/usage" --output-format json --no-session-persistence --strict-mcp-config --setting-sources ""
        printf '\n{"coai_exit":%s}\n' "$?"
      } | timeout -k 1 9 tail -c 1048576 | timeout -k 1 9 jq -n -r "$jq_usage" |
        LC_ALL=C grep -E -e '^=timeout$' -e '^=read [0-9]{1,6} [0-9]{1,6}$' -e "^-Current session: $window" -e "^-Current week \\([A-Za-z0-9][A-Za-z0-9 ._-]{0,39}\\): $window"
    )
    head=${kept%%"$nl"*}
    case $head in
      '=timeout') printf 'COAI-USAGE claude failed timeout\n'; return 0 ;;
      '=read '*) ;;
      *) printf 'COAI-USAGE claude failed error\n'; return 0 ;;
    esac
    set -- $head
    sent=$(printf '%s\n' "$kept" | LC_ALL=C grep -c '^-')
    [ "$sent" -le 8 ] || sent=8
    printf 'COAI-USAGE claude read\n' || return 0
    printf ':turns %s\n' "$2"
    printf '%s\n' "$kept" | LC_ALL=C sed -n -e 's/^-/:line /p' | LC_ALL=C sed -n -e '1,8p'
    printf ':unmatched %s\n' "$(($3 - sent))"
    return 0
  }

  coai_codex() {
    coai_vendor codex quiet || {
      if [ -d "$HOME/.codex" ]; then
        [ "$jq" = found ] || { printf 'COAI-VENDOR codex unreadable no-jq\n'; return 0; }
        printf 'COAI-VENDOR codex found\n' || return 0
        coai_sign_in "$HOME/.codex/auth.json"
        printf 'COAI-USAGE codex failed not-started\n'
      else
        printf 'COAI-VENDOR codex absent\n'
      fi
      return 0
    }
    [ "$jq" = found ] || { printf 'COAI-VENDOR codex unreadable no-jq\n'; return 0; }
    printf 'COAI-VENDOR codex found\n' || return 0
    coai_sign_in "$HOME/.codex/auth.json"
    coai_warn
    record=$(
      {
        printf '%s\n' '{"method":"initialize","id":1,"params":{"clientInfo":{"name":"coai","title":"coai","version":"1"}}}' '{"method":"initialized"}' '{"method":"account/read","id":2,"params":{}}' '{"method":"account/rateLimits/read","id":3}'
        sleep 3
      } | {
        timeout -k 1 4 codex app-server
        printf '\n{"coai_exit":%s}\n' "$?"
      } | timeout -k 1 5 tail -c 1048576 | timeout -k 1 5 jq -n -R -r "$jq_codex" |
        LC_ALL=C grep -E -e "^:account $mail_re" -e '^:plan ' -e '^COAI-USAGE codex (read|failed none|failed error|failed timeout)$' -e '^:window [-0-9.]{1,8} [-0-9]{1,6} [-0-9]{1,11}$'
    )
    case $record in
      *'COAI-USAGE codex '*) printf '%s\n' "$record" ;;
      *) printf 'COAI-USAGE codex failed error\n' ;;
    esac
    return 0
  }

  coai_quota() {
    kept=$(
      {
        timeout -k 1 4 agy -p /quota --output-format json
        printf '\n{"coai_exit":%s}\n' "$?"
      } | timeout -k 1 5 tail -c 1048576 | timeout -k 1 5 jq -n -r "$jq_agy" |
        LC_ALL=C grep -E -e '^=(timeout|error|model-call|read)$' -e '^:quota (gemini|third-party) (5h|weekly) [0-9]{1,3} (-|[0-9]{1,11})$'
    )
    case ${kept%%"$nl"*} in
      '=timeout') printf 'COAI-USAGE agy failed timeout\n' ;;
      '=model-call') printf 'COAI-USAGE agy failed model-call\n' ;;
      '=read')
        printf 'COAI-USAGE agy read\n'
        printf '%s\n' "$kept" | LC_ALL=C sed -n -e '/^:quota /p' | LC_ALL=C sed -n -e '1,8p'
        ;;
      *) printf 'COAI-USAGE agy failed error\n' ;;
    esac
  }

  coai_agy() {
    coai_vendor agy || return 0
    printf 'COAI-VENDOR agy found\n' || return 0
    coai_version "$(timeout -k 1 2 agy --version | LC_ALL=C timeout -k 1 2 sed -n -e "$version" -e 1q)"
    coai_sign_in "$HOME/.gemini/antigravity-cli/antigravity-oauth-token"
    coai_warn
    if [ "$jq" != found ]; then
      printf 'COAI-USAGE agy failed no-jq\n'
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
        timeout -k 1 1 tail -c 1048576 "$logf" | LC_ALL=C timeout -k 1 1 sed -n -e 's/^.*applyAuthResult: email=\([^,]*\),.*$/\1/p' |
          timeout -k 1 1 tail -n 1 | LC_ALL=C timeout -k 1 1 grep -E -e "^$mail_re"
      )
    fi
    [ -z "$mail" ] || printf ':account %s\n' "$mail"
    printf '%s\n' "$quota"
    return 0
  }

  printf 'COAI-ACCOUNTS 1\n' || return 0
  askq=yes
  case $#:${1-}:${2-} in
    1:no-claude-usage:) want= ;;
    2:no-claude-usage:no-agy-quota) want=; askq=no ;;
    2:claude-usage:*)
      want=$2
      case $want in
        *[!0-9a-f]*) want= ;;
      esac
      [ "${#want}" -eq 64 ] || { printf 'COAI-BAD operands\n'; return 0; }
      ;;
    *) printf 'COAI-BAD operands\n'; return 0 ;;
  esac
  mode=$1
  case ${HOME-} in
    *:*) printf 'COAI-BAD home\n'; return 0 ;;
    /*) ;;
    *) printf 'COAI-BAD home\n'; return 0 ;;
  esac
  cd / || { printf 'COAI-BAD folder\n'; return 0; }
  PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
  export PATH
  command -v id >/dev/null || { printf 'COAI-BAD tool\n'; return 0; }
  uid=$(id -u)
  case $uid in
    ''|*[!0-9]*) printf 'COAI-BAD tool\n'; return 0 ;;
  esac
  unsafe=
  home_unsafe=
  if [ "$uid" = 0 ]; then
    st=$(command -v stat) || { printf 'COAI-BAD tool\n'; return 0; }
    rl=$(command -v readlink) || { printf 'COAI-BAD tool\n'; return 0; }
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
      1) printf 'COAI-BAD tool\n'; return 0 ;;
      *) printf 'COAI-BAD unsafe\n'; coai_path; return 0 ;;
    esac
  done
  coai_may jq
  case $? in
    0) jq=found; printf 'COAI-JQ found\n' || return 0 ;;
    1) jq=missing; printf 'COAI-JQ missing %s\n' "$(coai_manager)" || return 0 ;;
    *) printf 'COAI-BAD unsafe\n'; coai_path; return 0 ;;
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
{ </dev/null 2>/dev/null coai_accounts "$@"; printf 'COAI-ACCOUNTS-END\n'; }
```

## R2.4 — What changed against revision 1

Every other line is revision 1's (its spec, § A2.3, explains each).

| # | Where | Revision 2 |
|---|---|---|
| 1 | The operands | `1:no-claude-usage`, or `2:claude-usage` with `$2` of 64 characters of `0-9a-f`, kept as `want` |
| 2 | `HOME` | `*:*` is `COAI-BAD home`, before the test for `/*` |
| 3 | `PATH` | Set to the six system folders and exported. `id` must be there (`COAI-BAD tool`); `uid=$(id -u)` must be digits |
| 4 | The home folder | Not UID 0: `$HOME/.local/bin` goes in front. UID 0: `stat` and `readlink` must be on the six folders, and their paths are kept (`st`, `rl`) and used from then on, so root's folder cannot put another `stat` or `readlink` in front of them; the folder goes in front only when `coai_closed` passes it; else its path is kept in `home_unsafe` |
| 5 | The tools | `timeout tail ls sed grep`, and `sha256sum` in the usage mode, each through `coai_may`: missing → `COAI-BAD tool`; as UID 0 not closed → `COAI-BAD unsafe` and a `:path` line. `jq` the same: found, `missing <word>`, or `COAI-BAD unsafe` |
| 6 | `coai_closed`, `coai_may`, `coai_path`, `coai_vendor` | New. The rule, the walk and what each answers: the S1 spec, § W8 |
| 7 | `coai_claude`, `coai_agy` | Their first line is `coai_vendor <name> \|\| return 0`: `absent`, or as UID 0 `unsafe` with its `:path`, and no start |
| 8 | The usage mode | After the version line: `sum=$(printf '%s\n' "$auth" \| timeout -k 1 1 sha256sum)`; when its first word is not `want`, `COAI-USAGE claude skipped account-changed` and return. Only then the usage command, unchanged |
| 9 | The end | `[ "$mode" != claude-usage ] \|\| return 0` after `coai_claude`: no Codex read and no `agy` in the usage mode |

`ACCOUNTS_TOOLS` becomes `['id', 'timeout', 'tail', 'ls', 'sed', 'grep']`, with `sha256sum` for the usage mode.
`ACCOUNTS_PATH` is the six folders as one constant; a test holds the text's `PATH=` line to it and each entry to
the rule: absolute, no `/tmp`, nothing under `/home` or `/mnt`, none empty (E3S2-T3).

## R2.5 — The time table, with the KILL seconds (T46)

A vendor command gets a TERM at its limit and a KILL one second later. A step ends when its last process ends,
so a vendor program that ignores TERM holds its step for that second. For the account and the usage the second
is inside the reader's limit; for the two version commands it is one more.

| Mode | The steps, at their latest | Sum | With ssh's 5 s to connect |
|---|---|---|---|
| `no-claude-usage` | account 4 s; version 2 + 1 s; the Codex files 1 + 1 + 3 s; `agy` 4 + 1 s | 17 s | **22 s** |
| `claude-usage` | account 4 s; version 2 + 1 s; the digest 1 s; usage 9 s | 17 s | **22 s** |

- `ACCOUNTS_READ_SECONDS` gains `digest: 1`. New: `ACCOUNTS_KILL_SECONDS = { version: 1, agy: 1 }`,
  `ACCOUNTS_CONNECT_SECONDS = 5`, and the pure `worstSeconds(mode)`, which sums the reader limits of the steps
  that mode runs, its KILL seconds and the connect time. **The test asserts `worstSeconds(mode) <=
  ACCOUNTS_CEILING_SECONDS - 2` for both modes** (E3S2-T12), and A2-T26 still holds each number to the text.
- No limit is shortened and the ceiling stays 30 s. A WSL call has no connect; its start took 0.3 to 0.4 s (W9g).
- **Not in the sum, said and not hidden:** what the shell does itself (a glob, a test of a file), and as UID 0
  the `stat` and `readlink` calls of the check, which run under no `timeout`. They read local folders; one that
  hangs is ended by the runner's kill at 30 s, and that place says it did not answer.
- The todo file `todo/PLAN_the_usage_mode_stays_under_the_30_s_ceiling.md` and its README row are deleted by the
  commit that makes E3S2-T12 and T13 green.

## R2.6 — What leaves the host

Revision 1's list (its spec, § A2.4) holds. New, and nothing else:

| What | When | Its form |
|---|---|---|
| `COAI-USAGE claude skipped account-changed` | The usage mode, the digest does not fit | A fixed line |
| `COAI-VENDOR claude unsafe`, `COAI-VENDOR agy unsafe`, `COAI-BAD unsafe`, each with one `:path` line | UID 0 only, and only when the check refuses | The path that failed, when it fits `[A-Za-z0-9._/@+-]{1,200}`; else `-` |

**Less leaves in the usage mode:** no Codex line and no agy line. The digest is an operand and is never printed.
What is in an argv on the host: the mode word and 64 hex characters, never an email.

## R2.7 — The reply and the parser by mode (`src/remoteAccountsReply.ts`)

**The grammar gains four rows**, and nothing is removed:

| Marker line | Keys |
|---|---|
| `COAI-BAD unsafe` | `path` once |
| `COAI-VENDOR claude unsafe` | `path` once |
| `COAI-VENDOR agy unsafe` | `path` once |
| `COAI-USAGE claude skipped account-changed` | none |

- A `path` value is closed: `-`, or a text that starts with `/` and fits the class of § R2.6. Else the fault is
  `value`. It becomes `path: string | null` (`-` is `null`) and goes through `plainValue` like every text.
- `AccountsUnreadable` gains `unsafe`; that arm then holds `path`. `ClaudeSide` and `AgySide` gain
  `{ state: 'unsafe'; path: string | null }`. `ClaudeUsage` gains `{ state: 'skipped'; why: 'account-changed' }`
  beside the plain `skipped`.
- The sentences (the table's model): *not read: `<path>` is writable by another user*; with no path, *not read:
  a program there is writable by another user*.

**The arrangement, by the mode that was SENT** (one more check in `CHECKS`; the fault is `shape`):

| Mode sent | The records after the `COAI-JQ` line | The Claude usage word, when Claude is `found` |
|---|---|---|
| `no-claude-usage` | the Claude part, the Codex part, the agy part — as revision 1 | `skipped` exactly |
| `claude-usage` | the Claude part and NOTHING else: a Codex or an agy record is `shape` | `read`, `failed timeout`, `failed error` or `skipped account-changed`; a plain `skipped` is `shape` |

`readAccounts(run, mode, nowMs)` answers, for `claude-usage`, the new arm
`{ kind: 'claude-only'; claude: ClaudeSide; jqMissing }`, and never `read`; for `no-claude-usage` it never
answers `claude-only`. A `COAI-BAD` record is `unreadable` in both modes, as today.

## R2.8 — The round: the usage call and the join (`src/remoteAccountsRound.ts`)

Until E3.S3 a place is a host; the rules are written for a place.

- **The call.** `AccountsCall` becomes `(place, mode, digest?) => Promise<RemoteRun>`. After a place's first
  reply, when `needsSecondCall` says so (unchanged: never for an account in the round's usage set), the round
  takes `accountsDigest(run.stdout)` of THAT run. No digest → no second call, and the usage cell says *Usage not
  read: this reply names no account lines*. The first run's stdout is held for this one step and not kept.
- **The join.** `publishedState` no longer replaces a reading by the reply of a usage call. ONE pure function,
  `joined(first, firstKey, second, place)`, where `first` is the reading of the first call, `firstKey` its
  `claudeAccountKey`, and `second` what the usage call came to:

| The usage call came to | The place's reading after it | Usage counted for `firstKey` |
|---|---|---|
| `refused` (the runner's sentence) | `first`, whole; `usageMiss = { why: 'call-failed', sentence }` | no |
| `unreadable <word>`, or the arm `read` | `first`, whole; `usageMiss = { why: 'unreadable', word }` (`shape` for `read`) | no |
| `claude-only`, Claude `found`, the SAME key, usage `read` or `failed` | `first` with its Claude part replaced by the second's; Codex, agy and `jqMissing` of `first` | yes when `read`; `usageAtMs` is now |
| `claude-only`, Claude `found`, usage `skipped account-changed`, or ANOTHER key with any usage | `first` with its Claude part replaced by the second's account, its usage set to `skipped`; `usageMiss = { why: 'account-changed' }`; `claudeAccount` is the second's key | no — the account the call was for is read by its next place |
| `claude-only`, Claude not `found` | `first` with its Claude part replaced by the second's state | no |

- `HostMemory` gains `usageMiss`, cleared by the place's next first reply. The block rule (`blockedAfter`) reads
  the joined reading. `roundUsage` gains `firstKey` only in the row that says yes. **No third call is made**:
  `hostRound` makes at most one usage call for a place in a round.
- The table's model gains three sentences that span the window cells: *Usage not read: the usage call failed —
  `<sentence>`*; *Usage not read: the usage reply could not be read (`<clause>`)*; *Usage not read: the account
  changed between the two reads*. When the round read the changed-to account at another place, the cell shows
  those figures (the sharing rule), not the sentence.
- The checks before a call and before a result (the generation, the Stop, the terminal, the door) are unchanged
  and stand before the usage call and before the join.

## R2.9 — `USAGE_POLICY` every round (story E3.S5, the parent's E4)

- **The facts** (`RESULTS_accounts_and_usage_on_a_vm.md`, § 13): W7 passed — a usage read started nothing that
  is not Claude Code's own. W6 selected the row "Claude Code keeps a fixed number → every round": five backups,
  and the older ones were seen deleted. Both were measured with revision 1's usage command, which revision 2
  does not change.
- **The change:** `USAGE_POLICY = { kind: 'every-round' }`, ONE constant. `hourly`, `on-press` and `off` stay as
  rows of `DUE`. Its commit comes after this story's code and **merges only with revision 2 and the join — on
  main, or in the same pull request** (the parent's E4: *Depends on*, and its merge condition, as entry 4.2 left
  them), and like every pull request of E2 to E4 only after the one real-window check (merge condition 5).
- **Tests:** E3S5-T1 the constant is `every-round`, and the test's comment names W6 and W7; E3S5-T2 with the
  constant, a round over three places of two accounts makes exactly two usage calls, each with the digest of its
  place's first reply; E3S5-T3 no call is `claude-usage` as a place's first call, and no row of the model says
  *CoAI does not ask Claude Code for usage in this build* any more.
- A result that needs another flag or another script line is a change of the parent, not of this constant.

## R2.10 — The security judgement on the text

1. **The `PATH`.** Nothing the caller's environment holds chooses a program: the list is fixed text, and
   `HOME` is used only after it is absolute and holds no `:`. On WSL the Windows folders are not searched, so a
   `claude` of the Windows side is not started through interop. E3S2-T2 holds it by a start that is not recorded.
2. **The digest is in an argv.** Every user of that machine can read it for the seconds the call lives. It is
   not the email. A user who guesses the account lines can test the guess against it: accepted, as the parent
   says (§ 4, item 3).
3. **The check of the account is the script's**, a second before the usage command. What is left: an account
   that changes between those two commands (the parent's residual).
4. **A hostile place** can print any account and any figures for it; with the sharing rule those figures are
   then shown at every place that names the same account text. That was so in revision 1. No right is gained:
   wrong numbers on screen. The join counts usage only for the key the call was made for (D10).
5. **The patterns are not locale-proof.** The script sets no locale for itself (it would change what the vendor
   programs see), so in a shell that reads ranges by locale a class can be wider than written. Every class is
   checked again on this side: the digest before it is sent, the `:path` value and every reply line after.
6. **New programs started:** `id`, and in the usage mode `sha256sum`; as UID 0 also `stat` and `readlink`. Each
   is found by the fixed list. No new file is opened: `sha256sum` reads a pipe.
7. **As UID 0** the judgement is the S1 spec's (§ W8, § W15).

## R2.11 — Scenarios

Unit tests and the runs of the REAL script by `sh -s` with false programs in a temporary `HOME`, as revision
1's `remoteAccountsRuns.test.ts`. RED first; a compile error counts for a new export. **The script no longer
reads the inherited `PATH`, so a test cannot put a program in front of a system one or hide one by it:**

- A false program stands in `$HOME/.local/bin`, which is first on the script's own list.
- **A tool is hidden by a prelude**, sent before the pinned text: a shell function `command` that answers
  `command -v <name>` with "not there" for the names the test hides and walks the `PATH` for every other. The
  pinned text is unchanged and is what runs. Seen on 2026-11-26 in dash and bash with `sha256sum` and with
  `timeout` hidden. The runs of revision 1 that hid `jq` or a tool by the `PATH` move to this prelude.

| # | What must be shown |
|---|---|
| E3S2-T1 | **The pin** (A2-T2, moved): the constant's sha256, the pin line of § R2.3 and the `sh` block after `## R2.3` are one |
| E3S2-T2 | **The inherited `PATH` is not used** (real script): a false `claude` that is only in a folder of the caller's `PATH` records no start, and the reply says `absent`. Break-it: revision 1's `PATH` line |
| E3S2-T3 | The text's `PATH=` line equals `ACCOUNTS_PATH`; each entry is absolute, none is `/tmp`, under `/home` or under `/mnt`, none is empty; `HOME=/a:/b` answers `COAI-BAD home` and starts nothing |
| E3S2-T4 | The operands (real script): the first mode with a second operand, the usage mode with none, a digest of 63 and of 65 characters, one in capitals, one with a space → `COAI-BAD operands`, and no program recorded a start |
| E3S2-T5 | **The usage mode reads Claude only** (real script): the false `agy` records no start; a Codex session file is in place and no line of the reply names Codex; the frame holds the `COAI-JQ` line and the Claude part only |
| E3S2-T6 | **The account check, the digest fits** (real script, real `jq`): the false `claude` records ONE start with `-p /usage`, and the reply holds `COAI-USAGE claude read` |
| E3S2-T7 | **Another digest** (real script): `skipped account-changed`, and the false `claude` records NO start with `-p`. Break-it: the comparison removed |
| E3S2-T8 | `accountsDigest`: equals `sha256sum` of the lines the real script printed, for an account with all three keys, with the email only and with none of them; a reply with no `found` marker is `undefined`; the `:version` line does not change it; a value with a format character is hashed as received |
| E3S2-T9 | `accountsArgv` and `runAccounts`: the two legal forms word for word; the usage mode with no digest, a digest outside its class, the first mode with a digest → the fixed refusal, no call |
| E3S2-T10 | **No email in an argv**: over every argv the round's call seam builds for places whose account text holds `@`, no word holds `@` |
| E3S2-T11 | The usage mode with `sha256sum` hidden by the prelude → `COAI-BAD tool`; the first mode there reads the three parts; `jq` hidden → `COAI-JQ missing <word>` |
| E3S2-T12 | **T46, the sum**: `worstSeconds` of each mode is at most the ceiling minus 2 s; the number equals the table of § R2.5 (17) plus the connect time |
| E3S2-T13 | **T46, the run** (real script, both modes started together): a false `claude --version` and a false `agy` that ignore TERM, every other step at its limit → each run ends inside its sum plus 1 s, and the end line is written. **The usage-mode run must reach the usage step** (gate E3-G1, F2): its false `sha256sum` prints the TRUE digest just under its 1 s, and the test asserts the reply holds `COAI-USAGE claude read` or `failed timeout` — a digest killed at its limit would skip the usage step and the run would measure 8 s, not 17 |
| E3S2-T14 | The grammar's four rows; a `path` of `-`, of a legal path, of one with a space or 201 characters (`value`); `unsafe` in the three places gives the three typed results |
| E3S2-T15 | **The parser by mode**: a first-mode reply sent as `claude-usage` and a usage reply sent as `no-claude-usage` are `shape`; a usage reply with a Codex or an agy record is `shape`; a plain `skipped` in the usage mode is `shape`; `skipped account-changed` in the first mode is `shape` |
| E3S2-T16 | `claude-only` for each legal usage reply; `read` is never answered for the usage mode |
| E3S2-T17 | **The join, the same account**: after the second call the place holds the Codex and agy parts of the FIRST reply and the usage of the second; the key is in the round's usage set. Break-it: `publishedState` replaces the reading |
| E3S2-T18 | **The join, a call that fails or cannot be read**: the first reading whole, with `usageMiss`; the model's cell says the reason; the key is not in the usage set |
| E3S2-T19 | **The join, account changed**: place A read the usage of X; place B first answers Y and at its usage call answers X with `skipped account-changed` → X's usage command ran once in the round, B shows X with A's figures, and no third call is made |
| E3S2-T20 | **The join, another key with a usage**: the second reply names another account and holds `read` → no usage is counted, the figures are not shown, and the first key stays out of the usage set. Break-it: the key comparison removed |
| E3S2-T21 | A call for an account already in the round's usage set is never built (the seam records no call) |
| E3S2-T22 | **Leak, the usage mode** (real script): a marker text in a Codex session file, in the false `agy`'s output and in a field of the usage JSON that no pattern names is in no byte of stdout; the digest is in no line of the reply |
| E3S2-T23 | **Leak, the path**: a run that does not stand for UID 0 never prints a `:path` line; a refused path that holds a space is printed as `-` |
| E3S2-T24 | The log scan covers the changed modules: no line holds stdout, a digest or an account text |
| E3S5-T1…T3 | § R2.9 |

Revision 1's tests stay green with these edits: the false programs move into `$HOME/.local/bin`; A2-T26 gains
the digest's limit; the tests that sent `claude-usage` with no digest send one.

## R2.12 — Break-its

Each is a PRODUCTION change, seen RED with its message, then restored; the pull request lists the observation.

| Break | Must go red |
|---|---|
| The `PATH` line back to revision 1's | E3S2-T2 |
| The line `[ "$mode" != claude-usage ] \|\| return 0` removed | E3S2-T5, T15, T22 |
| The digest comparison removed | E3S2-T7 |
| `2:claude-usage)` accepts any `$2` | E3S2-T4 |
| `accountsDigest` takes the `:version` line too | E3S2-T8, T6 |
| The mode check dropped from `CHECKS` | E3S2-T15 |
| `publishedState` replaces instead of joining | E3S2-T17 |
| `joined` does not compare the keys | E3S2-T20 |
| `ACCOUNTS_KILL_SECONDS.agy` set to 0, or a reader limit raised by 12 | E3S2-T12 |
| The pinned text with one byte changed | E3S2-T1 |
| The `*:*)` row of the `HOME` check removed | E3S2-T3 |
| The round's call seam given the first reply's `:email` value where the digest goes | E3S2-T10 |
| `needsSecondCall` no longer reads the round's usage set | E3S2-T21 |
| `coai_path`'s class test removed (the path printed as found) | E3S2-T23 |
| One log line of a changed module given the run's stdout | E3S2-T24 |

## R2.13 — The brief: order, files, commands

1. **The text.** `src/remoteAccountsScript.ts`: the constant from § R2.3, `ACCOUNTS_PATH`, `ACCOUNTS_TOOLS`,
   `ACCOUNTS_DIGEST`, `accountsDigest`, the limits and `worstSeconds`. The pin test moves to this file in the
   same commit. Check: `node -e` over the built constant prints the pinned sha256.
2. **The runs.** `src/test/remoteAccountsRuns.test.ts`: E3S2-T2, T4…T7, T11, T13, T22, T23, and the UID-0 runs
   of the S1 spec (E3S1-T20…T26), which are tests of this text and land with it.
3. **The parser.** `src/remoteAccountsReply.ts`: the four rows, the types, the mode check, `claude-only`.
4. **The argv.** `src/remoteSsh.ts`: the digest.
5. **The join.** `src/remoteAccountsRound.ts`: `AccountsCall`, the digest step, `joined`, `usageMiss`; the three
   sentences in the table's model.
6. The todo file of T46 and its README row are deleted (§ R2.5).

Commands: `cd src_vs_code && npm ci && npm test`. Never `git add -A`. The coder does not change a limit, the
ceiling, a `jq` program or a pattern of revision 1; a need to is reported, not made.

## R2.14 — What this story does not close

1. **A tool that lives outside the list is `absent` or `missing`**: a `jq` under `/snap/bin`, a `claude` in a
   folder that only a profile adds. Over ssh that was so for a shell that is not a login shell, but for the
   folders `/etc/environment` adds. R15 and R16 show what the owner's places answer.
2. As UID 0, when `~/.local/bin` is not closed, a `jq` that is only there is said `missing`; the rows of
   `claude` and `agy` name the folder.
3. An account that changes between the account command and the usage command of one call.
4. A hostile place and the sharing rule (§ R2.10, 4).
5. The usage figures of an account whose lines were not UTF-8 are never read (§ R2.2).
6. The `stat` and `readlink` calls of the UID-0 check run under no `timeout` (§ R2.5).
7. **The sums differ from the parent's by one second**: the parent's § 4 says 16 s and 21 s for the usage mode
   and does not count the digest; here it is 17 s and 22 s, the same as the first mode. The rule the parent
   sets — at most the ceiling minus 2 s, no limit shortened — holds. The parent calls the limits under the
   ceiling "within the plan as written" (its § 5); the planner corrects the two numbers there.

## Definition of Done — E3.S2

- [ ] The constant is the block of § R2.3 and E3S2-T1 is green with the pin of this file.
- [ ] E3S2-T1…T24 are green; T2, T3, T4, T5, T7, T10, T15, T17, T20, T21, T22, T23 and T24 were seen RED by
      their break-its, with the message — every leak test and every "never" test of the story (merge condition 2).
- [ ] Revision 1's tests are green with the edits named in § R2.11 and no other.
- [ ] The UID-0 runs of the S1 spec are green (E3S1-T20…T26).
- [ ] No limit, ceiling, `jq` program or pattern of revision 1 changed.
- [ ] The todo file of T46 and its README row are gone with the commit that closes it.
- [ ] `USAGE_POLICY` is changed by its own commit (E3.S5), not by this story's.
