#!/bin/bash
cd $(dirname $0)
set -e
source ./env.conf

#build and push to Docker Hub
if [ "$#" -lt 1 ]; then
    echo "Illegal number of parameters : build.sh compose_service_name(ex:deploy) [OPTIONS:-p(push) or -o(opt)]"
    exit -1
fi

SERVICE_NAME=$1
echo "Registry Server : $REGISTRY_SERVER"
echo "#Build $SERVICE_NAME"
saved=$@
IS_PUSH=false
NO_CACHE=
IS_UP=false
IS_ROLLBACK=false
ENV_VALUES=
while [ $# -ne 0 ]
do
    case "$1" in
        -p|--push) IS_PUSH=true;;
        --no-cache) NO_CACHE=--no-cache;;
        -u|--up) IS_UP=true;;
        -e)
            export $2
            shift;;
    esac
    shift
done
set -- $saved

echo "========================================================="
echo Service Name : $SERVICE_NAME
echo BuildDate : $BUILD_DATE

echo NO_CACHE : $NO_CACHE
echo IS_PUSH : $IS_PUSH
echo IS_ROLLBACK : $IS_ROLLBACK
echo "========================================================="

docker-compose $DOCKER_COMPOSE_FILES build --pull --force-rm --build-arg NO_CACHE=$(date +%s) $NO_CACHE $SERVICE_NAME

if $IS_PUSH ; then
    echo "Push to $REGISTRY_SERVER"

    docker-compose $DOCKER_COMPOSE_FILES push $SERVICE_NAME
fi

if $IS_UP ; then
    echo "UP $SERVICE_NAME"
    docker-compose $DOCKER_COMPOSE_FILES up -d --no-build --force-recreate $SERVICE_NAME
fi