dotnet fsi %~dp0\update.fsx

del build.fsx.lock

build WS-Clean

git add -A
git commit -m "Use FSharp.Core 5.0 in build script"
if errorlevel 1 exit 0

git push