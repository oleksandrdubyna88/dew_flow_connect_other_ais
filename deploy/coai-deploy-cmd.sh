#!/bin/sh
# Forced command for the coai-deploy-ci key. That key can execute THIS file and nothing else
# (authorized_keys: restrict,command=...) — no shell, no pty, no forwarding, no scp. Whatever the
# client asked for arrives in SSH_ORIGINAL_COMMAND, and only the exact shapes below are honoured,
# so a leaked key is an "update button", not root. The model is lifted from its sibling next door,
# /root/rsd-deploy-cmd.sh, which has been the deploy path for CredsForDevs on this same host since
# it shipped; this file exists because ConnectOtherAIs was deploying over an unrestricted root key
# and did not have to be.
#
# THE FORCED COMMAND POINTS AT THIS FILE IN THE CHECKOUT, not at a copy under /root. The refresh
# below updates the checkout, so a fix to this wrapper reaches the host the same way a fix to
# systemd-release.sh does — from the next run onward. A copy under /root would have been a second
# thing to keep in step, which is the whole defect this file was written after: on 2026-09-08 the
# host's release script was three commits behind the workflow that invoked it and the deploy failed
# on an option it had never heard of.
#
#   authorized_keys line:
#     restrict,command="/opt/coai/src/deploy/coai-deploy-cmd.sh" ssh-ed25519 AAAA... coai-deploy-ci
#
# Accepted, and nothing else:
#   deploy <version>[ nopull]     publish that RELEASE's linux-x64 artefact and switch to it
#   deploy --rollback[ nopull]    pop one deployment off the trail
#   health                        what the unit itself answers — a REPORT, never a verdict
set -eu

SRC=/opt/coai/src
RELEASES=https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/releases/download
# The RID this host runs. A fact about the host, so it lives here rather than in a workflow input:
# the person dispatching a deploy is choosing a version, not an architecture. The workflow's
# preflight holds a copy so it can refuse an incomplete release before an approval is spent, and
# `install.test.ts` asserts the two agree — a migration that changed only one would otherwise
# approve a release for one architecture and fetch another.
RID=linux-x64
TOKEN_FILE=/etc/coai-canary.token
HEALTH=http://127.0.0.1:8090/api/health
# Every network call is bounded. An ssh session that hangs on a stalled fetch holds a CI job open
# until the job's own timeout, and the log then says nothing about which call stalled.
NET_TIMEOUT=300

die() { echo "$1" >&2; exit "${2:-91}"; }

CMD="${SSH_ORIGINAL_COMMAND:-}"

# `nopull` is stripped only from a DEPLOY form, and before the version token is taken. Stripping it
# ahead of the dispatch would have quietly turned `health nopull` — a command this contract does not
# have — into a successful health report, and validating the version before the strip would either
# reject `deploy 0.5.5 nopull` or let its suffix through unchecked. Both raised on review.
PULL=1
case "$CMD" in
  "deploy "*" nopull") PULL=0; CMD="${CMD% nopull}" ;;
esac

VERSION=""
case "$CMD" in
  health)              ACTION=health ;;
  "deploy --rollback") ACTION=rollback ;;
  "deploy "*)
    VERSION="${CMD#deploy }"
    # A `case` glob rather than grep: grep decides line by line, so a version carrying a newline
    # would be judged by its FIRST line and the rest would travel on into a filename. This looks at
    # the whole string or nothing.
    case "$VERSION" in
      "" | *[!0-9A-Za-z._-]* ) die "refused: version must be a plain tag, got: $VERSION" 90 ;;
    esac
    [ "${#VERSION}" -le 40 ] || die "refused: version must be at most 40 characters" 90
    ACTION=deploy ;;
  *)
    die "refused: this key accepts only 'deploy <version>', 'deploy --rollback' or 'health'" 90 ;;
esac

# ── health: a report, and it must not be a gate ────────────────────────────────────────────────
#
# It answers 0 even when the unit does not, and that is the point rather than a leniency. The
# workflow asks this to prove it can REACH the host before it deploys; if a stopped or crashed
# server made that step fail, the one workflow able to replace the binary would refuse to run
# exactly when the box needs it most — and a first deploy to a fresh host could never happen at all.
# What is running is judged by the caller, against the version it asked for.
if [ "$ACTION" = health ]; then
    if OUT=$(curl -fsS --max-time 10 "$HEALTH" 2>&1); then
        printf '%s\n' "$OUT"
    else
        printf 'unit: no answer on %s (%s)\n' "$HEALTH" "$OUT"
    fi
    exit 0
fi

RELEASE="$SRC/deploy/systemd-release.sh"

# ── rollback: before the network is touched ────────────────────────────────────────────────────
#
# A rollback is what an incident reaches for, and an incident is exactly when GitHub or this box's
# route to it may be the thing that is broken. It needs nothing that a refresh would fetch: the
# release it returns to is already on the disk, in the trail. Refreshing first would have made a
# network outage the reason an emergency rollback could not run — raised on the code round.
if [ "$ACTION" = rollback ]; then
    [ -x "$RELEASE" ] || die "$RELEASE is not executable — the repository committed it without the bit"
    exec env "COAI_TOKEN_FILE=$TOKEN_FILE" "$RELEASE" --rollback
fi

# ── the checkout the release script comes from ────────────────────────────────────────────────
#
# Tracked state — worktree AND index — is reset rather than merged around. This host's checkout is a
# deployment artefact, not a workspace: the only local edits it has ever collected were a hand
# `chmod +x` somebody applied to work around a file committed non-executable, and a pull that aborts
# on them breaks every future deploy with a message about the working tree. `reset --hard` also
# clears a STAGED edit, which `checkout -- .` leaves behind to refuse the merge anyway. Untracked
# files are left alone — discarding those would be destroying something nobody asked us to touch.
refresh() {
    [ -d "$SRC/.git" ] || die "$SRC is not a git checkout — this host was not set up by deploy/README.md"

    if [ -n "$(git -C "$SRC" status --porcelain --untracked-files=no)" ]; then
        echo "the checkout had local modifications; discarding them:"
        git -C "$SRC" status --short --untracked-files=no
    fi

    echo "refreshing $SRC ..."
    timeout "$NET_TIMEOUT" git -C "$SRC" fetch --quiet origin \
      || die "could not fetch from origin within ${NET_TIMEOUT}s — is this host's route to GitHub up?"
    git -C "$SRC" reset --hard --quiet origin/main \
      || die "could not reset $SRC to origin/main — run 'git -C $SRC status' on the host"
    echo "checkout: $(git -C "$SRC" log --oneline -1)"
}

if [ "$PULL" = 1 ]; then refresh; else echo "checkout: not refreshed (nopull)"; fi

[ -x "$RELEASE" ] || die "$RELEASE is not executable — the repository committed it without the bit"

# ── deploy: the PUBLISHED artefact, never one handed to us ─────────────────────────────────────
#
# The archive is fetched here rather than scp'd by the workflow, and that is not only because a
# forced command has no scp: what a person can verify is the file attached to the release, and the
# runner's copy is one hop further from it. The repository is public, so no credential is involved —
# an unauthenticated GET of a release asset, checked by the checksum published beside it.
ASSET="coai-server-$VERSION-$RID.tar.gz"
BASE="$RELEASES/server-v$VERSION"
STAGE=$(mktemp -d /root/.coai-deploy.XXXXXX)
# NOT /tmp: the script unpacks what it is given and /tmp is world-writable, so anything else on the
# box could pre-create a name as a symlink and have root extract through it.
#
# HUP is in the list because sshd sends it when a client disconnects, and an untrapped HUP kills a
# POSIX shell without running its EXIT trap — which is how a cancelled CI job would leave an
# archive behind on every attempt.
trap 'rm -rf "$STAGE"' EXIT INT TERM HUP

fetch() {
    echo "fetching $(basename "$1") ..."
    timeout "$NET_TIMEOUT" curl -fSL --retry 3 --retry-delay 2 -o "$2" "$1" \
      || die "refused: $1 could not be downloaded — is server-v$VERSION published with a $RID build?"
}

fetch "$BASE/$ASSET"        "$STAGE/$ASSET"
fetch "$BASE/$ASSET.sha256" "$STAGE/$ASSET.sha256"

# `sha256sum -c` checks the digest AND the name recorded beside it, so an asset published under a
# different name cannot satisfy this. Fails closed: a truncated download, a mismatched digest and a
# checksum file naming something else all stop here, before anything is unpacked.
( cd "$STAGE" && sha256sum -c "$ASSET.sha256" >/dev/null ) \
  || die "refused: $ASSET does not match the sha256 published beside it"
echo "artefact: $ASSET, sha256 verified"

# Not `exec`, so the trap above still runs and the stage directory does not accumulate. The status
# is carried out by hand for the same reason.
set +e
env "COAI_TOKEN_FILE=$TOKEN_FILE" "$RELEASE" --from "$STAGE/$ASSET" "$VERSION"
status=$?
set -e
exit "$status"
