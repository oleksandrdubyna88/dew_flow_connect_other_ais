import { isSessionId } from './chatSource';
import { credsSession } from './credsSession';

/**
 * What is sent to a VM to read a Claude session, and how it is asked (E2.S2, contracts C2 and C2a): the script text, its
 * constants, the operands and their quoting.
 *
 * <p>Pure: no `vscode`, no process. The script REPORTS facts (marker lines and data lines) and the parser
 * (`remoteFrame.ts`) decides; the one decision the script takes itself is which file it reads next. Every rule written
 * here is written ONCE: the session by `credsSession`, the id by `isSessionId`, the folder by `FOLDER` below — and
 * `sh` matches nothing but shapes.</p>
 *
 * <p>It lives apart from the parser because the script alone is 293 lines, and the two together would pass the 800-line
 * limit (X1 of the stories document).</p>
 */

/** The version of the frame the script prints (`COAI-BEGIN 1`). A parser that reads another number says so. */
export const REMOTE_PROTOCOL = 1;

/** The most the script reads from the end of a transcript: 4 MiB (`tail -c 4194304`). */
export const TAIL_MAX = 4 * 1024 * 1024;

/** The most a registry file may hold: 64 KiB. The script reads one byte more (`head -c 65537`) so that "too big" can be seen. */
export const REGISTRY_MAX = 64 * 1024;

/**
 * The tools the script runs on the VM — the eleven names of its two `for tool in` checks. A tool that is not there is
 * SAID (`COAI-BAD tool`), never read as "nothing found". `stat` is not in the list: without it a file's time is `-`.
 */
export const REMOTE_TOOLS: readonly string[] = ['sed', 'cut', 'head', 'tail', 'wc', 'grep', 'awk', 'ls', 'date', 'tmux', 'ps'];

/** What one ssh call asks. The ONE source of the operands, and what `readRemote` reads the answer against. */
export type RemoteCall =
  | { readonly kind: 'session'; readonly session: string }
  | { readonly kind: 'id'; readonly session: string; readonly id: string }
  | { readonly kind: 'titles'; readonly session: string; readonly folders: readonly string[] };

/**
 * A project folder as an OPERAND. It becomes a path part on the VM: no `/`, and the first character keeps out `.` and `..`.
 * A real name starts with `-` (`/home/x` becomes `-home-x`), so `-` is legal first. No space, tab, quote or shell character:
 * on win32 node then passes the word to `ssh.exe` as it is.
 */
const FOLDER = /^[A-Za-z0-9_-][A-Za-z0-9._-]{0,254}$/u;

/** Whether this is a project folder's name by the operand rule — the ONE place it is written; the parser asks it for the names it is told. */
export const isFolderName = (name: string): boolean => FOLDER.test(name);

/** The most folders one `titles` call may name. */
const FOLDERS_MAX = 3;

/** Whether this is a tmux session name — C1's rule, called and not typed again (a host is only there to complete the name). */
function isSessionName(session: string): boolean {
  return credsSession(`Creds: h / ${session}`).kind === 'session';
}

/** `<session> id <uuid>`: exactly one more operand, and it is a session id. */
function idCall(session: string, rest: readonly string[]): RemoteCall | undefined {
  const [id] = rest;

  return rest.length === 1 && id !== undefined && isSessionId(id) ? { kind: 'id', session, id } : undefined;
}

/** `<session> titles <folder>…`: one to three DIFFERENT folders, each by the folder rule. */
function titlesCall(session: string, rest: readonly string[]): RemoteCall | undefined {
  return foldersOk(rest) ? { kind: 'titles', session, folders: rest } : undefined;
}

/** One to three folders, each by the folder rule, no two the same. */
function foldersOk(folders: readonly string[]): boolean {
  return folders.length >= 1 && folders.length <= FOLDERS_MAX && folders.every(isFolderName)
    && new Set(folders).size === folders.length;
}

/** The forms that follow the session, by their literal word. A `Map`, so a word like `constructor` is no form. */
const FORMS: ReadonlyMap<string, (session: string, rest: readonly string[]) => RemoteCall | undefined> = new Map([
  ['id', idCall],
  ['titles', titlesCall],
]);

/** The call for the words after the session name, or nothing. */
function formOf(session: string, after: readonly string[]): RemoteCall | undefined {
  const [word, ...rest] = after;

  return word === undefined ? { kind: 'session', session } : FORMS.get(word)?.(session, rest);
}

/**
 * THE grammar of the operands (C2a): `<session>`, `<session> id <uuid>` and `<session> titles <folder>…`. `sshArgv` asks it
 * at the one road in, and the script checks the same shapes again in `sh`.
 */
export function callOf(operands: readonly string[]): RemoteCall | undefined {
  const [session, ...after] = operands;

  return session !== undefined && isSessionName(session) ? formOf(session, after) : undefined;
}

/** The operands of a call — the grammar's own words. Throws for a call the grammar refuses (a fixed text: never the value). */
export function operandsOf(call: RemoteCall): readonly string[] {
  const operands = call.kind === 'session'
    ? [call.session]
    : call.kind === 'id'
      ? [call.session, 'id', call.id]
      : [call.session, 'titles', ...call.folders];
  if (callOf(operands) === undefined) {
    throw new Error('The remote read was asked in a form its operands do not allow.');
  }

  return operands;
}

/**
 * One word for the REMOTE login shell: single quotes around it, each `'` inside written `'\''`. Total — it also quotes a
 * quote — so a loosened alphabet does not become an injection. A NUL, a CR or an LF throws (a fixed text: never the value).
 *
 * <p>The result holds no `"`, no space and no tab for a legal operand, so on win32 it crosses to `ssh.exe` as it is.</p>
 */
export function shQuoted(word: string): string {
  if (/[\0\r\n]/u.test(word)) {
    throw new Error('A word with a NUL, a CR or an LF cannot be given to a remote shell.');
  }

  return `'${word.replace(/'/gu, "'\\''")}'`;
}

/**
 * The text sent to `sh -s` on the VM, byte for byte: 293 lines, 7497 bytes, sha256
 * `1c3b25f9325f79cab47c84513fd126cac79f89105b016163bd75233686ecad07` (the spec's § S2.3; no test holds the hash, the
 * behaviour tests of `remoteScriptRuns.test.ts` run THIS text).
 *
 * <p>The whole script is ONE function, `coai_main`, read to its end and only then called with `</dev/null 2>/dev/null`; the
 * last line prints `COAI-END`. So a script cut on the way does nothing, no tool can read the rest of the script, and the
 * frame proves it arrived whole. It reads and prints: no write, no `cd`, no background job, no here-document.</p>
 *
 * <p>In this literal every backslash is written twice and every `${` is escaped, because it is a template literal; what `sh`
 * receives is the text of the spec.</p>
 */
export const REMOTE_SCRIPT = `coai_main() {
  LC_ALL=C
  export LC_ALL
  unset IFS
  set -f
  nl='
'
  h='[0-9A-Fa-f]'
  h4="$h$h$h$h"
  uuid="$h4$h4-$h4-$h4-$h4-$h4$h4$h4"

  coai_data() {
    sed 's/^/:/'
  }

  coai_walk() {
    seen=$((seen + 1))
    if [ "$seen" -gt 256 ] || [ "$2" -gt 16 ]; then
      printf 'over\\n'
      return 0
    fi
    if [ "$(ps -o comm= -p "$1")" = claude ]; then
      printf '%s\\n' "$1"
      return 0
    fi
    for kid in $(ps -o pid= --ppid "$1"); do
      case $kid in
        ''|*[!0-9]*) continue ;;
      esac
      coai_walk "$kid" $(($2 + 1))
    done
    return 0
  }

  coai_claude() {
    start=$(sed 's/^.*) //' <"/proc/$2/stat" | cut -d' ' -f20)
    case $start in
      ''|*[!0-9]*) start=- ;;
    esac
    printf 'COAI-CLAUDE %s %s %s\\n' "$1" "$2" "$start" || return 0
    reg="$HOME/.claude/sessions/$2.json"
    regid=''
    if [ ! -f "$reg" ]; then
      regstate=none
    elif [ ! -r "$reg" ]; then
      regstate=denied
    else
      regstate=ok
    fi
    printf 'COAI-REGISTRY %s %s\\n' "$2" "$regstate"
    [ "$regstate" = ok ] || return 0
    regtext=$(head -c 65537 <"$reg")
    printf '%s\\n' "$regtext" | coai_data
    regid=$(printf '%s\\n' "$regtext" | sed -n 's/.*"sessionId"[[:space:]]*:[[:space:]]*"\\([0-9A-Fa-f-]*\\)".*/\\1/p')
    case $regid in
      $uuid) ;;
      *) regid='' ;;
    esac
    return 0
  }

  coai_transcript() {
    set +f
    set -- "$1" "$HOME"/.claude/projects/*/"$1.jsonl"
    set -f
    want=$1
    shift
    found=0
    file=''
    for one do
      [ -f "$one" ] || continue
      found=$((found + 1))
      file=$one
    done
    printf 'COAI-TRANSCRIPT %s %s\\n' "$want" "$found" || return 0
    [ "$found" -eq 1 ] || return 0
    dir=\${file%/*}
    folder=\${dir##*/}
    size=$(wc -c <"$file")
    [ -r "$file" ] || size=''
    case $folder in
      ''|*"$nl"*) size='' ;;
    esac
    case $size in
      ''|*[!0-9\\ ]*) printf 'COAI-BAD transcript\\n'; return 0 ;;
    esac
    printf 'COAI-FILE %s\\n' "$((size + 0))"
    printf '%s\\n' "$folder" | coai_data
    printf 'COAI-TAIL\\n'
    { tail -c 4194304 <"$file"; printf '\\n'; } | coai_data
    size=$(wc -c <"$file")
    case $size in
      ''|*[!0-9\\ ]*) size=0 ;;
    esac
    printf 'COAI-FILE-END %s\\n' "$((size + 0))"
    return 0
  }

  coai_fallback() {
    cwd=$(tmux display-message -p -t "$1" '#{pane_current_path}')
    case $cwd in
      ''|*"$nl"*) printf 'COAI-BAD cwd\\n'; return 0 ;;
    esac
    printf 'COAI-CWD\\n' || return 0
    printf '%s\\n' "$cwd" | coai_data
    printf 'COAI-FOLDERS\\n'
    skipped=0
    listed=0
    more=0
    set +f
    set -- "$HOME"/.claude/projects/*/
    set -f
    for one do
      [ -d "$one" ] || continue
      name=\${one%/}
      name=\${name##*/}
      case $name in
        ''|.*|*[!A-Za-z0-9._-]*) skipped=$((skipped + 1)); continue ;;
      esac
      if [ "$listed" -ge 2000 ]; then
        more=1
        break
      fi
      listed=$((listed + 1))
      printf ':%s\\n' "$name" || return 0
    done
    printf 'COAI-FOLDERS-END %s %s\\n' "$skipped" "$more"
    return 0
  }

  coai_titles() {
    shift 2
    for folder do
      case $folder in
        ''|.*|*[!A-Za-z0-9._-]*) printf 'COAI-BAD operands\\n'; return 0 ;;
      esac
    done
    began=$(date +%s)
    took=0
    why=all
    for folder do
      dir="$HOME/.claude/projects/$folder"
      if [ ! -d "$dir" ]; then
        printf 'COAI-FOLDER %s none\\n' "$folder"
        continue
      fi
      printf 'COAI-FOLDER %s ok\\n' "$folder" || return 0
      for name in $(ls -t -- "$dir"); do
        case $name in
          $uuid.jsonl) ;;
          *) continue ;;
        esac
        file="$dir/$name"
        [ -f "$file" ] || continue
        if [ "$took" -ge 250 ]; then
          why=count
          break 2
        fi
        now=$(date +%s)
        if [ "$((now - began))" -ge 8 ]; then
          why=time
          break 2
        fi
        took=$((took + 1))
        size=$(wc -c <"$file")
        [ -r "$file" ] || size=''
        case $size in
          ''|*[!0-9\\ ]*) size=-1 ;;
        esac
        mtime=$(stat -c %Y -- "$file")
        case $mtime in
          ''|*[!0-9]*) mtime=- ;;
        esac
        if [ "$((size + 0))" -lt 0 ] || [ "$((size + 0))" -gt 67108864 ]; then
          printf 'COAI-SESSION %s %s %s skipped\\n' "\${name%.jsonl}" "$mtime" "$((size + 0))" || return 0
          continue
        fi
        printf 'COAI-SESSION %s %s %s rows\\n' "\${name%.jsonl}" "$mtime" "$((size + 0))" || return 0
        grep -F -e '"custom-title"' -e '"ai-title"' <"$file" | awk '
          1024 < length($0) { toolong++; next }
          { last = $0; rows++ }
          !($0 in seen) { seen[$0] = 1; if (kept < 24) { kept++; print ":" $0 } else more = 1 }
          END { if (rows) print ":" last; printf "COAI-ROWS %d %d\\n", toolong, more }'
      done
    done
    printf 'COAI-SCAN %s %s\\n' "$took" "$why"
    return 0
  }

  coai_session() {
    session=$1
    mode=\${2-}
    picked=\${3-}
    for tool in tmux ps; do
      command -v "$tool" >/dev/null || { printf 'COAI-BAD tool\\n'; return 0; }
    done
    panes=$(tmux list-panes -s -t "=$session:" -F '#{window_index}.#{pane_index} #{pane_id} #{pane_pid}') || {
      printf 'COAI-NO-SESSION\\n'
      return 0
    }
    set -- $panes
    if [ "$#" -eq 0 ] || [ "$(($# % 3))" -ne 0 ]; then
      printf 'COAI-BAD panes\\n'
      return 0
    fi
    if [ "$#" -gt 48 ]; then
      printf 'COAI-OVER panes\\n'
      return 0
    fi
    npanes=$(($# / 3))
    nclaude=0
    over=0
    onlypane=''
    while [ "$#" -ge 3 ]; do
      case $1 in
        ''|*[!0-9.]*) printf 'COAI-BAD panes\\n'; return 0 ;;
      esac
      case $2 in
        %*[!0-9]*|%) printf 'COAI-BAD panes\\n'; return 0 ;;
        %*) ;;
        *) printf 'COAI-BAD panes\\n'; return 0 ;;
      esac
      case $3 in
        ''|*[!0-9]*) printf 'COAI-BAD panes\\n'; return 0 ;;
      esac
      printf 'COAI-PANE %s %s %s\\n' "$1" "$2" "$3" || return 0
      onlypane=$2
      seen=0
      for one in $(coai_walk "$3" 0); do
        if [ "$one" = over ]; then
          over=1
          continue
        fi
        nclaude=$((nclaude + 1))
        coai_claude "$1" "$one"
      done
      shift 3
    done
    if [ "$over" -eq 1 ]; then
      printf 'COAI-OVER walk\\n'
      return 0
    fi
    exact=0
    if [ "$npanes" -eq 1 ] && [ "$nclaude" -eq 1 ]; then
      case $regstate in
        ok) exact=1 ;;
        denied) return 0 ;;
      esac
    fi
    if [ "$exact" -eq 1 ]; then
      [ -n "$regid" ] && coai_transcript "$regid"
      return 0
    fi
    if [ "$mode" = id ]; then
      coai_transcript "$picked"
      return 0
    fi
    if [ "$npanes" -eq 1 ] && [ "$nclaude" -le 1 ]; then
      coai_fallback "$onlypane"
    fi
    return 0
  }

  printf 'COAI-BEGIN 1\\n'
  case \${HOME-} in
    /*) ;;
    *) printf 'COAI-BAD home\\n'; return 0 ;;
  esac
  for tool in sed cut head tail wc grep awk ls date; do
    command -v "$tool" >/dev/null || { printf 'COAI-BAD tool\\n'; return 0; }
  done
  case \${1-} in
    [A-Za-z0-9]*) ;;
    *) printf 'COAI-BAD operands\\n'; return 0 ;;
  esac
  case $1 in
    *[!A-Za-z0-9_-]*) printf 'COAI-BAD operands\\n'; return 0 ;;
  esac
  case $#:\${2-} in
    1:) coai_session "$1" ;;
    3:id)
      case $3 in
        $uuid) coai_session "$1" id "$3" ;;
        *) printf 'COAI-BAD operands\\n' ;;
      esac
      ;;
    3:titles|4:titles|5:titles) coai_titles "$@" ;;
    *) printf 'COAI-BAD operands\\n' ;;
  esac
  return 0
}
coai_main "$@" </dev/null 2>/dev/null
printf 'COAI-END\\n'
`;
