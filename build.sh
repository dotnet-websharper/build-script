#!/bin/bash

set -e

if [ "$OS" = "Windows_NT" ]; then EXE=.exe; else EXE=; fi
if [ ! -f .paket/fake$EXE ]; then dotnet tool install fake-cli --tool-path .paket; fi

if [ "$BuildBranch" != "" ]; then
    .paket/fake$exe ws-checkout
    export BuildFromRef=$(<build/buildFromRef)
fi

if [ "$VisualStudioVersion" == ""  ]; then
    export VisualStudioVersion=15.0
fi

.paket/fake$exe run build.fsx "$@"
