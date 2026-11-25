# Common settings of the codex/agy coder comparison (sourced by the other scripts). No credential in this file.
# CMP_SCRATCH: private 0700 folder holding the sign-in COPIES and the vendor homes (never under CMP_BASE: the check unit binds job folders).
: "${CMP_SCRATCH:?set CMP_SCRATCH to a private 0700 scratch folder}"
export CMP_BASE=${CMP_BASE:-$HOME/coai-t3/cmp3}              # real disk: coder root, job folders, mounts
export CMP_CLONE=${CMP_CLONE:-$HOME/git/coai-v2-t3-clone}    # the main checkout of the task (never pushed)
export CMP_BASE_SHA=ae72656cbbff561b4f94e18caa6c4bb08c0476a2
export CMP_NOTES=${CMP_NOTES:-$HOME/coai-t3/cmp3-notes}
export CMP_TOOLS=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
export CMP_CR=$CMP_BASE/coder-root                           # the folder that holds the coder worktrees
export CMP_OUT=$CMP_SCRATCH/out
mkdir -p "$CMP_OUT"
vendor_job() { echo "cmp3-$1"; }                              # job id of a vendor's run
job_root()   { echo "$CMP_BASE/jobs/$(vendor_job "$1")"; }    # the job folder (JobRoot)
job_tree()   { echo "$CMP_CR/coai-coder-$(vendor_job "$1")"; }
job_mounts() { echo "$(job_root "$1").mounts"; }
