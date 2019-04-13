#!/bin/bash

set -e

if [ "$OS" = "Windows_NT" ]; then
    EXE_EXT=.exe
fi

PAKET_EXE=".paket/paket$EXE_EXT"
FAKE_EXE=".paket/fake$EXE_EXT"
if ! [ -f "$PAKET_EXE" ]; then dotnet tool install paket --tool-path .paket; fi
if ! [ -f "$FAKE_EXE" ]; then dotnet tool install fake-cli --tool-path .paket; fi

if [ "$BuildBranch" != "" ]; then
    "$FAKE_EXE" ws-checkout
    export BuildFromRef=$(<build/buildFromRef)
fi

if [ "$VisualStudioVersion" == ""  ]; then
    export VisualStudioVersion=15.0
fi

if [ "$WsUpdate" != "" ]; then
    "$PAKET_EXE" update -g wsbuild --no-install
fi

"$PAKET_EXE" restore

"$FAKE_EXE" run build.fsx "$@"
