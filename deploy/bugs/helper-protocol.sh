#!/bin/sh
# Does the ROOT HELPER on this host speak the protocol this checkout is about to speak?
#
# ---------------------------------------------------------------------------
# WHY THIS EXISTS, AND WHY IT IS A FILE OF ITS OWN.
#
# `install-env.sh` is copied to /usr/local/sbin/coai-bugs-install-env when the host is provisioned,
# and it is the ONE file here that a deploy does NOT update: it runs as root, and this checkout is
# writable by the deploy account, so refreshing it from here would be a way to become root. That
# property is deliberate and stays. Its cost is that the helper can fall BEHIND the checkout, and on
# 2026-09-22 it had — by ten hours and one release.
#
# What that cost looked like: the pre-administrator helper read stdin as `head -1`. It took the
# secret, DISCARDED the base64 key list without a word, wrote an environment file holding no
# administrators, printed "the environment file is written" and exited 0. So the deploy's delivery
# step was green, the server started perfectly — an absent COAI_BUGS_ADMIN_KEYS is "no
# administrators", which is a legitimate way to run it — and every /admin call answered 401. The
# `admin-check` step caught it a whole deploy later, which is the right net at the wrong distance.
#
# So the two ends declare a PROTOCOL and this file compares them, BEFORE the secret is sent and
# before anything is written. `deploy-cmd.sh` runs it; `install-env.sh` declares it; the marker is
# spelled once, in `install-env.sh`, and demanded once, here.
#
# It is a file of its own for the reason `first-key.sh` is: the check has to run on the host from
# inside the forced command, and it has to be exercised against helpers that are deliberately wrong.
# Spelled inline in the wrapper it could only be tested by running the wrapper, which wants root, a
# sudoers line and an ssh forced command. `TheHostsHelperIsTheOneThisDeployNeedsTests` runs THIS
# FILE over the real helper and over a helper wrong in each way it can be wrong.
#
# It NEVER repairs anything. A script that reinstalled the helper it is checking would be the
# privilege escalation the copy exists to prevent — it would mean the deploy account can put a file
# where root executes it. It reports, and names the one command a person runs.
# ---------------------------------------------------------------------------
set -eu

# The protocol this checkout speaks, spelled EXACTLY as `install-env.sh` declares it. Compared whole,
# number included: a check asking whether the file merely mentions the helper would accept every
# future protocol, including the one that changes what the records mean again.
NEED='coai-bugs-install-env protocol 2'

HELPER=${1:-}
[ -n "$HELPER" ] || { printf 'usage: helper-protocol.sh <path to the installed helper>\n' >&2; exit 2; }

# The path is in the message on purpose. "The helper is missing" sends somebody to look at the
# helper; "/usr/local/sbin/coai-bugs-install-env is missing" is already the answer.
if [ ! -r "$HELPER" ]; then
  printf '%s is not there, or cannot be read. This host has never been provisioned, or the helper was removed.\n' "$HELPER" >&2
  printf 'Install it as root from this checkout:\n' >&2
  printf '  install -m 0755 -o root -g root %s/deploy/bugs/install-env.sh %s\n' "${SRC:-/opt/coai-bugs/src}" "$HELPER" >&2
  exit 1
fi

# `-F`, because the marker is a literal and a `.` in it must not match a character somebody else
# chose. Not `-x`: the declaration is a shell assignment, so the line carries the quoting around it.
if grep -Fq "$NEED" "$HELPER"; then
  exit 0
fi

printf '%s does not speak "%s".\n' "$HELPER" "$NEED" >&2
printf 'It is the ONE file here that a deploy does not update, and it is behind this checkout. An\n' >&2
printf 'older helper reads only the first line of what it is sent: it would take the secret, discard\n' >&2
printf 'the administrator list in silence, report success, and leave a server answering 401 to every\n' >&2
printf 'admin request. Nothing was sent.\n' >&2
printf 'Reinstall it as root, then run this deploy again:\n' >&2
printf '  install -m 0755 -o root -g root %s/deploy/bugs/install-env.sh %s\n' "${SRC:-/opt/coai-bugs/src}" "$HELPER" >&2
exit 1
