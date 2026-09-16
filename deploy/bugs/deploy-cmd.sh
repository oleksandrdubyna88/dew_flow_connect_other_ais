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
# `/proc/<pid>/cmdline` and in the auth log. This file writes it to `/etc/coai-bugs/env` with the
# mode the unit's ExecStartPre insists on, and prints nothing back: a wrapper that echoed it would
# put it in the workflow log.
#
# THE FORCED COMMAND POINTS AT THIS FILE IN THE CHECKOUT, not at a copy under /root, so a fix to
# this wrapper reaches the host the same way a fix to release.sh does. A copy would be a second
# thing to keep in step, and the Team server already paid for that lesson: its host script was
# three commits behind the workflow invoking it and the deploy failed on an option it had never
# heard of.
#
#   authorized_keys line:
#     restrict,command="/opt/coai-bugs/src/deploy/bugs/deploy-cmd.sh" ssh-ed25519 AAAA... coai-bugs-deploy-ci
#
# Accepted, and nothing else:
#   deploy <version>      install that RELEASE's linux-x64 artefact and switch to it
#   deploy --rollback     pop one deployment off the trail
#   secret                read one line from stdin into /etc/coai-bugs/env, print nothing
#   health                what the unit answers — a REPORT, never a verdict
# ---------------------------------------------------------------------------
set -eu

SRC=/opt/coai-bugs/src
RELEASES=https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/releases/download
RID=linux-x64
ENV_FILE=/etc/coai-bugs/env

refuse() { printf '%s\n' "$1" >&2; exit 1; }

CMD=${SSH_ORIGINAL_COMMAND:-}
[ -n "$CMD" ] || refuse "this key runs one command; it was given none"

# Split on whitespace ONLY, and never eval: the argument is validated below against a pattern, so
# nothing the client sends can become code even if this file is read by a shell that word-splits.
set -- $CMD
VERB=${1:-}

case "$VERB" in
  secret)
    # One line, from stdin. `head -1` bounds what a client can write; `tr -d` removes the newline
    # a shell pipeline adds, which would otherwise become part of the secret and hash differently
    # from the one the client thinks it sent.
    SECRET=$(head -c 4096 | head -1 | tr -d '\r\n')
    [ -n "$SECRET" ] || refuse "no secret arrived on stdin"

    install -d -m 0750 -o root -g coai-bugs /etc/coai-bugs
    umask 077
    TMP=$(mktemp)
    {
      printf 'COAI_BUGS_SECRET=%s\n' "$SECRET"
      printf 'COAI_BUGS_DATA=/opt/coai-bugs/data\n'
    } > "$TMP"
    # Both values in ONE file so the daemon and every maintenance command read the same database.
    # Setting the data directory in the unit and not here is how keys get issued into a file the
    # server never opens, and every upload is then rejected with a 401 nobody can explain.
    chown root:coai-bugs "$TMP"
    chmod 0640 "$TMP"
    mv -f "$TMP" "$ENV_FILE"
    # Nothing is printed. A wrapper that confirmed the value would put it in a CI log.
    printf 'the environment file is written\n'
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

# `--proto '=https'` and `--proto-redir '=https'`, because `-L` follows redirects and this is
# downloading a BINARY this host is about to execute as a service. Without them a redirect to
# plain http is followed silently, and the checksum below does not save it: the checksum comes
# down the same hijacked connection. SonarCloud S6506 flagged both lines and was right.
curl -fsSL --proto '=https' --proto-redir '=https' --tlsv1.2 \
  --max-time 120 -o "$WORK/$ARCHIVE" "$RELEASES/bugs-v$WHAT/$ARCHIVE" \
  || refuse "no $ARCHIVE in release bugs-v$WHAT"
curl -fsSL --proto '=https' --proto-redir '=https' --tlsv1.2 \
  --max-time 30 -o "$WORK/$ARCHIVE.sha256" "$RELEASES/bugs-v$WHAT/$ARCHIVE.sha256" \
  || refuse "no checksum for $ARCHIVE"

# VERIFIED before anything is unpacked. A download this host did not check is a download somebody
# else could have chosen.
( cd "$WORK" && sha256sum -c "$ARCHIVE.sha256" >/dev/null 2>&1 ) \
  || refuse "$ARCHIVE does not match its checksum"

exec "$SRC/deploy/bugs/release.sh" --from "$WORK/$ARCHIVE" "$WHAT"
