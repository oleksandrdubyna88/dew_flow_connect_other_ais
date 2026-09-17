#!/bin/sh
# Forced command for the `coai-bugs-deploy-ci` key. That key can execute THIS file and nothing
# else (authorized_keys: restrict,command=...) — no shell, no pty, no forwarding, no scp. Whatever
# the client asked for arrives in SSH_ORIGINAL_COMMAND, and only the exact shapes below are
# honoured, so a leaked key is an "update button", not root.
#
# ---------------------------------------------------------------------------
# A SEPARATE KEY AND A SEPARATE FILE from the Team server's, by operator decision: this is a fully
# independent deployment. The two wrappers look alike because the shape works, not because they
# share anything — and `coai-bugs` needs one thing its sibling does not, which is why they must not
# be merged: it accepts the SERVER'S OWN SECRET on stdin.
#
# All secrets live in the repository's Actions Secrets. `COAI_BUGS_SECRET` is delivered from there
# on every deploy, over ssh, on STDIN — never as an argument, because an argument is in `ps`, in
# `/proc/<pid>/cmdline` and in the auth log. It reaches `/etc/coai-bugs/env` through the root
# helper described at `secret` below, and nothing here prints it back: a wrapper that echoed the
# value would put it in the workflow log.
#
# NOTHING IN THIS FILE RUNS AS ROOT. It runs as `coai-bugs-deploy`, which owns the release area and
# may restart one unit. Everything needing more than that is a named, argument-less helper reached
# through one sudoers line — see `install-env.sh`.
#
# THE FORCED COMMAND POINTS AT THIS FILE IN THE CHECKOUT, not at a copy under /root, so a fix to
# this wrapper reaches the host the same way a fix to release.sh does. A copy would be a second
# thing to keep in step, and the Team server already paid for that lesson: its host script was
# three commits behind the workflow invoking it and the deploy failed on an option it had never
# heard of. `install-env.sh` is the ONE exception, and for the opposite reason: it runs as root,
# this checkout is writable by the deploy account, and a root script in a writable directory is a
# way to become root.
#
#   authorized_keys line:
#     restrict,command="/opt/coai-bugs/src/deploy/bugs/deploy-cmd.sh" ssh-ed25519 AAAA... coai-bugs-deploy-ci
#
# Accepted, and nothing else:
#   deploy <version>      install that RELEASE's linux-x64 artefact and switch to it
#   deploy --rollback     pop one deployment off the trail
#   secret                read two lines from stdin into /etc/coai-bugs/env, print nothing
#   admin-check           read the base64 key list from stdin, ask the local server, print the code
#   health                what the unit answers — a REPORT, never a verdict
# ---------------------------------------------------------------------------
set -eu

SRC=/opt/coai-bugs/src
RELEASES=https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/releases/download
RID=linux-x64

refuse() { printf '%s\n' "$1" >&2; exit 1; }

CMD=${SSH_ORIGINAL_COMMAND:-}
[ -n "$CMD" ] || refuse "this key runs one command; it was given none"

# Split on whitespace ONLY, and never eval: the argument is validated below against a pattern, so
# nothing the client sends can become code even if this file is read by a shell that word-splits.
set -- $CMD
VERB=${1:-}

case "$VERB" in
  secret)
    # HANDED TO A ROOT HELPER, because this script is not root and the file it is asking for is.
    #
    # `/etc/coai-bugs/env` has to be `root:coai-bugs 0640` — the service reads it, the deploy
    # account must not be able to read a secret back out of it. This wrapper runs as
    # `coai-bugs-deploy`, so every line that used to be here (`install -o root`, `chown
    # root:coai-bugs`, writing under `/etc`) failed for the account that actually runs it. The
    # whole `secret` verb could not work as written, which meant the FIRST deploy could not work:
    # it is the step that gives the service its secret before anything is installed.
    #
    # The helper is `/usr/local/sbin/coai-bugs-install-env`, installed from `install-env.sh` and
    # owned by root — never executed out of this checkout, which the deploy account can write.
    # stdin passes straight through; nothing here reads the secret. (CodeRabbit, #328.)
    exec sudo -n /usr/local/sbin/coai-bugs-install-env
    ;;

  admin-check)
    # IS ADMINISTRATION ACTUALLY WORKING on the build that is now serving?
    #
    # `/health` is public and says nothing about it, so a release can start perfectly and answer 401
    # to every admin call — which is exactly what this deployment did, with nothing anywhere
    # noticing. The only thing that proves otherwise is one authenticated request, and it is made
    # HERE, on loopback, rather than from the runner against the edge.
    #
    # The key arrives on STDIN, never as an argument: an argument is in `ps`, in
    # /proc/<pid>/cmdline and in the auth log. It reaches curl through a config file, because
    # `-H "Authorization: ..."` is an argument too — `umask` makes it 0600 and a trap removes it on
    # every exit path, including a signal.
    # THE WHOLE LIST arrives, base64, exactly as it was delivered — and the FIRST USABLE KEY is
    # picked out HERE rather than in the workflow. One place knows how to read the list: a copy of
    # that rule in a runner is a copy that drifts from the one the server uses, and a check that
    # tests a key the server does not hold reports a failure nobody can find. (Code round, gemini.)
    #
    # The marker line and the operator's notes are `#` comments, so the same filter drops both.
    BLOB=$(head -c 65536 | tr -d '\r\n')
    [ -n "$BLOB" ] || refuse "no key list arrived on stdin"

    # Decoded FIRST and checked on its own, so "this is not base64" and "this holds no key" are two
    # different sentences. Folded together they produced the wrong one for a value nobody encoded:
    # `base64 -d` emits partial output for partial input, and the refusal blamed the key's characters.
    # The alphabet, before the decode, because `base64 -d` emits partial output for partial input and
    # the refusal then blamed the wrong thing entirely — it complained about the KEY's characters for
    # a value nobody had encoded at all.
    case "$BLOB" in
      ''|*[!A-Za-z0-9+/=]*) refuse "the key list is not base64" ;;
    esac

    LIST=$(printf '%s' "$BLOB" | base64 -d 2>/dev/null || true)
    [ -n "$LIST" ] || refuse "the key list is not base64"

    KEY=$(printf '%s\n' "$LIST" | tr -d '\r' | grep -v '^#' | grep -v '^[[:space:]]*$' | head -n 1 || true)
    [ -n "$KEY" ] || refuse "the key list holds no key: every line is blank or a comment"

    # WHAT A KEY MAY CONTAIN, checked before it is written anywhere. curl's config format gives
    # meaning to quotes and backslash escapes inside a quoted value, so a key holding one could
    # change what the rest of the line means. Issued keys are hex; this admits the wider set an
    # operator might reasonably choose and refuses everything that could be punctuation in a config
    # file. (Code round, gemini.)
    case "$KEY" in
      *[!A-Za-z0-9_.:+/=-]*) refuse "the first key holds a character a credential should not" ;;
    esac

    # The trap is armed BEFORE the file exists, so a signal between `mktemp` and the next line still
    # removes it; `umask` is what makes it 0600 rather than the mode a default umask would give.
    CONF=''
    trap 'rm -f "$CONF"' EXIT INT TERM HUP
    umask 077
    CONF=$(mktemp) || refuse "could not make a temporary file"
    printf 'header = "Authorization: Bearer %s"\n' "$KEY" > "$CONF"

    # The unit was restarted moments ago by `deploy`, so the first attempt can arrive before it is
    # listening: `000` is curl saying it never connected, which is not an answer to report. Bounded
    # at ten seconds, and it still REPORTS rather than deciding — the workflow decides.
    ATTEMPT=0
    CODE=000
    while [ "$ATTEMPT" -lt 10 ]; do
      CODE=$(curl -sS -o /dev/null -w '%{http_code}' --connect-timeout 2 --max-time 3 --config "$CONF" \
             'http://127.0.0.1:8110/admin/keys?limit=1' 2>/dev/null || printf '000')
      [ "$CODE" = "000" ] || break
      ATTEMPT=$((ATTEMPT + 1))
      # On STDERR, so the caller reading stdout still gets one line with one number on it — and so
      # forty seconds of waiting looks like waiting rather than like a hang. (Code round, codex.)
      printf 'the unit has not answered yet; attempt %s of 10\n' "$ATTEMPT" >&2
      sleep 1
    done

    printf '%s\n' "$CODE"
    exit 0
    ;;

  health)
    # A REPORT. It never decides anything: `release.sh`'s canary does that, and this exists so a
    # person can ask without opening a shell.
    systemctl is-active coai-bugs || true
    curl -fsS --max-time 3 http://127.0.0.1:8110/health || printf 'no answer on loopback\n'
    exit 0
    ;;

  deploy) ;;
  *) refuse "'$VERB' is not something this key can do" ;;
esac

WHAT=${2:-}
[ -n "$WHAT" ] || refuse "deploy needs a version or --rollback"

# The checkout is refreshed BEFORE the script runs, so the host's release script is never behind
# the workflow that invokes it.
git -C "$SRC" fetch --depth 1 origin main >/dev/null 2>&1 || refuse "could not fetch"
git -C "$SRC" reset --hard origin/main >/dev/null 2>&1 || refuse "could not update the checkout"

if [ "$WHAT" = "--rollback" ]; then
  exec "$SRC/deploy/bugs/release.sh" --rollback
fi

# Validated on THIS side of the connection too. The workflow checks it, and a wrapper that trusted
# the client for that would be a wrapper whose safety depended on the caller.
case "$WHAT" in
  [0-9]*.[0-9]*.[0-9]*) ;;
  *) refuse "'$WHAT' is not a version" ;;
esac
printf '%s' "$WHAT" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+$' || refuse "'$WHAT' is not a version"

ARCHIVE="coai-bugs-$WHAT-$RID.tar.gz"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

# ONE function, with the flags written LITERALLY inside it.
#
# `--proto '=https'` and `--proto-redir '=https'` matter because `-L` follows redirects and this is
# downloading a BINARY the host is about to run as a service. Without them a redirect to plain http
# is followed silently, and the checksum below does not save it — the checksum arrives over the same
# hijacked connection.
#
# It was two literal `curl` lines (S1192: the same literal four times), then one shell VARIABLE
# holding the flags — and that second attempt was worse in a way worth recording: SonarCloud raised
# S6506 again on both calls, because a scheme pinned through `$HTTPS_ONLY` is invisible to anything
# reading the command. The analyser was right to be unconvinced. A function keeps the literal in one
# place AND keeps it where a reader, and a scanner, can see it.
fetch_https() {
  # $1 timeout, $2 destination, $3 url
  curl -fsSL --proto '=https' --proto-redir '=https' --tlsv1.2 \
    --max-time "$1" -o "$2" "$3"
}

fetch_https 120 "$WORK/$ARCHIVE" "$RELEASES/bugs-v$WHAT/$ARCHIVE" \
  || refuse "no $ARCHIVE in release bugs-v$WHAT"
fetch_https 30 "$WORK/$ARCHIVE.sha256" "$RELEASES/bugs-v$WHAT/$ARCHIVE.sha256" \
  || refuse "no checksum for $ARCHIVE"

# VERIFIED before anything is unpacked. A download this host did not check is a download somebody
# else could have chosen.
( cd "$WORK" && sha256sum -c "$ARCHIVE.sha256" >/dev/null 2>&1 ) \
  || refuse "$ARCHIVE does not match its checksum"

exec "$SRC/deploy/bugs/release.sh" --from "$WORK/$ARCHIVE" "$WHAT"
