#! /bin/bash

if [ "$WsUpdate" != "" ]; then
    dotnet paket update -g wsbuild --no-install
fi
