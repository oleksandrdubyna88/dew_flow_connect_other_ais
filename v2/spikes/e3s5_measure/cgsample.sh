#!/bin/bash
# usage: cgsample.sh <unitname> <out> [seconds]   samples the processes of a running unit: pid ppid comm, mount and pid namespace ids, first 60 chars of argv (no secret is in an argv)
u=$1; out=$2; n=${3:-400}; : > $out
for i in $(seq 1 $n); do
  cg=$(systemctl --user show -p ControlGroup --value "$u.service" 2>/dev/null)
  if [ -n "$cg" ] && [ -r /sys/fs/cgroup$cg/cgroup.procs ]; then
    for p in $(cat /sys/fs/cgroup$cg/cgroup.procs 2>/dev/null); do
      [ -r /proc/$p/stat ] || continue
      pp=$(awk '{print $4}' /proc/$p/stat 2>/dev/null); c=$(cat /proc/$p/comm 2>/dev/null)
      m=$(readlink /proc/$p/ns/mnt 2>/dev/null); pn=$(readlink /proc/$p/ns/pid 2>/dev/null); un=$(readlink /proc/$p/ns/user 2>/dev/null)
      a=$(tr '\0' ' ' < /proc/$p/cmdline 2>/dev/null | cut -c1-60)
      echo "$p $pp $c $m $pn $un :: $a"
    done | sort -u >> $out
  fi
  sleep 0.15
done
