#! /bin/bash

/usr/sbin/ntpd -u ntp:ntp -g
/sbin/crond
APP_NAME=startapp
BUILD_INFO_FILE=/tmp/docker_build_info.txt
if [ ! -d "$APP_NAME "]; then
    #Create django project
    echo "Create django project..."

    django_admin startproject $APP_NAME
    STATIC_DIR=public
    mkdir -p $APP_NAME/$STATIC_DIR
    cd $APP_NAME
    cp /tmp/urls.py $APP_NAME/.
    echo "import os" >> ./$APP_NAME/setting.py
    echo "STATIC_URL = '/$STATIC_DIR/'" >> ./$APP_NAME/settings.py
	echo "STATIC_ROOT = os.path.join(BASE_DIR, '$STATIC_DIR')" >> ./$APP_NAME/settings.py

    echo "Create health check file : ping.html"
    if [ ! -f $DJANGO_SERVER_ROOT/$APP_NAME/$STATIC_DIR/ping.html ]; then
        echo "ok" > $DJANGO_SERVER_ROOT/$APP_NAME/$STATIC_DIR/ping.html
    fi
    echo "Create index file : index.html"
	echo "Server Environment : $SERVER_ENV<br>" > $DJANGO_SERVER_ROOT/$APP_NAME/$STATIC_DIR/index.html
	echo $(cat $BUILD_INFO_FILE | grep 'Build Version') >> $DJANGO_SERVER_ROOT/$APP_NAME/$STATIC_DIR/index.html

    echo Server Environment : $SERVER_ENV >> $BUILD_INFO_FILE
	ln -nfs $BUILD_INFO_FILE $DJANGO_SERVER_ROOT/$APP_NAME/$STATIC_DIR/docker_build_info.txt
fi

echo "========================================================="
cat $BUILD_INFO_FILE
echo "========================================================="

cd $DJANGO_SERVER_ROOT/$APP_NAME
echo "Django makemigrations.."
python3 manage.py makemigrations apps
echo "Django migrate.."
python3 manage.py migrate apps
echo "Starting django.."
nohup python3 manage.py runserver 0.0.0.0:8000 &
echo "Started!! django.."
tail -f /dev/null