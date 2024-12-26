#!/bin/bash
if [ $# = 0 ] 
then 
    echo "No Parameter"
else
    cd docker-compose
    docker-compose -f  "$1.yml" up -d
fi
