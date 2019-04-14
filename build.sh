#!/bin/bash

set -e

if [ "$OS" = "Windows_NT" ]; then EXE=.exe; else EXE=; fi
if [ ! -f .paket/fake$EXE ]; then dotnet tool install fake-cli --tool-path .paket; fi

if [ "$BuildBranch" != "" ]; then
    .paket/fake$EXE ws-checkout
    export BuildFromRef=$(<build/buildFromRef)
fi

if [ "$VisualStudioVersion" == ""  ]; then
    export VisualStudioVersion=15.0
fi

# Allow running `build SomeTask` instead of `build -t SomeTask`
if [ "$1" != "" -a "$1" != -t -a "$1" != --target ]; then EXTRA_ARG=-t; else EXTRA_ARG=; fi

.paket/fake$EXE run build.fsx $EXTRA_ARG "$@"
