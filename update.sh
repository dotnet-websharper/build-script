#! /bin/bash

if [ "$WsUpdate" != "" ]; then
    .paket/paket update -g wsbuild --no-install
fi
