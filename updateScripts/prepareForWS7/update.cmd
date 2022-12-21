@ECHO OFF
for /f %%i in ('git.exe rev-parse --show-toplevel') do (
    SET reponame=%%~ni
)

if "%reponame%" NEQ "core" (
    git branch -b websharper70
    git push -u origin websharper70
    dotnet fsi removePrerelease.fsx
) else (
    dotnet fsi removePrerelease.fsx
)

git add -A 
git commit -m "Use non-prerelease packages for WS6"
if errorlevel 1 exit 0

git push