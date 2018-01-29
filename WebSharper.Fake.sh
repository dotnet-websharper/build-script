#!/bin/bash

set -e

if [ "$OS" = "Windows_NT" ]; then
    fake() { packages/build/FAKE/tools/FAKE.exe "$@" --fsiargs build.fsx; }
    paket() { .paket/paket.exe "$@"; }
else
    fake() { mono packages/build/FAKE/tools/FAKE.exe "$@" --fsiargs -d:MONO build.fsx; }
    paket() { mono .paket/paket.exe "$@"; }
fi

if [ "$BuildBranch" != "" ]; then
    fake ws-checkout
    export BuildFromRef=$(<build/buildFromRef)
fi

if [ "$NOT_DOTNET" = "" ]; then
    dotnet restore $DOTNETSOLUTION
else
    paket restore --touch-affected-refs
fi

if [ "$VisualStudioVersion" == ""  ]; then
    export VisualStudioVersion=15.0
fi

fake "$@"
