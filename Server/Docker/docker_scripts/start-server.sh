#!/bin/bash
set -e
if [ "$#" -lt 1 ]; then
    echo "Usage : start-server.sh service_name(ex: deploy) options"
    echo "  options :"
    echo "      --local(-1) : Use Project source in local host"
    echo "      --no-cache : When build, not use cache"
    exit -1
fi

. ./start-init.sh
export BUILD_USER=local
export SERVICE_NAME=$1
COMPOSE_FILES=$DOCKER_COMPOSE_FILES
NO_CACHE=
NO_UP=
NO_BUILD=
saved=$@
while [ $# -ne 0 ]
do
    case "$1" in
      --local|-l) 
        COMPOSE_FILES=$DOCKER_COMPOSE_FILES_LOCAL
        export IS_LOCAL_SOURCE=true
        BUILD_STAGE=local-source;;
      --no-cache)
        NO_CACHE=$1;;
      --no-up)
        NO_UP=true;;
      --no-build)
        NO_BUILD=true
    esac
    shift
done

set -- $saved
if $IS_LOCAL_SOURCE;then
    #Check Need Directories
    if [ $SERVICE_NAME == "deploy" ] && [ ! -d $DEPLOY_ROOT ]; then
        $SUDO mkdir -p $DEPLOY_ROOT
        git clone $GIT_PATH/DeployServer.git -b master $DEPLOY_ROOT
        echo "$DEPLOY_ROOT"
    fi
fi

export DOCKER_COMPOSE_FILES=$COMPOSE_FILES

if [ ! $NO_BUILD ]; then
    ./build.sh $SERVICE_NAME $NO_CACHE
fi

if [ ! $NO_UP ]; then
    docker-compose $COMPOSE_FILES up -d -no-build --force-recreate $SERVICE_NAME
    ./logf.sh $SERVICE_NAME
fi
