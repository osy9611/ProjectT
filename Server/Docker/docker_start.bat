@echo off

if "%1" == "" ( 
    echo No Parameter 
    exit /b
    )


cd docker-compose
docker-compose -f "%~dp0/docker-compose/%1.yml" up -d
