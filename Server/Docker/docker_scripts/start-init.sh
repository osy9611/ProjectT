#!/bin/bash
set -e

if [ -f "env.conf" ]; then
    source ./env.conf
else 
    echo "Please execute on 'docker_scripts' path."
    exit -1
fi

echo "OS : $OS_NAME"

#git
#git fetch
#git pull

if [ $OS_NAME = "WINDOWS" ]; then
    if [[ $(file -b - < env.conf) =~ CRLF ]]; then        
        git config --global core.autocrlf input
        find ../ -type f -not -path "*git/*" -print0 | xargs -0 dos2unix
    fi
fi

#Build
BUILD_INFO_FILE=../docker_build_info.txt

