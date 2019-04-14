@echo off
setlocal
set PATH=%GitToolPath%;%PATH%

if not exist .paket\fake.exe dotnet tool install fake-cli --tool-path .paket

if not "%BuildBranch%"=="" (
  .paket\fake.exe run build.fsx -t ws-checkout
  if errorlevel 1 exit /b %errorlevel%

  set /p BuildFromRef=<build\buildFromRef
)

if "%VisualStudioVersion%"=="" (
  set VisualStudioVersion=15.0
)

:: Allow running `build SomeTask` instead of `build -t SomeTask`
set _Add-t=""
if not "%1"=="" if not "%1"=="-t" if not "%1"=="--target" set _Add-t=1
if "%_Add-t%"=="1" (
  .paket\fake.exe run build.fsx -t %*
) else (
  .paket\fake.exe run build.fsx %*
)