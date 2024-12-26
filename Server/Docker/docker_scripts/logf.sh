#!/bin/bash
set -e
source ./env.conf
##logs container
if [ "$#" -ne 1]; then
    echo "Illergal number of parameters : log.sh service_name(ex: deploy)"
    exit -1
fi

SERVICE_NAME=$1
docker-compose $DOCKER_COMPOSE_FILES logs --tail 100 -f -t $SERVICE_NAME