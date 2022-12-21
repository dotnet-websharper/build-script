@ECHO OFF
for /f %%i in ('git.exe rev-parse --show-toplevel') do (
    SET reponame=%%~ni
)

echo %reponame%

if %reponame% NEQ "build-script" (
    @REM git branch -b websharper70
    @REM git push -u origin websharper70
    @REM dotnet fsi removePrerelease.fsx
    echo "FALSE"
) else (
    @REM dotnet fsi removePrerelease.fsx
    echo "TRUE"
)

git add -A 
git commit -m "Use non-prerelease packages for WS6"
if errorlevel 1 exit 0

git push