@echo off
setlocal
set PATH=%GitToolPath%;%PATH%

cls

if not exist .paket\fake.exe dotnet tool install fake-cli --tool-path .paket
if not exist .paket\paket.exe dotnet tool install paket --tool-path .paket

if not "%BuildBranch%"=="" (
  .paket\fake.exe run build.fsx -t ws-checkout
  if errorlevel 1 exit /b %errorlevel%

  set /p BuildFromRef=<build\buildFromRef
)

if "%VisualStudioVersion%"=="" (
  set VisualStudioVersion=15.0
)

if not "%WsUpdate%"=="" (
  .paket\paket.exe update -g wsbuild --no-install
  if errorlevel 1 exit /b %errorlevel%
)

.paket\paket.exe restore
if errorlevel 1 exit /b %errorlevel%

.paket\fake.exe run build.fsx %*
