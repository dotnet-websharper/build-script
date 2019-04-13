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

.paket\fake.exe run build.fsx %*
