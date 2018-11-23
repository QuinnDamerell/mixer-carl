#!/bin/bash

CARL_HOME=/opt/carl

git pull

./BuildCarlLinux.sh

sudo cp ./CarlBuild/bin/* $CARL_HOME

sudo cp ./../../CarlConfig.json $CARL_HOME

sudo chown -R carl:carl $CARL_HOME

sudo cp carl.service /usr/lib/systemd/system/

sudo systemctl daemon-reload

sudo systemctl restart carl
