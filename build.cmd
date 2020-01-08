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
  if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2017\" (
    set VisualStudioVersion=15.0
  )
  if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2019\" (
    set VisualStudioVersion=16.0
  )
)

:: Allow running `build SomeTask` instead of `build -t SomeTask`
set _Add-t=""
set FirstArg=%1
if not "%FirstArg%"=="" if not "%FirstArg:~0,1%"=="-" set _Add-t=1
if "%_Add-t%"=="1" (
  .paket\fake.exe run build.fsx -t %*
) else (
  .paket\fake.exe run build.fsx %*
)
