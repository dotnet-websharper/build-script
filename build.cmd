@echo off
setlocal
set PATH=%GitToolPath%;%PATH%

if not "%BuildBranch%"=="" (
  dotnet fake run build.fsx -t ws-checkout
  if errorlevel 1 exit /b %errorlevel%

  set /p BuildFromRef=<build\buildFromRef
)

:: Allow running `build SomeTask` instead of `build -t SomeTask`
set _Add-t=""
set FirstArg=%1
if not "%FirstArg%"=="" if not "%FirstArg:~0,1%"=="-" set _Add-t=1
if "%_Add-t%"=="1" (
  dotnet fake run build.fsx -t %*
) else (
  dotnet fake run build.fsx %*
)
