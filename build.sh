#!/bin/bash

set -e

if [ "$BuildBranch" != "" ]; then
    dotnet fake ws-checkout
    export BuildFromRef=$(<build/buildFromRef)
fi

# if [ "$VisualStudioVersion" == ""  ]; then
#     export VisualStudioVersion=15.0
# fi

# Allow running `build SomeTask` instead of `build -t SomeTask`
if [ "$1" != "" -a "$1" != -t -a "$1" != --target ]; then EXTRA_ARG=-t; else EXTRA_ARG=; fi

dotnet fake run build.fsx $EXTRA_ARG "$@"
