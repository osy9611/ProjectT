#!/bin/bash
set -e
/usr/sbin/ntpd -u ntp:ntp -g
/usr/sbin/cron

echo "========================================================="
cat /tmp/docker_build_info.txt
echo "========================================================="
tail -f /dev/null